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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using POS.Application.CasosDeUso.Ventas;
using POS.Application.CasosDeUso.Caja;
using POS.Application.CasosDeUso.Facturacion;
using POS.Application.Interfaces;
using POS.Application.Security;
using POS.Application.Services;
using POS.Domain.Repositories;
using POS.Infrastructure.DGII;
using POS.Infrastructure.DGII.Recording;
using POS.Infrastructure.Persistence;
using POS.Infrastructure.Persistence.Repositories;
using POS.Infrastructure.Security;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;
using POS.UI.Security;
using POS.UI.Services;

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

// Cofre de contratos DGII: cuando GrabarTransmisiones=true, cada respuesta HTTP de la DGII se
// graba en un JSON por sesión (artifacts/cofre-dgii/), insumo para actualizar los contratos
// asumidos de la KB tras el primer contacto real con testecf. Nunca activo fuera de Development.
var rutaCofre = Path.Combine(builder.Environment.ContentRootPath, "artifacts", "cofre-dgii",
    $"sesion-{DateTime.Now:yyyyMMdd-HHmmss}.json");
builder.Services.AddSingleton(sp =>
{
    var grabar = dgiiConfig.GrabarTransmisiones && builder.Environment.IsDevelopment();
    Directory.CreateDirectory(Path.GetDirectoryName(rutaCofre)!);
    return new GrabadorTransmisionesDGII(
        grabar ? rutaCofre : "",
        sp.GetRequiredService<ILogger<GrabadorTransmisionesDGII>>());
});

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

// Cliente HTTP DGII. El autenticador (sub-fase 5.2) emite el token Bearer: semilla → firmar con el
// certificado del emisor → validarsemilla; vigencia 1 h con refresco temprano a los 55 minutos. El
// cliente lo recibe por DI y renueva el token UNA vez ante 401/403 antes de rendirse.
// El autenticador es SINGLETON porque guarda la caché del token: un registro transitorio (typed
// client) la descartaría en cada scope y forzaría re-autenticación en cada envío.
builder.Services.AddHttpClient("DgiiAutenticacion")
    .AddHttpMessageHandler(sp => new GrabadorTransmisionesHandler(
        dgiiConfig.GrabarTransmisiones && builder.Environment.IsDevelopment()
            ? sp.GetRequiredService<GrabadorTransmisionesDGII>()
            : null));
builder.Services.AddSingleton<IDgiiAuthenticator>(sp => new DgiiAuthenticator(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("DgiiAutenticacion"),
    sp.GetRequiredService<DgiiConfig>(),
    sp.GetRequiredService<IProveedorCertificadoDigital>(),
    sp.GetRequiredService<IFirmadorComprobanteECF>(),
    sp.GetRequiredService<ILogger<DgiiAuthenticator>>()));
builder.Services.AddHttpClient<IDgiiApiClient, DgiiApiClient>()
    .AddHttpMessageHandler(sp => new GrabadorTransmisionesHandler(
        dgiiConfig.GrabarTransmisiones && builder.Environment.IsDevelopment()
            ? sp.GetRequiredService<GrabadorTransmisionesDGII>()
            : null));

// Health checks de producción: BD (el recurso crítico), certificado (sin él no se firma) y
// worker de cola (sin él los comprobantes no salen). El mapa /health responde 503 si alguno falla.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<POSDbContext>("base-de-datos")
    .AddCheck<CertificadoDigitalHealthCheck>("certificado-digital", HealthStatus.Degraded)
    .AddCheck<WorkerColaDgiiHealthCheck>("worker-cola-dgii", HealthStatus.Degraded);

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
builder.Services.AddScoped<ISecuenciaLibreRepository, SecuenciaLibreRepository>();
builder.Services.AddScoped<ISecuenciaECFRepository, SecuenciaECFRepository>();
builder.Services.AddScoped<IAuditoriaRepository, POS.Infrastructure.Persistence.Repositories.AuditoriaRepository>();
builder.Services.AddScoped<IDevolucionRepository, POS.Infrastructure.Persistence.Repositories.DevolucionRepository>();

// Frontera transaccional y casos de uso
builder.Services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajo>();
builder.Services.AddScoped<ProcesarVentaHandler>();
builder.Services.AddScoped<RegistrarDevolucionHandler>();
builder.Services.AddScoped<EmitirNotaCreditoDevolucionHandler>();

// Servicio Orquestador de Facturación Electrónica DGII. La firma XML-DSig (sub-fase 5.1) exige el
// certificado del emisor y el modo de operación (simulador/real): registro explícito para inyectarlos.
builder.Services.AddSingleton<ISecurityCodeGenerator, POS.Infrastructure.Services.GeneradorCodigoSeguridad>();
builder.Services.AddSingleton<IProveedorCertificadoDigital, ProveedorCertificadoDigital>();
builder.Services.AddSingleton<IFirmadorComprobanteECF, FirmadorComprobanteECF>();

