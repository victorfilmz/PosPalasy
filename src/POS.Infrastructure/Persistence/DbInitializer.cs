using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POS.Application.Interfaces;
using POS.Application.Validators;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Infrastructure.Persistence;

public static class DbInitializer
{
    /// <summary>
    /// Inicializa la base de datos: esquema faltante, datos semilla y cuenta administradora inicial.
    /// </summary>
    /// <param name="passwordHasher">Servicio de hashing; la contraseña inicial nunca se almacena en claro.</param>
    /// <param name="opcionesAdmin">Credenciales de la cuenta inicial leídas de configuración.</param>
    /// <param name="logger">Registro de avisos (DDL directo, contraseña inicial generada).</param>
    public static async Task SeedAsync(
        POSDbContext context,
        IPasswordHasher passwordHasher,
        OpcionesAdminInicial opcionesAdmin,
        ILogger? logger = null)
    {
        // Asegurar que la base de datos existe
        await context.Database.EnsureCreatedAsync();

        // Asegurar que las tablas de Control de Caja existen
        try
        {
            var createCajaSql = @"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CajaTurnos')
BEGIN
    CREATE TABLE [CajaTurnos] (
        [Id] int NOT NULL IDENTITY,
        [Cajero] nvarchar(100) NOT NULL,
        [FechaApertura] datetime2 NOT NULL,
        [FechaCierre] datetime2 NULL,
        [Estado] int NOT NULL,
        [MontoInicial] decimal(18,2) NOT NULL,
        [VentasEfectivo] decimal(18,2) NOT NULL,
        [VentasTarjeta] decimal(18,2) NOT NULL,
        [VentasTransferencia] decimal(18,2) NOT NULL,
        [TotalVentas] decimal(18,2) NOT NULL,
        [CantidadTransacciones] int NOT NULL,
        [TotalEntradasEfectivo] decimal(18,2) NOT NULL,
        [TotalSalidasEfectivo] decimal(18,2) NOT NULL,
        [MontoRealCierre] decimal(18,2) NULL,
        [Diferencia] decimal(18,2) NULL,
        [Observaciones] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_CajaTurnos] PRIMARY KEY ([Id])
    );
END

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MovimientosCaja')
BEGIN
    CREATE TABLE [MovimientosCaja] (
        [Id] int NOT NULL IDENTITY,
        [CajaTurnoId] int NOT NULL,
        [Tipo] int NOT NULL,
        [Monto] decimal(18,2) NOT NULL,
        [Concepto] nvarchar(250) NOT NULL,
        [Fecha] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_MovimientosCaja] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MovimientosCaja_CajaTurnos_CajaTurnoId] FOREIGN KEY ([CajaTurnoId]) REFERENCES [CajaTurnos] ([Id]) ON DELETE CASCADE
    );
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Ventas]') AND name = 'CajaTurnoId')
BEGIN
    ALTER TABLE [Ventas] ADD [CajaTurnoId] int NULL;
END

-- Columnas de Empresa y Factura Física
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Enterprises]') AND name = 'AnchoPapelMm')
BEGIN
    ALTER TABLE [Enterprises] ADD [AnchoPapelMm] int NOT NULL DEFAULT 80;
    ALTER TABLE [Enterprises] ADD [MostrarLogoTicket] bit NOT NULL DEFAULT 1;
    ALTER TABLE [Enterprises] ADD [MensajeEncabezadoExtra] nvarchar(200) NULL;
    ALTER TABLE [Enterprises] ADD [MensajePieTicket] nvarchar(250) NOT NULL DEFAULT '¡Gracias por su compra!';
    ALTER TABLE [Enterprises] ADD [PoliticaGarantiaTicket] nvarchar(500) NOT NULL DEFAULT 'No se aceptan devoluciones pasadas las 48 horas. Todo cambio requiere su comprobante fiscal.';
    ALTER TABLE [Enterprises] ADD [CantidadCopiasTicket] int NOT NULL DEFAULT 1;
    ALTER TABLE [Enterprises] ADD [PermitirVentaSinStock] bit NOT NULL DEFAULT 1;
END

-- Columnas de Producto
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Productos]') AND name = 'TieneLotes')
BEGIN
    ALTER TABLE [Productos] ADD [TieneLotes] bit NOT NULL DEFAULT 0;
END
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Productos]') AND name = 'Categoria')
BEGIN
    ALTER TABLE [Productos] ADD [Categoria] nvarchar(100) NOT NULL DEFAULT 'General';
    ALTER TABLE [Productos] ADD [CostoUnitario] decimal(18,4) NOT NULL DEFAULT 0;
END

