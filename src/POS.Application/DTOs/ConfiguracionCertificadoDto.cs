using System;

namespace POS.Application.DTOs;

/// <summary>
/// DTO con los metadatos inspeccionados de un Certificado Digital X.509 (.pfx).
/// </summary>
public class ConfiguracionCertificadoDto
{
    public bool TieneCertificado { get; set; }
    public string RutaArchivo { get; set; } = string.Empty;
    public string? Sujeto { get; set; }
    public string? EmisorCertificado { get; set; }
    public string? NumeroSerie { get; set; }
    public string? HuellaDigitalSHA1 { get; set; }
    public DateTime? ValidoDesde { get; set; }
    public DateTime? ValidoHasta { get; set; }
    public bool EstaVigente { get; set; }
    public int DiasRestantes { get; set; }
    public string? ErrorCarga { get; set; }

    // Diagnóstico operativo (FASE 5.1/5.2): la pregunta que el usuario necesita responder ANTES de
    // vender en modo real es si el sistema puede firmar comprobantes con este certificado.
    public bool PuedeFirmar { get; set; }
    public string DiagnosticoOperativo { get; set; } = string.Empty;

    // Parámetros de Ambiente DGII
    public string AmbienteActual { get; set; } = "TestECF"; // "TestECF" o "Produccion"
    public string BaseUrlDgii { get; set; } = "https://ecf.dgii.gov.do/testecf";
}
