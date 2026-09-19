using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Application.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;
using POS.Infrastructure.Persistence;
using Xunit;

namespace POS.UI.SecurityTests;

/// <summary>
/// Pruebas de extremo a extremo del módulo de caja (Fase 4) por el pipeline HTTP real:
/// autorización de la devolución (Supervision), titularidad del turno en el cierre y
/// atomicidad del arqueo frente a un doble envío del formulario.
/// </summary>
public class CajaEndToEndTests : IClassFixture<PosAppFactory>
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private readonly PosAppFactory _app;

    public CajaEndToEndTests(PosAppFactory app) => _app = app;

    // ------------------------------------------------------------------ devolución

    [Fact]
    public async Task DevolucionPorHttp_SupervisorRegistra_EfectivoSaleReferenciadoALaVenta()
    {
        var productoId = await SembrarProductoAsync(precio: 100m, stock: 5m);
        var sesion = await CrearSesionAsync(RolUsuario.Supervisor, abrirTurno: true);

        var venta = await VenderConTarjetaAsync(sesion, productoId);

        var respuesta = await PostFormularioAsync(sesion, "/Caja/Devolucion", new Dictionary<string, string>
        {
            ["VentaId"] = venta.Id.ToString(),
            ["Motivo"] = "Producto defectuoso (E2E)"
        });

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);

        await using var contexto = CrearContexto();

        var devolucion = await contexto.Devoluciones.AsNoTracking().SingleAsync();
        Assert.Equal(venta.Id, devolucion.VentaId);
        Assert.Equal(venta.Total, devolucion.TotalDevuelto);
        Assert.Equal(venta.Total, devolucion.TarjetaDevuelto);   // se reembolsa como se pagó
        Assert.Equal(0m, devolucion.EfectivoDevuelto);

        // Trazabilidad: la venta se pagó 100% con tarjeta, así que el reembolso vuelve por el
        // mismo medio y la gaveta no se toca (tampoco se registra un movimiento de RD$ 0.00).
        Assert.Equal(0, await contexto.MovimientosCaja.AsNoTracking().CountAsync(m => m.VentaId != null));

        // Inventario: el stock reingresó y dejó kardex.
        var stock = await contexto.InventariosAlmacen.AsNoTracking()
            .Where(i => i.ProductoId == productoId)
            .Select(i => i.StockActual)
            .SingleAsync();
        Assert.Equal(5m, stock);
        var tiposDeMovimiento = await contexto.MovimientosInventario.AsNoTracking()
            .Where(m => m.ProductoId == productoId)
            .Select(m => m.Tipo)
            .ToListAsync();
        Assert.Contains(TipoMovimientoInventario.DevolucionVenta, tiposDeMovimiento);
    }

    [Fact]
    public async Task DevolucionPorHttp_CajeroSinSupervision_Recibe403YNoRegistraNada()
    {
        var productoId = await SembrarProductoAsync(precio: 100m, stock: 5m);
        var cajero = await CrearSesionAsync(RolUsuario.Cajero, abrirTurno: true);
        var venta = await VenderConTarjetaAsync(cajero, productoId);

        var respuesta = await PostFormularioAsync(cajero, "/Caja/Devolucion", new Dictionary<string, string>
        {
            ["VentaId"] = venta.Id.ToString(),
            ["Motivo"] = "Intento no autorizado"
        });

        // La política de supervisión se aplica en el servidor: 403 directo o redirección a la
        // página de acceso denegado; en ningún caso la operación alcanza al caso de uso.
        Assert.True(
            respuesta.StatusCode == HttpStatusCode.Forbidden
            || (respuesta.StatusCode == HttpStatusCode.Redirect
                && (respuesta.Headers.Location?.ToString().Contains("AccesoDenegado") ?? false)),
            $"El cajero no debía registrar la devolución (código: {respuesta.StatusCode}).");

        await using var contexto = CrearContexto();
        Assert.Equal(0, await contexto.Devoluciones.AsNoTracking().CountAsync());
        Assert.Equal(0, await contexto.MovimientosCaja.AsNoTracking().CountAsync(m => m.VentaId != null));
        Assert.Equal(4m, await StockDeAsync(productoId));   // el stock no reingresó
    }

    // ------------------------------------------------------------------ cierre de turno

    [Fact]
    public async Task CierrePorHttp_TurnoAjeno_EsRechazadoYElTurnoSigueAbierto()
    {
        var supervisor = await CrearSesionAsync(RolUsuario.Supervisor, abrirTurno: true);
        var cajero = await CrearSesionAsync(RolUsuario.Cajero, abrirTurno: true);

        var respuesta = await PostFormularioAsync(supervisor, "/Caja/Cierre", new Dictionary<string, string>
        {
            ["id"] = cajero.TurnoId.ToString(),
            ["montoRealCierre"] = "9999",
            ["observaciones"] = "cierre ajeno"
        });

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);

        await using var contexto = CrearContexto();
        var turnoAjeno = await contexto.CajaTurnos.AsNoTracking().SingleAsync(t => t.Id == cajero.TurnoId);
        Assert.Equal(TurnoCajaEstado.Abierto, turnoAjeno.Estado);
        Assert.Null(turnoAjeno.MontoRealCierre);
    }

    [Fact]
    public async Task CierrePorHttp_DobleEnvio_ElArqueoSoloRegistraElPrimero()
    {
        var sesion = await CrearSesionAsync(RolUsuario.Supervisor, abrirTurno: true);

        var primera = await PostFormularioAsync(sesion, "/Caja/Cierre", new Dictionary<string, string>
        {
            ["id"] = sesion.TurnoId.ToString(),
            ["montoRealCierre"] = "1000",
            ["observaciones"] = "arqueo real"
        });
        var segunda = await PostFormularioAsync(sesion, "/Caja/Cierre", new Dictionary<string, string>
        {
            ["id"] = sesion.TurnoId.ToString(),
            ["montoRealCierre"] = "7777",
            ["observaciones"] = "doble envío"
        });

        Assert.Equal(HttpStatusCode.Redirect, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, segunda.StatusCode);

        await using var contexto = CrearContexto();
        var cerrado = await contexto.CajaTurnos.AsNoTracking().SingleAsync(t => t.Id == sesion.TurnoId);
        Assert.Equal(TurnoCajaEstado.Cerrado, cerrado.Estado);
        Assert.Equal(1000m, cerrado.MontoRealCierre);          // el segundo intento no altera nada
        Assert.Equal("arqueo real", cerrado.Observaciones);
    }

    // ------------------------------------------------------------------ soporte

    private sealed record Sesion(HttpClient Cliente, string Token, int TurnoId, string NombreUsuario);

    private POSDbContext CrearContexto()
    {
        var scope = _app.Services.CreateScope();
        return new POSDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<POSDbContext>>());
    }

    private async Task<int> SembrarProductoAsync(decimal precio, decimal stock)
    {
        await using var contexto = CrearContexto();

        var sucursal = await contexto.Sucursales.AsNoTracking().OrderBy(s => s.Id).FirstAsync();

        var producto = new Producto
        {
            Codigo = "CAJA-E2E-" + Guid.NewGuid().ToString("N")[..8],
            Descripcion = "Producto de prueba de caja E2E",
            PrecioUnitario = precio,
            CostoUnitario = precio / 2,
            IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
            UnidadMedida = UnidadMedidaType.Unidad,
            EstaActivo = true
        };

        contexto.Productos.Add(producto);
        await contexto.SaveChangesAsync();

        contexto.InventariosAlmacen.Add(new InventarioAlmacen
        {
            ProductoId = producto.Id,
            SucursalId = sucursal.Id,
            StockActual = stock,
            StockMinimo = 1m
        });
        await contexto.SaveChangesAsync();

        return producto.Id;
    }

    private async Task<Sesion> CrearSesionAsync(RolUsuario rol, bool abrirTurno)
    {
        var nombre = "caja-e2e-" + Guid.NewGuid().ToString("N")[..8];
        _app.CrearUsuario(nombre, rol);

        var cliente = _app.CrearCliente();
        await IniciarSesionAsync(cliente, nombre);

        var token = PosAppFactory.ExtraerTokenAntiforgery(await cliente.GetStringAsync("/Pos"));

        var turnoId = 0;
        if (abrirTurno)
        {
            var apertura = await PostFormularioAsync(
                new Sesion(cliente, token, 0, nombre), "/Caja/Apertura",
                new Dictionary<string, string> { ["montoInicial"] = "1000" });
            Assert.Equal(HttpStatusCode.Redirect, apertura.StatusCode);

            await using var contexto = CrearContexto();
            var usuarioId = await contexto.Usuarios.AsNoTracking()
                .Where(u => u.NombreUsuario == nombre)
                .Select(u => u.Id)
                .SingleAsync();
            turnoId = await contexto.CajaTurnos.AsNoTracking()
                .Where(t => t.UsuarioId == usuarioId && t.Estado == TurnoCajaEstado.Abierto)
                .Select(t => t.Id)
                .SingleAsync();
        }

        return new Sesion(cliente, token, turnoId, nombre);
    }

    /// <summary>Registra una venta con tarjeta por el terminal HTTP y devuelve la venta sembrada.</summary>
    private async Task<Venta> VenderConTarjetaAsync(Sesion sesion, int productoId)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/Pos/ProcesarVenta")
        {
            Content = JsonContent.Create(new VentaPosRequest
            {
                ClaveIdempotencia = Guid.NewGuid(),
                TipoeCF = TipoeCFType.FacturaConsumo,
                TipoPago = TipoPago.Contado,
                MetodoPago = MetodoPago.TarjetaDebitoCredito,
                MontoRecibido = 118m,
                Items = new List<VentaPosItemRequest> { new() { ProductoId = productoId, Cantidad = 1m } }
            }, options: JsonWeb)
        };
        peticion.Headers.Add("RequestVerificationToken", sesion.Token);

        var respuesta = await sesion.Cliente.SendAsync(peticion);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var resultado = await respuesta.Content.ReadFromJsonAsync<VentaPosResponse>(JsonWeb);
        Assert.True(resultado!.Exitoso, resultado.Mensaje);

        await using var contexto = CrearContexto();
        return await contexto.Ventas.AsNoTracking().SingleAsync(v => v.Id == resultado.VentaId);
    }

    private static async Task<HttpResponseMessage> PostFormularioAsync(
        Sesion sesion, string ruta, Dictionary<string, string> campos)
    {
        var camposConToken = new Dictionary<string, string>(campos)
        {
            ["__RequestVerificationToken"] = sesion.Token
        };

        return await sesion.Cliente.PostAsync(ruta, new FormUrlEncodedContent(camposConToken));
    }

    private async Task<decimal> StockDeAsync(int productoId)
    {
        await using var contexto = CrearContexto();
        return await contexto.InventariosAlmacen.AsNoTracking()
            .Where(i => i.ProductoId == productoId)
            .Select(i => i.StockActual)
            .SingleAsync();
    }

    private static async Task IniciarSesionAsync(HttpClient cliente, string usuario)
    {
        var html = await cliente.GetStringAsync("/Cuenta/Login");
        var token = PosAppFactory.ExtraerTokenAntiforgery(html);

        var respuesta = await cliente.PostAsync(
            "/Cuenta/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Usuario"] = usuario,
                ["Password"] = PosAppFactory.PasswordAuxiliar,
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
    }
}
