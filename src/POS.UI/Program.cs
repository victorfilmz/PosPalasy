using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using POS.Application.Interfaces;
using POS.Application.Services;
using POS.Domain.Repositories;
using POS.Infrastructure.DGII;
using POS.Infrastructure.Persistence;
using POS.Infrastructure.Persistence.Repositories;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Base de Datos EF Core
builder.Services.AddDbContext<POSDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configuración DGII
var dgiiConfig = new DgiiConfig();
builder.Configuration.GetSection("DGII").Bind(dgiiConfig);
builder.Services.AddSingleton(dgiiConfig);

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

// Servicio Orquestador de Facturación Electrónica DGII
builder.Services.AddScoped<IElectronicInvoiceService, DgiiElectronicInvoiceService>();

// Servicio en segundo plano para resiliencia y cola offline DGII
builder.Services.AddHostedService<POS.UI.Services.DgiiQueueBackgroundService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

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
        await DbInitializer.SeedAsync(dbContext);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Aviso: No se pudo conectar a SQL Server para inicializar datos semilla. La aplicación continuará funcionando.");
    }
}

app.Run();
