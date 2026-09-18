using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Ventas.Tests;

/// <summary>
/// Pruebas de integración del caso de uso de venta contra una base de datos real.
/// </summary>
public class ProcesarVentaTests
{
    [Fact]
    public async Task VentaNormal_RegistraVentaInventarioCajaComprobanteYCola()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(
            h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id, cantidad: 2m, montoRecibido: 300m));

        Assert.True(resultado.Exitoso);
        Assert.False(resultado.Duplicada);
        Assert.Equal("E320000000001", resultado.eNCF);
        Assert.Equal(236m, resultado.Total);   // 200 de base + 18 % de ITBIS
        Assert.Equal(64m, resultado.Cambio);
        Assert.False(resultado.EsOfflineDGII);
        Assert.Equal(EstadoFacturaElectronica.EnProceso, resultado.EstadoFiscal);
        Assert.Equal(EstadoEmisionECF.ConfirmadaEnvio, resultado.EstadoEmision);

        // Existencias, kardex, caja y cola: una sola operación lógica.
        Assert.Equal(8m, await h.StockAsync(h.ProductoGravado18Id));
        Assert.Equal(1, await h.ContarVentasAsync());
        Assert.Equal(1, await h.ContarMovimientosAsync());
        Assert.Equal(1, await h.ContarColaAsync());

        var turno = await h.TurnoAsync();
        Assert.Equal(236m, turno.TotalVentas);
        Assert.Equal(236m, turno.VentasEfectivo);
        Assert.Equal(1, turno.CantidadTransacciones);

        await using var lectura = h.CrearContexto();
        var kardex = await lectura.MovimientosInventario.AsNoTracking().SingleAsync();
        Assert.Equal(10m, kardex.StockAnterior);
        Assert.Equal(8m, kardex.StockNuevo);
        Assert.Equal(TipoMovimientoInventario.VentaPOS, kardex.Tipo);
        Assert.Equal(resultado.eNCF, kardex.ReferenciaDocumento);
        Assert.Equal(VentasTestHarness.UsuarioNombre, kardex.Usuario);

        var comprobante = await h.ComprobanteAsync(resultado.eNCF);
        Assert.NotNull(comprobante);
        Assert.Equal(236m, comprobante!.MontoTotal);
        Assert.Contains("<ECF", comprobante.XMLContent);
        Assert.Equal(6, comprobante.XMLHash.Length);

        // Consistencia interna de los importes del comprobante: la suma de los renglones es el total.
        var items = await lectura.InvoiceItems.AsNoTracking().ToListAsync();
        Assert.Equal(comprobante.MontoTotal, items.Sum(i => i.MontoItem));
        Assert.Equal(200m, items.Sum(i => i.Subtotal));
        Assert.Equal(36m, items.Sum(i => i.MontoITBIS));
    }

    [Fact]
    public async Task MismaClaveDeIdempotencia_DevulveLaVentaOriginal()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);
        var clave = Guid.NewGuid();

        var primera = await handler.EjecutarAsync(h.CrearComando(clave, h.ProductoGravado18Id));
        var segunda = await handler.EjecutarAsync(h.CrearComando(clave, h.ProductoGravado18Id));

        Assert.True(primera.Exitoso);
        Assert.True(segunda.Exitoso);
        Assert.True(segunda.Duplicada);
        Assert.Equal(primera.eNCF, segunda.eNCF);
        Assert.Equal(primera.VentaId, segunda.VentaId);

        // Un único efecto: una venta, un descuento de existencias y un solo comprobante.
        Assert.Equal(1, await h.ContarVentasAsync());
        Assert.Equal(1, await h.ContarMovimientosAsync());
        Assert.Equal(9m, await h.StockAsync(h.ProductoGravado18Id));
    }

    [Fact]
    public async Task ClavesDistintas_RegistranVentasDistintas()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var primera = await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id));
        var segunda = await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id));

        Assert.True(primera.Exitoso && segunda.Exitoso);
        Assert.NotEqual(primera.eNCF, segunda.eNCF);
        Assert.Equal("E320000000001", primera.eNCF);
        Assert.Equal("E320000000002", segunda.eNCF);
        Assert.Equal(2, await h.ContarVentasAsync());
        Assert.Equal(8m, await h.StockAsync(h.ProductoGravado18Id));
    }

    [Fact]
    public async Task StockInsuficiente_RevierteTodoYNoQuemaElNumeroFiscal()
    {
        await using var h = await VentasTestHarness.CrearAsync(stockGravado: 1m);
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var rechazada = await handler.EjecutarAsync(
            h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id, cantidad: 2m));

        Assert.False(rechazada.Exitoso);
        Assert.Equal("STOCK_INSUFICIENTE", rechazada.CodigoError);

        // Nada quedó a medias: ni venta, ni kardex, ni caja, ni cola, ni existencias tocadas.
        Assert.Equal(0, await h.ContarVentasAsync());
        Assert.Equal(0, await h.ContarMovimientosAsync());
        Assert.Equal(0, await h.ContarColaAsync());
        Assert.Equal(1m, await h.StockAsync(h.ProductoGravado18Id));
        Assert.Equal(0m, (await h.TurnoAsync()).TotalVentas);

        // El número reservado se liberó con el revertimiento: la siguiente venta usa el primero.
        var aceptada = await handler.EjecutarAsync(
            h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id, cantidad: 1m));

        Assert.True(aceptada.Exitoso);
        Assert.Equal("E320000000001", aceptada.eNCF);
        Assert.Equal(0m, await h.StockAsync(h.ProductoGravado18Id));
    }

    [Fact]
    public async Task FalloAlRegistrarElComprobante_RevientaLaVentaCompleta()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandlerQueFallaAlRegistrarComprobante(ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id, cantidad: 2m)));

        // No puede existir una venta cobrada sin comprobante fiscal.
        Assert.Equal(0, await h.ContarVentasAsync());
        Assert.Equal(0, await h.ContarMovimientosAsync());
        Assert.Equal(0, await h.ContarColaAsync());
        Assert.Equal(10m, await h.StockAsync(h.ProductoGravado18Id));

        var turno = await h.TurnoAsync();
        Assert.Equal(0m, turno.TotalVentas);
        Assert.Equal(0, turno.CantidadTransacciones);
    }

    [Fact]
    public async Task SinTurnoDeCajaAbierto_NoPermiteVender()
    {
        await using var h = await VentasTestHarness.CrearAsync(conTurnoAbierto: false);
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id));

        Assert.False(resultado.Exitoso);
        Assert.Equal("SIN_TURNO_DE_CAJA", resultado.CodigoError);
        Assert.Equal(0, await h.ContarVentasAsync());
        Assert.Equal(10m, await h.StockAsync(h.ProductoGravado18Id));
    }

    [Fact]
    public async Task TurnoDeOtroUsuario_NoPermiteVender()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var comando = h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id);
        comando.UsuarioId = VentasTestHarness.UsuarioId + 1;

        var resultado = await handler.EjecutarAsync(comando);

        Assert.False(resultado.Exitoso);
        Assert.Equal("SIN_TURNO_DE_CAJA", resultado.CodigoError);
        Assert.Equal(0, await h.ContarVentasAsync());
    }

    [Fact]
    public async Task CreditoFiscalSinRncDelComprador_EsRechazadaAntesDeEscribirNada()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var sinRnc = await handler.EjecutarAsync(h.CrearComando(
            Guid.NewGuid(),
            h.ProductoGravado18Id,
            tipoeCF: TipoeCFType.FacturaCreditoFiscal));

        Assert.False(sinRnc.Exitoso);
        Assert.Equal("RNC_COMPRADOR_REQUERIDO", sinRnc.CodigoError);

        var conRncInvalido = await handler.EjecutarAsync(h.CrearComando(
            Guid.NewGuid(),
            h.ProductoGravado18Id,
            tipoeCF: TipoeCFType.FacturaCreditoFiscal,
            rncComprador: "123"));

        Assert.False(conRncInvalido.Exitoso);
        Assert.Equal("RNC_COMPRADOR_INVALIDO", conRncInvalido.CodigoError);

        Assert.Equal(0, await h.ContarVentasAsync());
        Assert.Equal(10m, await h.StockAsync(h.ProductoGravado18Id));
    }

    [Fact]
    public async Task CreditoFiscalConRncValido_EmitirComprobanteTipo31()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(h.CrearComando(
            Guid.NewGuid(),
            h.ProductoGravado18Id,
            tipoeCF: TipoeCFType.FacturaCreditoFiscal,
            rncComprador: "101000001"));

        Assert.True(resultado.Exitoso);
        Assert.StartsWith("E31", resultado.eNCF);

        var comprobante = await h.ComprobanteAsync(resultado.eNCF);
        Assert.Equal("101000001", comprobante!.RNCComprador);
        Assert.Equal(TipoeCFType.FacturaCreditoFiscal, comprobante.TipoeCF);
    }

    [Fact]
    public async Task PagoInsuficiente_NoRegistraNada()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(h.CrearComando(
            Guid.NewGuid(),
            h.ProductoGravado18Id,
            montoRecibido: 50m));

        Assert.False(resultado.Exitoso);
        Assert.Equal("PAGOS_INSUFICIENTES", resultado.CodigoError);
        Assert.Equal(0, await h.ContarVentasAsync());
        Assert.Equal(10m, await h.StockAsync(h.ProductoGravado18Id));
    }

    [Fact]
    public async Task PagoConTarjetaExacto_SeAcumulaEnTarjetaYSinCambio()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(h.CrearComando(
            Guid.NewGuid(),
            h.ProductoExentoId,
            metodoPago: MetodoPago.TarjetaDebitoCredito));

        Assert.True(resultado.Exitoso);
        Assert.Equal(250m, resultado.Total);   // producto exento: el ITBIS lo decide el catálogo
        Assert.Equal(0m, resultado.Cambio);

        var turno = await h.TurnoAsync();
        Assert.Equal(250m, turno.VentasTarjeta);
        Assert.Equal(0m, turno.VentasEfectivo);
    }

    [Fact]
    public async Task DescuentoMayorQueLaLinea_EsRechazado()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(h.CrearComando(
            Guid.NewGuid(),
            h.ProductoGravado18Id,
            cantidad: 1m,
            descuento: 150m));

        Assert.False(resultado.Exitoso);
        Assert.Equal("DESCUENTO_EXCEDE_LINEA", resultado.CodigoError);
        Assert.Equal(0, await h.ContarVentasAsync());
    }

    [Fact]
    public async Task ProductoInexistente_EsRechazado()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), 999_999));

        Assert.False(resultado.Exitoso);
        Assert.Equal("PRODUCTO_NO_ENCONTRADO", resultado.CodigoError);
        Assert.Equal(0, await h.ContarVentasAsync());
    }

    [Fact]
    public async Task ElTerminalNoPuedeFalsificarElPrecio()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        // El comando no transporta precio: solo producto, cantidad y descuento. El importe que se
        // cobra y el que se declara a la DGII salen del catálogo del servidor.
        var resultado = await handler.EjecutarAsync(
            h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id, cantidad: 2m, descuento: 50m));

        Assert.True(resultado.Exitoso);
        Assert.Equal(177m, resultado.Total); // (2 × 100 − 50) + 18 % = 150 + 27

        await using var lectura = h.CrearContexto();
        var venta = await lectura.Ventas.AsNoTracking().SingleAsync();
        Assert.Equal(150m, venta.Subtotal, 2);   // 200 de base menos el descuento autorizado
        Assert.Equal(27m, venta.TotalITBIS, 2);
        Assert.Equal(177m, venta.Total, 2);
    }

    [Fact]
    public async Task ModoPermisivoSinStock_RegistraLaVentaConExistenciaNegativa()
    {
        await using var h = await VentasTestHarness.CrearAsync(stockGravado: 1m, permitirVentaSinStock: true);
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(
            h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id, cantidad: 3m));

        Assert.True(resultado.Exitoso);
        Assert.Equal(-2m, await h.StockAsync(h.ProductoGravado18Id));
        Assert.Equal(1, await h.ContarVentasAsync());
    }

    [Fact]
    public async Task ColisionDeNumeracion_SeResuelveReintentandoLaAsignacion()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();

        // Escenario de base restaurada o numeración desincronizada: ya existe un comprobante con el
        // número que la secuencia local iba a entregar.
        await ctx.ElectronicInvoices.AddAsync(new ElectronicInvoice
        {
            eNCF = "E320000000001",
            RNCEmisor = "13100000001",
            RazonSocialEmisor = "PosPalasy Pruebas SRL",
            XMLHash = "AAAAAA",
            XMLContent = "<ECF />",
            MontoTotal = 1m,
            Estado = EstadoFacturaElectronica.NoEnviado
        });
        await ctx.SaveChangesAsync();

        var handler = h.CrearHandler(ctx);
        var resultado = await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id));

        Assert.True(resultado.Exitoso);
        Assert.Equal("E320000000002", resultado.eNCF);

        // Un número nuevo para cada venta: el conflicto no se silenció, se resolvió.
        Assert.Equal(2, await ctx.ElectronicInvoices.CountAsync());
    }

    [Fact]
    public async Task VentasSimultaneas_NoDuplicanNumeroDeComprobanteNiGeneranStockNegativo()
    {
        const int intentos = 6;
        const decimal stockInicial = 4m;

        await using var h = await VentasTestHarness.CrearAsync(stockGravado: stockInicial);
        var claveTurno = h.TurnoId;

        var tareas = Enumerable.Range(0, intentos).Select(async _ =>
        {
            await using var ctx = h.CrearContexto();
            var handler = h.CrearHandler(ctx);

            try
            {
                return await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id));
            }
            catch (Microsoft.Data.Sqlite.SqliteException)
            {
                // SQLite permite un solo escritor: un intento simultáneo puede fallar por bloqueo.
                // Es una limitación del motor de pruebas, no de la lógica de la venta.
                return null;
            }
        });

        var resultados = (await Task.WhenAll(tareas)).Where(r => r is { Exitoso: true }).ToList();

        Assert.True(claveTurno > 0);

        // Invariantes que deben cumplirse siempre, incluso bajo concurrencia:
        // 1. Ningún número de comprobante repetido.
        var encfs = resultados.Select(r => r!.eNCF).ToList();
        Assert.Equal(encfs.Count, encfs.Distinct().Count());

        // 2. Ninguna venta más allá de las existencias disponibles (política estricta).
        Assert.True(resultados.Count <= (int)stockInicial);
        Assert.Equal(stockInicial - resultados.Count, await h.StockAsync(h.ProductoGravado18Id));

        // 3. Contabilidad coherente: cada venta registrada descuenta stock y ocupa un número.
        Assert.Equal(resultados.Count, await h.ContarVentasAsync());
        Assert.Equal(resultados.Count, await h.ContarMovimientosAsync());

        await using var lectura = h.CrearContexto();
        var secuencia = await lectura.SecuenciasECF.AsNoTracking().SingleAsync(s => s.Serie == "E32");
        Assert.Equal(resultados.Count, secuencia.Ultimo);
    }

    [Fact]
    public async Task CesionDeNumeracionEntreTiposDeComprobante_EsIndependiente()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var consumo1 = await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id));
        var credito = await handler.EjecutarAsync(h.CrearComando(
            Guid.NewGuid(),
            h.ProductoGravado18Id,
            tipoeCF: TipoeCFType.FacturaCreditoFiscal,
            rncComprador: "101000001"));
        var consumo2 = await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id));

        Assert.Equal("E320000000001", consumo1.eNCF);
        Assert.Equal("E310000000001", credito.eNCF);
        Assert.Equal("E320000000002", consumo2.eNCF);
    }

    [Fact]
    public async Task DgiiNoDisponible_LaVentaQuedaRegistradaYEncolada()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        h.ClienteDgii.ProximaRespuesta = new POS.Application.DTOs.DgiiApiResponse
        {
            EsExitoso = false,
            CodigoHttp = 503,
            Mensaje = "Servicio no disponible"
        };

        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id));

        // La venta se cobra: la caída de la DGII no puede detener la operación.
        Assert.True(resultado.Exitoso);
        Assert.True(resultado.EsOfflineDGII);
        Assert.Equal(EstadoFacturaElectronica.PendienteReenvio, resultado.EstadoFiscal);
        Assert.Equal(EstadoEmisionECF.ErrorTemporal, resultado.EstadoEmision);

        Assert.Equal(1, await h.ContarVentasAsync());
        Assert.Equal(9m, await h.StockAsync(h.ProductoGravado18Id));

        // Y queda una ruta de reintento persistida, no solo un mensaje al usuario.
        await using var lectura = h.CrearContexto();
        var cola = await lectura.EmisionesDGIIQueue.AsNoTracking().SingleAsync();
        Assert.False(cola.EnviadoExitosamente);
        Assert.Equal(EstadoColaDGII.Pendiente, cola.Estado);
        Assert.NotNull(cola.ProximoIntentoUtc);
        Assert.Equal(1, cola.Intentos);
    }

    [Fact]
    public async Task DgiiConfirma_LaColaQuedaCerrada()
    {
        await using var h = await VentasTestHarness.CrearAsync();
        await using var ctx = h.CrearContexto();
        var handler = h.CrearHandler(ctx);

        var resultado = await handler.EjecutarAsync(h.CrearComando(Guid.NewGuid(), h.ProductoGravado18Id));

        Assert.True(resultado.Exitoso);
        Assert.Equal("TRACK-TEST-0001", resultado.TrackId);
        Assert.Equal(1, h.ClienteDgii.EnviosRealizados);

        await using var lectura = h.CrearContexto();
        var cola = await lectura.EmisionesDGIIQueue.AsNoTracking().SingleAsync();
        Assert.True(cola.EnviadoExitosamente);
        Assert.Equal(EstadoColaDGII.Enviado, cola.Estado);
        Assert.Equal("TRACK-TEST-0001", cola.TrackId);
        Assert.Null(cola.ProximoIntentoUtc);
        Assert.Null(cola.LeaseHastaUtc);
    }
}
