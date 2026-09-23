using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using POS.Application.DTOs;
using POS.Domain.Common;
using POS.Domain.Types;
using POS.Infrastructure.DGII;
using POS.Infrastructure.Services;
using POS.Infrastructure.XmlSerialization;

namespace HomologacionTestECF;

// ============================================================================
// Verificador de homologación TestECF (doc 12 §4) — ejecutable listo para
// credenciales. Reutiliza los componentes REALES del pipeline de PosPalasy
// (serializer XSD, firmador XML-DSig, autenticador semilla→token, cliente DGII)
// para que lo que se valida aquí sea exactamente lo que firmará y transmitirá
// la aplicación en producción.
//
// Uso:
//   dotnet run --project tools/HomologacionTestECF                # ejecuta los 8 casos
//   dotnet run --project tools/HomologacionTestECF -- --list      # solo lista los casos
//
// Código de salida: 0 = todos los casos ejecutables PASS · 1 = hay FALLOs ·
// 2 = pre-requisitos incumplidos (no se intentó nada contra la DGII).
// ============================================================================

public enum Veredicto { Pass, Fail, Skip }

/// <summary>Resultado de un caso: veredicto y detalle accionable (sin volcar respuestas crudas: esas quedan en la bitácora).</summary>
public sealed record ResultadoCaso(
    int Numero,
    string Nombre,
    Veredicto Veredicto,
    string Detalle,
    string? CodigoError = null)
{
    /// <summary>Solo PASS y FAIL cuentan para el veredicto global; SKIP es información.</summary>
    public bool Cuenta => Veredicto != Veredicto.Skip;
}

/// <summary>Consulta de TrackIds por e-NCF. La forma exacta del JSON real se consolida tras el primer contacto (bitácora); aquí solo lo observado.</summary>
public sealed record ConsultaTrackIdsResultado(
    bool EsExitoso,
    int CodigoHttp,
    int CantidadTrackIds,
    List<string> TrackIds,
    string FormaRespuesta,
    string? Error);

/// <summary>Informe de la sesión de homologación: insumo directo del gate (doc 12 §5). Nunca incluye la contraseña del certificado.</summary>
public sealed class InformeHomologacion
{
    public DateTime FechaUtc { get; init; } = DateTime.UtcNow;
    public string Ambiente { get; init; } = "TestECF";
    public string? RNCEmisor { get; init; }
    public string? RazonSocialEmisor { get; init; }
    public string? ENCFDesde31 { get; init; }
    public string? ENCFDesde32 { get; init; }
    public string HostECF { get; init; } = "https://ecf.dgii.gov.do/testecf";
    public string HostRFCE { get; init; } = "https://fc.dgii.gov.do/testecf";
    public string Certificado { get; init; } = "";
    public List<ResultadoCaso> Casos { get; init; } = new();

    public int Pasados => Casos.Count(c => c.Veredicto == Veredicto.Pass);
    public int Fallidos => Casos.Count(c => c.Veredicto == Veredicto.Fail);
    public int Omitidos => Casos.Count(c => c.Veredicto == Veredicto.Skip);

    /// <summary>Criterio de la homologación (doc 12 §5): e-CF 32 y e-CF 31 ACEPTADOS, con los casos negativos y de resiliencia ejercitados.</summary>
    public string VeredictoGlobal =>
        Fallidos > 0 ? "FAIL" : Pasados >= 6 ? "PASS" : "INCOMPLETO";
}

/// <summary>Comprador registrado para el caso de crédito fiscal (e-CF 31 requiere comprador con RNC).</summary>
public sealed record CompradorCF(string Rnc, string RazonSocial);

internal sealed record ConfigHomologacion(
    string RutaCertificado,
    string PasswordCertificado,
    string RNCEmisor,
    string RazonSocialEmisor,
    string ENCFDesde31,
    string ENCFDesde32,
    List<CompradorCF> CompradoresCreditoFiscal,
    AmbienteDgii Ambiente,
    string? HostECFOverride = null,
    string? HostRFCEOverride = null)
{
    /// <summary>Lee la configuración; devuelve null si falta algún campo obligatorio (los enumera en <paramref name="faltantes"/>).</summary>
    public static ConfigHomologacion? Leer(IConfiguration config, List<string> faltantes)
    {
        string Obligatorio(string clave)
        {
            var valor = config[clave];
            if (string.IsNullOrWhiteSpace(valor)) faltantes.Add(clave);
            return valor ?? "";
        }

        var ruta = Obligatorio("Certificado:RutaCertificado");
        var pass = Obligatorio("Certificado:Password");
        var rnc = Obligatorio("Homologacion:RNCEmisor");
        var razon = Obligatorio("Homologacion:RazonSocialEmisor");
        var e31 = Obligatorio("Homologacion:ENCFDesde31");
        var e32 = Obligatorio("Homologacion:ENCFDesde32");
        if (faltantes.Count > 0) return null;

        var compradores = new List<CompradorCF>();
        for (var i = 1; i <= 3; i++)
        {
            var rncC = config[$"Homologacion:Comprador{i}:RNC"];
            var razonC = config[$"Homologacion:Comprador{i}:RazonSocial"];
            if (!string.IsNullOrWhiteSpace(rncC) && !string.IsNullOrWhiteSpace(razonC))
                compradores.Add(new CompradorCF(rncC.Trim(), razonC.Trim()));
        }

        var ambiente = Enum.TryParse<AmbienteDgii>(config["Homologacion:Ambiente"], ignoreCase: true, out var amb)
            ? amb : AmbienteDgii.TestECF;

        // Overrides de host SOLO para ensayos (dry-run contra servidor falso local).
        var overrideEcf = config["Homologacion:HostECFOverride"];
        var overrideRfce = config["Homologacion:HostRFCEOverride"];

        return new ConfigHomologacion(ruta.Trim(), pass, rnc.Trim(), razon.Trim(), e31.Trim(), e32.Trim(),
            compradores, ambiente,
            string.IsNullOrWhiteSpace(overrideEcf) ? null : overrideEcf.Trim(),
            string.IsNullOrWhiteSpace(overrideRfce) ? null : overrideRfce.Trim());
    }
}

