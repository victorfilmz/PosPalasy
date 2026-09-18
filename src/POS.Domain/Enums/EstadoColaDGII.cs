using System;

namespace POS.Domain.Enums;

/// <summary>
/// Estado de un elemento de la cola de emisión hacia la DGII (outbox persistente).
/// </summary>
public enum EstadoColaDGII : int
{
    /// <summary>Pendiente de transmisión (dentro o fuera de su ventana de reintento).</summary>
    Pendiente = 0,

    /// <summary>Reclamado por un trabajador con lease vigente: ningún otro debe procesarlo.</summary>
    EnProceso = 1,

    /// <summary>La DGII confirmó la recepción. Es el único estado terminal de éxito.</summary>
    Enviado = 2,

    /// <summary>Intentos agotados por fallos recuperables: requiere intervención.</summary>
    Fallido = 3,

    /// <summary>Fallo no recuperable (documento inválido, credenciales): no se reintenta automáticamente.</summary>
    Definitivo = 4
}

/// <summary>
/// Política de reintento de la cola: espera exponencial con jitter, acotada, y clasificación de
/// errores recuperables frente a permanentes.
/// </summary>
public static class PoliticaReintentoCola
{
    /// <summary>Intentos máximos por documento antes de marcarlo como <see cref="EstadoColaDGII.Fallido"/>.</summary>
    public const int IntentosMaximos = 8;

    /// <summary>Vigencia del lease de un trabajador; si el proceso muere, otro lo retoma al vencer.</summary>
    public static readonly TimeSpan DuracionLease = TimeSpan.FromMinutes(5);

    /// <summary>Espera base del primer reintento.</summary>
    public static readonly TimeSpan EsperaBase = TimeSpan.FromSeconds(30);

    /// <summary>Tope de espera entre intentos.</summary>
    public static readonly TimeSpan EsperaMaxima = TimeSpan.FromHours(1);

    /// <summary>
    /// Calcula el próximo instante de intento: 30s, 60s, 120s… con jitter determinista por documento
    /// para que los elementos encolados juntos no vuelvan todos a la vez.
    /// </summary>
    public static DateTime ProximoIntento(int intentosYaRealizados, DateTime ahoraUtc, int semillaJitter)
    {
        var exponente = Math.Min(Math.Max(intentosYaRealizados, 1), 12);
        var segundos = EsperaBase.TotalSeconds * Math.Pow(2, exponente - 1);
        var espera = TimeSpan.FromSeconds(Math.Min(segundos, EsperaMaxima.TotalSeconds));

        // Jitter de hasta el 20 % de la espera, estable por documento (evita estampida de reintentos).
        var jitterSegundos = Math.Abs(semillaJitter % 100) / 100.0 * espera.TotalSeconds * 0.2;

        return ahoraUtc.Add(espera).AddSeconds(jitterSegundos);
    }
}
