using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using POS.Application.DTOs;
using POS.Domain.Repositories;
using POS.Infrastructure.DGII;
using POS.UI.Security;

namespace POS.UI.Controllers;

/// <summary>
/// Configuración del sistema: datos de la empresa, certificado digital, formato de impresión y
/// ambiente DGII. El cambio de ambiente queda reservado al rol SuperAdmin por su impacto fiscal
/// sobre todos los usuarios.
/// </summary>
[Authorize(Policy = Politicas.Configuracion)]
public class ConfiguracionController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly DgiiConfig _dgiiConfig;
    private readonly IEnterpriseRepository _enterpriseRepo;
    private readonly HttpClient _httpClient;

    public ConfiguracionController(
        IConfiguration configuration,
        DgiiConfig dgiiConfig,
        IEnterpriseRepository enterpriseRepo,
        HttpClient httpClient)
    {
        _configuration = configuration;
        _dgiiConfig = dgiiConfig;
        _enterpriseRepo = enterpriseRepo;
        _httpClient = httpClient;
    }

    [HttpGet]
    public async Task<IActionResult> Certificado()
    {
        var rutaCert = ObtenerRutaCertificado();
        var password = _configuration["Certificado:Password"] ?? "";

        var dto = new ConfiguracionCertificadoDto
        {
            RutaArchivo = rutaCert,
            BaseUrlDgii = _dgiiConfig.BaseUrl,
            AmbienteActual = _dgiiConfig.BaseUrl.Contains("test", StringComparison.OrdinalIgnoreCase) ? "TestECF" : "Produccion"
        };

        if (System.IO.File.Exists(rutaCert))
        {
            dto.TieneCertificado = true;
            try
            {
                using var cert = X509CertificateLoader.LoadPkcs12FromFile(rutaCert, password, X509KeyStorageFlags.EphemeralKeySet);
                dto.Sujeto = cert.Subject;
                dto.EmisorCertificado = cert.Issuer;
                dto.NumeroSerie = cert.SerialNumber;
                dto.HuellaDigitalSHA1 = cert.Thumbprint;
                dto.ValidoDesde = cert.NotBefore;
                dto.ValidoHasta = cert.NotAfter;
                dto.EstaVigente = DateTime.Now >= cert.NotBefore && DateTime.Now <= cert.NotAfter;
                dto.DiasRestantes = (int)(cert.NotAfter - DateTime.Now).TotalDays;
            }
            catch (Exception ex)
            {
                dto.ErrorCarga = $"No se pudo abrir el certificado: {ex.Message} (Verifique la contraseña).";
            }
        }

        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        ViewBag.Enterprise = enterprise;

        return View(dto);
    }

    [HttpPost]
    public async Task<IActionResult> CargarCertificado(IFormFile? pfxFile, string? password)
    {
        if (pfxFile == null || pfxFile.Length == 0)
        {
            TempData["Error"] = "Debe seleccionar un archivo de certificado digital (.pfx o .p12).";
            return RedirectToAction(nameof(Certificado));
        }

        var ext = Path.GetExtension(pfxFile.FileName).ToLowerInvariant();
        if (ext != ".pfx" && ext != ".p12")
        {
            TempData["Error"] = "El archivo debe tener extensión .pfx o .p12.";
            return RedirectToAction(nameof(Certificado));
        }

        byte[] certBytes;
        using (var ms = new MemoryStream())
        {
            await pfxFile.CopyToAsync(ms);
            certBytes = ms.ToArray();
        }

        // Validar contraseña cargando el certificado
        try
        {
            using var testCert = X509CertificateLoader.LoadPkcs12(certBytes, password ?? string.Empty, X509KeyStorageFlags.EphemeralKeySet);
            if (!testCert.HasPrivateKey)
            {
                TempData["Error"] = "El certificado seleccionado no contiene la clave privada requerida para firmar documentos XML-DSig.";
                return RedirectToAction(nameof(Certificado));
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Contraseña incorrecta o archivo de certificado corrupto: {ex.Message}";
            return RedirectToAction(nameof(Certificado));
        }

        // Guardar el certificado fuera del directorio de la aplicación: no se pierde al publicar
        // ni queda accesible desde el contenido web servido.
        var certDir = ObtenerDirectorioDatos();
        if (!Directory.Exists(certDir)) Directory.CreateDirectory(certDir);

        var destPath = Path.Combine(certDir, "emisor.pfx");
        await System.IO.File.WriteAllBytesAsync(destPath, certBytes);

        // La contraseña del certificado nunca se persiste: debe definirse en configuración
        // (user-secrets o variable de entorno) y coincidir con la usada al instalarlo.
        TempData["Mensaje"] = $"Certificado digital .pfx instalado y validado en '{destPath}'. " +
            "Defina Certificado:Password por user-secrets o variable de entorno para firmar los comprobantes.";
        return RedirectToAction(nameof(Certificado));
    }

    [HttpPost]
    [Authorize(Policy = Politicas.CambioAmbiente)]
    public IActionResult CambiarAmbiente(string ambiente)
    {
        if (ambiente == "Produccion")
        {
            _dgiiConfig.BaseUrl = "https://dfe.dgii.gov.do";
            TempData["Mensaje"] = "Ambiente cambiado a DGII Producción (dfe.dgii.gov.do).";
        }
        else
        {
            _dgiiConfig.BaseUrl = "https://ecf.dgii.gov.do/testecf";
            TempData["Mensaje"] = "Ambiente cambiado a DGII Homologación / Certificación (TestECF).";
        }

        return RedirectToAction(nameof(Certificado));
    }

    [HttpPost]
    [Authorize(Policy = Politicas.CambioAmbiente)]
    public async Task<IActionResult> ProbarConectividad()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var testUrl = $"{_dgiiConfig.BaseUrl.TrimEnd('/')}/fe/autenticacion/api";
            using var response = await _httpClient.GetAsync(_dgiiConfig.BaseUrl);
            sw.Stop();

            TempData["Mensaje"] = $"Conectividad exitosa con DGII ({_dgiiConfig.BaseUrl}). Tiempo de respuesta: {sw.ElapsedMilliseconds} ms. Estado HTTP: {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            sw.Stop();
            TempData["Error"] = $"Fallo al conectar con {_dgiiConfig.BaseUrl}: {ex.Message} (Latencia: {sw.ElapsedMilliseconds} ms).";
        }

        return RedirectToAction(nameof(Certificado));
    }

    [HttpGet]
    public async Task<IActionResult> Empresa()
    {
        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        if (enterprise == null)
        {
            TempData["Error"] = "No se encontró la empresa configurada.";
            return RedirectToAction(nameof(Certificado));
        }

        var dto = new ConfiguracionEmpresaDto
        {
            RNC = enterprise.RNC,
            RazonSocial = enterprise.RazonSocial,
            NombreComercial = enterprise.NombreComercial,
            Direccion = enterprise.Direccion,
            Telefono = enterprise.Telefono,
            Email = enterprise.Email,
            SitioWeb = enterprise.SitioWeb,
            CodigoProvincia = enterprise.CodigoProvincia,
            CodigoMunicipio = enterprise.CodigoMunicipio,
            TipoComprobantePredeterminado = enterprise.TipoComprobantePredeterminado,
            PermitirVentaSinStock = enterprise.PermitirVentaSinStock
        };

        ViewBag.Enterprise = enterprise;
        return View(dto);
    }

    [HttpPost]
    public async Task<IActionResult> GuardarEmpresa(ConfiguracionEmpresaDto model)
    {
        if (string.IsNullOrWhiteSpace(model.RNC) || string.IsNullOrWhiteSpace(model.RazonSocial))
        {
            TempData["Error"] = "El RNC y la Razón Social son campos requeridos por la DGII.";
            return RedirectToAction(nameof(Empresa));
        }

        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        if (enterprise != null)
        {
            enterprise.RNC = model.RNC.Trim();
            enterprise.RazonSocial = model.RazonSocial.Trim();
            enterprise.NombreComercial = model.NombreComercial?.Trim();
            enterprise.Direccion = model.Direccion?.Trim() ?? "";
            enterprise.Telefono = model.Telefono?.Trim() ?? "";
            enterprise.Email = model.Email?.Trim() ?? "";
            enterprise.SitioWeb = model.SitioWeb?.Trim();
            enterprise.CodigoProvincia = model.CodigoProvincia ?? "01";
            enterprise.CodigoMunicipio = model.CodigoMunicipio ?? "010100";
            enterprise.TipoComprobantePredeterminado = model.TipoComprobantePredeterminado;
            enterprise.PermitirVentaSinStock = model.PermitirVentaSinStock;

            await _enterpriseRepo.UpdateAsync(enterprise);
            TempData["Mensaje"] = "Datos de la empresa y políticas de venta actualizados exitosamente.";
        }

        return RedirectToAction(nameof(Empresa));
    }

    [HttpGet]
    public async Task<IActionResult> FacturaFisica()
    {
        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        if (enterprise == null)
        {
            TempData["Error"] = "No se encontró la empresa configurada.";
            return RedirectToAction(nameof(Certificado));
        }

        var dto = new ConfiguracionFacturaFisicaDto
        {
            AnchoPapelMm = enterprise.AnchoPapelMm,
            MostrarLogoTicket = enterprise.MostrarLogoTicket,
            MensajeEncabezadoExtra = enterprise.MensajeEncabezadoExtra,
            MensajePieTicket = enterprise.MensajePieTicket,
            PoliticaGarantiaTicket = enterprise.PoliticaGarantiaTicket,
            CantidadCopiasTicket = enterprise.CantidadCopiasTicket
        };

        ViewBag.Enterprise = enterprise;
        return View(dto);
    }

    [HttpPost]
    public async Task<IActionResult> GuardarFacturaFisica(ConfiguracionFacturaFisicaDto model)
    {
        var enterprise = await _enterpriseRepo.GetDefaultAsync();
        if (enterprise != null)
        {
            enterprise.AnchoPapelMm = model.AnchoPapelMm == 58 ? 58 : 80;
            enterprise.MostrarLogoTicket = model.MostrarLogoTicket;
            enterprise.MensajeEncabezadoExtra = model.MensajeEncabezadoExtra?.Trim();
            enterprise.MensajePieTicket = model.MensajePieTicket?.Trim() ?? "¡Gracias por su compra!";
            enterprise.PoliticaGarantiaTicket = model.PoliticaGarantiaTicket?.Trim() ?? "";
            enterprise.CantidadCopiasTicket = model.CantidadCopiasTicket <= 1 ? 1 : 2;

            await _enterpriseRepo.UpdateAsync(enterprise);
            TempData["Mensaje"] = "Configuración del diseño de ticket térmico guardada exitosamente.";
        }

        return RedirectToAction(nameof(FacturaFisica));
    }

    /// <summary>
    /// Directorio de datos local del POS (certificado digital y otros archivos sensibles).
    /// Por defecto queda en %LOCALAPPDATA%\PosPalasy\certificados, nunca dentro del directorio
    /// de la aplicación ni del contenido web servido.
    /// </summary>
    private string ObtenerDirectorioDatos()
    {
        var configurado = _configuration["Certificado:DirectorioDatos"];

        var directorio = !string.IsNullOrWhiteSpace(configurado)
            ? configurado
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PosPalasy",
                "certificados");

        return Path.GetFullPath(directorio);
    }

    /// <summary>
    /// Ruta efectiva del certificado. Una ruta absoluta en configuración se respeta;
    /// cualquier otra se resuelve dentro del directorio de datos local.
    /// </summary>
    private string ObtenerRutaCertificado()
    {
        var configurada = _configuration["Certificado:RutaCertificado"];

        if (!string.IsNullOrWhiteSpace(configurada) && Path.IsPathRooted(configurada))
            return Path.GetFullPath(configurada);

        var nombreArchivo = string.IsNullOrWhiteSpace(configurada)
            ? "emisor.pfx"
            : Path.GetFileName(configurada);

        return Path.Combine(ObtenerDirectorioDatos(), nombreArchivo);
    }
}