-- Sucursales
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Sucursales')
BEGIN
    CREATE TABLE [Sucursales] (
        [Id] int NOT NULL IDENTITY,
        [EnterpriseId] int NOT NULL,
        [CodigoSucursal] nvarchar(20) NOT NULL DEFAULT '001',
        [Nombre] nvarchar(150) NOT NULL,
        [Direccion] nvarchar(200) NOT NULL,
        [Telefono] nvarchar(50) NULL,
        [Email] nvarchar(100) NULL,
        [CodigoProvincia] nvarchar(10) NOT NULL DEFAULT '01',
        [CodigoMunicipio] nvarchar(10) NOT NULL DEFAULT '010100',
        [EstaActiva] bit NOT NULL DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Sucursales] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Sucursales_Enterprises_EnterpriseId] FOREIGN KEY ([EnterpriseId]) REFERENCES [Enterprises] ([Id]) ON DELETE CASCADE
    );
END

-- InventariosAlmacen (Multi-Sucursal)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'InventariosAlmacen')
BEGIN
    CREATE TABLE [InventariosAlmacen] (
        [Id] int NOT NULL IDENTITY,
        [ProductoId] int NOT NULL,
        [SucursalId] int NOT NULL,
        [StockActual] decimal(18,2) NOT NULL DEFAULT 0,
        [StockMinimo] decimal(18,2) NOT NULL DEFAULT 5,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_InventariosAlmacen] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_InventariosAlmacen_Productos_ProductoId] FOREIGN KEY ([ProductoId]) REFERENCES [Productos] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_InventariosAlmacen_Sucursales_SucursalId] FOREIGN KEY ([SucursalId]) REFERENCES [Sucursales] ([Id]) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX [IX_InventariosAlmacen_ProductoId_SucursalId] ON [InventariosAlmacen] ([ProductoId], [SucursalId]);
END

-- Proveedores
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Proveedores')
BEGIN
    CREATE TABLE [Proveedores] (
        [Id] int NOT NULL IDENTITY,
        [RNC] nvarchar(11) NOT NULL,
        [RazonSocial] nvarchar(150) NOT NULL,
        [Telefono] nvarchar(50) NOT NULL,
        [Email] nvarchar(100) NULL,
        [Direccion] nvarchar(200) NULL,
        [EstaActivo] bit NOT NULL DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Proveedores] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_Proveedores_RNC] ON [Proveedores] ([RNC]);
END

-- PagosFactura (Pagos Mixtos)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'PagosFactura')
BEGIN
    CREATE TABLE [PagosFactura] (
        [Id] int NOT NULL IDENTITY,
        [VentaId] int NOT NULL,
        [FacturaId] int NULL,
        [MetodoPago] nvarchar(50) NOT NULL,
        [Monto] decimal(18,2) NOT NULL,
        [Referencia] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_PagosFactura] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PagosFactura_Ventas_VentaId] FOREIGN KEY ([VentaId]) REFERENCES [Ventas] ([Id]) ON DELETE CASCADE
    );
END

-- EmisionesDGIIQueue (Cola Asíncrona Resiliente)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EmisionesDGIIQueue')
BEGIN
    CREATE TABLE [EmisionesDGIIQueue] (
        [Id] int NOT NULL IDENTITY,
        [FacturaId] int NOT NULL,
        [eNCF] nvarchar(20) NOT NULL,
        [XmlFirmado] nvarchar(max) NOT NULL,
        [Intentos] int NOT NULL DEFAULT 0,
        [UltimoError] nvarchar(500) NULL,
        [EnviadoExitosamente] bit NOT NULL DEFAULT 0,
        [FechaRegistro] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [FechaUltimoIntento] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_EmisionesDGIIQueue] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_EmisionesDGIIQueue_FacturaId] ON [EmisionesDGIIQueue] ([FacturaId]);
    CREATE INDEX [IX_EmisionesDGIIQueue_EnviadoExitosamente] ON [EmisionesDGIIQueue] ([EnviadoExitosamente]);
END

-- Actualizaciones en CajaTurnos
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[CajaTurnos]') AND name = 'SucursalId')
BEGIN
    ALTER TABLE [CajaTurnos] ADD [SucursalId] int NOT NULL DEFAULT 1;
END

-- Actualizaciones en MovimientosInventario
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'MovimientosInventario')
BEGIN
    CREATE TABLE [MovimientosInventario] (
        [Id] int NOT NULL IDENTITY,
        [ProductoId] int NOT NULL,
        [SucursalId] int NOT NULL DEFAULT 1,
        [ProveedorId] int NULL,
        [NumeroLote] nvarchar(50) NULL,
        [FechaVencimiento] datetime2 NULL,
        [Tipo] int NOT NULL,
        [Cantidad] decimal(18,2) NOT NULL,
        [StockAnterior] decimal(18,2) NOT NULL,
        [StockNuevo] decimal(18,2) NOT NULL,
        [CostoUnitario] decimal(18,4) NOT NULL,
        [Concepto] nvarchar(250) NOT NULL,
        [ReferenciaDocumento] nvarchar(100) NULL,
        [Fecha] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [Usuario] nvarchar(100) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_MovimientosInventario] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MovimientosInventario_Productos_ProductoId] FOREIGN KEY ([ProductoId]) REFERENCES [Productos] ([Id])
    );
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[MovimientosInventario]') AND name = 'SucursalId')
    BEGIN
        ALTER TABLE [MovimientosInventario] ADD [SucursalId] int NOT NULL DEFAULT 1;
    END
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[MovimientosInventario]') AND name = 'ProveedorId')
    BEGIN
        ALTER TABLE [MovimientosInventario] ADD [ProveedorId] int NULL;
        ALTER TABLE [MovimientosInventario] ADD [NumeroLote] nvarchar(50) NULL;
        ALTER TABLE [MovimientosInventario] ADD [FechaVencimiento] datetime2 NULL;
    END
