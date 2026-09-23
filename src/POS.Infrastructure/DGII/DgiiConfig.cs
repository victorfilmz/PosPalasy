using System;
using System.Linq;

namespace POS.Infrastructure.DGII;

/// <summary>Ambientes de la DGII según <c>api_rest.md</c> (sección 3).</summary>
public enum AmbienteDgii
{
    /// <summary>Pre-certificación (testecf): único ambiente de homologación de la DGII.</summary>
    TestECF = 0,

    /// <summary>Certificación (Certecf): entregado por la DGII al aprobar la homologación.</summary>
    CertECF = 1,

    /// <summary>Producción.</summary>
    Produccion = 2
}

/// <summary>
/// Configuración de la API REST de la DGII con las URLs OFICIALES por ambiente.
/// </summary>
/// <remarks>
/// La estructura de la API es distinta para e-CF y RFCE (api_rest.md): envían a hosts diferentes
/// (ecf.dgii.gov.do / fc.dgii.gov.do) con rutas diferentes, y la consulta de resultado de e-CF es
/// por parámetro trackid, no por segmento de ruta. <see cref="DgiiEndpoints"/> deriva todos los
/// endpoints a partir del ambiente; no se aceptan rutas sueltas de configuración (fuente #1 de
/// errores de integración).
/// </remarks>
public class DgiiConfig
{
    /// <summary>Ambiente operativo contra el que se transmite.</summary>
    public AmbienteDgii Ambiente { get; set; } = AmbienteDgii.TestECF;

    /// <summary>Modo simulador: sin red ni certificado (desarrollo y pruebas).</summary>
    public bool ModoSimulador { get; set; } = false;

    /// <summary>
    /// Cofre de contratos: graba cada respuesta HTTP real de la DGII en un JSON por sesión.
    /// Solo tiene efecto en Development; insumo para verificar/actualizar los contratos asumidos
    /// de la KB tras el primer contacto con testecf.
    /// </summary>
    public bool GrabarTransmisiones { get; set; } = false;

    /// <summary>
    /// SOLO para ensayos (dry-run del verificador de homologación contra un servidor falso local):
    /// sustituye la autoridad del host e-CF (p. ej. "http://127.0.0.1:8443/testecf"). Las rutas
    /// oficiales se derivan igual; nunca configurar en producción ni en la aplicación.
    /// </summary>
    public string? HostECFOverride { get; set; }

    /// <summary>SOLO para ensayos: sustituye la autoridad del host RFCE. Mismas condiciones que <see cref="HostECFOverride"/>.</summary>
    public string? HostRFCEOverride { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>Resuelve los endpoints oficiales para el ambiente configurado (con overrides de ensayo si existen).</summary>
    public DgiiEndpoints Endpoints()
    {
        var endpoints = DgiiEndpoints.De(Ambiente);
        if (HostECFOverride is null && HostRFCEOverride is null) return endpoints;
        return new DgiiEndpoints
        {
            HostECF = HostECFOverride ?? endpoints.HostECF,
            HostRFCE = HostRFCEOverride ?? endpoints.HostRFCE
        };
    }

    /// <summary>URL raíz del host e-CF del ambiente (informativo, diagnóstico y conectividad).</summary>
    public string HostECF() => HostECFOverride ?? Ambiente switch
    {
        AmbienteDgii.CertECF => "https://ecf.dgii.gov.do/certecf",
        AmbienteDgii.Produccion => "https://ecf.dgii.gov.do/ecf",
        _ => "https://ecf.dgii.gov.do/testecf"
    };

    /// <summary>URL raíz del host RFCE (facturas de consumo &lt; RD$250,000) del ambiente.</summary>
    public string HostRFCE() => HostRFCEOverride ?? Ambiente switch
    {
        AmbienteDgii.CertECF => "https://fc.dgii.gov.do/Certecf",
        AmbienteDgii.Produccion => "https://fc.dgii.gov.do/ecf",
        _ => "https://fc.dgii.gov.do/testecf"
    };
}

/// <summary>
/// Endpoints oficiales derivados del ambiente (api_rest.md, secciones 3, 4 y 5). Cada host expone
/// su propia ruta de autenticación: los tokens NO son intercambiables entre e-CF y RFCE.
/// </summary>
public sealed class DgiiEndpoints
{
    /// <summary>Host e-CF (envío ≥ RD$250,000 y cualquier tipo no consumo pequeño).</summary>
    public required string HostECF { get; init; }