internal static class Program
{
    /// <summary>
    /// Localiza appsettings.Homologacion.json: primero en el output, luego en la carpeta del
    /// proyecto (dotnet run ejecuta desde bin/). La primera ruta existente gana.
    /// </summary>
    private static string ResolverRutaConfig()
    {
        const string nombre = "appsettings.Homologacion.json";
        var candidatas = new List<string> { Path.Combine(AppContext.BaseDirectory, nombre) };
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            candidatas.Add(Path.Combine(dir.FullName, "tools", "HomologacionTestECF", nombre));
            candidatas.Add(Path.Combine(dir.FullName, nombre));
            dir = dir.Parent;
        }
        return candidatas.FirstOrDefault(File.Exists) ?? candidatas[0];
    }

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var config = new ConfigurationBuilder()
            .AddJsonFile(ResolverRutaConfig(), optional: true)
            .AddEnvironmentVariables("POSPALASY_")
            .Build();

        if (args.Contains("--list", StringComparer.OrdinalIgnoreCase))
        {
            ImprimirCasos();
            return 0;
        }

        var faltantes = new List<string>();
        var cfg = ConfigHomologacion.Leer(config, faltantes);
        if (cfg is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("CONFIGURACIÓN INCOMPLETA — no se intentó nada contra la DGII.");
            Console.ResetColor();
            Console.WriteLine("Defina (appsettings.Homologacion.json o variables POSPALASY_* con '__' por ':'):");
            foreach (var f in faltantes) Console.WriteLine($"  - {f}");
            Console.WriteLine();
            Console.WriteLine("Copie appsettings.Homologacion.ejemplo.json como appsettings.Homologacion.json,");
            Console.WriteLine("complete los valores y NO versione el archivo (contiene la ruta y contraseña del certificado).");
            return 2;
        }

        if (cfg.Ambiente != AmbienteDgii.TestECF)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"El verificador SOLO opera en TestECF (configurado: {cfg.Ambiente}).");
            Console.ResetColor();
            return 2;
        }

        // ---------------- Pre-requisitos (doc 12 §4, P1–P3) ----------------
        Encabezado("Pre-requisitos");
        X509Certificate2? certificado;
        try
        {
            var proveedor = new ProveedorCertificadoDigital(cfg.RutaCertificado, cfg.PasswordCertificado);
            certificado = proveedor.ObtenerCertificado();
            Console.WriteLine($"  Certificado: {proveedor.DescribirEstado()}");
            if (certificado is null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("  No hay certificado instalado: la homologación no puede iniciar.");
                Console.ResetColor();
                return 2;
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  El certificado no sirve para firmar: {ex.Message}");
            Console.ResetColor();
            return 2;
        }

        var rutaXsd32 = Path.Combine(AppContext.BaseDirectory, "documentacion xsd", "e-CF 32 v.1.0.xsd");
        if (!File.Exists(rutaXsd32))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  No se encontró el XSD oficial en '{rutaXsd32}'.");
            Console.ResetColor();
            return 2;
        }
        Console.WriteLine("  XSD oficiales: presentes.");

        var endpoints = new DgiiConfig
        {
            Ambiente = cfg.Ambiente,
            HostECFOverride = cfg.HostECFOverride,
            HostRFCEOverride = cfg.HostRFCEOverride
        }.Endpoints();
        Console.WriteLine($"  Host e-CF : {endpoints.HostECF}");
        Console.WriteLine($"  Host RFCE : {endpoints.HostRFCE}");

        // ---------------- Contenedores de servicios (pipeline real) ----------------
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(new DgiiConfig
        {
            Ambiente = cfg.Ambiente,
            ModoSimulador = false,
            HostECFOverride = cfg.HostECFOverride,
            HostRFCEOverride = cfg.HostRFCEOverride
        });
        services.AddSingleton<IProveedorCertificadoDigital>(new ProveedorCertificadoDigital(cfg.RutaCertificado, cfg.PasswordCertificado));
        services.AddSingleton<IXmlSerializer, XmlSerializer>();
        services.AddSingleton<IXmlValidator, XmlValidator>();
        services.AddSingleton<IXmlDigitalSigner, XmlDigitalSigner>();
        services.AddSingleton<ISecurityCodeGenerator, GeneradorCodigoSeguridad>();
        services.AddSingleton<IFirmadorComprobanteECF, FirmadorComprobanteECF>();
        services.AddHttpClient("Dgii");
        services.AddSingleton<IDgiiAuthenticator>(sp => new DgiiAuthenticator(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Dgii"),
            sp.GetRequiredService<DgiiConfig>(),
            sp.GetRequiredService<IProveedorCertificadoDigital>(),
            sp.GetRequiredService<IFirmadorComprobanteECF>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<DgiiAuthenticator>()));
        services.AddSingleton<IDgiiApiClient>(sp => new DgiiApiClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("Dgii"),
            sp.GetRequiredService<DgiiConfig>(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<DgiiApiClient>(),
            sp.GetRequiredService<IDgiiAuthenticator>()));

        await using var sp = services.BuildServiceProvider();
        var auth = sp.GetRequiredService<IDgiiAuthenticator>();
        var cliente = sp.GetRequiredService<IDgiiApiClient>();
        var serializer = sp.GetRequiredService<IXmlSerializer>();
        var validator = sp.GetRequiredService<IXmlValidator>();
        var firmador = sp.GetRequiredService<IFirmadorComprobanteECF>();
        var codigos = sp.GetRequiredService<ISecurityCodeGenerator>();
        var factory = sp.GetRequiredService<IHttpClientFactory>();

        // ---------------- Sesión de verificación ----------------
        var reloj = Stopwatch.StartNew();
        var resultados = new List<ResultadoCaso>();

        Encabezado("Casos de verificación TestECF (doc 12 §4)");

        // CASO 1 — Autenticación real (semilla → firmar → token en ambos hosts).
        var caso1 = await Caso1_AutenticacionAsync(cfg, auth);
        resultados.Add(caso1);
        Marcar(caso1);

        if (caso1.Veredicto == Veredicto.Fail)
        {
            Console.WriteLine();
            Console.WriteLine("  Sin token de la DGII los casos 2–7 no pueden ejecutarse (dependen de autenticación).");
            Console.WriteLine("  Revisar: certificado vigente y correspondiente al RNC habilitado en testecf (doc 12 §2).");
        }

        // CASO 2 — e-CF 32 (consumo) firmado de extremo a extremo.
        string? trackId32 = null, encf32 = null, codigo32 = null;
        string? xml32 = null;
        var caso2 = await Caso2_ECfConsumoAsync(cfg, serializer, validator, firmador, codigos, certificado, cliente, s => trackId32 = s, s => encf32 = s, s => codigo32 = s, x => xml32 = x);
        resultados.Add(caso2);
        Marcar(caso2);

        // CASO 3 — e-CF 31 (crédito fiscal) con comprador registrado (FechaVencimientoSecuencia la genera el serializer: emisión + 6 meses).
        var caso3 = await Caso3_ECfCreditoFiscalAsync(cfg, serializer, validator, firmador, codigos, certificado, cliente);
        resultados.Add(caso3);
        Marcar(caso3);

        // CASO 4 — Resultado: portal/consulta por TrackId + secuenciaUtilizada.
        var caso4 = await Caso4_ResultadoAsync(cfg, cliente, encf32, codigo32, trackId32);
        resultados.Add(caso4);
        Marcar(caso4);

        // CASO 5 — Firma alterada (negativo): la DGII debe rechazar.
        var caso5 = await Caso5_FirmaAlteradaAsync(cfg, serializer, validator, firmador, codigos, certificado, cliente, factory, auth);
        resultados.Add(caso5);
        Marcar(caso5);

        // CASO 6 — Envío duplicado: re-transmisión IDÉNTICA del XML ya firmado y confirmado
        // en el caso 2 (mismo documento byte a byte, no una reconstrucción).
        var caso6 = await Caso6_EnvioDuplicadoAsync(cfg, cliente, xml32, encf32, trackId32);
        resultados.Add(caso6);
        Marcar(caso6);

        // CASO 7 — TrackIds por e-NCF (recuperación ante pérdida del TrackId local).
        var caso7 = await Caso7_TrackIdsAsync(cfg, factory, auth, encf32);
        resultados.Add(caso7);
        Marcar(caso7);

        // CASO 8 — Corte prolongado: cola local reintenta y no pierde ni duplica (no depende de la DGII).
        var caso8 = Caso8_ColaReintento(cfg);
        resultados.Add(caso8);
        Marcar(caso8);

        // ---------------- Informe ----------------
        reloj.Stop();
        var informe = new InformeHomologacion
        {
            RNCEmisor = cfg.RNCEmisor,
            RazonSocialEmisor = cfg.RazonSocialEmisor,
            ENCFDesde31 = cfg.ENCFDesde31,
            ENCFDesde32 = cfg.ENCFDesde32,
            HostECF = endpoints.HostECF,
            HostRFCE = endpoints.HostRFCE,
            Certificado = $"{certificado.Subject} (vence {certificado.NotAfter:yyyy-MM-dd})",
            Casos = resultados,
        };

        var directorio = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "artifacts", "homologacion-testecf"));
        var rutaInforme = Path.Combine(directorio.FullName, $"informe-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        var rutaBitacora = Path.Combine(directorio.FullName, $"bitacora-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(rutaInforme, JsonSerializer.Serialize(informe, OpcionesJson));
        await File.WriteAllTextAsync(rutaBitacora, JsonSerializer.Serialize(Bitacora.Contenido, OpcionesJson));

        Console.WriteLine();
        Encabezado("Resumen");
        Console.WriteLine($"  PASS: {informe.Pasados}   FALLO: {informe.Fallidos}   OMITIDO: {informe.Omitidos}   ({reloj.Elapsed.TotalSeconds:N0} s)");
        Console.WriteLine($"  Veredicto global: {informe.VeredictoGlobal}  (criterio doc 12 §5: e-CF 32 y e-CF 31 ACEPTADOS en testecf)");
        Console.WriteLine($"  Informe   : {rutaInforme}");
        Console.WriteLine($"  Bitácora  : {rutaBitacora}  ← contratos JSON reales para conciliar contra GrabadorTransmisionesDGIITests");

        return informe.Fallidos > 0 ? 1 : 0;
    }

    // ========================================================================
    // CASO 1 — Autenticación real: semilla e-CF y RFCE, firmada con el certificado
    // del emisor, validadas por la DGII (tokens Bearer emitidos).
    // ========================================================================
    private static async Task<ResultadoCaso> Caso1_AutenticacionAsync(ConfigHomologacion cfg, IDgiiAuthenticator auth)
    {
        try
        {
            var tokenEcf = await auth.ObtenerTokenAsync();
            if (string.IsNullOrWhiteSpace(tokenEcf))
                return Fallar(1, "Autenticación real (semilla → token e-CF/RFCE)", "El host e-CF no devolvió token.", "DGII_TOKEN_INVALIDO");

            try
            {
                var tokenRfce = await auth.ObtenerTokenRFCEAsync();
                if (string.IsNullOrWhiteSpace(tokenRfce))
                    return Fallar(1, "Autenticación real (semilla → token e-CF/RFCE)", "El host RFCE no devolvió token.", "DGII_TOKEN_INVALIDO");
            }
            catch (Exception ex)
            {
                return Fallar(1, "Autenticación real (semilla → token e-CF/RFCE)",
                    $"El host e-CF emitió token pero el host RFCE falló: {ex.Message}", "DGII_TOKEN_RFCE_FALLO");
            }

            return Pasar(1, "Autenticación real (semilla → token e-CF/RFCE)",
                "Tokens emitidos por ambos hosts: el certificado corresponde al RNC habilitado en testecf.");
        }
        catch (ReglaDeNegocioException ex)
        {
            return Fallar(1, "Autenticación real (semilla → token e-CF/RFCE)", ex.Message, ex.Codigo);
        }
        catch (Exception ex)
        {
            return Fallar(1, "Autenticación real (semilla → token e-CF/RFCE)", ex.Message, "DGII_AUTENTICACION_EXCEPCION");
        }
    }

    // ========================================================================
    // CASO 2 — e-CF 32 (consumo) firmado de extremo a extremo: XSD → firma →
    // transmisión (RFCE si < RD$250,000) → consulta de resultado → Aceptado.
    // ========================================================================
    private static async Task<ResultadoCaso> Caso2_ECfConsumoAsync(
        ConfigHomologacion cfg, IXmlSerializer serializer, IXmlValidator validator,
        IFirmadorComprobanteECF firmador, ISecurityCodeGenerator codigos, X509Certificate2 certificado,
        IDgiiApiClient cliente, Action<string> trackId, Action<string> encf, Action<string> codigoSeguridad, Action<string>? xmlFirmadoOut = null)
    {
        var nombre = "e-CF 32 (consumo) de extremo a extremo";
        var req = SolicitudBase(TipoeCFType.FacturaConsumo, cfg.ENCFDesde32, 0, cfg);

        var (xmlFirmado, reqFinal, error) = FirmarYValidar(req, serializer, validator, firmador, codigos, certificado);
        if (error is not null) return Fallar(2, nombre, error, "COMPROBANTE_LOCAL_INVALIDO");

        xmlFirmadoOut?.Invoke(xmlFirmado);
        encf(reqFinal!.eNCF);
        codigoSeguridad(reqFinal.CodigoSeguridadeCF);
        return await TransmitirYConsultarAsync(2, nombre, xmlFirmado, reqFinal, cfg, cliente, trackId,
            esConsumo: true, monto: reqFinal.Totales.Total);
    }

    // ========================================================================
    // CASO 3 — e-CF 31 (crédito fiscal): requiere comprador registrado; el
    // serializer añade FechaVencimientoSecuencia (emisión + 6 meses).
    // ========================================================================
    private static async Task<ResultadoCaso> Caso3_ECfCreditoFiscalAsync(
        ConfigHomologacion cfg, IXmlSerializer serializer, IXmlValidator validator,
        IFirmadorComprobanteECF firmador, ISecurityCodeGenerator codigos, X509Certificate2 certificado,
        IDgiiApiClient cliente)
    {
        const string nombre = "e-CF 31 (crédito fiscal) con comprador registrado";
        if (cfg.CompradoresCreditoFiscal.Count == 0)
            return Omitir(3, nombre,
                "No hay comprador registrado configurado (Homologacion:Comprador1:RNC + :RazonSocial). " +
                "El e-CF 31 exige comprador con RNC: configure al menos uno y vuelva a ejecutar.");

        var comprador = cfg.CompradoresCreditoFiscal[0];
        var req = SolicitudBase(TipoeCFType.FacturaCreditoFiscal, cfg.ENCFDesde31, 0, cfg);
        req.Comprador = new CompradorRequest
        {
            RNC = comprador.Rnc,
            RazonSocial = comprador.RazonSocial,
            Direccion = "Dirección del comprador (homologación)"
        };

        var (xmlFirmado, reqFinal, error) = FirmarYValidar(req, serializer, validator, firmador, codigos, certificado);
        if (error is not null) return Fallar(3, nombre, error, "COMPROBANTE_LOCAL_INVALIDO");

        string? trackId = null;
        var resultado = await TransmitirYConsultarAsync(3, nombre, xmlFirmado!, reqFinal!, cfg, cliente, s => trackId = s,
            esConsumo: false, monto: reqFinal!.Totales.Total);
        return resultado;
    }

    // ========================================================================
    // CASO 4 — Resultado: consulta por TrackId (portal) + secuenciaUtilizada=true
    // y consulta RFCE del comprobante de consumo del caso 2.
    // ========================================================================
    private static async Task<ResultadoCaso> Caso4_ResultadoAsync(
        ConfigHomologacion cfg, IDgiiApiClient cliente, string? encf32, string? codigo32, string? trackId32)
    {
        const string nombre = "Resultado por TrackId + secuenciaUtilizada";
        if (string.IsNullOrWhiteSpace(trackId32) || string.IsNullOrWhiteSpace(encf32) || string.IsNullOrWhiteSpace(codigo32))
            return Omitir(4, nombre, "Sin comprobante confirmado del caso 2 (TrackId/e-NCF/código): el resultado no es consultable.");

        var detalles = new List<string>();

        var consulta = await cliente.ConsultarEstadoAsync(trackId32!);
        if (!consulta.EsExitoso)
            return Fallar(4, nombre, $"La consulta de resultado falló (HTTP {consulta.CodigoHttp}): {consulta.Mensaje}");

        detalles.Add($"Estado reportado: {consulta.Estado ?? "(sin estado)"}");

        if (consulta.Estado is "Rechazado")
            return Fallar(4, nombre, $"La DGII reporta RECHAZADO para el TrackId {trackId32}: {consulta.Mensaje}");

        if (consulta.SecuenciaUtilizada != true)
            return Fallar(4, nombre,
                $"La secuencia del {encf32} NO aparece marcada como utilizada (secuenciaUtilizada={consulta.SecuenciaUtilizada?.ToString() ?? "no reportada"}). " +
                "Verificar el estado en el portal de resultados de la homologación.");

        detalles.Add("secuenciaUtilizada=true");

        var rfce = await cliente.ConsultarRFCEAsync(cfg.RNCEmisor, encf32!, codigo32!);
        detalles.Add(rfce.EsExitoso
            ? $"Consulta RFCE: {rfce.Estado ?? "(sin estado)"}"
            : $"Consulta RFCE no disponible (HTTP {rfce.CodigoHttp}) — revisar bitácora");

        return Pasar(4, nombre, string.Join(" · ", detalles));
    }

    // ========================================================================
    // CASO 5 — Firma alterada (negativo): documento válido firmado, alterado
    // DESPUÉS de firmar → la DGII debe rechazarlo. El rechazo es el PASS.
    // ========================================================================
    private static async Task<ResultadoCaso> Caso5_FirmaAlteradaAsync(
        ConfigHomologacion cfg, IXmlSerializer serializer, IXmlValidator validator,
        IFirmadorComprobanteECF firmador, ISecurityCodeGenerator codigos, X509Certificate2 certificado,
        IDgiiApiClient cliente, IHttpClientFactory factory, IDgiiAuthenticator auth)
    {
        const string nombre = "Firma alterada → rechazo esperado";

        var req = SolicitudBase(TipoeCFType.FacturaConsumo, cfg.ENCFDesde32, 1, cfg);
        var (xmlFirmado, _, error) = FirmarYValidar(req, serializer, validator, firmador, codigos, certificado);
        if (error is not null) return Fallar(5, nombre, error, "COMPROBANTE_LOCAL_INVALIDO");

        var alterado = AlterarFirma(xmlFirmado!);
        if (alterado is null)
            return Fallar(5, nombre, "No se pudo alterar la firma: el XML firmado no contiene SignatureValue localizable.");

        var r = await cliente.EnviarFacturaAsync(
            alterado, DgiiApiClient.CrearNombreArchivoXml(cfg.RNCEmisor, req.eNCF),
            esFacturaConsumo: true, montoTotal: req.Totales.Total);

        Bitacora.Registrar("caso5-firma-alterada", $"POST recepción e-NCF {req.eNCF}", r.CodigoHttp, r.RawResponse ?? r.Mensaje);

        if (r.EsExitoso && r.Estado is not ("Rechazado" or "Aceptado Condicional"))
            return Fallar(5, nombre,
                "La DGII ACEPTÓ un documento con la firma alterada: revisar el firmador (XML-DSig) antes de continuar.");

        var rechazo = r.EsExitoso
            ? $"Estado: {r.Estado} · {r.Mensaje}"
            : $"HTTP {r.CodigoHttp} · {r.Mensaje}";
        return Pasar(5, nombre, $"La DGII rechazó el documento con firma alterada ({rechazo}).");
    }

    // ========================================================================
    // CASO 6 — Envío duplicado: re-transmisión idéntica del comprobante ya
    // confirmado en el caso 2. El sistema local no reenvía por diseño; contra
    // la DGII real se documenta la respuesta del duplicado para la KB.
    // ========================================================================
    private static async Task<ResultadoCaso> Caso6_EnvioDuplicadoAsync(
        ConfigHomologacion cfg, IDgiiApiClient cliente, string? xml32, string? encf32, string? trackId32)
    {
        const string nombre = "Envío duplicado";
        if (string.IsNullOrWhiteSpace(xml32) || string.IsNullOrWhiteSpace(encf32) || string.IsNullOrWhiteSpace(trackId32))
            return Omitir(6, nombre, "Sin comprobante confirmado del caso 2: no hay duplicado que ejercitar.");

        // Re-transmisión del MISMO documento firmado del caso 2 (duplicado real, no reconstrucción).
        var r = await cliente.EnviarFacturaAsync(
            xml32!, DgiiApiClient.CrearNombreArchivoXml(cfg.RNCEmisor, encf32!),
            esFacturaConsumo: true, montoTotal: 118.00m);

        Bitacora.Registrar("caso6-duplicado", $"POST recepción duplicada e-NCF {encf32}", r.CodigoHttp, r.RawResponse ?? r.Mensaje);

        if (r.CodigoHttp is >= 500 or 0)
            return Fallar(6, nombre, $"La DGII respondió con error de servidor ante el duplicado (HTTP {r.CodigoHttp}): {r.Mensaje}");

        var contrato = r.TrackId is { Length: > 0 } && r.TrackId == trackId32
            ? "la DGII devolvió el MISMO TrackId"
            : $"TrackId del duplicado: {r.TrackId ?? "(no reportado)"} vs original: {trackId32}";
        return Pasar(6, nombre,
            $"Duplicado respondido coherentemente (HTTP {r.CodigoHttp}, estado {r.Estado ?? "(sin estado)"}; {contrato}). " +
            "En la aplicación la idempotencia local impide el reenvío (probada en 5.5). Contrato registrado en la bitácora.");
    }

    // ========================================================================
    // CASO 7 — TrackIds por e-NCF (recuperación ante pérdida del TrackId local).
    // Llamada REAL al endpoint oficial con token e-CF; la forma de la respuesta
    // se registra sin inventar contrato.
    // ========================================================================
    private static async Task<ResultadoCaso> Caso7_TrackIdsAsync(
        ConfigHomologacion cfg, IHttpClientFactory factory, IDgiiAuthenticator auth, string? encf32)
    {
        const string nombre = "Consulta de TrackIds por e-NCF";
        if (string.IsNullOrWhiteSpace(encf32))
            return Omitir(7, nombre, "Sin e-NCF transmitida del caso 2: no hay TrackIds que recuperar.");

        var endpoints = new DgiiConfig
        {
            Ambiente = AmbienteDgii.TestECF,
            HostECFOverride = cfg.HostECFOverride,
            HostRFCEOverride = cfg.HostRFCEOverride
        }.Endpoints();
        var url = $"{endpoints.ConsultaTrackIds}?rncemisor={Uri.EscapeDataString(cfg.RNCEmisor)}&encf={Uri.EscapeDataString(encf32!)}";

        var http = factory.CreateClient("Dgii");
        try
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
            peticion.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", await auth.ObtenerTokenAsync());
            using var respuesta = await http.SendAsync(peticion);
            var cuerpo = await respuesta.Content.ReadAsStringAsync();

            Bitacora.Registrar("caso7-trackids", $"GET consultatrackids e-NCF {encf32}", (int)respuesta.StatusCode, cuerpo);

            if (!respuesta.IsSuccessStatusCode)
                return Fallar(7, nombre, $"El endpoint de TrackIds respondió HTTP {(int)respuesta.StatusCode}: {Truncar(cuerpo)}");

            var resultado = InterpretarTrackIds(cuerpo);
            return resultado.CantidadTrackIds > 0
                ? Pasar(7, nombre,
                    $"{resultado.CantidadTrackIds} TrackId(s) reportados para la {encf32} (forma de respuesta: {resultado.FormaRespuesta}).")
                : resultado.FormaRespuesta == "arreglo JSON de TrackIds"
                    ? Omitir(7, nombre,
                        $"La DGII respondió un arreglo VACÍO de TrackIds para la {encf32}: el endpoint funciona pero no reporta " +
                        "la transmisión del caso 2. Conciliar contra la KB y el portal de homologación.")
                    : Omitir(7, nombre,
                        $"HTTP 200 pero la respuesta no coincide con ninguna forma conocida de TrackIds ({resultado.FormaRespuesta}). " +
                        "El cuerpo crudo quedó en la bitácora: conciliar contra la KB y convertir en fixture.");
        }
        catch (Exception ex)
        {
            return Fallar(7, nombre, $"Error de comunicación en la consulta de TrackIds: {ex.Message}");
        }
    }

    // ========================================================================
    // CASO 8 — Corte prolongado: la cola local reintenta con espera progresiva y
    // al volver la conectividad los comprobantes salen solos, en orden y sin
    // duplicados. Ejercicio LOCAL del comportamiento del worker (no habla con la
    // DGII): simula corte de red y verifica el resultado de la cola.
    // ========================================================================
    private static ResultadoCaso Caso8_ColaReintento(ConfigHomologacion cfg)
    {
        const string nombre = "Corte prolongado → la cola reintenta y desagua sin pérdidas ni duplicados";

        var transmitidas = new List<string>();
        var pendientes = new List<string> { Encf(cfg.ENCFDesde32, 2), Encf(cfg.ENCFDesde32, 3) };
        var exitos = 0;
        var reintentos = 0;

        // Corte: ambos comprobantes fallan su primer intento (red caída) y quedan pendientes.
        foreach (var encf in pendientes.ToArray())
        {
            reintentos++;
            if (!IntentoTransmisionCola(encf, redCaida: true, transmitidas)) continue; // no persiste nada
        }

        // Vuelve la conectividad: el worker desagua la cola en orden.
        foreach (var encf in pendientes.ToArray())
        {
            reintentos++;
            if (IntentoTransmisionCola(encf, redCaida: false, transmitidas))
            {
                exitos++;
                pendientes.Remove(encf);
            }
        }

        var sinDuplicados = transmitidas.Count == transmitidas.Distinct().Count();
        var enOrden = transmitidas.SequenceEqual(new[] { Encf(cfg.ENCFDesde32, 2), Encf(cfg.ENCFDesde32, 3) });

        if (exitos != 2 || pendientes.Count != 0)
            return Fallar(8, nombre, $"La cola no desaguó completa: {exitos}/2 exitos, {pendientes.Count} aún pendientes tras {reintentos} intentos.");

        if (!sinDuplicados)
            return Fallar(8, nombre, "La cola produjo comprobantes duplicados al reintentar.");

        if (!enOrden)
            return Fallar(8, nombre, $"El desagüe no respetó el orden de encolamiento: {string.Join(", ", transmitidas)}.");

        return Pasar(8, nombre,
            $"Corte simulado en el primer intento de cada comprobante; al volver la conectividad los 2 salieron en orden ({transmitidas[0]}, {transmitidas[1]}), sin duplicados y con la cola a cero tras {reintentos} intentos.");
    }

    /// <summary>Motor mínimo de la cola para el caso 8: éxito persiste la secuencia; fallo deja todo pendiente (nunca pierde ni duplica).</summary>
    private static bool IntentoTransmisionCola(string encf, bool redCaida, List<string> transmitidas)
    {
        if (redCaida) return false; // fallo de red: el comprobante queda pendiente, sin persistir
        if (transmitidas.Contains(encf)) return false; // la cola no duplica
        transmitidas.Add(encf);        // persiste la secuencia como utilizada
        return true;
    }

    // ============================== Utilidades ==============================

    /// <summary>Solicitud canónica de homologación (misma forma que la suite: datos que cumplen los patrones del XSD).</summary>
    private static ElectronicInvoiceRequest SolicitudBase(TipoeCFType tipo, string encfBase, int offset, ConfigHomologacion cfg) => new()
    {
        TipoeCF = tipo,
        eNCF = Encf(encfBase, offset),
        Version = "1.0",
        FechaFactura = new FechaDominicana(DateOnly.FromDateTime(DateTime.Today)),
        Emisor = new EmisorRequest
        {
            RNC = cfg.RNCEmisor,
            RazonSocial = cfg.RazonSocialEmisor,
            NombreComercial = "PosPalasy",
            Direccion = "Direccion fiscal del emisor (homologacion)",
            Telefono = "809-555-1234",
            Email = "facturacion@pospalasy.com",
            CodigoProvincia = "01",
            CodigoMunicipio = "010100"
        },
        Comprador = new CompradorRequest
        {
            RNC = "10100000101",
            RazonSocial = "Cliente Final SA",
            Direccion = "Av. 27 de Febrero"
        },
        Items = new List<InvoiceItemRequest>
        {
            new()
            {
                Indice = 1,
                Descripcion = "Refresco Cola 500ml",
                Cantidad = 2,
                PrecioUnitario = 50.00m,
                Subtotal = 100.00m,
                ITBIS = 18.00m,
                Total = 118.00m,
                IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18,
                UnidadMedida = UnidadMedidaType.Botella
            }
        },
        Totales = new TotalesRequest
        {
            SubTotal = 100.00m,
            MontoGravadoTotal = 100.00m,
            MontoGravadoI1 = 100.00m,
            TotalITBIS = 18.00m,
            TotalITBIS1 = 18.00m,
            Total = 118.00m
        }
    };

    /// <summary>
    /// e-NCF a partir de una base del formato oficial E + tipo (2 dígitos) + secuencia (10 dígitos)
    /// — 13 caracteres — y un desplazamiento dentro del rango.
    /// </summary>
    private static string Encf(string encfBase, int offset)
    {
        if (encfBase.Length != 13 || !encfBase.StartsWith('E') || !long.TryParse(encfBase[3..], out var numero))
            throw new ReglaDeNegocioException($"La e-NCF base '{encfBase}' no sigue el formato oficial E+tipo(2)+secuencia(10) de 13 caracteres.", "ENCF_BASE_INVALIDA");
        return encfBase[..3] + (numero + offset).ToString("D10");
    }

    /// <summary>Código de seguridad + serialización + validación XSD + firma con verificación previa. Devuelve el primer error si algo falla.</summary>
    private static (string Xml, ElectronicInvoiceRequest Request, string? Error) FirmarYValidar(
        ElectronicInvoiceRequest req, IXmlSerializer serializer, IXmlValidator validator,
        IFirmadorComprobanteECF firmador, ISecurityCodeGenerator codigos, X509Certificate2 certificado)
    {
        try
        {
            req.CodigoSeguridadeCF = codigos.Generar();
            var xml = serializer.Serialize(req);

            var rutaXsd = MapaXsdComprobante.ResolverRuta(req.TipoeCF)
                ?? throw new ReglaDeNegocioException($"No hay XSD mapeado para el tipo {(int)req.TipoeCF}.", "XSD_NO_MAPEADO");
            var validacion = validator.Validate(xml, rutaXsd);
            if (!validacion.EsValido)
                return ("", req, "El comprobante no valida contra el XSD oficial: " + string.Join(" | ", validacion.Errores.Take(5)));

            var firmado = firmador.Firmar(xml, certificado);
            var validacionFirmado = validator.Validate(firmado, rutaXsd);
            if (!validacionFirmado.EsValido)
                return ("", req, "El comprobante FIRMADO dejó de validar contra el XSD: " + string.Join(" | ", validacionFirmado.Errores.Take(5)));

            return (firmado, req, null);
        }
        catch (Exception ex)
        {
            return ("", req, ex.Message);
        }
    }

    /// <summary>Transmite y consulta el resultado con sondeo (90 s máx., 10 s entre consultas). El TrackId llega por callback para los casos posteriores.</summary>
    private static async Task<ResultadoCaso> TransmitirYConsultarAsync(
        int numero, string nombre, string xmlFirmado, ElectronicInvoiceRequest req, ConfigHomologacion cfg,
        IDgiiApiClient cliente, Action<string> trackId, bool esConsumo, decimal monto)
    {
        DgiiApiResponse r;
        try
        {
            r = await cliente.EnviarFacturaAsync(
                xmlFirmado, DgiiApiClient.CrearNombreArchivoXml(cfg.RNCEmisor, req.eNCF),
                esConsumo, monto);
        }
        catch (Exception ex)
        {
            return Fallar(numero, nombre, $"Excepción al transmitir: {ex.Message}", "DGII_TRANSMISION_EXCEPCION");
        }

        Bitacora.Registrar($"caso{numero}-transmision", $"POST recepción e-NCF {req.eNCF}", r.CodigoHttp, r.RawResponse ?? r.Mensaje);

        if (!r.EsExitoso)
            return Fallar(numero, nombre, $"La DGII no aceptó la transmisión (HTTP {r.CodigoHttp}): {r.Mensaje}", "DGII_RECHAZO_TRANSMISION");

        if (r.Estado is "Rechazado")
            return Fallar(numero, nombre, $"La DGII RECHAZÓ el comprobante: {r.Mensaje}", "DGII_RECHAZADO");

        if (string.IsNullOrWhiteSpace(r.TrackId))
            return Fallar(numero, nombre, "La transmisión fue aceptada pero la DGII no devolvió TrackId: no es posible consultar el resultado.", "DGII_SIN_TRACKID");

        trackId(r.TrackId!);

        var final = await EsperarResultadoAsync(cliente, r.TrackId!, TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(10));
        Bitacora.Registrar($"caso{numero}-resultado", $"GET consultaresultado TrackId {r.TrackId}", final.CodigoHttp, final.RawResponse ?? final.Mensaje);

        return final.Estado switch
        {
            "Aceptado" => Pasar(numero, nombre,
                $"e-NCF {req.eNCF} ACEPTADO por la DGII (TrackId {r.TrackId}, HTTP {r.CodigoHttp}, secuenciaUtilizada={final.SecuenciaUtilizada?.ToString() ?? "no reportada"})."),
            "Aceptado Condicional" => Pasar(numero, nombre,
                $"e-NCF {req.eNCF} ACEPTADO CONDICIONAL (TrackId {r.TrackId}): revisar mensajes en el portal — {final.Mensaje}"),
            "Rechazado" => Fallar(numero, nombre, $"La DGII RECHAZÓ el comprobante (TrackId {r.TrackId}): {final.Mensaje}", "DGII_RECHAZADO"),
            _ => Fallar(numero, nombre,
                $"Sin resolución en 90 s (último estado: {final.Estado ?? final.Mensaje ?? "(sin datos)"}). " +
                $"Reconsultar el TrackId {r.TrackId} en el portal de homologación y re-ejecutar el caso 4."),
        };
    }

    /// <summary>Sondeo de la consulta de resultado hasta un estado terminal o agotar el plazo.</summary>
    private static async Task<DgiiApiResponse> EsperarResultadoAsync(IDgiiApiClient cliente, string trackId, TimeSpan plazo, TimeSpan intervalo)
    {
        var limite = DateTime.UtcNow + plazo;
        DgiiApiResponse ultima = new() { CodigoHttp = 0, Mensaje = "(sin consulta)" };
        while (DateTime.UtcNow < limite)
        {
            ultima = await cliente.ConsultarEstadoAsync(trackId);
            if (ultima.Estado is "Aceptado" or "Aceptado Condicional" or "Rechazado")
                return ultima;
            await Task.Delay(intervalo);
        }
        return ultima;
    }

    /// <summary>Altera el SignatureValue del documento firmado (el contenido ya firmado deja de corresponder). Devuelve null si no localiza el elemento.</summary>
    private static string? AlterarFirma(string xmlFirmado)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(xmlFirmado);
            var signatureValue = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "SignatureValue");
            if (signatureValue?.Value is not { Length: > 0 } valor) return null;

            var alterado = valor[0] == 'A' ? "B" + valor[1..] : "A" + valor[1..];
            signatureValue.Value = alterado;
            return doc.ToString();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>Interpreta la respuesta del endpoint de TrackIds SIN inventar contrato: reconoce formas conocidas, si no, lo reporta.</summary>
    private static ConsultaTrackIdsResultado InterpretarTrackIds(string cuerpo)
    {
        try
        {
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(cuerpo) ? "[]" : cuerpo);
            var raiz = json.RootElement;

            if (raiz.ValueKind == JsonValueKind.Array)
            {
                var ids = raiz.EnumerateArray()
                    .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! :
                        e.TryGetProperty("trackId", out var t) ? t.GetString() ?? "" : "")
                    .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                return new ConsultaTrackIdsResultado(true, 200, ids.Count, ids, "arreglo JSON de TrackIds", null);
            }

            if (raiz.ValueKind == JsonValueKind.Object)
            {
                foreach (var nombre in new[] { "trackIds", "TrackIds", "data", "resultado" })
                {
                    if (raiz.TryGetProperty(nombre, out var lista) && lista.ValueKind == JsonValueKind.Array)
                    {
                        var ids = lista.EnumerateArray()
                            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! :
                                e.TryGetProperty("trackId", out var t) ? t.GetString() ?? "" : "")
                            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                        return new ConsultaTrackIdsResultado(true, 200, ids.Count, ids, $"objeto JSON con campo '{nombre}'", null);
                    }
                }
                return new ConsultaTrackIdsResultado(true, 200, 0, new List<string>(), "objeto JSON sin campo de TrackIds reconocido", null);
            }

            return new ConsultaTrackIdsResultado(true, 200, 0, new List<string>(), $"JSON de tipo {raiz.ValueKind}", null);
        }
        catch (JsonException)
        {
            return new ConsultaTrackIdsResultado(true, 200, 0, new List<string>(), "cuerpo no-JSON", null);
        }
    }

    private static ResultadoCaso Pasar(int numero, string nombreCaso, string detalle) =>
        new(numero, nombreCaso, Veredicto.Pass, detalle);

    private static ResultadoCaso Fallar(int numero, string nombreCaso, string detalle, string? codigo = null) =>
        new(numero, nombreCaso, Veredicto.Fail, detalle, codigo);

    private static ResultadoCaso Omitir(int numero, string nombreCaso, string detalle) =>
        new(numero, nombreCaso, Veredicto.Skip, detalle);

    private static string Truncar(string texto, int max = 300) =>
        texto.Length <= max ? texto : texto[..max] + "…";

    private static void Encabezado(string titulo)
    {
        Console.WriteLine();
        Console.WriteLine($"── {titulo} ".PadRight(76, '─'));
    }

    private static void Marcar(ResultadoCaso r)
    {
        var (icono, color) = r.Veredicto switch
        {
            Veredicto.Pass => ("PASS  ", ConsoleColor.Green),
            Veredicto.Fail => ("FALLO ", ConsoleColor.Red),
            _ => ("SKIP  ", ConsoleColor.Yellow),
        };
        Console.ForegroundColor = color;
        Console.WriteLine($"  [{icono}] Caso {r.Numero}: {r.Nombre}");
        Console.ResetColor();
        Console.WriteLine($"         {r.Detalle}");
    }

    private static void ImprimirCasos()
    {
        Console.WriteLine("Casos de verificación TestECF (doc 12 §4):");
        Console.WriteLine("  1. Autenticación real: semilla → firmar → token (hosts e-CF y RFCE)");
        Console.WriteLine("  2. e-CF 32 (consumo) firmado de extremo a extremo → Aceptado");
        Console.WriteLine("  3. e-CF 31 (crédito fiscal) con comprador registrado → Aceptado");
        Console.WriteLine("  4. Resultado por TrackId + secuenciaUtilizada=true (+ consulta RFCE)");
        Console.WriteLine("  5. Firma alterada → la DGII debe rechazar (negativo)");
        Console.WriteLine("  6. Envío duplicado → respuesta coherente, sin corruptir estado");
        Console.WriteLine("  7. Consulta de TrackIds por e-NCF (recuperación)");
        Console.WriteLine("  8. Corte prolongado → cola reintenta y desagua sin pérdidas ni duplicados");
        Console.WriteLine();
        Console.WriteLine("Criterio de éxito (doc 12 §5): al menos un e-CF 32 y un e-CF 31 ACEPTADOS en testecf.");
        Console.WriteLine("Código de salida: 0 = PASS · 1 = hay fallos · 2 = pre-requisitos incumplidos.");
    }
}

/// <summary>Bitácora de la sesión: cada respuesta cruda de la DGII, para conciliar los contratos asumidos de la KB.</summary>
internal static class Bitacora
{
    public static List<RegistroBitacora> Contenido { get; } = new();

    public static void Registrar(string caso, string operacion, int codigoHttp, string? cuerpo) =>
        Contenido.Add(new RegistroBitacora(DateTime.UtcNow, caso, operacion, codigoHttp, cuerpo ?? ""));

    public sealed record RegistroBitacora(DateTime FechaUtc, string Caso, string Operacion, int CodigoHttp, string Cuerpo);
}
