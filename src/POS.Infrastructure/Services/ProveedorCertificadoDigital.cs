using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using POS.Domain.Common;

namespace POS.Infrastructure.Services;

/// <summary>
/// Contrato de resolución del certificado digital del emisor en runtime. La firma XML-DSig de
/// comprobantes exige clave privada: sin certificado válido, nada se transmite a la DGII.
/// </summary>
public interface IProveedorCertificadoDigital
{
    /// <summary>
    /// Devuelve el certificado con clave privada, o <c>null</c> si no hay certificado instalado.
    /// Lanza <see cref="ReglaDeNegocioException"/> si el certificado existe pero no sirve para
    /// firmar (sin clave privada, vencido).
    /// </summary>
    X509Certificate2? ObtenerCertificado();

    /// <summary>Estado legible del certificado para mensajes de error y pantalla de configuración.</summary>
    string DescribirEstado();
}

/// <summary>
/// Resuelve el certificado A1 (.pfx) del emisor con la MISMA convención de rutas que la pantalla de
/// Configuración: ruta absoluta se respeta; ruta relativa se ancla al directorio de datos local
/// (%LOCALAPPDATA%\PosPalasy\certificados por defecto). La contraseña nunca se versiona: entra por
/// user-secrets o variable de entorno (Certificado:Password).
/// </summary>
/// <remarks>
/// La carga se cachea y se invalida por fecha de modificación del archivo: instalar un certificado
/// nuevo desde Configuración toma efecto sin reiniciar el proceso. La clave privada se mantiene
/// efímera en memoria (EphemeralKeySet): nunca se persiste al almacén de claves de la máquina.
/// </remarks>
public sealed class ProveedorCertificadoDigital : IProveedorCertificadoDigital
{
    public const int DiasAvisoExpiracion = 30;

    private readonly string _rutaPfx;
    private readonly string? _password;
    private readonly object _cerrojo = new();
    private X509Certificate2? _cache;
    private DateTime _ultimaModificacionUtc;

    /// <summary>Constructor de producción: resuelve la ruta desde la configuración.</summary>
    public ProveedorCertificadoDigital(IConfiguration configuration)
        : this(ResolverRuta(configuration), configuration["Certificado:Password"])
    {
    }

    /// <summary>Constructor directo para pruebas y alojamiento especial.</summary>
    public ProveedorCertificadoDigital(string rutaPfx, string? password)
    {
        if (string.IsNullOrWhiteSpace(rutaPfx))
            throw new ArgumentException("La ruta del certificado no puede estar vacía.", nameof(rutaPfx));

        _rutaPfx = Path.GetFullPath(rutaPfx);
        _password = password;
    }

    public X509Certificate2? ObtenerCertificado()
    {
        lock (_cerrojo)
        {
            if (!File.Exists(_rutaPfx))
            {
                _cache = null;
                return null;
            }

            var modificacion = File.GetLastWriteTimeUtc(_rutaPfx);
            if (_cache != null && modificacion == _ultimaModificacionUtc)
                return _cache;

            // La carga de un .pfx corrupto o con contraseña errónea es un fallo de configuración,
            // no un estado transitorio: se propaga como error permanente con diagnóstico claro.
            var certificado = X509CertificateLoader.LoadPkcs12FromFile(
                _rutaPfx, _password, X509KeyStorageFlags.EphemeralKeySet);

            ValidarCertificado(certificado);

            _cache = certificado;
            _ultimaModificacionUtc = modificacion;
            return certificado;
        }
    }

    public string DescribirEstado()
    {
        if (!File.Exists(_rutaPfx))
            return $"No hay certificado digital instalado en '{_rutaPfx}'.";

        lock (_cerrojo)
        {
            if (_cache != null)
                return DescribirCertificado(_cache);
        }

        try
        {
            using var inspeccion = X509CertificateLoader.LoadPkcs12FromFile(_rutaPfx, _password);
            return DescribirCertificado(inspeccion);
        }
        catch (Exception ex)
        {
            return $"El certificado en '{_rutaPfx}' no se pudo abrir (¿contraseña incorrecta?): {ex.Message}";
        }
    }

    private static void ValidarCertificado(X509Certificate2 certificado)
    {
        if (!certificado.HasPrivateKey)
            throw new ReglaDeNegocioException(
                "El certificado digital no contiene clave privada: no puede firmar comprobantes.",
                "CERTIFICADO_SIN_CLAVE_PRIVADA");

        if (certificado.NotAfter < DateTime.Now)
            throw new ReglaDeNegocioException(
                $"El certificado digital expiró el {certificado.NotAfter:dd-MM-yyyy}. " +
                "Instale un certificado vigente antes de emitir comprobantes.",
                "CERTIFICADO_VENCIDO");
    }

    private static string DescribirCertificado(X509Certificate2 certificado)
    {
        var sujeto = certificado.SubjectName.Name;
        var vigencia = $"vence el {certificado.NotAfter:dd-MM-yyyy}";
        var dias = (certificado.NotAfter - DateTime.Now).TotalDays;

        if (dias < 0)
            return $"Certificado de '{sujeto}' VENCIDO ({vigencia}).";
        if (!certificado.HasPrivateKey)
            return $"Certificado de '{sujeto}' ({vigencia}) SIN clave privada: no puede firmar.";
        if (dias <= DiasAvisoExpiracion)
            return $"Certificado de '{sujeto}' ({vigencia}): ATENCIÓN, expira en {Math.Ceiling(dias)} días.";

        return $"Certificado de '{sujeto}' ({vigencia}).";
    }

    private static string ResolverRuta(IConfiguration configuration)
    {
        var configurada = configuration["Certificado:RutaCertificado"];

        if (!string.IsNullOrWhiteSpace(configurada) && Path.IsPathRooted(configurada))
            return Path.GetFullPath(configurada);

        var nombreArchivo = string.IsNullOrWhiteSpace(configurada)
            ? "emisor.pfx"
            : Path.GetFileName(configurada);

        var directorioConfigurado = configuration["Certificado:DirectorioDatos"];
        var directorio = !string.IsNullOrWhiteSpace(directorioConfigurado)
            ? directorioConfigurado
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PosPalasy",
                "certificados");

        return Path.Combine(Path.GetFullPath(directorio), nombreArchivo);
    }
}
