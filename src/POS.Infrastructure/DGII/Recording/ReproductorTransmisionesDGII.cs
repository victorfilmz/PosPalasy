using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace POS.Infrastructure.DGII.Recording;

/// <summary>
/// Reproduce un cofre de contratos grabado por <see cref="GrabadorTransmisionesDGII"/>: sirve la
/// respuesta real capturada para cada petición (por método + URL), sin tocar la red. Usos: tests
/// de integración con evidencia real de testecf y diagnóstico de una sesión de homologación.
/// Una URL sin grabación responde 404 con el nombre del método y la URL esperados.
/// </summary>
public sealed class ReproductorTransmisionesDGII : HttpMessageHandler
{
    private readonly Dictionary<(string Metodo, string Url), RegistroTransmisionDGII> _respuestas;

    public ReproductorTransmisionesDGII(IEnumerable<RegistroTransmisionDGII> registros)
    {
        _respuestas = registros.ToDictionary(
            r => (r.Metodo, r.Url),
            r => r,
            new ClavePeticionComparer());
    }

    /// <summary>Clave de petición insensible a mayúsculas (método + URL).</summary>
    private sealed class ClavePeticionComparer : IEqualityComparer<(string Metodo, string Url)>
    {
        public bool Equals((string Metodo, string Url) x, (string Metodo, string Url) y) =>
            string.Equals(x.Metodo, y.Metodo, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Url, y.Url, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Metodo, string Url) clave) =>
            HashCode.Combine(
                clave.Metodo.ToUpperInvariant(),
                clave.Url.ToUpperInvariant());
    }

    /// <summary>Carga un cofre grabado desde disco.</summary>
    public static ReproductorTransmisionesDGII DeArchivo(string rutaArchivo) =>
        new(System.Text.Json.JsonSerializer.Deserialize<List<RegistroTransmisionDGII>>(
            File.ReadAllText(rutaArchivo))!);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clave = (Metodo: request.Method.Method, Url: request.RequestUri?.ToString() ?? string.Empty);
        if (_respuestas.TryGetValue(clave, out var registro))
        {
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)registro.CodigoHttp)
            {
                Content = new StringContent(registro.Cuerpo, Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                $"[Reproductor] Sin grabación para {clave.Metodo} {clave.Url}", Encoding.UTF8, "text/plain")
        });
    }
}
