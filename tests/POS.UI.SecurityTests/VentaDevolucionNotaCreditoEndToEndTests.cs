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
using POS.Domain.Enums;
using POS.Domain.Types;
using POS.Infrastructure.Persistence;
using Xunit;

namespace POS.UI.SecurityTests;

/// <summary>
/// Prueba de extremo a extremo del cierre del círculo de devolución (5.4) navegando la UI real:
/// venta por el terminal → devolución autorizada por el cajero → nota de crédito e-CF 34 emitida
/// por el supervisor desde la pantalla de devolución → detalle del comprobante con su referencia
/// fiscal al comprobante corregido. Todo por el pipeline HTTP autenticado (cookies, antiforgery,
/// políticas de autorización), contra la base de datos real de la aplicación.
/// </summary>
public class VentaDevolucionNotaCreditoEndToEndTests : IClassFixture<PosAppFactory>
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private readonly PosAppFactory _app;

    public VentaDevolucionNotaCreditoEndToEndTests(PosAppFactory app) => _app = app;

    [Fact]
    public async Task FlujoCompletoPorUI_VentaDevolucionEmisionNC34_DejaTrazabilidadFiscalReferenciada()
    {
        // ---------------------------------------------------------------- venta por el terminal POS
        var productoId = await SembrarProductoAsync(precio: 100m, stock: 5m);
        var sesion = await CrearSesionSupervisorConTurnoAsync();

        var venta = await VenderAsync(sesion, productoId);

        var ventaOriginal = await ComprobanteDeVentaAsync(venta.Id);
        Assert.NotNull(ventaOriginal.eNCF);

        // ---------------------------------------------------------------- devolución por la UI

        var alta = await PostFormularioAsync(sesion, "/Caja/Devolucion", new Dictionary<string, string>
        {
            ["VentaId"] = venta.Id.ToString(),
            ["Motivo"] = "Producto defectuoso (E2E flujo completo)"
        });
        Assert.Equal(HttpStatusCode.Redirect, alta.StatusCode);

        var devolucionId = await DevolucionIdDeVentaAsync(venta.Id);

        // ---------------------------------------------------------------- emisión de la NC 34
        // La pantalla de devolución consulta por venta y ofrece emitir la nota pendiente.
        var pantallaDevolucion = await sesion.Cliente.GetStringAsync($"/Caja/Devolucion?ventaId={venta.Id}");
        Assert.Contains("Pendiente de nota de crédito", pantallaDevolucion);
        Assert.Contains("Emitir nota de crédito", pantallaDevolucion);

        var emision = await PostFormularioAsync(
            sesion, $"/Facturacion/EmitirNotaCredito?devolucionId={devolucionId}", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, emision.StatusCode);

        var rutaDetalle = emision.Headers.Location!.ToString();
        Assert.Contains("/Facturacion/Detalle/", rutaDetalle);
        var notaId = int.Parse(rutaDetalle[(rutaDetalle.LastIndexOf('/') + 1)..]);

        // ---------------------------------------------------------------- el detalle muestra la referencia
        var detalle = await sesion.Cliente.GetStringAsync($"/Facturacion/Detalle/{notaId}");
        var detalleTexto = System.Net.WebUtility.HtmlDecode(detalle);
        Assert.Contains("e-CF 34 Nota de Crédito", detalleTexto);
        Assert.Contains(ventaOriginal.eNCF, detalleTexto);            // referencia al comprobante corregido
        Assert.Contains("corrección de montos", detalleTexto);        // código de modificación 3

        // ---------------------------------------------------------------- la pantalla de devolución saldada
        pantallaDevolucion = await sesion.Cliente.GetStringAsync($"/Caja/Devolucion?ventaId={venta.Id}");
        Assert.Contains("NC E34", pantallaDevolucion);
        Assert.DoesNotContain("Emitir nota de crédito", pantallaDevolucion);

        // ---------------------------------------------------------------- integridad fiscal en la BD
        await using var contexto = CrearContexto();
        var nota = await contexto.ElectronicInvoices.AsNoTracking().SingleAsync(i => i.Id == notaId);

        Assert.Equal(TipoeCFType.NotaCredito, nota.TipoeCF);
        Assert.StartsWith("E34", nota.eNCF);
        Assert.Equal(devolucionId, nota.DevolucionId);
        Assert.Equal(ventaOriginal.eNCF, nota.eNCFModificado);
        Assert.Equal(ventaOriginal.FechaEmision, nota.FechaNCFModificado);
        Assert.Equal(3, nota.CodigoModificacion);
        Assert.Equal(venta.Total, nota.MontoTotal);              // devolución total: invierte la venta

        // La nota nace local y encolada: la transmisión es post-commit (worker o botón), nunca
        // dentro del turno del operador. En el instante de la lectura puede seguir NoEnviado o
        // estar ya EnProceso si el worker de la cola ganó la carrera — pero nunca final.
        Assert.True(
            nota.Estado is EstadoFacturaElectronica.NoEnviado or EstadoFacturaElectronica.EnProceso,
            $"Estado inesperado antes de la transmisión deliberada: {nota.Estado}");
        Assert.False(string.IsNullOrWhiteSpace(nota.XMLContent));
        Assert.Contains("InformacionReferencia", nota.XMLContent);
        Assert.Contains($"<NCFModificado>{ventaOriginal.eNCF}</NCFModificado>", nota.XMLContent);

        Assert.Equal(1, await contexto.EmisionesDGIIQueue.AsNoTracking()
            .CountAsync(q => q.FacturaId == notaId));

        // La devolución queda saldada contra su comprobante.
        var devolucion = await contexto.Devoluciones.AsNoTracking().SingleAsync(d => d.Id == devolucionId);
        Assert.Equal(venta.Total, devolucion.TotalDevuelto);

        // ---------------------------------------------------------------- ciclo fiscal completo
        // Transmisión por el botón real de la UI (Reenviar usa la misma ruta única del worker) y
        // consolidación del resultado con la consulta de estado — el simulador responde "Aceptado".
        var transmision = await PostFormularioAsync(
            sesion, $"/Facturacion/Reenviar?encf={nota.eNCF}", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, transmision.StatusCode);

        await using var contexto3 = CrearContexto();
        var notaTransmitida = await contexto3.ElectronicInvoices.AsNoTracking()
            .SingleAsync(i => i.Id == notaId);
        Assert.False(string.IsNullOrWhiteSpace(notaTransmitida.TrackId), "La transmisión debió asignar TrackId.");

        var consulta = await PostFormularioAsync(
            sesion, $"/Facturacion/ConsultarEstado?id={notaId}", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, consulta.StatusCode);

        await using var contexto4 = CrearContexto();
        var notaAceptada = await contexto4.ElectronicInvoices.AsNoTracking()
            .SingleAsync(i => i.Id == notaId);
        Assert.Equal(EstadoFacturaElectronica.Aceptado, notaAceptada.Estado);
        Assert.Equal(EstadoFacturaElectronica.Aceptado.ToString(), notaAceptada.EstadoDgii);
        Assert.NotNull(notaAceptada.FechaAprobacion);

        // ---------------------------------------------------------------- idempotencia desde la UI
        var reintento = await PostFormularioAsync(
            sesion, $"/Facturacion/EmitirNotaCredito?devolucionId={devolucionId}", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, reintento.StatusCode);
        Assert.Contains($"/Facturacion/Detalle/{notaId}", reintento.Headers.Location!.ToString());

        await using var contexto2 = CrearContexto();
        Assert.Equal(1, await contexto2.ElectronicInvoices.AsNoTracking()
            .CountAsync(i => i.DevolucionId == devolucionId));
        Assert.Equal(1, await contexto2.EmisionesDGIIQueue.AsNoTracking()
            .CountAsync(q => q.FacturaId == notaId));
    }

    [Fact]
    public async Task FlujoCompletoPorUI_CajeroSinSupervision_NoPuedeEmitirLaNota()
    {
        var productoId = await SembrarProductoAsync(precio: 100m, stock: 5m);

        // El supervisor vende y registra la devolución (ambas operaciones requieren supervisión).
        var supervisor = await CrearSesionSupervisorConTurnoAsync();
        var venta = await VenderAsync(supervisor, productoId);
        var alta = await PostFormularioAsync(supervisor, "/Caja/Devolucion", new Dictionary<string, string>
        {
            ["VentaId"] = venta.Id.ToString(),
            ["Motivo"] = "Devolución registrada por el supervisor"
        });
        Assert.Equal(HttpStatusCode.Redirect, alta.StatusCode);
        var devolucionId = await DevolucionIdDeVentaAsync(venta.Id);

        // El cajero, con su propia sesión, intenta emitir la nota: la emisión exige supervisión.
        var cajero = await CrearSesionSupervisorConTurnoAsync(rol: RolUsuario.Cajero);
        var emision = await PostFormularioAsync(
            cajero, $"/Facturacion/EmitirNotaCredito?devolucionId={devolucionId}", new Dictionary<string, string>());

        Assert.True(
            emision.StatusCode == HttpStatusCode.Forbidden
            || (emision.StatusCode == HttpStatusCode.Redirect
                && (emision.Headers.Location?.ToString().Contains("AccesoDenegado") ?? false)),
            $"El cajero no debía emitir la nota de crédito (código: {emision.StatusCode}).");

        await using var contexto = CrearContexto();
        Assert.Equal(0, await contexto.ElectronicInvoices.AsNoTracking()
            .CountAsync(i => i.DevolucionId == devolucionId));
    }

    // ------------------------------------------------------------------ soporte

    private sealed record Sesion(HttpClient Cliente, string Token, string NombreUsuario);

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

        var producto = new POS.Domain.Entities.Producto
        {
            Codigo = "NC-E2E-" + Guid.NewGuid().ToString("N")[..8],
            Descripcion = "Producto de prueba de nota de crédito E2E",
            PrecioUnitario = precio,
            CostoUnitario = precio / 2,
            IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
            UnidadMedida = UnidadMedidaType.Unidad,
            EstaActivo = true
        };

        contexto.Productos.Add(producto);
        await contexto.SaveChangesAsync();

        contexto.InventariosAlmacen.Add(new POS.Domain.Entities.InventarioAlmacen
        {
            ProductoId = producto.Id,
            SucursalId = sucursal.Id,
            StockActual = stock,
            StockMinimo = 1m
        });
        await contexto.SaveChangesAsync();

        return producto.Id;
    }

    private async Task<Sesion> CrearSesionSupervisorConTurnoAsync(RolUsuario rol = RolUsuario.Supervisor)
    {
        var nombre = "nc-e2e-" + Guid.NewGuid().ToString("N")[..8];
        _app.CrearUsuario(nombre, rol);

        var cliente = _app.CrearCliente();
        await IniciarSesionAsync(cliente, nombre);

        var token = PosAppFactory.ExtraerTokenAntiforgery(await cliente.GetStringAsync("/Pos"));

        var apertura = await PostFormularioAsync(
            new Sesion(cliente, token, nombre), "/Caja/Apertura",
            new Dictionary<string, string> { ["montoInicial"] = "1000" });
        Assert.Equal(HttpStatusCode.Redirect, apertura.StatusCode);

        return new Sesion(cliente, token, nombre);
    }

    /// <summary>Registra una venta por el terminal HTTP y devuelve la venta sembrada.</summary>
    private async Task<POS.Domain.Entities.Venta> VenderAsync(Sesion sesion, int productoId)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/Pos/ProcesarVenta")
        {
            Content = JsonContent.Create(new VentaPosRequest
            {
                ClaveIdempotencia = Guid.NewGuid(),
                TipoeCF = TipoeCFType.FacturaConsumo,
                TipoPago = TipoPago.Contado,
                MetodoPago = MetodoPago.Efectivo,
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

    private async Task<POS.Domain.Entities.ElectronicInvoice> ComprobanteDeVentaAsync(int ventaId)
    {
        await using var contexto = CrearContexto();
        return await contexto.ElectronicInvoices.AsNoTracking()
            .SingleAsync(i => i.VentaId == ventaId);
    }

    private async Task<int> DevolucionIdDeVentaAsync(int ventaId)
    {
        await using var contexto = CrearContexto();
        return await contexto.Devoluciones.AsNoTracking()
            .Where(d => d.VentaId == ventaId)
            .Select(d => d.Id)
            .SingleAsync();
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
