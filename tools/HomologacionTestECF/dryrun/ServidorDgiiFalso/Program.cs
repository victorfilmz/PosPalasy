// Servidor falso de la API DGII para ENSAYAR el verificador de homologación
// (tools/HomologacionTestECF) sin credenciales reales. NO es la DGII.
//
// Ejecutar:  dotnet run --project tools/HomologacionTestECF/dryrun/ServidorDgiiFalso
// Escucha en http://127.0.0.1:8443 y responde las rutas oficiales de testecf.
//
// Comportamiento:
//   GET  .../autenticacion/semilla         → XML SemillaModel
//   POST .../autenticacion/validarsemilla  → JSON {token, expira}
//   POST .../recepcion*/...                → acepta multipart; VERIFICA la firma XML-DSig del
//                                             documento contra el certificado incrustado (igual
//                                             que la DGII): Rechazado si no verifica.
//   GET  .../consultaresultado?trackid=... → "En proceso" ×2 y luego "Aceptado"
//   GET  .../consultarfce?...              → {codigo:1, estado:Aceptado}
//   GET  .../consultatrackids?...          → arreglo JSON de TrackIds
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

var puerto = 8443;
var oyente = new HttpListener();
oyente.Prefixes.Add($"http://127.0.0.1:{puerto}/");
oyente.Start();
Console.WriteLine($"DGII falsa escuchando en http://127.0.0.1:8443 (solo ensayo del verificador)");

var consultasPorTrack = new Dictionary<string, int>();
var tracksPorEncf = new Dictionary<string, string>();

while (true)
{
    var ctx = await oyente.GetContextAsync();
    try
    {
        var ruta = ctx.Request.Url!.AbsolutePath;
        var query = ctx.Request.QueryString ?? new System.Collections.Specialized.NameValueCollection();
        Console.WriteLine($"  [dgii-falsa] {ctx.Request.HttpMethod} {ruta}");

        // ------------------------------------------------------- autenticación
        if (ruta.EndsWith("/autenticacion/semilla"))
        {
            await XmlAsync(ctx, 200,
                $"<SemillaModel><valor>{Guid.NewGuid():N}</valor></SemillaModel>");
        }
        else if (ruta.EndsWith("/autenticacion/validarsemilla"))
        {
            await JsonAsync(ctx, 200, new Dictionary<string, object?>
            {
                ["token"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                ["expira"] = "2030-01-01T00:00:00"
            });
        }
        // ------------------------------------------------------------ recepción
        else if (ruta.Contains("/recepcion"))
        {
            var cuerpo = await LeerCuerpoAsync(ctx);
            var xml = ExtraerXml(cuerpo);
            var encf = Ultimos13(NombreEncf(cuerpo));
            var (firmaVerifica, detalle) = VerificarFirmaXmlDsig(xml);

            var track = Guid.NewGuid().ToString("N")[..15].ToUpperInvariant();
            tracksPorEncf[encf] = track;

            if (firmaVerifica)
                await JsonAsync(ctx, 200, new Dictionary<string, object?>
                    { ["trackId"] = track, ["estado"] = "En proceso" });
            else
                await JsonAsync(ctx, 200, new Dictionary<string, object?>
                {
                    ["trackId"] = track, ["estado"] = "Rechazado",
                    ["mensaje"] = $"Firma del comprobante no valida (simulado): {detalle}"
                });
        }
        // ------------------------------------------------------------ consultas
        else if (ruta.Contains("consultaresultado"))
        {
            var trackid = query["trackid"] ?? "";
            consultasPorTrack.TryGetValue(trackid, out var n);
            consultasPorTrack[trackid] = n + 1;
            await JsonAsync(ctx, 200, new Dictionary<string, object?>
            {
                ["trackId"] = trackid,
                ["estado"] = n < 2 ? "En proceso" : "Aceptado",
                ["secuenciaUtilizada"] = true
            });
        }
        else if (ruta.Contains("consultarfce"))
        {
            await JsonAsync(ctx, 200, new Dictionary<string, object?>
            {
                ["rnc"] = "", ["encf"] = query["ENCF"] ?? "",
                ["codigo"] = 1, ["estado"] = "Aceptado"
            });
        }
        else if (ruta.Contains("consultatrackids"))
        {
            var encf = query["encf"] ?? "";
            await JsonAsync(ctx, 200,
                tracksPorEncf.TryGetValue(encf, out var t) ? new[] { t } : Array.Empty<string>());
        }
        else if (ruta.Contains("consultaestado"))
        {
            await JsonAsync(ctx, 200, new Dictionary<string, object?>
                { ["estado"] = "Aceptado", ["secuenciaUtilizada"] = true });
        }
        else
        {
            await JsonAsync(ctx, 404, new Dictionary<string, object?>
                { ["error"] = $"ruta no simulada: {ruta}" });
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  [dgii-falsa] ERROR: {ex.Message}");
        try { await JsonAsync(ctx, 500, new Dictionary<string, object?> { ["error"] = ex.Message }); }
        catch { /* el cliente ya cerró la conexión */ }
    }
}

// ------------------------------------------------------------------ firma XML-DSig real
/// <summary>
/// Verificación criptográfica REAL de la firma enveloped del documento, contra el certificado
/// incrustado en el propio XML (mismo criterio que la DGII). Devuelve (verifica, detalle).
/// </summary>
static (bool, string) VerificarFirmaXmlDsig(string? xml)
{
    if (string.IsNullOrEmpty(xml))
        return (false, "el cuerpo no contiene XML");

    try
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);

        var nodosFirma = doc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
        if (nodosFirma.Count == 0 || nodosFirma[0] is not XmlElement nodoFirma)
            return (false, "sin ds:Signature incrustada");

        var signedXml = new SignedXml(doc);
        try { signedXml.LoadXml(nodoFirma); }
        catch (Exception ex) { return (false, $"Signature ilegible: {ex.Message}"); }

        var certificado = ExtraerCertificadoDeLaFirma(signedXml);
        if (certificado is null)
            return (false, "sin X509Certificate en la firma");

        if (!signedXml.CheckSignature(certificado, true))
            return (false, "la verificación criptográfica falló (contenido alterado o clave distinta)");

        return (true, $"firma válida de {certificado.Subject}");
    }
    catch (XmlException ex)
    {
        return (false, $"XML ilegible: {ex.Message}");
    }
}