    /// <summary>Host RFCE (resúmenes de factura de consumo &lt; RD$250,000).</summary>
    public required string HostRFCE { get; init; }

    // --- Autenticación (por host: cada API exige su propio token) ---

    /// <summary>GET semilla del host e-CF.</summary>
    public string SemillaECF => $"{HostECF}/autenticacion/api/autenticacion/semilla";

    /// <summary>POST validarsemilla del host e-CF (multipart, campo xml) → token.</summary>
    public string ValidarSemillaECF => $"{HostECF}/autenticacion/api/autenticacion/validarsemilla";

    /// <summary>GET semilla del host RFCE.</summary>
    public string SemillaRFCE => $"{HostRFCE}/autenticacion/api/autenticacion/semilla";

    /// <summary>POST validarsemilla del host RFCE → token.</summary>
    public string ValidarSemillaRFCE => $"{HostRFCE}/autenticacion/api/autenticacion/validarsemilla";

    // --- Envío (multipart form-data, campo xml=@RNC+eNCF.xml) ---

    /// <summary>POST recepción de e-CF completo (≥ 250k y demás tipos).</summary>
    public string RecepcionECF => $"{HostECF}/recepcion/api/facturaselectronicas";

    /// <summary>POST recepción de RFCE (resumen factura de consumo &lt; 250k).</summary>
    public string RecepcionRFCE => $"{HostRFCE}/recepcionfc/api/recepcion/ecf";

    // --- Consultas ---

    /// <summary>GET resultado de e-CF por TrackId (estados: En proceso, Aceptado, Aceptado Condicional, Rechazado, No encontrado).</summary>
    public string ConsultaResultadoECF => $"{HostECF}/consultaresultado/api/consultas/estado";

    /// <summary>GET consulta de resumen RFCE por RNC/eNCF/código de seguridad.</summary>
    public string ConsultaRFCE => $"{HostRFCE}/consultarfce/api/Consultas/Consulta";

    /// <summary>GET consulta de estado e-CF por RNC/eNCF/código de seguridad (requiere delegación).</summary>
    public string ConsultaEstadoECF => $"{HostECF}/consultaestado/api/consultaestado";

    /// <summary>GET trackIds emitidos para un e-NCF (recuperación ante pérdida del trackId local).</summary>
    public string ConsultaTrackIds => $"{HostECF}/consultatrackids/api/trackids/consulta";

    // --- Otros ---

    /// <summary>POST aprobación comercial (e-CF entre contribuyentes).</summary>
    public string AprobacionComercial => $"{HostECF}/aprobacioncomercial/api/aprobacioncomercial";

    /// <summary>POST anulación de rangos e-NCF (ANECF).</summary>
    public string AnulacionRangos => $"{HostECF}/anulacionrangos/api/operaciones/anularrango";

    /// <summary>
    /// Construye los endpoints para un ambiente. Valores del estándar: si la DGII cambia una ruta,
    /// este es el único punto de actualización.
    /// </summary>
    public static DgiiEndpoints De(AmbienteDgii ambiente) => ambiente switch
    {
        AmbienteDgii.CertECF => new DgiiEndpoints
        {
            HostECF = "https://ecf.dgii.gov.do/certecf",
            HostRFCE = "https://fc.dgii.gov.do/Certecf"
        },
        AmbienteDgii.Produccion => new DgiiEndpoints
        {
            HostECF = "https://ecf.dgii.gov.do/ecf",
            HostRFCE = "https://fc.dgii.gov.do/ecf"
        },
        _ => new DgiiEndpoints
        {
            HostECF = "https://ecf.dgii.gov.do/testecf",
            HostRFCE = "https://fc.dgii.gov.do/testecf"
        }
    };
}
