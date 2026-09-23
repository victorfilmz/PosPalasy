using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace POS.Infrastructure.DGII.Recording;

/// <summary>
/// Grabación de una respuesta de la DGII para el cofre de contratos: captura el método, la URL
/// solicitada, el código HTTP y el cuerpo crudo. Es el insumo de los tests de contrato (fixtures
/// oficiales) y del análisis del primer contacto real con testecf.
/// </summary>
public sealed record RegistroTransmisionDGII(
    DateTimeOffset Fecha,
    string Metodo,
    string Url,
    int CodigoHttp,
    string Cuerpo);

/// <summary>
/// Cofre de contratos DGII: graba las respuestas reales del primer contacto con testecf en un
/// archivo JSON por sesión, para que los contratos JSON asumidos de la KB dejen de ser supuestos.
/// Se activa por configuración (<c>DGII:GrabarTransmisiones=true</c>); si el archivo está bloqueado
/// u ocurre otro error de E/S, la grabación se desactiva con un log de error y el flujo fiscal
/// sigue intacto — la grabación nunca debe romper una transmisión.
/// </summary>
public sealed class GrabadorTransmisionesDGII
{
    private readonly object _candado = new();
    private readonly string _rutaArchivo;
    private readonly ILogger<GrabadorTransmisionesDGII> _logger;
    private readonly List<RegistroTransmisionDGII> _registros = new();
    private bool _deshabilitado;

    public GrabadorTransmisionesDGII(string rutaArchivo, ILogger<GrabadorTransmisionesDGII> logger)
    {
        _rutaArchivo = rutaArchivo;
        _logger = logger;
    }

    /// <summary>Ruta del archivo de la sesión en curso (informativo / diagnóstico).</summary>
    public string RutaArchivo => _rutaArchivo;

    /// <summary>Registra una transmisión y persiste el cofre inmediatamente (corte de luz seguro).</summary>
    public void Grabar(RegistroTransmisionDGII registro)
    {
        lock (_candado)
        {
            if (_deshabilitado) return;
            _registros.Add(registro);
            try
            {
                var opciones = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(_rutaArchivo, JsonSerializer.Serialize(_registros, opciones));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _deshabilitado = true;
                _logger.LogError(ex,
                    "Grabación DGII desactivada: no se pudo escribir el cofre {Archivo}. El flujo fiscal continúa sin grabación.",
                    _rutaArchivo);
            }
        }
    }
}
