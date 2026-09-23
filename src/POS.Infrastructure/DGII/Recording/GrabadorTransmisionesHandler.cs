using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace POS.Infrastructure.DGII.Recording;

/// <summary>
/// Intercepta todas las respuestas HTTP de los clientes DGII (autenticación y API) y las graba
/// en el cofre de contratos. Cero cambios en los clientes: se enchufa como mensaje handler de
/// <c>AddHttpClient(...).AddHttpMessageHandler(...)</c>. Solo escribe en modo grabación; en
/// cualquier otro caso es un passthrough puro de la respuesta.
/// </summary>
public sealed class GrabadorTransmisionesHandler : DelegatingHandler
{
    private readonly GrabadorTransmisionesDGII? _grabador;

    public GrabadorTransmisionesHandler(GrabadorTransmisionesDGII? grabador) => _grabador = grabador;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var respuesta = await base.SendAsync(request, cancellationToken);

        if (_grabador != null)
        {
            var cuerpo = respuesta.Content == null
                ? string.Empty
                : await respuesta.Content.ReadAsStringAsync(cancellationToken);
            var cuerpoConBuffer = new StringContent(
                cuerpo, Encoding.UTF8, respuesta.Content?.Headers.ContentType?.MediaType ?? "text/plain");
            respuesta.Content?.Dispose();
            respuesta.Content = cuerpoConBuffer;

            _grabador.Grabar(new RegistroTransmisionDGII(
                Fecha: DateTimeOffset.Now,
                Metodo: request.Method.Method,
                Url: request.RequestUri?.ToString() ?? string.Empty,
                CodigoHttp: (int)respuesta.StatusCode,
                Cuerpo: cuerpo));
        }

        return respuesta;
    }
}