static X509Certificate2? ExtraerCertificadoDeLaFirma(SignedXml signedXml)
{
    foreach (KeyInfoClause clause in signedXml.KeyInfo)
    {
        if (clause is KeyInfoX509Data datos && datos.Certificates.Count > 0)
            return (X509Certificate2)datos.Certificates[0]!;
    }
    return null;
}

// ------------------------------------------------------------------ utilidades HTTP
static async Task<string> LeerCuerpoAsync(HttpListenerContext ctx)
{
    using var lector = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
    return await lector.ReadToEndAsync();
}

static string? ExtraerXml(string cuerpo)
{
    // El serializer de PosPalasy NO emite declaración <?xml ...?>: el documento empieza en <ECF.
    // Se extrae desde la primera etiqueta del documento hasta el cierre raíz.
    var inicio = cuerpo.IndexOf("<ECF", StringComparison.Ordinal);
    if (inicio < 0) inicio = cuerpo.IndexOf("<ANECF", StringComparison.Ordinal);
    if (inicio < 0) return null;
    var fin = cuerpo.IndexOf("--", inicio, StringComparison.Ordinal);
    return fin > inicio ? cuerpo[inicio..fin].TrimEnd('\r', '\n') : cuerpo[inicio..];
}

static string NombreEncf(string cuerpo)
{
    // RNC (9-11 dígitos) + E + tipo (2) + secuencia (10).
    var m = Regex.Match(cuerpo, @"(\d+E\d{12})\.xml");
    return m.Success ? m.Groups[1].Value : "?";
}

/// <summary>La e-NCF son los últimos 13 caracteres del nombre de archivo (RNC + eNCF).</summary>
static string Ultimos13(string nombre) => nombre.Length >= 13 ? nombre[^13..] : nombre;

static async Task JsonAsync(HttpListenerContext ctx, int codigo, object objeto)
{
    var cuerpo = JsonSerializer.Serialize(objeto);
    var bytes = Encoding.UTF8.GetBytes(cuerpo);
    ctx.Response.StatusCode = codigo;
    ctx.Response.ContentType = "application/json";
    ctx.Response.ContentLength64 = bytes.Length;
    await ctx.Response.OutputStream.WriteAsync(bytes);
    ctx.Response.Close();
}

static async Task XmlAsync(HttpListenerContext ctx, int codigo, string texto)
{
    var bytes = Encoding.UTF8.GetBytes(texto);
    ctx.Response.StatusCode = codigo;
    ctx.Response.ContentType = "text/xml";
    ctx.Response.ContentLength64 = bytes.Length;
    await ctx.Response.OutputStream.WriteAsync(bytes);
    ctx.Response.Close();
}