END
";
            await context.Database.ExecuteSqlRawAsync(createCajaSql);
        }
        catch (Exception ex)
        {
            // En proveedores sin compatibilidad con este DDL (por ejemplo SQLite en pruebas) el esquema ya
            // fue creado por EnsureCreated; se registra el aviso en lugar de silenciar el error.
            logger?.LogWarning(ex, "No se pudo aplicar el DDL de control (Caja/Sucursal/Inventario) sobre la base existente.");
        }

        // Integridad transaccional, idempotencia y numeración fiscal:
        // columnas y tabla nuevas sobre bases ya existentes creadas con EnsureCreated.
        try
        {
            // Se aplica en tres lotes porque SQL Server compila el lote completo antes de ejecutarlo:
            // una sentencia que use una columna añadida en el mismo lote falla al compilarse.
            // Lote 1: tabla nueva.
            var integridadTablasSql = @"
-- Secuencias fiscales (eNCF) autorizadas
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SecuenciasECF')
BEGIN
    CREATE TABLE [SecuenciasECF] (
        [Id] int NOT NULL IDENTITY,
        [Serie] nvarchar(3) NOT NULL,
        [TipoECF] int NOT NULL,
        [Ultimo] bigint NOT NULL DEFAULT 0,
        [DesdeAutorizado] bigint NULL,
        [HastaAutorizado] bigint NULL,
        [Version] uniqueidentifier NOT NULL,
        [FechaActualizacion] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_SecuenciasECF] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_SecuenciasECF_Serie] ON [SecuenciasECF] ([Serie]);
END

-- Devoluciones de venta (Fase 4): reembolso referenciado con idempotencia.
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Devoluciones')
BEGIN
    CREATE TABLE [Devoluciones] (
        [Id] int NOT NULL IDENTITY,
        [ClaveIdempotencia] uniqueidentifier NOT NULL,
        [VentaId] int NOT NULL,
        [CajaTurnoId] int NULL,
        [Usuario] nvarchar(100) NOT NULL,
        [Motivo] nvarchar(500) NOT NULL,
        [Fecha] datetime2 NOT NULL,
        [TotalDevuelto] decimal(18,2) NOT NULL,
        [EfectivoDevuelto] decimal(18,2) NOT NULL,
        [TarjetaDevuelto] decimal(18,2) NOT NULL,
        [TransferenciaDevuelto] decimal(18,2) NOT NULL,
        [RequiereComprobanteFiscal] bit NOT NULL DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Devoluciones] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Devoluciones_Ventas_VentaId] FOREIGN KEY ([VentaId]) REFERENCES [Ventas] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Devoluciones_CajaTurnos_CajaTurnoId] FOREIGN KEY ([CajaTurnoId]) REFERENCES [CajaTurnos] ([Id]) ON DELETE SET NULL
    );
    CREATE UNIQUE INDEX [IX_Devoluciones_ClaveIdempotencia] ON [Devoluciones] ([ClaveIdempotencia]);
    CREATE INDEX [IX_Devoluciones_VentaId] ON [Devoluciones] ([VentaId]);
END
-- Referencia fiscal de la nota de crédito (Fase 5.4): la devolución que origina el e-CF 34 y el
-- comprobante original modificado (InformacionReferencia del XSD e-CF 34).
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectronicInvoices')
    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'DevolucionId')
    ALTER TABLE [ElectronicInvoices] ADD [DevolucionId] int NULL
        CONSTRAINT [FK_ElectronicInvoices_Devoluciones_DevolucionId] REFERENCES [Devoluciones] ([Id]) ON DELETE SET NULL;
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectronicInvoices')
    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'eNCFModificado')
    ALTER TABLE [ElectronicInvoices] ADD [eNCFModificado] nvarchar(13) NULL;
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectronicInvoices')
    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'CodigoModificacion')
    ALTER TABLE [ElectronicInvoices] ADD [CodigoModificacion] int NULL;
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectronicInvoices')
    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'FechaNCFModificado')
    ALTER TABLE [ElectronicInvoices] ADD [FechaNCFModificado] nvarchar(10) NULL;

-- Régimen de contingencia (Fase 6.1): metadato local del emisor. Los XSD e-CF v1.0 no llevan
-- campo XML de contingencia; el tipo se declara al transmitir el comprobante diferido.
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectronicInvoices')
    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'TipoContingencia')
    ALTER TABLE [ElectronicInvoices] ADD [TipoContingencia] int NULL;
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectronicInvoices')
    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'ContingenciaDesdeUtc')
    ALTER TABLE [ElectronicInvoices] ADD [ContingenciaDesdeUtc] datetime2 NULL;
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectronicInvoices')
    AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'ContingenciaHastaUtc')
    ALTER TABLE [ElectronicInvoices] ADD [ContingenciaHastaUtc] datetime2 NULL;

