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
        var defaultEnterprise = await context.Enterprises.FirstOrDefaultAsync();
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