// Health checks concretos (usados por el mapa /health)
builder.Services.AddSingleton<CertificadoDigitalHealthCheck>();
builder.Services.AddSingleton<WorkerColaDgiiHealthCheck>();
builder.Services.AddSingleton<POS.UI.Services.DgiiQueueBackgroundService>();
builder.Services.AddScoped<IElectronicInvoiceService>(sp => new DgiiElectronicInvoiceService(
    sp.GetRequiredService<IXmlSerializer>(),
    sp.GetRequiredService<IXmlValidator>(),
    sp.GetRequiredService<IHashGenerator>(),
    sp.GetRequiredService<IDgiiApiClient>(),
    sp.GetRequiredService<IInvoiceRepository>(),
    sp.GetRequiredService<IAnulacionRepository>(),
    sp.GetRequiredService<IEmisionDGIIQueueRepository>(),
    sp.GetRequiredService<ILogger<DgiiElectronicInvoiceService>>(),
    codigoSeguridad: sp.GetRequiredService<ISecurityCodeGenerator>(),
    proveedorCertificado: sp.GetRequiredService<IProveedorCertificadoDigital>(),
    firmador: sp.GetRequiredService<IFirmadorComprobanteECF>(),
    dgiiConfig: dgiiConfig,
    secuenciasLibresRepository: sp.GetRequiredService<ISecuenciaLibreRepository>(),
    auditoriaRepository: sp.GetRequiredService<IAuditoriaRepository>()));

// Servicio en segundo plano para resiliencia y cola offline DGII (misma instancia que consulta
// su health check: es singleton, el AddHostedService reutiliza el registro anterior).
builder.Services.AddHostedService(sp => sp.GetRequiredService<POS.UI.Services.DgiiQueueBackgroundService>());

// Guarda de entorno: el simulador DGII no puede estar activo fuera de desarrollo.
// Un simulador activo en producción reporta comprobantes "enviados" que nunca salieron del sistema.
if (dgiiConfig.ModoSimulador && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "DGII:ModoSimulador está activo en un entorno que no es Development (" +
        $"{builder.Environment.EnvironmentName}). Configure DGII:ModoSimulador=false antes de operar con la DGII.");
}

// Logging persistente a archivo (producción): un incidente debe dejar evidencia aunque nadie
// esté mirando la consola. %LOCALAPPDATA%\PosPalasy\logs\pospalasy-AAAAMMDD.log con rotación
// diaria y sin dependencias externas. Se registra ANTES de Build() porque la colección de
// servicios queda read-only después. En Development la consola es la fuente principal.
if (!builder.Environment.IsDevelopment())
{
    var directorioLogs = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PosPalasy", "logs");
    Directory.CreateDirectory(directorioLogs);
    builder.Logging.AddProvider(new ArchivoLoggerProvider(directorioLogs));
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

// Health check de producción (sin exponer detalle interno): solo dice si el sistema está vivo.
// La autenticación NO aplica a este endpoint (queda antes de UseAuthentication) porque un
// monitor externo no tiene sesión; no expone datos — únicamente 200 Healthy / 503 Unhealthy.
app.MapHealthChecks("/health");

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
/// Logger de archivo mínimo con rotación diaria (pospalasy-AAAAMMDD.log): una sola cola de
/// escritura con append atómico por línea. Suficiente para evidencia de incidentes; el volumen
/// alto lo limita el nivel de appsettings (EF en Warning). Sin paquetes externos.
/// </summary>
public sealed class ArchivoLoggerProvider : ILoggerProvider
{
    private readonly string _directorio;
    private readonly object _cerrojo = new();
    private StreamWriter? _escritor;
    private DateTime _fechaActual;

    public ArchivoLoggerProvider(string directorio) => _directorio = directorio;

    public ILogger CreateLogger(string categoryName) => new ArchivoLogger(this, categoryName);

    private void Escribir(DateTime utcAhora, string linea)
    {
        lock (_cerrojo)
        {
            try
            {
                if (_escritor is null || _fechaActual != utcAhora.Date)
                {
                    _escritor?.Dispose();
                    _fechaActual = utcAhora.Date;
                    _escritor = new StreamWriter(
                        Path.Combine(_directorio, $"pospalasy-{utcAhora:yyyyMMdd}.log"), append: true);
                }
                _escritor.WriteLine(linea);
                _escritor.Flush();
            }
            catch (IOException)
            {
                // El log no puede tumbar la aplicación: si el disco falla, se pierde la línea y sigue.
            }
        }
    }

    public void Dispose() => _escritor?.Dispose();

    private sealed class ArchivoLogger : ILogger
    {
        private readonly ArchivoLoggerProvider _proveedor;
        private readonly string _categoria;

        public ArchivoLogger(ArchivoLoggerProvider proveedor, string categoria)
        {
            _proveedor = proveedor;
            _categoria = categoria;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var utcAhora = DateTime.UtcNow;
            _proveedor.Escribir(utcAhora,
                $"{utcAhora:yyyy-MM-dd HH:mm:ss.fff} [{logLevel}] {_categoria}: {formatter(state, exception)}" +
                (exception is null ? "" : $"\n{exception}"));
        }
    }
}

/// <summary>
/// Punto de entrada expuesto para las pruebas de integración HTTP (WebApplicationFactory).
/// </summary>
public partial class Program { }