-- Serie E34 (nota de crédito electrónica): la numeración fiscal de las devoluciones.
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'SecuenciasECF')
    AND NOT EXISTS (SELECT 1 FROM [SecuenciasECF] WHERE [Serie] = 'E34')
    INSERT INTO [SecuenciasECF] ([Serie],[TipoECF],[Ultimo],[DesdeAutorizado],[HastaAutorizado],[Version],[FechaActualizacion])
    VALUES ('E34', 34, 0, 1, NULL, NEWID(), GETUTCDATE());

-- Pool de secuencias devueltas al stock tras rechazo corregible (Fase 5.5):
-- secuenciaUtilizada=false significa que la DGII declaró el número reutilizable.
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SecuenciasLibres')
BEGIN
    CREATE TABLE [SecuenciasLibres] (
        [Id] int NOT NULL IDENTITY,
        [Serie] nvarchar(3) NOT NULL,
        [Numero] bigint NOT NULL,
        [ENCF] nvarchar(13) NOT NULL,
        [FacturaRechazadaId] int NOT NULL,
        [FechaLiberacionUtc] datetime2 NOT NULL,
        [LiberadaPor] nvarchar(100) NULL,
        [MotivoRechazo] nvarchar(500) NULL,
        [Consumida] bit NOT NULL DEFAULT 0,
        [FechaConsumoUtc] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_SecuenciasLibres] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_SecuenciasLibres_Serie_Numero_Activa]
        ON [SecuenciasLibres] ([Serie], [Numero]) WHERE [Consumida] = 0;
    CREATE INDEX [IX_SecuenciasLibres_Cola] ON [SecuenciasLibres] ([Serie], [Consumida], [FechaLiberacionUtc]);
END

-- Renglones de la devolución (FASE 4): snapshot prorrateado de las líneas vendidas.
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'DevolucionItems')
BEGIN
    CREATE TABLE [DevolucionItems] (
        [Id] int NOT NULL IDENTITY,
        [DevolucionId] int NOT NULL,
        [VentaItemId] int NOT NULL,
        [ProductoId] int NOT NULL,
        [Descripcion] nvarchar(200) NOT NULL,
        [Cantidad] decimal(18,2) NOT NULL,
        [MontoBase] decimal(18,2) NOT NULL,
        [MontoITBIS] decimal(18,2) NOT NULL,
        [MontoTotal] decimal(18,2) NOT NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_DevolucionItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DevolucionItems_Devoluciones_DevolucionId] FOREIGN KEY ([DevolucionId]) REFERENCES [Devoluciones] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_DevolucionItems_Productos_ProductoId] FOREIGN KEY ([ProductoId]) REFERENCES [Productos] ([Id]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_DevolucionItems_DevolucionId] ON [DevolucionItems] ([DevolucionId]);
END
";
            await context.Database.ExecuteSqlRawAsync(integridadTablasSql);

            // Lote 2: columnas nuevas sobre tablas existentes.
            var integridadColumnasSql = @"
-- Idempotencia y trazabilidad de ventas
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Ventas]') AND name = 'ClaveIdempotencia')
    ALTER TABLE [Ventas] ADD [ClaveIdempotencia] uniqueidentifier NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Ventas]') AND name = 'Usuario')
    ALTER TABLE [Ventas] ADD [Usuario] nvarchar(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Ventas]') AND name = 'MontoRecibido')
    ALTER TABLE [Ventas] ADD [MontoRecibido] decimal(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Ventas]') AND name = 'Cambio')
    ALTER TABLE [Ventas] ADD [Cambio] decimal(18,2) NOT NULL DEFAULT 0;

-- Instantánea fiscal del renglón
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[VentaItems]') AND name = 'CodigoProducto')
    ALTER TABLE [VentaItems] ADD [CodigoProducto] nvarchar(50) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[VentaItems]') AND name = 'UnidadMedida')
    ALTER TABLE [VentaItems] ADD [UnidadMedida] int NOT NULL DEFAULT 1;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[VentaItems]') AND name = 'IndicadorBienoServicio')
    ALTER TABLE [VentaItems] ADD [IndicadorBienoServicio] int NOT NULL DEFAULT 1;

-- Responsable del turno de caja
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[CajaTurnos]') AND name = 'UsuarioId')
    ALTER TABLE [CajaTurnos] ADD [UsuarioId] int NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[CajaTurnos]') AND name = 'UsuarioNombre')
    ALTER TABLE [CajaTurnos] ADD [UsuarioNombre] nvarchar(100) NULL;

-- Estado técnico de emisión del comprobante
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'EstadoEmision')
    ALTER TABLE [ElectronicInvoices] ADD [EstadoEmision] int NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'FechaUltimoIntentoEnvio')
    ALTER TABLE [ElectronicInvoices] ADD [FechaUltimoIntentoEnvio] datetime2 NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'UltimoCodigoHttp')
    ALTER TABLE [ElectronicInvoices] ADD [UltimoCodigoHttp] int NULL;

