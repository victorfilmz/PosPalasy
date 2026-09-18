using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using POS.Application.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;
using POS.Infrastructure.Persistence;
using Xunit;

namespace POS.UI.SecurityTests;

/// <summary>
/// Verificaciones de FASE 3: contrato antiforgery explícito y política de existencias de tres
/// estados impuesta por el servidor (Bloquear y Advertir; Permitir ya se ejercita en las pruebas E2E).
/// </summary>
public class Fase3Tests : IClassFixture<PosAppFactory>
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    private readonly PosAppFactory _app;

    public Fase3Tests(PosAppFactory app) => _app = app;

    // ------------------------------------------------------------------ OBJ 4: antiforgery explícito

    /// <summary>
    /// El contrato antiforgery debe estar FIJADO en la configuración de la aplicación, no delegado a
    /// los valores por defecto del framework: un cambio silencioso de defaults dejaría sin cobrar a
    /// todos los terminales. Esta prueba es la barrera.
    /// </summary>
    [Fact]
    public void Antiforgery_HeaderName_DebeEstarExplícitoYFijado()
    {
        var opciones = _app.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;

        Assert.Equal("RequestVerificationToken", opciones.HeaderName);
        Assert.Equal("PosPalasy.Antiforgery", opciones.Cookie.Name);
        Assert.True(opciones.Cookie.HttpOnly);
    }

    // ------------------------------------------------------------------ OBJ 2: política BLOQUEAR por HTTP

    /// <summary>
    /// Con política BLOQUEAR, una venta por encima de la existencia se rechaza por HTTP y no deja
    /// rastro alguno: ni venta, ni kardex, ni caja, ni número fiscal consumido.
    /// </summary>
    [Fact]
    public async Task VentaPorHttp_PoliticaBloquear_RechazaYNoDejaRastro()
    {
        await EstablecerPoliticaAsync(PoliticaStock.Bloquear);

        var productoId = await SembrarProductoAsync(precio: 50m, stock: 1m);
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

        Assert.Equal(0, await ContarVentasDeAsync(clave));
        Assert.Equal(1m, await StockAsync(productoId));
        Assert.Equal(0, await ContarMovimientosAsync(productoId));
        Assert.Equal(consecutivoAntes, await ConsecutivoE32Async());
        Assert.Equal(0, await TransaccionesDeCajaAsync(sesion.TurnoId));
    }

    // ------------------------------------------------------------------ OBJ 2: política ADVERTIR por HTTP

    /// <summary>
    /// Con política ADVERTIR la venta sobre-existente PROSIGUE, pero queda marcada en la venta y en
    /// el kardex (RequiereRevision) para revisión: no es un comportamiento silencioso.
    /// </summary>
    [Fact]
    public async Task VentaPorHttp_PoliticaAdvertir_RegistraLaVentaMarcadaParaRevision()
    {
        await EstablecerPoliticaAsync(PoliticaStock.Advertir);

        var productoId = await SembrarProductoAsync(precio: 50m, stock: 1m);
        var sesion = await CrearCajeroConTurnoAsync();
        var clave = Guid.NewGuid();

        var respuesta = await PostVentaAsync(sesion, new VentaPosRequest
        {
            ClaveIdempotencia = clave,
            MetodoPago = MetodoPago.Efectivo,
            MontoRecibido = 500m,
            Items = new List<VentaPosItemRequest> { new() { ProductoId = productoId, Cantidad = 3m } }
        });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        var resultado = await LeerAsync<VentaPosResponse>(respuesta);
        Assert.True(resultado.Exitoso, resultado.Mensaje);

        await using var contexto = CrearContexto();

        // Venta marcada y cobrada con normalidad.
        var venta = await contexto.Ventas.AsNoTracking().SingleAsync(v => v.ClaveIdempotencia == clave);
        Assert.True(venta.RequiereRevisionStock);
        Assert.Equal(3m * 50m * 1.18m, venta.Total);

        // Kardex documentado con la advertencia y la marca de revisión.
        var movimiento = await contexto.MovimientosInventario.AsNoTracking().SingleAsync(m => m.ProductoId == productoId);
        Assert.True(movimiento.RequiereRevision);
        Assert.Contains("ADVERTENCIA", movimiento.Concepto);
        Assert.Equal(1m, movimiento.StockAnterior);
        Assert.Equal(-2m, movimiento.StockNuevo);

        // Caja y comprobante: la venta es normal en todo lo demás.
        Assert.Equal(1, await TransaccionesDeCajaAsync(sesion.TurnoId));

        var enCola = await contexto.EmisionesDGIIQueue.AsNoTracking()
            .SingleAsync(q => q.eNCF == resultado.eNCF);
        Assert.False(enCola.EnviadoExitosamente == false && resultado.EsOfflineDGII && resultado.Exitoso == false);
    }

    /// <summary>
    /// Con política ADVERTIR, una venta DENTRO de la existencia no se marca: la advertencia es solo
    /// para las salidas sobre-existentes.
    /// </summary>
    [Fact]
    public async Task VentaPorHttp_PoliticaAdvertir_VentaNormal_NoQuedaMarcada()
    {
        await EstablecerPoliticaAsync(PoliticaStock.Advertir);

        var productoId = await SembrarProductoAsync(precio: 50m, stock: 10m);
        var sesion = await CrearCajeroConTurnoAsync();
        var clave = Guid.NewGuid();

        var respuesta = await PostVentaAsync(sesion, new VentaPosRequest
        {
            ClaveIdempotencia = clave,
            MetodoPago = MetodoPago.Efectivo,
            MontoRecibido = 100m,
            Items = new List<VentaPosItemRequest> { new() { ProductoId = productoId, Cantidad = 1m } }
        });

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        await using var contexto = CrearContexto();

        var venta = await contexto.Ventas.AsNoTracking().SingleAsync(v => v.ClaveIdempotencia == clave);
        Assert.False(venta.RequiereRevisionStock);

        var movimiento = await contexto.MovimientosInventario.AsNoTracking().SingleAsync(m => m.ProductoId == productoId);
        Assert.False(movimiento.RequiereRevision);
        Assert.DoesNotContain("ADVERTENCIA", movimiento.Concepto);
    }

    // ------------------------------------------------------------------ OBJ 3: auditoría del cambio de política

    /// <summary>
    /// Cambiar la política de stock por la pantalla de configuración deja la traza completa:
    /// usuario, fecha, valor anterior, valor nuevo y motivo.
    /// </summary>
    [Fact]
    public async Task CambioDePoliticaStock_QuedaAuditadoConUsuarioValoresYMotivo()
    {
        await EstablecerPoliticaAsync(PoliticaStock.Permitir);

        var cliente = await CrearSesionAdminAsync();

        var html = await cliente.GetStringAsync("/Configuracion/Empresa");
        var token = PosAppFactory.ExtraerTokenAntiforgery(html);

        // El formulario envía los campos del DTO; el motivo es lo que debe aparecer en la auditoría.
        var respuesta = await cliente.PostAsync("/Configuracion/GuardarEmpresa", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["RNC"] = "13100000001",
                ["RazonSocial"] = "PosPalasy SRL",
                ["PoliticaStock"] = "Bloquear",
                ["TipoComprobantePredeterminado"] = "32",
                ["CodigoProvincia"] = "01",
                ["CodigoMunicipio"] = "010100",
                ["MotivoCambioPoliticaStock"] = "Inventario detenido: congelar ventas sin existencia",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);

        await using var contexto = CrearContexto();

        var empresa = await contexto.Enterprises.AsNoTracking().FirstAsync();
        Assert.Equal(PoliticaStock.Bloquear, empresa.PoliticaStock);

        var auditoria = await contexto.AuditoriaCambios.AsNoTracking()
            .Where(a => a.Entidad == nameof(Enterprise) && a.Campo == nameof(Enterprise.PoliticaStock))
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditoria);
        Assert.Equal("Permitir", auditoria!.ValorAnterior);
        Assert.Equal("Bloquear", auditoria.ValorNuevo);
        Assert.Equal("Inventario detenido: congelar ventas sin existencia", auditoria.Motivo);
        Assert.Equal(PosAppFactory.UsuarioAdmin, auditoria.Usuario);
        Assert.True(auditoria.FechaUtc <= DateTime.UtcNow.AddMinutes(1));
    }

    // ------------------------------------------------------------------ utilidades

    /// <summary>Sesión HTTP del administrador sembrado (para la pantalla de configuración).</summary>
    private async Task<HttpClient> CrearSesionAdminAsync()
    {
        var cliente = _app.CrearCliente();

        var html = await cliente.GetStringAsync("/Cuenta/Login");
        var token = PosAppFactory.ExtraerTokenAntiforgery(html);

        var login = await cliente.PostAsync("/Cuenta/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Usuario"] = PosAppFactory.UsuarioAdmin,
            ["Password"] = PosAppFactory.PasswordAdmin,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return cliente;
    }

    private sealed record Sesion(HttpClient Cliente, string Token, int TurnoId);

    private POSDbContext CrearContexto()
    {
        var scope = _app.Services.CreateScope();
        return new POSDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<POSDbContext>>());
    }

    private async Task EstablecerPoliticaAsync(PoliticaStock politica)
    {
        await using var contexto = CrearContexto();

        var empresa = await contexto.Enterprises.FirstAsync();
        empresa.PoliticaStock = politica;

        await contexto.SaveChangesAsync();
    }

    private async Task<int> SembrarProductoAsync(decimal precio, decimal stock)
    {
        await using var contexto = CrearContexto();

        var sucursal = await contexto.Sucursales.AsNoTracking().OrderBy(s => s.Id).FirstAsync();

        var producto = new Producto
        {
            Codigo = "F3-" + Guid.NewGuid().ToString("N")[..10],
            Descripcion = "Producto Fase 3",
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

    private async Task<Sesion> CrearCajeroConTurnoAsync()
    {
        var nombre = "f3-" + Guid.NewGuid().ToString("N")[..10];
        _app.CrearUsuario(nombre, RolUsuario.Cajero);

        var cliente = _app.CrearCliente();

        var html = await cliente.GetStringAsync("/Cuenta/Login");
        var tokenLogin = PosAppFactory.ExtraerTokenAntiforgery(html);

        var login = await cliente.PostAsync("/Cuenta/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Usuario"] = nombre,
            ["Password"] = PosAppFactory.PasswordAuxiliar,
            ["__RequestVerificationToken"] = tokenLogin
        }));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var token = PosAppFactory.ExtraerTokenAntiforgery(await cliente.GetStringAsync("/Pos"));

        var apertura = await cliente.PostAsync("/Caja/Apertura", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["cajero"] = "Cajero Fase 3",
            ["montoInicial"] = "1000",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, apertura.StatusCode);

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
            PosAppFactory.ExtraerTokenAntiforgery(await cliente.GetStringAsync("/Pos")),
            turno?.Id ?? 0);
    }

    private static Task<HttpResponseMessage> PostVentaAsync(Sesion sesion, VentaPosRequest payload)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "/Pos/ProcesarVenta")
        {
            Content = JsonContent.Create(payload, options: JsonWeb)
        };

        peticion.Headers.Add("RequestVerificationToken", sesion.Token);
        return sesion.Cliente.SendAsync(peticion);
    }

    private static async Task<T> LeerAsync<T>(HttpResponseMessage respuesta)
    {
        var contenido = await respuesta.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<T>(contenido, JsonWeb)
            ?? throw new InvalidOperationException($"Respuesta inesperada: {contenido}");
    }

    private async Task<int> ContarVentasDeAsync(Guid clave)
    {
        await using var contexto = CrearContexto();
        return await contexto.Ventas.CountAsync(v => v.ClaveIdempotencia == clave);
    }

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

    private async Task<int> TransaccionesDeCajaAsync(int turnoId)
    {
        await using var contexto = CrearContexto();
        return await contexto.CajaTurnos
            .AsNoTracking()
            .Where(t => t.Id == turnoId)
            .Select(t => t.CantidadTransacciones)
            .SingleAsync();
    }
}
