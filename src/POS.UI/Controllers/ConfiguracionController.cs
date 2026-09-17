using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using POS.Application.DTOs;
using POS.Domain.Repositories;
using POS.Infrastructure.DGII;

namespace POS.UI.Controllers;

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
        var rutaCert = _configuration["Certificado:RutaCertificado"] ?? "certificados/emisor.pfx";
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
                using var cert = new X509Certificate2(rutaCert, password, X509KeyStorageFlags.Exportable);
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
            using var testCert = new X509Certificate2(certBytes, password ?? "", X509KeyStorageFlags.DefaultKeySet);
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

        // Guardar archivo en la carpeta de certificados
        var certDir = Path.Combine(Directory.GetCurrentDirectory(), "certificados");
        if (!Directory.Exists(certDir)) Directory.CreateDirectory(certDir);

        var destPath = Path.Combine(certDir, "emisor.pfx");
        await System.IO.File.WriteAllBytesAsync(destPath, certBytes);

        TempData["Mensaje"] = "Certificado digital .pfx instalado y validado exitosamente.";
        return RedirectToAction(nameof(Certificado));
    }

    [HttpPost]
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
}
