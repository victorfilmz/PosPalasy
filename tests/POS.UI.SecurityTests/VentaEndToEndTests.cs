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
/// Prueba de extremo a extremo del flujo de venta por el pipeline HTTP real: autenticación,
/// autorización, antiforgery, caso de uso, base de datos, inventario, caja y emisión.
/// </summary>
/// <remarks>
/// Las pruebas de <c>POS.Ventas.Tests</c> invocan el caso de uso directamente. Aquí se comprueba lo
/// que solo se puede ver de extremo a extremo: que la petición atraviesa de verdad todos los filtros
/// del servidor (incluido el antiforgery global) y termina en un estado consistente en la base de
/// datos. Es la diferencia entre "el caso de uso funciona" y "el terminal puede vender".
/// </remarks>
public class VentaEndToEndTests : IClassFixture<PosAppFactory>
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private readonly PosAppFactory _app;

    public VentaEndToEndTests(PosAppFactory app) => _app = app;

    // ------------------------------------------------------------------ venta normal

    [Fact]
    public async Task VentaPorHttp_RegistraVentaInventarioCajaComprobanteYCola()
    {
        const decimal precio = 100m;
        var productoId = await SembrarProductoAsync(precio, stock: 5m);
        var sesion = await CrearCajeroConTurnoAsync();
        var clave = Guid.NewGuid();

        var respuesta = await PostVentaAsync(sesion, new VentaPosRequest
        {
            ClaveIdempotencia = clave,
            TipoeCF = TipoeCFType.FacturaConsumo,
            TipoPago = TipoPago.Contado,
            MetodoPago = MetodoPago.Efectivo,
            MontoRecibido = 120m,
            Items = new List<VentaPosItemRequest> { new() { ProductoId = productoId, Cantidad = 1m } }
        });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var resultado = await LeerAsync<VentaPosResponse>(respuesta);

        Assert.True(resultado.Exitoso, resultado.Mensaje);
        Assert.False(resultado.Duplicada);
        Assert.Equal(precio * 1.18m, resultado.Total);      // 118.00: el servidor calcula el ITBIS
        Assert.Equal(2.00m, resultado.Cambio);
        Assert.Equal(13, resultado.eNCF.Length);
        Assert.StartsWith("E32", resultado.eNCF);
        Assert.False(resultado.EsOfflineDGII);

        await using var contexto = CrearContexto();

        var venta = await contexto.Ventas.AsNoTracking().SingleAsync(v => v.Id == resultado.VentaId);
        Assert.Equal(clave, venta.ClaveIdempotencia);
        Assert.Equal(precio * 1.18m, venta.Total);
        Assert.Equal(precio, venta.Subtotal);        // base imponible, sin ITBIS
        Assert.Equal(18m, venta.TotalITBIS);
        Assert.Equal(120m, venta.MontoRecibido);
        Assert.Equal(2m, venta.Cambio);
        Assert.Equal(1, await contexto.VentaItems.CountAsync(i => i.VentaId == venta.Id));

        // Inventario: se descontó dentro de la misma transacción y dejó kardex.
        var stock = await contexto.InventariosAlmacen
            .AsNoTracking()
            .Where(i => i.ProductoId == productoId)
            .Select(i => i.StockActual)
            .SingleAsync();
        Assert.Equal(4m, stock);
        Assert.Equal(1, await contexto.MovimientosInventario.CountAsync(m => m.ProductoId == productoId));

        // Caja: la venta queda imputada al turno del cajero autenticado.
        var turno = await contexto.CajaTurnos.AsNoTracking().SingleAsync(t => t.Id == sesion.TurnoId);
        Assert.Equal(1, turno.CantidadTransacciones);
        Assert.Equal(118m, turno.VentasEfectivo);
        Assert.Equal(118m, turno.TotalVentas);

        // Comprobante y outbox: mismo eNCF en la respuesta, en el comprobante y en la cola.
        var comprobante = await contexto.ElectronicInvoices.AsNoTracking().SingleAsync(e => e.eNCF == resultado.eNCF);
        Assert.Equal(resultado.ElectronicInvoiceId, comprobante.Id);

        var enCola = await contexto.EmisionesDGIIQueue.AsNoTracking().SingleAsync(e => e.FacturaId == comprobante.Id);
        Assert.Equal(resultado.eNCF, enCola.eNCF);
    }

    // ------------------------------------------------------------------ autorización y CSRF

    [Fact]
    public async Task VentaPorHttp_SinSesion_RedirigeAlLoginYNoRegistraNada()
    {
        var productoId = await SembrarProductoAsync(50m, stock: 5m);
        var clave = Guid.NewGuid();

        var cliente = _app.CrearCliente();
        var respuesta = await cliente.PostAsJsonAsync("/Pos/ProcesarVenta", PayloadBasico(productoId, clave));

        // El filtro global exige sesión: el terminal sin autenticar no llega siquiera al caso de uso.
        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Contains("/Cuenta/Login", respuesta.Headers.Location?.ToString() ?? string.Empty);
        Assert.Equal(0, await ContarVentasDeAsync(clave));
    }

    [Fact]
    public async Task VentaPorHttp_SinTokenAntiforgery_Devuelve400YNoRegistraNada()
    {
        var productoId = await SembrarProductoAsync(50m, stock: 5m);
        var sesion = await CrearCajeroConTurnoAsync();
        var clave = Guid.NewGuid();

        // Misma petición, misma sesión, pero sin el token: debe rechazarse antes de tocar el negocio.
        var respuesta = await sesion.Cliente.PostAsJsonAsync(
            "/Pos/ProcesarVenta",
            PayloadBasico(productoId, clave));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, await ContarVentasDeAsync(clave));
    }

    [Fact]
    public async Task VentaPorHttp_SinTurnoDeCajaAbierto_Devuelve400YNoRegistraNada()
    {
        var productoId = await SembrarProductoAsync(50m, stock: 5m);
        var sesion = await CrearCajeroConTurnoAsync(abrirTurno: false);
        var clave = Guid.NewGuid();

        var respuesta = await PostVentaAsync(sesion, PayloadBasico(productoId, clave));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        var resultado = await LeerAsync<VentaPosResponse>(respuesta);
        Assert.False(resultado.Exitoso);
        Assert.Equal("SIN_TURNO_DE_CAJA", resultado.CodigoError);
        Assert.Equal(0, await ContarVentasDeAsync(clave));
    }

    // ------------------------------------------------------------------ integridad

    /// <summary>
    /// Con la política estricta, el servidor rechaza vender existencias que no hay y la venta no deja
    /// rastro: ni venta, ni kardex, ni número fiscal consumido.
    /// </summary>
    [Fact]
    public async Task VentaPorHttp_PoliticaEstricta_RechazaVentaSinStockYNoQuemaElNumeroFiscal()
    {
        await EstablecerPoliticaDeStockAsync(permitirVentaSinStock: false);

        var productoId = await SembrarProductoAsync(50m, stock: 1m);
        var sesion = await CrearCajeroConTurnoAsync();
        var clave = Guid.NewGuid();

        var consecutivoAntes = await ConsecutivoE32Async();

        var respuesta = await PostVentaAsync(sesion, new VentaPosRequest
        {
            ClaveIdempotencia = clave,
            MetodoPago = MetodoPago.Efectivo,
            MontoRecibido = 500m,
            Items = new List<VentaPosItemRequest> { new() { ProductoId = productoId, Cantidad = 3m } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        var resultado = await LeerAsync<VentaPosResponse>(respuesta);
        Assert.Equal("STOCK_INSUFICIENTE", resultado.CodigoError);

        // Ni venta, ni kardex, ni número consumido: el reintento del cajero usará el mismo eNCF.
        Assert.Equal(0, await ContarVentasDeAsync(clave));
        Assert.Equal(1m, await StockAsync(productoId));
        Assert.Equal(0, await ContarMovimientosAsync(productoId));
        Assert.Equal(consecutivoAntes, await ConsecutivoE32Async());
    }

    /// <summary>
    /// Con la política permisiva (valor por defecto del sistema) la venta sí se registra y la existencia
    /// queda negativa. La prueba fija el comportamiento real de ese modo para que sea una decisión
    /// consciente y no un efecto colateral inadvertido.
    /// </summary>
    [Fact]
    public async Task VentaPorHttp_PoliticaPermisiva_RegistraLaExistenciaNegativaConKardex()
    {
        await EstablecerPoliticaDeStockAsync(permitirVentaSinStock: true);

        var productoId = await SembrarProductoAsync(50m, stock: 1m);
        var sesion = await CrearCajeroConTurnoAsync();

        var respuesta = await PostVentaAsync(sesion, new VentaPosRequest
        {
            ClaveIdempotencia = Guid.NewGuid(),
            MetodoPago = MetodoPago.Efectivo,
            MontoRecibido = 500m,
            Items = new List<VentaPosItemRequest> { new() { ProductoId = productoId, Cantidad = 3m } }
        });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var resultado = await LeerAsync<VentaPosResponse>(respuesta);
        Assert.True(resultado.Exitoso, resultado.Mensaje);

        await using var contexto = CrearContexto();

        Assert.Equal(-2m, await StockAsync(productoId));

        var movimiento = await contexto.MovimientosInventario
            .AsNoTracking()
            .SingleAsync(m => m.ProductoId == productoId);

        Assert.Equal(1m, movimiento.StockAnterior);
        Assert.Equal(-2m, movimiento.StockNuevo);
        Assert.Equal(TipoMovimientoInventario.VentaPOS, movimiento.Tipo);
    }

    [Fact]
    public async Task VentaPorHttp_DoblePostConLaMismaClave_RegistraUnaSolaVenta()
    {
        var productoId = await SembrarProductoAsync(50m, stock: 5m);
        var sesion = await CrearCajeroConTurnoAsync();
        var clave = Guid.NewGuid();

        var payload = new VentaPosRequest
        {
            ClaveIdempotencia = clave,
            MetodoPago = MetodoPago.Efectivo,
            MontoRecibido = 200m,
            Items = new List<VentaPosItemRequest> { new() { ProductoId = productoId, Cantidad = 1m } }
        };

        var primero = await LeerAsync<VentaPosResponse>(await PostVentaAsync(sesion, payload));
        var segundo = await LeerAsync<VentaPosResponse>(await PostVentaAsync(sesion, payload));

        Assert.True(primero.Exitoso);
        Assert.True(segundo.Exitoso);
        Assert.True(segundo.Duplicada);
        Assert.Equal(primero.VentaId, segundo.VentaId);
        Assert.Equal(primero.eNCF, segundo.eNCF);

        await using var contexto = CrearContexto();
        Assert.Equal(1, await contexto.Ventas.CountAsync(v => v.ClaveIdempotencia == clave));

        // Un solo movimiento: el doble clic no puede consumir dos veces el inventario ni la caja.
        Assert.Equal(4m, await StockAsync(productoId));
        Assert.Equal(1, await ContarMovimientosAsync(productoId));

        var turno = await contexto.CajaTurnos.AsNoTracking().SingleAsync(t => t.Id == sesion.TurnoId);
        Assert.Equal(1, turno.CantidadTransacciones);
    }

    [Fact]
    public async Task VentaPorHttp_PrecioManipuladoPorElTerminal_NoAlteraElCobro()
    {
        const decimal precio = 100m;
        var productoId = await SembrarProductoAsync(precio, stock: 5m);
        var sesion = await CrearCajeroConTurnoAsync();

        // El terminal intenta imponer el precio y el importe. El servidor debe ignorarlos.
        var payload = new Dictionary<string, object?>
        {
            ["claveIdempotencia"] = Guid.NewGuid(),
            ["tipoeCF"] = (int)TipoeCFType.FacturaConsumo,
            ["tipoPago"] = (int)TipoPago.Contado,
            ["metodoPago"] = (int)MetodoPago.Efectivo,
            ["montoRecibido"] = 120m,
            ["total"] = 0.01m,
            ["items"] = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["productoId"] = productoId,
                    ["cantidad"] = 1m,
                    ["descuento"] = 0m,
                    ["precioUnitario"] = 0.01m
                }
            }
        };

        var respuesta = await EnviarJsonAsync(sesion, payload);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var resultado = await LeerAsync<VentaPosResponse>(respuesta);
        Assert.Equal(precio * 1.18m, resultado.Total);

        await using var contexto = CrearContexto();
        var renglon = await contexto.VentaItems.AsNoTracking().SingleAsync(i => i.VentaId == resultado.VentaId);
        Assert.Equal(precio, renglon.PrecioUnitario);
        Assert.Equal(precio * 1.18m, renglon.Total);
    }

    // ------------------------------------------------------------------ utilidades

    private sealed record Sesion(HttpClient Cliente, string Token, int TurnoId);

    private POSDbContext CrearContexto()
    {
        var scope = _app.Services.CreateScope();
        return new POSDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<POSDbContext>>());
    }

    private static VentaPosRequest PayloadBasico(int productoId, Guid clave) => new()
    {
        ClaveIdempotencia = clave,
        MetodoPago = MetodoPago.Efectivo,
        MontoRecibido = 500m,
        Items = new List<VentaPosItemRequest> { new() { ProductoId = productoId, Cantidad = 1m } }
    };

    /// <summary>Siembra un producto activo con existencias en la sucursal principal.</summary>
    private async Task<int> SembrarProductoAsync(decimal precio, decimal stock)
    {
        await using var contexto = CrearContexto();

        var sucursal = await contexto.Sucursales.AsNoTracking().OrderBy(s => s.Id).FirstAsync();

        var producto = new Producto
        {
            Codigo = "E2E-" + Guid.NewGuid().ToString("N")[..10],
            Descripcion = "Producto de prueba E2E",
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

    /// <summary>Crea un cajero, inicia sesión por HTTP y (opcionalmente) abre su turno de caja.</summary>
    private async Task<Sesion> CrearCajeroConTurnoAsync(bool abrirTurno = true)
    {
        var nombre = "e2e-" + Guid.NewGuid().ToString("N")[..10];
        _app.CrearUsuario(nombre, RolUsuario.Cajero);

        var cliente = _app.CrearCliente();
        await IniciarSesionAsync(cliente, nombre, PosAppFactory.PasswordAuxiliar);

        var token = await TokenDeOperacionAsync(cliente);

        if (abrirTurno)
        {
            var apertura = await cliente.PostAsync(
                "/Caja/Apertura",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["cajero"] = "Cajero E2E",
                    ["montoInicial"] = "1000",
                    ["__RequestVerificationToken"] = token
                }));

            Assert.Equal(HttpStatusCode.Redirect, apertura.StatusCode);
        }

        using var scope = _app.Services.CreateScope();
        await using var contexto = new POSDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<POSDbContext>>());

        var usuarioId = await contexto.Usuarios.AsNoTracking()
            .Where(u => u.NombreUsuario == nombre)
            .Select(u => u.Id)
            .SingleAsync();

        var turno = await contexto.CajaTurnos.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UsuarioId == usuarioId && t.Estado == TurnoCajaEstado.Abierto);

        return new Sesion(
            cliente,
            await TokenDeOperacionAsync(cliente),
            turno?.Id ?? 0);
    }

    private static async Task IniciarSesionAsync(HttpClient cliente, string usuario, string password)
    {
        var html = await cliente.GetStringAsync("/Cuenta/Login");
        var token = PosAppFactory.ExtraerTokenAntiforgery(html);

        var respuesta = await cliente.PostAsync(
            "/Cuenta/Login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Usuario"] = usuario,
                ["Password"] = password,
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
    }

    /// <summary>Token antiforgery vigente de la sesión, tal como lo envía el terminal de venta.</summary>
    private static async Task<string> TokenDeOperacionAsync(HttpClient cliente) =>
        PosAppFactory.ExtraerTokenAntiforgery(await cliente.GetStringAsync("/Pos"));

    /// <summary>POST de venta con el token en la cabecera, igual que el JavaScript del terminal.</summary>
    private static Task<HttpResponseMessage> PostVentaAsync(Sesion sesion, VentaPosRequest payload) =>
        EnviarJsonAsync(sesion, payload, JsonWeb);

    private static Task<HttpResponseMessage> EnviarJsonAsync(
        Sesion sesion,
        object payload,
        JsonSerializerOptions? opciones = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/Pos/ProcesarVenta")
        {
            Content = JsonContent.Create(payload, options: opciones)
        };

        peticion.Headers.Add("RequestVerificationToken", sesion.Token);
        return sesion.Cliente.SendAsync(peticion);
    }

    private static async Task<T> LeerAsync<T>(HttpResponseMessage respuesta)
    {
        var contenido = await respuesta.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<T>(contenido, JsonWeb)
            ?? throw new InvalidOperationException(
                $"El servidor no devolvió el resultado de venta esperado: {contenido}");
    }

    /// <summary>
    /// Fija la política de existencias de la empresa emisora. Es el único punto donde se decide si el
    /// sistema puede vender por encima de lo disponible, y la prueba lo hace explícito.
    /// </summary>
    private async Task EstablecerPoliticaDeStockAsync(bool permitirVentaSinStock)
    {
        await using var contexto = CrearContexto();

        var empresa = await contexto.Enterprises.FirstAsync();
        empresa.PermitirVentaSinStock = permitirVentaSinStock;

        await contexto.SaveChangesAsync();
    }

    /// <summary>Ventas registradas con esa clave de idempotencia (0 o 1: nunca dos).</summary>
    private async Task<int> ContarVentasDeAsync(Guid clave)
    {
        await using var contexto = CrearContexto();
        return await contexto.Ventas.CountAsync(v => v.ClaveIdempotencia == clave);
    }    /// <summary>
    /// Último consecutivo asignado a la serie de consumo (E32). Devuelve 0 cuando la serie todavía no
    /// tiene fila: la secuencia se crea bajo demanda, y una venta revertida no debe crearla ni avanzarla.
    /// </summary>
    private async Task<long> ConsecutivoE32Async()
    {
        await using var contexto = CrearContexto();

        return await contexto.SecuenciasECF
            .AsNoTracking()
            .Where(s => s.Serie == "E32")
            .Select(s => s.Ultimo)
            .FirstOrDefaultAsync();
    }

    private async Task<decimal> StockAsync(int productoId)
    {
        await using var contexto = CrearContexto();
        return await contexto.InventariosAlmacen
            .AsNoTracking()
            .Where(i => i.ProductoId == productoId)
            .Select(i => i.StockActual)
            .SingleAsync();
    }

    private async Task<int> ContarMovimientosAsync(int productoId)
    {
        await using var contexto = CrearContexto();
        return await contexto.MovimientosInventario.CountAsync(m => m.ProductoId == productoId);
    }
}
