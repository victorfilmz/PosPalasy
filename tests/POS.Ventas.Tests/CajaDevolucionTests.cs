using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Application.CasosDeUso.Caja;
using POS.Application.CasosDeUso.Ventas;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;
using POS.Infrastructure.Persistence.Repositories;
using Xunit;

namespace POS.Ventas.Tests;

/// <summary>
/// Pruebas de integración del módulo de caja (Fase 4): devoluciones totales y parciales, tope de
/// reembolso, idempotencia, integridad ante fallo, trazabilidad de movimientos y arqueo.
/// </summary>
/// <remarks>
/// Todas se ejecutan contra SQLite real con el caso de uso real: el valor de estas pruebas es la
/// verificación del estado final en la base de datos, no del objeto devuelto en memoria.
/// </remarks>
public class CajaDevolucionTests : IAsyncDisposable
{
    private readonly CajaTestHarness _harness = CajaTestHarness.CrearAsync().GetAwaiter().GetResult();

    // ------------------------------------------------------------------ devolución total

    [Fact]
    public async Task DevolucionTotal_ReembolsaPorLasFormasDePagoReingresaStockYDejaCajaExacta()
    {
        // Venta de 2 unidades a RD$ 50 (base 100, ITBIS 18, total 118) cobrada en efectivo (120).
        var venta = await _harness.SembrarVentaAsync(cantidad: 2m, montoRecibido: 120m);
        var stockTrasVenta = await _harness.StockAsync();

        var resultado = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta.Id,
                Motivo = "Producto defectuoso",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre
            });

        Assert.True(resultado.Exitoso, resultado.Mensaje);
        Assert.Equal(118m, resultado.TotalDevuelto);   // base 100 + ITBIS 18
        Assert.Equal(118m, resultado.EfectivoDevuelto); // todo en efectivo: sale de la gaveta
        Assert.Equal(0m, resultado.TarjetaDevuelto);
        Assert.Equal(2m, resultado.StockReingresado);

        // Inventario: vuelve exactamente al nivel previo a la venta.
        Assert.Equal(stockTrasVenta + 2m, await _harness.StockAsync());

        // Kardex: reingreso con tipo DevolucionVenta y referencia al documento.
        Assert.Equal(1, await _harness.ContarMovimientosInventarioAsync(TipoMovimientoInventario.DevolucionVenta));

        // Caja: un solo movimiento de salida, con referencia a la venta y usuario responsable.
        var movimiento = await _harness.MovimientoDevolucionAsync();
        Assert.Equal(TipoMovimientoCaja.Salida, movimiento.Tipo);
        Assert.Equal(118m, movimiento.Monto);
        Assert.Equal(venta.Id, movimiento.VentaId);
        Assert.Equal(CajaTestHarness.UsuarioNombre, movimiento.Usuario);
        Assert.Contains("Devolución", movimiento.Concepto);

        // Turno: acumulado de devoluciones y salidas exactos; ventas no reescritas.
        var turno = await _harness.TurnoAsync();
        Assert.Equal(118m, turno.TotalDevoluciones);
        Assert.Equal(118m, turno.TotalSalidasEfectivo);
        Assert.Equal(118m, turno.VentasEfectivo);   // la venta sigue contabilizada
        Assert.Equal(118m, turno.TotalVentas);
    }

    // ------------------------------------------------------------------ devolución parcial

    [Fact]
    public async Task DevolucionParcial_DeUnaDeDosUnidades_ReembolsaLaParteProporcional()
    {
        var venta = await _harness.SembrarVentaAsync(cantidad: 2m, montoRecibido: 240m);
        var linea = venta.Items.Single();

        var resultado = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta.Id,
                Motivo = "Devolución parcial",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre,
                CantidadesPorLinea = new Dictionary<int, decimal> { [linea.Id] = 1m }
            });

        Assert.True(resultado.Exitoso, resultado.Mensaje);
        Assert.Equal(59m, resultado.TotalDevuelto);      // 50 base + 9 ITBIS
        Assert.Equal(1m, resultado.StockReingresado);
        Assert.Equal(59m, (await _harness.TurnoAsync()).TotalDevoluciones);

        // Queda pendiente de devolver 1 unidad: una segunda devolución total devuelve solo eso.
        var segunda = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta.Id,
                Motivo = "Segunda devolución",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre
            });

        Assert.True(segunda.Exitoso, segunda.Mensaje);
        Assert.Equal(59m, segunda.TotalDevuelto);        // la unidad restante
        Assert.Equal(118m, (await _harness.TurnoAsync()).TotalDevoluciones);
    }

    // ------------------------------------------------------------------ tope de reembolso

    [Fact]
    public async Task DevolucionQueExcedeLoVendido_EsRechazadaYSinEfectos()
    {
        var venta = await _harness.SembrarVentaAsync(cantidad: 2m, montoRecibido: 240m);
        var linea = venta.Items.Single();
        var stockAntes = await _harness.StockAsync();

        var resultado = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta.Id,
                Motivo = "Intento de sobre-devolución",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre,
                CantidadesPorLinea = new Dictionary<int, decimal> { [linea.Id] = 5m }
            });

        Assert.False(resultado.Exitoso);
        Assert.Equal("CANTIDAD_EXCEDE_VENDIDA", resultado.CodigoError);

        // Estado intacto: ni caja, ni inventario, ni devoluciones.
        Assert.Equal(0, await _harness.ContarDevolucionesAsync());
        Assert.Equal(0, await _harness.ContarMovimientosCajaAsync());
        Assert.Equal(stockAntes, await _harness.StockAsync());
        Assert.Equal(0m, (await _harness.TurnoAsync()).TotalDevoluciones);
    }

    [Fact]
    public async Task SegundaDevolucionTotal_NoPuedeExcederLoPendiente()
    {
        var venta = await _harness.SembrarVentaAsync(cantidad: 2m, montoRecibido: 240m);

        var primera = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta.Id,
                Motivo = "Primera total",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre
            });
        Assert.True(primera.Exitoso);

        var segunda = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta.Id,
                Motivo = "Intento repetido total",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre
            });

        Assert.False(segunda.Exitoso);
        Assert.Equal("DEVOLUCION_VACIA", segunda.CodigoError);
        Assert.Equal(1, await _harness.ContarDevolucionesAsync());
    }

    // ------------------------------------------------------------------ reembolso mixto

    [Fact]
    public async Task DevolucionConPagoMixto_ReembolsaPorCadaMedioYEfectivoDelResto()
    {
        // Venta de 2 uds (total 118) cobrada con 68 de tarjeta + 50 de efectivo.
        var venta = await _harness.SembrarVentaAsync(
            cantidad: 2m,
            pagos: new List<PagoVentaCommand>
            {
                new() { MetodoPago = MetodoPago.TarjetaDebitoCredito, Monto = 68m },
                new() { MetodoPago = MetodoPago.Efectivo, Monto = 50m }
            });

        var resultado = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta.Id,
                Motivo = "Reembolso mixto",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre
            });

        Assert.True(resultado.Exitoso, resultado.Mensaje);
        Assert.Equal(118m, resultado.TotalDevuelto);
        Assert.Equal(68m, resultado.TarjetaDevuelto);        // hasta lo pagado con tarjeta
        Assert.Equal(50m, resultado.EfectivoDevuelto);       // el resto sale de la gaveta
        Assert.Equal(0m, resultado.TransferenciaDevuelto);

        // Solo el efectivo toca la caja física; el turno acumula la devolución total.
        Assert.Equal(50m, (await _harness.MovimientoDevolucionAsync()).Monto);
        Assert.Equal(118m, (await _harness.TurnoAsync()).TotalDevoluciones);
    }

    // ------------------------------------------------------------------ idempotencia

    [Fact]
    public async Task DobleSolicitudConLaMismaClave_RegistraUnaSolaDevolucion()
    {
        var venta = await _harness.SembrarVentaAsync(cantidad: 2m, montoRecibido: 240m);
        var clave = Guid.NewGuid();
        var handler = _harness.CrearDevolucionHandler(_harness.CrearContexto());

        var comando = new RegistrarDevolucionCommand
        {
            ClaveIdempotencia = clave,
            VentaId = venta.Id,
            Motivo = "Doble clic",
            UsuarioId = CajaTestHarness.UsuarioId,
            UsuarioNombre = CajaTestHarness.UsuarioNombre
        };

        var primera = await handler.EjecutarAsync(comando);
        var segunda = await handler.EjecutarAsync(comando);

        Assert.True(primera.Exitoso);
        Assert.True(segunda.Exitoso);
        Assert.True(segunda.Duplicada);
        Assert.Equal(primera.DevolucionId, segunda.DevolucionId);
        Assert.Equal(1, await _harness.ContarDevolucionesAsync());
        Assert.Equal(1, await _harness.ContarMovimientosCajaAsync());
        Assert.Equal(118m, (await _harness.TurnoAsync()).TotalDevoluciones);
    }

    // ------------------------------------------------------------------ integridad ante fallo

    [Fact]
    public async Task DevolucionConTurnoCerradoSEReviertePorCompleto()
    {
        var venta = await _harness.SembrarVentaAsync(cantidad: 2m, montoRecibido: 240m);
        var stockTrasVenta = await _harness.StockAsync();

        // Se cierra el turno antes de intentar la devolución.
        await using (var ctx = _harness.CrearContexto())
        {
            var turno = await ctx.CajaTurnos.FirstAsync(t => t.Id == _harness.TurnoId);
            turno.Estado = TurnoCajaEstado.Cerrado;
            await ctx.SaveChangesAsync();
        }

        var resultado = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta.Id,
                Motivo = "Devolución sin turno",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre
            });

        Assert.False(resultado.Exitoso);
        Assert.Equal("SIN_TURNO_DE_CAJA", resultado.CodigoError);

        // Nada se escribió: ni devolución, ni kardex, ni caja.
        Assert.Equal(0, await _harness.ContarDevolucionesAsync());
        Assert.Equal(0, await _harness.ContarMovimientosCajaAsync());
        Assert.Equal(0, await _harness.ContarMovimientosInventarioAsync(TipoMovimientoInventario.DevolucionVenta));
        Assert.Equal(stockTrasVenta, await _harness.StockAsync());
    }

    [Fact]
    public async Task DevolucionDeVentaSinComprobante_EsRechazada()
    {
        // Venta "fósil" sin comprobante: se inserta directamente con la forma del modelo.
        await using (var ctx = _harness.CrearContexto())
        {
            var ventaFosil = new Venta
            {
                NumeroFacturaInterna = "FAC-SIN-ECF",
                EnterpriseId = _harness.EmpresaId,
                SucursalId = _harness.SucursalId,
                CajaTurnoId = _harness.TurnoId,
                Fecha = DateTime.UtcNow,
                TipoPago = TipoPago.Contado,
                MetodoPago = MetodoPago.Efectivo,
                Total = 100m,
                Subtotal = 100m
            };
            ventaFosil.Items.Add(new VentaItem
            {
                ProductoId = _harness.ProductoId,
                Descripcion = "Producto de Caja",
                Cantidad = 2m,
                PrecioUnitario = 50m,
                Subtotal = 100m,
                Total = 118m
            });

            await ctx.Ventas.AddAsync(ventaFosil);
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _harness.CrearContexto();
        var venta = await ctx2.Ventas.AsNoTracking().FirstAsync(v => v.NumeroFacturaInterna == "FAC-SIN-ECF");

        var resultado = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta.Id,
                Motivo = "Venta fósil",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre
            });

        Assert.False(resultado.Exitoso);
        Assert.Equal("VENTA_SIN_COMPROBANTE", resultado.CodigoError);
        Assert.Equal(0, await _harness.ContarDevolucionesAsync());
    }

    // ------------------------------------------------------------------ arqueo

    [Fact]
    public async Task ArqueoDelTurno_CuadraConVentasYDevoluciones()
    {
        var venta1 = await _harness.SembrarVentaAsync(cantidad: 2m, montoRecibido: 240m);   // total 118
        var venta2 = await _harness.SembrarVentaAsync(cantidad: 1m, montoRecibido: 60m);    // total 59

        var devolucion = await _harness.CrearDevolucionHandler(_harness.CrearContexto())
            .EjecutarAsync(new RegistrarDevolucionCommand
            {
                ClaveIdempotencia = Guid.NewGuid(),
                VentaId = venta1.Id,
                Motivo = "Arqueo",
                UsuarioId = CajaTestHarness.UsuarioId,
                UsuarioNombre = CajaTestHarness.UsuarioNombre
            });
        Assert.True(devolucion.Exitoso, devolucion.Mensaje);

        // Arqueo: el efectivo esperado de la base de datos ya descuenta la devolución.
        var turno = await _harness.TurnoAsync();
        var esperado = turno.MontoInicial + turno.VentasEfectivo
            + turno.TotalEntradasEfectivo - turno.TotalSalidasEfectivo;

        Assert.Equal(5000m + 118m + 59m - 118m, esperado);
        Assert.Equal(2, turno.CantidadTransacciones);
        Assert.Equal(177m, turno.TotalVentas);
        Assert.Equal(118m, turno.TotalDevoluciones);

        // Cierre atómico con el efectivo contado real: la diferencia queda registrada.
        await using var ctx = _harness.CrearContexto();
        var filas = await new CajaTurnoRepository(ctx).CerrarTurnoAsync(
            _harness.TurnoId, montoRealCierre: esperado, observaciones: "Arqueo exacto");
        Assert.Equal(1, filas);

        var cerrado = await ctx.CajaTurnos.AsNoTracking().FirstAsync(t => t.Id == _harness.TurnoId);
        Assert.Equal(TurnoCajaEstado.Cerrado, cerrado.Estado);
        Assert.Equal(0m, cerrado.Diferencia);
    }

    [Fact]
    public async Task CierreAtomico_SoloElPrimerCierreAlteraElArqueo()
    {
        var venta = await _harness.SembrarVentaAsync(cantidad: 1m, montoRecibido: 60m);

        await using var ctx = _harness.CrearContexto();
        var repo = new CajaTurnoRepository(ctx);

        Assert.Equal(1, await repo.CerrarTurnoAsync(_harness.TurnoId, 5000m + 59m, "primer cierre"));

        // Un segundo intento (doble clic o cierre sobre turno ya cerrado) no altera nada.
        Assert.Equal(0, await repo.CerrarTurnoAsync(_harness.TurnoId, 9999m, "segundo intento"));

        var cerrado = await ctx.CajaTurnos.AsNoTracking().FirstAsync(t => t.Id == _harness.TurnoId);
        Assert.Equal(5000m + 59m, cerrado.MontoRealCierre);
        Assert.Equal(0m, cerrado.Diferencia);
        Assert.Equal("primer cierre", cerrado.Observaciones);
    }

    // ------------------------------------------------------------------ concurrencia

    [Fact]
    public async Task DevolucionesConcurrentesSobreLaMismaVenta_NuncaExcedenLoPagado()
    {
        // Venta de 59 (1 ud). Dos devoluciones totales con claves distintas parten a la vez: solo
        // una puede reembolsar; la otra debe esperar el ancla de la fila y ver el tope consumido.
        var venta = await _harness.SembrarVentaAsync(cantidad: 1m, montoRecibido: 100m);

        var resultados = await EjecutarEnParaleloAsync(new[] { "Concurrente A", "Concurrente B" }, _ => venta);

        Assert.Equal(1, resultados.Count(r => r.Exitoso));
        Assert.Equal(1, resultados.Count(r => !r.Exitoso && r.CodigoError == "DEVOLUCION_VACIA"));
        Assert.Equal(59m, resultados.Where(r => r.Exitoso).Sum(r => r.TotalDevuelto));
        Assert.Equal(1, await _harness.ContarDevolucionesAsync());
        Assert.Equal(59m, (await _harness.TurnoAsync()).TotalDevoluciones);
    }

    [Fact]
    public async Task ArqueoBajoConcurrencia_TodasLasDevolucionesAcumulanYQuedanReferenciadas()
    {
        // Cinco ventas del mismo turno y cinco devoluciones simultáneas: el acumulado del turno y
        // el arqueo deben reflejar las cinco, sin movimientos perdidos ni solapados.
        var ventas = new List<Venta>();
        for (var i = 0; i < 5; i++)
            ventas.Add(await _harness.SembrarVentaAsync(cantidad: 1m, montoRecibido: 60m));

        var resultados = await EjecutarEnParaleloAsync(
            Enumerable.Range(0, 5).Select(i => $"Devolución {i}"),
            indice => ventas[indice]);

        Assert.All(resultados, r => Assert.True(r.Exitoso, r.Mensaje));
        Assert.Equal(5, await _harness.ContarDevolucionesAsync());
        Assert.Equal(5, await _harness.ContarMovimientosCajaAsync());

        var turno = await _harness.TurnoAsync();
        Assert.Equal(5m * 59m, turno.TotalDevoluciones);

        // Trazabilidad: cada movimiento de caja apunta a SU venta, sin mezclas.
        await using var ctx = _harness.CrearContexto();
        var referencias = await ctx.MovimientosCaja
            .Where(m => m.VentaId != null)
            .Select(m => m.VentaId!.Value)
            .ToListAsync();
        Assert.Equal(ventas.Select(v => v.Id).ToHashSet(), referencias.ToHashSet());
    }

    /// <summary>Lanza N devoluciones simultáneas (claves distintas) sobre las ventas del selector.</summary>
    private async Task<RegistrarDevolucionResult[]> EjecutarEnParaleloAsync(
        IEnumerable<string> motivos,
        Func<int, Venta> ventaDeIndice)
    {
        var motivosLista = motivos.ToList();
        var barrera = new Barrier(motivosLista.Count);

        var tareas = motivosLista.Select((motivo, indice) => Task.Run(async () =>
        {
            barrera.SignalAndWait();
            return await _harness.CrearDevolucionHandler(_harness.CrearContexto())
                .EjecutarAsync(new RegistrarDevolucionCommand
                {
                    ClaveIdempotencia = Guid.NewGuid(),
                    VentaId = ventaDeIndice(indice).Id,
                    Motivo = motivo,
                    UsuarioId = CajaTestHarness.UsuarioId,
                    UsuarioNombre = CajaTestHarness.UsuarioNombre
                });
        })).ToArray();

        return await Task.WhenAll(tareas);
    }

    public async ValueTask DisposeAsync() => await _harness.DisposeAsync();
}
