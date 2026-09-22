using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using POS.Application.CasosDeUso.Caja;
using POS.Application.CasosDeUso.Facturacion;
using POS.Application.DTOs;
using POS.Application.Services;
using POS.Domain.Enums;
using POS.Domain.Types;
using POS.Infrastructure.DGII;
using POS.Infrastructure.Persistence;
using POS.Infrastructure.Persistence.Repositories;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;
using Xunit;

namespace POS.Ventas.Tests;

/// <summary>
/// Pruebas de integración de la emisión de la Nota de Crédito e-CF 34 desde una devolución
/// (FASE 5, sub-fase 5.4).
/// </summary>
/// <remarks>
/// Ejercitan el caso de uso completo con SQLite real y los mismos repositorios de producción:
/// venta real (caso de uso), devolución real (FASE 4) y la nota construida, validada contra el
/// XSD 34 oficial, registrada y encolada en UNA transacción. La transmisión corre en simulador.
/// </remarks>
public class NotaCreditoDevolucionIntegrationTests
{
    /// <summary>Sembrado de devolución total (todo el stock y monto de la venta).</summary>
    private static async Task<RegistrarDevolucionResult> DevolucionTotalAsync(
        CajaTestHarness harness, Domain.Entities.Venta venta)
    {
        await using var ctx = harness.CrearContexto();
        var handler = harness.CrearDevolucionHandler(ctx);

        var resultado = await handler.EjecutarAsync(new RegistrarDevolucionCommand
        {
            ClaveIdempotencia = Guid.NewGuid(),
            VentaId = venta.Id,
            Motivo = "Producto defectuoso",
            UsuarioId = CajaTestHarness.UsuarioId,
            UsuarioNombre = CajaTestHarness.UsuarioNombre
        });

        Assert.True(resultado.Exitoso, resultado.Mensaje);
        return resultado;
    }

    private static EmitirNotaCreditoDevolucionHandler CrearNotaHandler(POSDbContext contexto)
    {
        // Servicio real en modo simulador: la nota se valida contra el XSD 34 oficial dentro de
        // PrepararYRegistrarAsync (un fallo de esquema revierte la transacción completa).
        var servicio = new DgiiElectronicInvoiceService(
            new XmlSerializer(),
            new XmlValidator(),
            new HashGenerator(),
            new ClienteDgiiFalso(),
            new InvoiceRepository(contexto),
            new CommonRepositories(contexto),
            new EmisionDGIIQueueRepository(contexto),
            NullLogger<DgiiElectronicInvoiceService>.Instance,
            dgiiConfig: new DgiiConfig { ModoSimulador = true },
            codigoSeguridad: new GeneradorCodigoSeguridad(),
            proveedorCertificado: new ProveedorCertificadoDigital(
                Path.Combine(Path.GetTempPath(), "pospalasy-sin-certificado", "emisor.pfx"),
                password: null),
            firmador: new FirmadorComprobanteECF(new XmlDigitalSigner()));

        return new EmitirNotaCreditoDevolucionHandler(
            new DevolucionRepository(contexto),
            new CommonRepositories(contexto),
            new SecuenciaECFRepository(contexto, new SecuenciaLibreRepository(contexto)),
            new InvoiceRepository(contexto),
            new EmisionDGIIQueueRepository(contexto),
            servicio,
            new TaxCalculator(),
            new UnidadDeTrabajo(contexto));
    }