-- Política de stock de tres estados (Fase 3): columnas nuevas (los UPDATE de migración van en el
-- lote siguiente: SQL Server compila el lote completo y no puede leer columnas creadas en él).
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Enterprises]') AND name = 'PoliticaStock')
    ALTER TABLE [Enterprises] ADD [PoliticaStock] int NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Ventas]') AND name = 'RequiereRevisionStock')
    ALTER TABLE [Ventas] ADD [RequiereRevisionStock] bit NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[MovimientosInventario]') AND name = 'RequiereRevision')
    ALTER TABLE [MovimientosInventario] ADD [RequiereRevision] bit NOT NULL DEFAULT 0;

-- Devoluciones de caja (Fase 4): acumulado de reembolsos del turno y trazabilidad del movimiento.
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[CajaTurnos]') AND name = 'TotalDevoluciones')
    ALTER TABLE [CajaTurnos] ADD [TotalDevoluciones] decimal(18,2) NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[MovimientosCaja]') AND name = 'Usuario')
    ALTER TABLE [MovimientosCaja] ADD [Usuario] nvarchar(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[MovimientosCaja]') AND name = 'VentaId')
    ALTER TABLE [MovimientosCaja] ADD [VentaId] int NULL;

-- Auditoría mínima de cambios de configuración (usuario, fecha, antes, después, motivo).
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditoriaCambios')
BEGIN
    CREATE TABLE [AuditoriaCambios] (
        [Id] int NOT NULL IDENTITY,
        [Usuario] nvarchar(100) NOT NULL,
        [FechaUtc] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [Entidad] nvarchar(100) NOT NULL,
        [Campo] nvarchar(100) NOT NULL,
        [ValorAnterior] nvarchar(200) NOT NULL,
        [ValorNuevo] nvarchar(200) NOT NULL,
        [Motivo] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_AuditoriaCambios] PRIMARY KEY ([Id])
    );
    CREATE INDEX [IX_AuditoriaCambios_Entidad_Campo_Fecha] ON [AuditoriaCambios] ([Entidad], [Campo], [FechaUtc]);
END

-- Lease y espera progresiva de la cola DGII
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EmisionesDGIIQueue]') AND name = 'Estado')
    ALTER TABLE [EmisionesDGIIQueue] ADD [Estado] int NOT NULL DEFAULT 0;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EmisionesDGIIQueue]') AND name = 'ProximoIntentoUtc')
    ALTER TABLE [EmisionesDGIIQueue] ADD [ProximoIntentoUtc] datetime2 NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EmisionesDGIIQueue]') AND name = 'LeaseToken')
    ALTER TABLE [EmisionesDGIIQueue] ADD [LeaseToken] nvarchar(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EmisionesDGIIQueue]') AND name = 'LeaseHastaUtc')
    ALTER TABLE [EmisionesDGIIQueue] ADD [LeaseHastaUtc] datetime2 NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EmisionesDGIIQueue]') AND name = 'TrackId')
    ALTER TABLE [EmisionesDGIIQueue] ADD [TrackId] nvarchar(100) NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EmisionesDGIIQueue]') AND name = 'UltimoCodigoHttp')
    ALTER TABLE [EmisionesDGIIQueue] ADD [UltimoCodigoHttp] int NULL;
";
            await context.Database.ExecuteSqlRawAsync(integridadColumnasSql);

            // Lote 3: relleno de datos y objetos que usan columnas creadas en el lote anterior.
            var integridadRellenoSql = @"
-- Semilla: series E31 (crédito fiscal) y E32 (consumo), sin límite de rango configurado.
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'SecuenciasECF')
    AND NOT EXISTS (SELECT 1 FROM [SecuenciasECF] WHERE [Serie] = 'E31')
    INSERT INTO [SecuenciasECF] ([Serie],[TipoECF],[Ultimo],[DesdeAutorizado],[HastaAutorizado],[Version],[FechaActualizacion])
    VALUES ('E31', 31, 0, 1, NULL, NEWID(), GETUTCDATE());
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'SecuenciasECF')
    AND NOT EXISTS (SELECT 1 FROM [SecuenciasECF] WHERE [Serie] = 'E32')
    INSERT INTO [SecuenciasECF] ([Serie],[TipoECF],[Ultimo],[DesdeAutorizado],[HastaAutorizado],[Version],[FechaActualizacion])
    VALUES ('E32', 32, 0, 1, NULL, NEWID(), GETUTCDATE());

-- Unicidad de la numeración VIGENTE (Fase 5.5): un rechazo con secuenciaUtilizada=false devuelve
-- el número al pool y el comprobante rechazado conserva su e-NCF para trazabilidad, de modo que el
-- índice único general se REPLANTEA como filtrado que excluye los rechazados (Estado=2). La base
-- nace con el índice filtrado desde EnsureCreated; esta migración cubre las bases previas.
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectronicInvoices')
    AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ElectronicInvoices_eNCF' AND object_id = OBJECT_ID(N'[ElectronicInvoices]'))
