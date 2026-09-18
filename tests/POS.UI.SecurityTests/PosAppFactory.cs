using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using POS.Application.Interfaces;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Infrastructure.Persistence;

namespace POS.UI.SecurityTests;

/// <summary>
/// Host de pruebas de la aplicación real (mismo pipeline HTTP que producción) con la base de datos
/// sustituida por SQLite en memoria. Permite verificar que los controles de seguridad se aplican
/// de extremo a extremo y no solo como configuración declarada.
/// </summary>
public class PosAppFactory : WebApplicationFactory<Program>
{
    public const string UsuarioAdmin = "admin-pruebas";

    /// <summary>Contraseña de la cuenta sembrada en las pruebas (cumple la política).</summary>
    public const string PasswordAdmin = "Pruebas-PosPalasy-2026!";

    /// <summary>Contraseña de las cuentas auxiliares creadas por las pruebas.</summary>
    public const string PasswordAuxiliar = "Auxiliar-PosPalasy-2026!";

    private readonly SqliteConnection _conexion = new("DataSource=:memory:");

    static PosAppFactory()
    {
        // La configuración del host se fija ANTES de que Program.cs arranque: la lectura de
        // Seguridad:AdminInicial y las decisiones de seguridad evaluadas durante el registro de
        // servicios (cookie Secure en producción, guarda del simulador DGII) ocurren en ese punto,
        // antes de que WebApplicationFactory aplique UseEnvironment/ConfigureAppConfiguration.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("Seguridad__AdminInicial__Usuario", UsuarioAdmin);
        Environment.SetEnvironmentVariable("Seguridad__AdminInicial__Password", PasswordAdmin);
        Environment.SetEnvironmentVariable("Seguridad__AdminInicial__NombreCompleto", "Administrador de Pruebas");
        Environment.SetEnvironmentVariable("DGII__ModoSimulador", "true");
    }

    public PosAppFactory()
    {
        _conexion.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuracion) =>
        {
            configuracion.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seguridad:AdminInicial:Usuario"] = UsuarioAdmin,
                ["Seguridad:AdminInicial:Password"] = PasswordAdmin,
                ["Seguridad:AdminInicial:NombreCompleto"] = "Administrador de Pruebas",
                ["DGII:ModoSimulador"] = "true"
            });
        });

        builder.ConfigureServices(servicios =>
        {
            // Se eliminan TODAS las configuraciones de opciones del contexto (SQL Server + sus helpers):
            // dejar una sola evita el error "Services for database providers ... have been registered".
            var aRemover = servicios
                .Where(d => d.ServiceType == typeof(POSDbContext)
                         || d.ServiceType == typeof(DbContextOptions<POSDbContext>)
                         || d.ServiceType.Name.StartsWith("DbContextOptions", StringComparison.Ordinal)
                         || d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal))
                .ToList();

            foreach (var descriptor in aRemover)
                servicios.Remove(descriptor);

            servicios.AddDbContext<POSDbContext>(opciones => opciones.UseSqlite(_conexion));
        });
    }

    /// <summary>Cliente HTTP que no sigue redirecciones (permite afirmar sobre el redirect de login/403).</summary>
    public HttpClient CrearCliente() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    /// <summary>Crea (una sola vez) un usuario auxiliar con el rol indicado y devuelve su nombre.</summary>
    public string CrearUsuario(string nombreUsuario, RolUsuario rol)
    {
        using var scope = Services.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<POSDbContext>();

        if (!contexto.Usuarios.Any(u => u.NombreUsuario == nombreUsuario))
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            var usuario = new Usuario
            {
                NombreUsuario = nombreUsuario,
                NombreCompleto = $"Usuario {rol} de pruebas",
                Rol = rol,
                EstaActivo = true
            };

            usuario.EstablecerPassword(hasher.Hash(PasswordAuxiliar));

            contexto.Usuarios.Add(usuario);
            contexto.SaveChanges();
        }

        return nombreUsuario;
    }

    /// <summary>
    /// Crea un usuario marcado con cambio de contraseña obligatorio (equivalente a una cuenta
    /// creada con contraseña temporal generada por el sistema).
    /// </summary>
    public string CrearUsuarioConCambioObligatorio(string nombreUsuario)
    {
        using var scope = Services.CreateScope();
        var contexto = scope.ServiceProvider.GetRequiredService<POSDbContext>();

        if (!contexto.Usuarios.Any(u => u.NombreUsuario == nombreUsuario))
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            var usuario = new Usuario
            {
                NombreUsuario = nombreUsuario,
                NombreCompleto = "Usuario con cambio obligatorio",
                Rol = RolUsuario.Cajero,
                EstaActivo = true,
                PasswordHash = hasher.Hash(PasswordAuxiliar),
                DebeCambiarPassword = true
            };

            contexto.Usuarios.Add(usuario);
            contexto.SaveChanges();
        }

        return nombreUsuario;
    }

    /// <summary>Lee el token antiforgery de una página HTML renderizada.</summary>
    public static string ExtraerTokenAntiforgery(string html)
    {
        var coincidencia = System.Text.RegularExpressions.Regex.Match(
            html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

        if (!coincidencia.Success)
            throw new InvalidOperationException("No se encontró el token antiforgery en el HTML devuelto.");

        return coincidencia.Groups[1].Value;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _conexion.Dispose();
    }
}