    [Fact]
    public async Task DevolucionTotal_GeneraNota34Valida_ConReferenciaAlOriginal_YEncolada()
    {
        await using var harness = await CajaTestHarness.CrearAsync();
        var venta = await harness.SembrarVentaAsync(cantidad: 2m);
        var devolucion = await DevolucionTotalAsync(harness, venta);

        await using var ctx = harness.CrearContexto();
        var handler = CrearNotaHandler(ctx);
        var resultado = await handler.EjecutarAsync(new NotaCreditoDevolucionCommand
        {
            DevolucionId = devolucion.DevolucionId,
            UsuarioNombre = CajaTestHarness.UsuarioNombre
        });

        Assert.True(resultado.Exitoso, resultado.Mensaje);
        Assert.False(resultado.Duplicada);

        // Serie e-NCF de nota de crédito y referencia al comprobante modificado.
        Assert.StartsWith("E34", resultado.eNCF);
        Assert.Equal(venta.ElectronicInvoice!.eNCF, resultado.eNCFModificado);
        Assert.Equal(devolucion.TotalDevuelto, resultado.TotalNota);
        Assert.Equal(devolucion.TotalDevuelto, venta.Total);           // total = nota total = venta total

        // La nota persistida es un e-CF 34 con la InformacionReferencia completa del XSD.
        var nota = await ctx.ElectronicInvoices.AsNoTracking()
            .SingleAsync(i => i.DevolucionId == devolucion.DevolucionId);
        Assert.Equal(TipoeCFType.NotaCredito, nota.TipoeCF);
        Assert.Equal(venta.ElectronicInvoice.eNCF, nota.eNCFModificado);
        Assert.Equal(venta.ElectronicInvoice.FechaEmision, nota.FechaNCFModificado);
        Assert.Equal(3, nota.CodigoModificacion);                       // corrige montos
        Assert.Equal(venta.Total, nota.MontoTotal);
        Assert.Contains("<TipoeCF>34</TipoeCF>", nota.XMLContent);
        Assert.Contains("<IndicadorNotaCredito>0</IndicadorNotaCredito>", nota.XMLContent);
        Assert.Contains("<InformacionReferencia>", nota.XMLContent);
        Assert.Contains($"<NCFModificado>{venta.ElectronicInvoice.eNCF}</NCFModificado>", nota.XMLContent);
        Assert.Contains($"<RazonModificacion>Devolución de venta: Producto defectuoso</RazonModificacion>", nota.XMLContent);

        // Outbox: la ruta de transmisión nació en la misma transacción que el documento.
        var enCola = await ctx.EmisionesDGIIQueue.AsNoTracking()
            .SingleOrDefaultAsync(q => q.FacturaId == nota.Id);
        Assert.NotNull(enCola);
        Assert.Equal(nota.eNCF, enCola!.eNCF);

        // La devolución quedó saldada fiscalmente.
        var devolucionPersistida = await ctx.Devoluciones.AsNoTracking()
            .SingleAsync(d => d.Id == devolucion.DevolucionId);
        Assert.False(devolucionPersistida.RequiereComprobanteFiscal);
    }

    [Fact]
    public async Task DevolucionParcial_NotaPorElMontoDevuelto_YReintentoEsIdempotente()
    {
        await using var harness = await CajaTestHarness.CrearAsync();
        var venta = await harness.SembrarVentaAsync(cantidad: 3m);

        // Devolución parcial: 1 de las 3 unidades (base 50.00 + ITBIS 9.00 = 59.00).
        await using var ctxDevolucion = harness.CrearContexto();
        var handlerDevolucion = harness.CrearDevolucionHandler(ctxDevolucion);
        var devolucion = await handlerDevolucion.EjecutarAsync(new RegistrarDevolucionCommand
        {
            ClaveIdempotencia = Guid.NewGuid(),
            VentaId = venta.Id,
            CantidadesPorLinea = new Dictionary<int, decimal>
            {
                [venta.Items.First().Id] = 1m
            },
            Motivo = "Devolución parcial de prueba",
            UsuarioId = CajaTestHarness.UsuarioId,
            UsuarioNombre = CajaTestHarness.UsuarioNombre
        });
        Assert.True(devolucion.Exitoso, devolucion.Mensaje);
        Assert.Equal(59.00m, devolucion.TotalDevuelto);

        var handler = CrearNotaHandler(harness.CrearContexto());
        var primera = await handler.EjecutarAsync(new NotaCreditoDevolucionCommand
        {
            DevolucionId = devolucion.DevolucionId,
            UsuarioNombre = CajaTestHarness.UsuarioNombre
        });

        Assert.True(primera.Exitoso, primera.Mensaje);
        Assert.False(primera.Duplicada);
        Assert.Equal(59.00m, primera.TotalNota);
        Assert.StartsWith("E34", primera.eNCF);

        // Reintento (doble clic, doble POST, otro turno): UNA devolución tiene UNA nota.
        var segundo = CrearNotaHandler(harness.CrearContexto());
        var reintento = await segundo.EjecutarAsync(new NotaCreditoDevolucionCommand
        {
            DevolucionId = devolucion.DevolucionId,
            UsuarioNombre = "otro.supervisor"
        });

        Assert.True(reintento.Exitoso, reintento.Mensaje);
        Assert.True(reintento.Duplicada);
        Assert.Equal(primera.eNCF, reintento.eNCF);
        Assert.Equal(primera.ElectronicInvoiceId, reintento.ElectronicInvoiceId);

        // Ni una segunda nota ni una segunda fila de cola para la misma devolución.
        await using var verificacion = harness.CrearContexto();
        var notas = await verificacion.ElectronicInvoices.AsNoTracking()
            .Where(i => i.DevolucionId == devolucion.DevolucionId)
            .ToListAsync();
        var nota = Assert.Single(notas);
        Assert.Equal(59.00m, nota.MontoTotal);

        var enCola = await verificacion.EmisionesDGIIQueue.AsNoTracking()
            .CountAsync(q => q.FacturaId == nota.Id);
        Assert.Equal(1, enCola);
    }
}