BEGIN
    DROP INDEX [IX_ElectronicInvoices_eNCF] ON [ElectronicInvoices];
    CREATE UNIQUE INDEX [IX_ElectronicInvoices_eNCF] ON [ElectronicInvoices] ([eNCF]) WHERE [Estado] <> 2;
END

-- Una devolución tiene UNA nota de crédito (idempotencia física de la emisión fiscal, Fase 5.4).
-- Va en ESTE lote y no en el del ALTER: SQL Server compila el lote completo y un índice sobre una
-- columna creada en el mismo lote falla con ""Invalid column name"".
IF EXISTS (SELECT * FROM sys.tables WHERE name = 'ElectronicInvoices')
    AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ElectronicInvoices_DevolucionId' AND object_id = OBJECT_ID(N'[ElectronicInvoices]'))
    CREATE UNIQUE INDEX [IX_ElectronicInvoices_DevolucionId] ON [ElectronicInvoices] ([DevolucionId]) WHERE [DevolucionId] IS NOT NULL;

-- Política de stock (Fase 3): migración del booleano anterior a la política, en un lote posterior
-- a la creación de la columna. PermitirVentaSinStock true -> Permitir (0); false -> Bloquear (2).
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Enterprises]') AND name = 'PoliticaStock')
    AND EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Enterprises]') AND name = 'PermitirVentaSinStock')
    UPDATE [Enterprises] SET [PoliticaStock] = CASE WHEN [PermitirVentaSinStock] = 1 THEN 0 ELSE 2 END;

-- Ventas existentes: clave de idempotencia única y obligatoria.
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Ventas]') AND name = 'ClaveIdempotencia')
BEGIN
    UPDATE [Ventas] SET [ClaveIdempotencia] = NEWID() WHERE [ClaveIdempotencia] IS NULL;
    ALTER TABLE [Ventas] ALTER COLUMN [ClaveIdempotencia] uniqueidentifier NOT NULL;
END
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Ventas]') AND name = 'ClaveIdempotencia')
    AND NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Ventas_ClaveIdempotencia' AND object_id = OBJECT_ID(N'[Ventas]'))
    CREATE UNIQUE INDEX [IX_Ventas_ClaveIdempotencia] ON [Ventas] ([ClaveIdempotencia]);

IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[CajaTurnos]') AND name = 'UsuarioId')
    AND NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_CajaTurnos_UsuarioId_Estado' AND object_id = OBJECT_ID(N'[CajaTurnos]'))
    CREATE INDEX [IX_CajaTurnos_UsuarioId_Estado] ON [CajaTurnos] ([UsuarioId], [Estado]);

-- FK del movimiento de caja a su venta de origen (creada en lote previo; el índice va aquí).
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[MovimientosCaja]') AND name = 'VentaId')
    AND NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_MovimientosCaja_Ventas_VentaId')
BEGIN
    ALTER TABLE [MovimientosCaja] ADD CONSTRAINT [FK_MovimientosCaja_Ventas_VentaId]
        FOREIGN KEY ([VentaId]) REFERENCES [Ventas] ([Id]) ON DELETE NO ACTION;
END
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[MovimientosCaja]') AND name = 'VentaId')
    AND NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_MovimientosCaja_VentaId' AND object_id = OBJECT_ID(N'[MovimientosCaja]'))
    CREATE INDEX [IX_MovimientosCaja_VentaId] ON [MovimientosCaja] ([VentaId]);

-- Los comprobantes ya transmitidos antes de este cambio quedan en envio confirmado (6).
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[ElectronicInvoices]') AND name = 'EstadoEmision')
    UPDATE [ElectronicInvoices] SET [EstadoEmision] = 6 WHERE [TrackId] IS NOT NULL AND [EstadoEmision] = 0;

IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[EmisionesDGIIQueue]') AND name = 'Estado')
    AND NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_EmisionesDGIIQueue_EnviadoExitosamente_Estado_ProximoIntentoUtc' AND object_id = OBJECT_ID(N'[EmisionesDGIIQueue]'))
    CREATE INDEX [IX_EmisionesDGIIQueue_EnviadoExitosamente_Estado_ProximoIntentoUtc]
        ON [EmisionesDGIIQueue] ([EnviadoExitosamente], [Estado], [ProximoIntentoUtc]);
