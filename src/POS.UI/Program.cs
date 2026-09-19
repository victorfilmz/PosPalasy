using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POS.Application.CasosDeUso.Ventas;
using POS.Application.CasosDeUso.Caja;
using POS.Application.Interfaces;
using POS.Application.Security;
using POS.Application.Services;
using POS.Domain.Repositories;
using POS.Infrastructure.DGII;
using POS.Infrastructure.Persistence;
using POS.Infrastructure.Persistence.Repositories;
using POS.Infrastructure.Security;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;
using POS.UI.Security;

var builder = WebApplication.CreateBuilder(args);

// Servicios MVC. Toda acción requiere autenticación por defecto (el atributo [AllowAnonymous]
// es la única excepción explícita) y toda petición que modifica estado exige token antiforgery.
// Antiforgery EXPLÍCITO (Fase 3): el contrato del terminal es la cabecera RequestVerificationToken.
// Antes dependíamos del valor por defecto del framework; fijarlo evita que un cambio silencioso de
// defaults deje sin poder cobrar a todos los terminales.
builder.Services.AddAntiforgery(opciones =>
{
    opciones.HeaderName = "RequestVerificationToken";
    opciones.Cookie.Name = "PosPalasy.Antiforgery";
    opciones.Cookie.HttpOnly = true;
    // El token de la cookie debe viajar aunque la sesión sea segura; SameSite estricta como la de sesión.
    opciones.Cookie.SameSite = SameSiteMode.Strict;
    opciones.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddControllersWithViews(opciones =>
{
    opciones.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    opciones.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// Base de Datos EF Core
builder.Services.AddDbContext<POSDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configuración DGII
var dgiiConfig = new DgiiConfig();
builder.Configuration.GetSection("DGII").Bind(dgiiConfig);
builder.Services.AddSingleton(dgiiConfig);

// Autenticación por cookie y autorización por políticas (matriz de permisos)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opciones =>
    {
        opciones.LoginPath = "/Cuenta/Login";
        opciones.LogoutPath = "/Cuenta/Logout";
        opciones.AccessDeniedPath = "/Cuenta/AccesoDenegado";
        opciones.ReturnUrlParameter = "returnUrl";
        opciones.Cookie.Name = "PosPalasy.Sesion";
        opciones.Cookie.HttpOnly = true;
        opciones.Cookie.SameSite = SameSiteMode.Strict;
        // En producción la cookie solo viaja por HTTPS; en desarrollo se permite HTTP local.
        opciones.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        opciones.ExpireTimeSpan = TimeSpan.FromHours(12); // duración de un turno
        opciones.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(Politicas.AgregarPoliticas);

// Seguridad de cuentas: hashing de contraseñas y cuenta administradora inicial
builder.Services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
var opcionesAdminInicial = new OpcionesAdminInicial();
builder.Configuration.GetSection("Seguridad:AdminInicial").Bind(opcionesAdminInicial);
builder.Services.AddSingleton(opcionesAdminInicial);

// Servicios de Dominio y Aplicación
builder.Services.AddSingleton<ITaxCalculator, TaxCalculator>();
builder.Services.AddSingleton<IHashGenerator, HashGenerator>();
builder.Services.AddSingleton<IReporteFiscalService, ReporteFiscalService>();

// Servicios de Infraestructura XML
builder.Services.AddSingleton<IXmlSerializer, XmlSerializer>();
builder.Services.AddSingleton<IXmlValidator, XmlValidator>();
builder.Services.AddSingleton<IXmlDigitalSigner, XmlDigitalSigner>();

// Cliente HTTP DGII
builder.Services.AddHttpClient<IDgiiApiClient, DgiiApiClient>();

// Repositorios
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IVentaRepository, VentaRepository>();
builder.Services.AddScoped<IProductoRepository, CommonRepositories>();
builder.Services.AddScoped<IClienteRepository, CommonRepositories>();
builder.Services.AddScoped<IEnterpriseRepository, CommonRepositories>();
builder.Services.AddScoped<IAnulacionRepository, CommonRepositories>();
builder.Services.AddScoped<ICajaTurnoRepository, CajaTurnoRepository>();
builder.Services.AddScoped<IMovimientoInventarioRepository, CommonRepositories>();
builder.Services.AddScoped<IInventarioAlmacenRepository, InventarioAlmacenRepository>();
builder.Services.AddScoped<ISucursalRepository, SucursalRepository>();
builder.Services.AddScoped<IProveedorRepository, ProveedorRepository>();
builder.Services.AddScoped<IEmisionDGIIQueueRepository, EmisionDGIIQueueRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IAutenticacionService, AutenticacionService>();
builder.Services.AddScoped<ISecuenciaECFRepository, SecuenciaECFRepository>();
builder.Services.AddScoped<IAuditoriaRepository, POS.Infrastructure.Persistence.Repositories.AuditoriaRepository>();
builder.Services.AddScoped<IDevolucionRepository, POS.Infrastructure.Persistence.Repositories.DevolucionRepository>();

// Frontera transaccional y casos de uso
builder.Services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
builder.Services.AddScoped<ProcesarVentaHandler>();
builder.Services.AddScoped<RegistrarDevolucionHandler>();

// Servicio Orquestador de Facturación Electrónica DGII
builder.Services.AddSingleton<ISecurityCodeGenerator, POS.Infrastructure.Services.GeneradorCodigoSeguridad>();
builder.Services.AddScoped<IElectronicInvoiceService, DgiiElectronicInvoiceService>();

// Servicio en segundo plano para resiliencia y cola offline DGII
builder.Services.AddHostedService<POS.UI.Services.DgiiQueueBackgroundService>();

// Guarda de entorno: el simulador DGII no puede estar activo fuera de desarrollo.
// Un simulador activo en producción reporta comprobantes "enviados" que nunca salieron del sistema.
if (dgiiConfig.ModoSimulador && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "DGII:ModoSimulador está activo en un entorno que no es Development (" +
        $"{builder.Environment.EnvironmentName}). Configure DGII:ModoSimulador=false antes de operar con la DGII.");
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();

// Obliga el cambio de contraseña antes de permitir cualquier operación.
app.UseMiddleware<CambioPasswordObligatorioMiddleware>();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

// Inicialización de la Base de Datos y Datos Semilla DGII
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<POSDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        await DbInitializer.SeedAsync(dbContext, passwordHasher, opcionesAdminInicial, logger);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        // La inicialización incluye la cuenta administradora: si falla, el sistema queda sin
        // usuarios y nadie podrá iniciar sesión. El aviso debe ser explícito y accionable.
        logger.LogCritical(
            ex,
            "Fallo la inicialización de la base de datos (esquema, datos semilla o cuenta administradora). " +
            "Mientras esto no se corrija, la aplicación no permitirá iniciar sesión a ningún usuario.");
    }
}

app.Run();

/// <summary>
/// Punto de entrada expuesto para las pruebas de integración HTTP (WebApplicationFactory).
/// </summary>
public partial class Program { }