";
            await context.Database.ExecuteSqlRawAsync(integridadRellenoSql);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "No se pudo aplicar el DDL de integridad (secuencias, idempotencia y cola) sobre la base existente.");
        }

        // Control de seguridad: tabla de usuarios para bases ya existentes creadas con EnsureCreated.
        try
        {
            var createUsuariosSql = @"
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Usuarios')
BEGIN
    CREATE TABLE [Usuarios] (
        [Id] int NOT NULL IDENTITY,
        [NombreUsuario] nvarchar(60) NOT NULL,
        [NombreCompleto] nvarchar(150) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [Rol] int NOT NULL,
        [EstaActivo] bit NOT NULL DEFAULT 1,
        [DebeCambiarPassword] bit NOT NULL DEFAULT 0,
        [IntentosFallidos] int NOT NULL DEFAULT 0,
        [BloqueadoHasta] datetime2 NULL,
        [UltimoAcceso] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL DEFAULT (GETUTCDATE()),
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Usuarios] PRIMARY KEY ([Id])
    );
    CREATE UNIQUE INDEX [IX_Usuarios_NombreUsuario] ON [Usuarios] ([NombreUsuario]);
END
";
            await context.Database.ExecuteSqlRawAsync(createUsuariosSql);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "No se pudo aplicar el DDL de la tabla Usuarios sobre la base existente.");
        }

        // 1. Empresa Emisora por Defecto
        if (!await context.Enterprises.AnyAsync())
        {
            var enterprise = new Enterprise
            {
                RNC = "13100000001",
                RazonSocial = "PosPalasy SRL",
                NombreComercial = "PosPalasy Facturación DGII",
                Direccion = "Av. 27 de Febrero #450, Piantini, Santo Domingo",
                Telefono = "809-555-0199",
                Email = "facturacion@pospalasy.com.do",
                SitioWeb = "https://pospalasy.com.do",
                CodigoProvincia = "01",
                CodigoMunicipio = "010100",
                TipoComprobantePredeterminado = TipoeCFType.FacturaConsumo,
                EstaActiva = true
            };

            enterprise.Sucursales.Add(new Sucursal
            {
                CodigoSucursal = "001",
                Nombre = "Sucursal Central Piantini",
                Direccion = "Av. 27 de Febrero #450",
                Telefono = "809-555-0199",
                Email = "sucursal01@pospalasy.com.do",
                CodigoProvincia = "01",
                CodigoMunicipio = "010100"
            });

            await context.Enterprises.AddAsync(enterprise);
            await context.SaveChangesAsync();
        }

        // 2. Clientes Iniciales
        if (!await context.Clientes.AnyAsync())
        {
            await context.Clientes.AddRangeAsync(
                new Cliente
                {
                    RazonSocial = "Consumidor Final",
                    NombreComercial = "Público General",
                    EstaActivo = true
                },
                new Cliente
                {
                    RNC = "101000001",
                    RazonSocial = "Distribuidora Nacional SRL",
                    NombreComercial = "Distribuidora Nacional",
                    Direccion = "Av. Luperón #25, Zona Industrial",
                    Telefono = "809-555-8888",
                    Email = "compras@distribuidora.com.do",
                    CodigoProvincia = "01",
                    CodigoMunicipio = "010100",
                    EstaActivo = true
                }
            );
            await context.SaveChangesAsync();
        }

        // 3. Catálogo de Productos con Atributos Fiscales DGII
        if (!await context.Productos.AnyAsync())
        {
            await context.Productos.AddRangeAsync(
                new Producto
                {
                    Codigo = "746001001",
                    Descripcion = "Refresco Cola 500ml",
                    PrecioUnitario = 45.00m,
                    IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
                    UnidadMedida = UnidadMedidaType.Botella,
                    EstaActivo = true
                },
                new Producto
                {
                    Codigo = "746001002",
                    Descripcion = "Arroz Blanco Selecto 10 Lbs",
                    PrecioUnitario = 350.00m,
                    IndicadorFacturacion = IndicadorFacturacionType.Exento,
                    UnidadMedida = UnidadMedidaType.Bolsa,
                    EstaActivo = true
                },
                new Producto
                {
                    Codigo = "746001003",
                    Descripcion = "Leche Evaporada Entera 315g",
                    PrecioUnitario = 75.00m,
                    IndicadorFacturacion = IndicadorFacturacionType.ITBIS2_16, // Primera necesidad
                    UnidadMedida = UnidadMedidaType.Lata,
                    EstaActivo = true
                },
                new Producto
                {
                    Codigo = "746001004",
                    Descripcion = "Café Molido Dominicano 1 Lb",
                    PrecioUnitario = 220.00m,
                    IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
                    UnidadMedida = UnidadMedidaType.Paquete,
                    EstaActivo = true
                },
                new Producto
                {
                    Codigo = "746001005",
                    Descripcion = "Pan de Agua (Funda 10 uds)",
                    PrecioUnitario = 80.00m,
                    IndicadorFacturacion = IndicadorFacturacionType.Exento,
                    UnidadMedida = UnidadMedidaType.Bolsa,
                    EstaActivo = true
                },
                new Producto
                {
                    Codigo = "746001006",
                    Descripcion = "Cerveza Rubia Especial 650ml",
                    PrecioUnitario = 180.00m,
                    IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
                    UnidadMedida = UnidadMedidaType.Botella,
                    CodigoISC = "023", // Cerveza Ad-Valorem
                    EstaActivo = true
                }
            );
            await context.SaveChangesAsync();
        }

        // 4. Sucursal por Defecto
        // Orden determinista: la consulta de arranque no debe depender del orden físico de la tabla.
        var defaultEnterprise = await context.Enterprises.OrderBy(e => e.Id).FirstOrDefaultAsync();
        Sucursal? defaultSucursal = null;
        if (defaultEnterprise != null)
        {
            defaultSucursal = await context.Sucursales.FirstOrDefaultAsync(s => s.EnterpriseId == defaultEnterprise.Id);
            if (defaultSucursal == null)
            {
                defaultSucursal = new Sucursal
                {
                    EnterpriseId = defaultEnterprise.Id,
                    CodigoSucursal = "001",
                    Nombre = "Sucursal Principal - Casa Matriz",
                    Direccion = defaultEnterprise.Direccion,
                    Telefono = defaultEnterprise.Telefono,
                    Email = defaultEnterprise.Email,
                    CodigoProvincia = defaultEnterprise.CodigoProvincia,
                    CodigoMunicipio = defaultEnterprise.CodigoMunicipio,
                    EstaActiva = true
                };
                await context.Sucursales.AddAsync(defaultSucursal);
                await context.SaveChangesAsync();
            }
        }

        // 5. Proveedor Inicial
        if (!await context.Proveedores.AnyAsync())
        {
            await context.Proveedores.AddAsync(new Proveedor
            {
                RNC = "101000001",
                RazonSocial = "Distribuidora Nacional SRL",
                Telefono = "809-555-8888",
                Email = "ventas@distribuidora.com.do",
                Direccion = "Av. Luperón #25, Zona Industrial",
                EstaActivo = true
            });
            await context.SaveChangesAsync();
        }

        // 6. Sincronizar InventarioAlmacen para todos los productos existentes
        if (defaultSucursal != null)
        {
            var productos = await context.Productos.ToListAsync();
            foreach (var prod in productos)
            {
                var existeInventario = await context.InventariosAlmacen
                    .AnyAsync(ia => ia.ProductoId == prod.Id && ia.SucursalId == defaultSucursal.Id);

                if (!existeInventario)
                {
                    await context.InventariosAlmacen.AddAsync(new InventarioAlmacen
                    {
                        ProductoId = prod.Id,
                        SucursalId = defaultSucursal.Id,
                        StockActual = 50.00m,
                        StockMinimo = 5.00m
                    });
                }
            }
            await context.SaveChangesAsync();
        }

        // 7. Cuenta administradora inicial del sistema (seguridad)
        await SeedUsuarioInicialAsync(context, passwordHasher, opcionesAdmin, logger);
    }

    /// <summary>
    /// Crea la primera cuenta de sistema (SuperAdmin) únicamente si no existe ningún usuario.
    /// La contraseña proviene de configuración; si no cumple la política o no fue definida, se genera
    /// una aleatoria, se marca la cuenta para cambio obligatorio y se informa una sola vez por el log.
    /// </summary>
    private static async Task SeedUsuarioInicialAsync(
        POSDbContext context,
        IPasswordHasher passwordHasher,
        OpcionesAdminInicial opcionesAdmin,
        ILogger? logger)
    {
        if (await context.Usuarios.AnyAsync())
            return;

        var nombreUsuario = string.IsNullOrWhiteSpace(opcionesAdmin.Usuario)
            ? "admin"
            : opcionesAdmin.Usuario.Trim();

        var passwordConfigurada = opcionesAdmin.Password;
        var passwordValida = !string.IsNullOrWhiteSpace(passwordConfigurada)
            && PasswordPolicy.Validar(passwordConfigurada, nombreUsuario).EsValido;

        var passwordInicial = passwordValida ? passwordConfigurada! : PasswordPolicy.GenerarAleatoria();

        var admin = new Usuario
        {
            NombreUsuario = nombreUsuario,
            NombreCompleto = string.IsNullOrWhiteSpace(opcionesAdmin.NombreCompleto)
                ? "Administrador del Sistema"
                : opcionesAdmin.NombreCompleto.Trim(),
            Rol = RolUsuario.SuperAdmin,
            EstaActivo = true
        };

        admin.EstablecerPassword(passwordHasher.Hash(passwordInicial));

        // Una contraseña generada por el sistema debe cambiarse en el primer acceso.
        // (EstablecerPassword limpia la marca, por lo que se aplica después del hashing).
        admin.DebeCambiarPassword = !passwordValida;

        await context.Usuarios.AddAsync(admin);
        await context.SaveChangesAsync();

        if (!passwordValida)
        {
            logger?.LogWarning(
                "Cuenta inicial creada: usuario '{Usuario}' con contraseña temporal generada. " +
                "Defina Seguridad:AdminInicial:Password (o variable de entorno) para fijarla. " +
                "La cuenta está marcada para cambio obligatorio de contraseña en el primer acceso. Contraseña temporal: {PasswordTemporal}",
                nombreUsuario, passwordInicial);
        }
        else
        {
            logger?.LogInformation("Cuenta inicial '{Usuario}' creada con la contraseña definida en configuración.", nombreUsuario);
        }
    }
}
