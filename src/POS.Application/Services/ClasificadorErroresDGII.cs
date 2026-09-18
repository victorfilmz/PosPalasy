using System;

namespace POS.Application.Services;

/// <summary>
/// Clasifica la respuesta de la DGII para decidir la política de reintento. Confundir un error
/// permanente con uno temporal produce reintentos infinitos sobre documentos que nunca serán
/// aceptados; lo contrario descarta documentos válidos por una caída momentánea.
/// </summary>
public static class ClasificadorErroresDGII
{
    /// <summary>
    /// Determina si un intento de transmisión puede repetirse automáticamente.
    /// </summary>
    /// <param name="codigoHttp">Código HTTP devuelto por la DGII (0 si no hubo respuesta).</param>
    /// <param name="esExitoso">Indica si la respuesta se consideró exitosa.</param>
    public static bool EsRecuperable(int codigoHttp, bool esExitoso)
    {
        if (esExitoso)
            return false;

        return codigoHttp switch
        {
            // Sin respuesta del servidor: red, DNS, timeout, corte. Ambiguo y recuperable.
            0 => true,

            // El servidor falló: puede ser transitorio.
            500 or 502 or 503 or 504 => true,

            // Límite de tasa: reintentar es exactamente lo correcto.
            429 => true,

            // Credenciales/permisos: reintentar no arregla nada.
            401 or 403 => false,

            // Documento mal formado o rechazado por validación: reintentar no arregla nada.
            400 or 404 or 409 or 422 => false,

            // Cualquier otro 4xx es un problema del cliente.
            >= 400 and < 500 => false,

            _ => true
        };
    }

    /// <summary>
    /// Determina si un envío terminó en estado ambiguo: no hubo respuesta alguna, por lo que el
    /// documento pudo haber llegado a la DGII sin que se sepa su resultado. Exige resolver por
    /// consulta antes de reenviar a ciegas.
    /// </summary>
    /// <remarks>
    /// Un error HTTP (incluido 5xx) no es ambiguo: la DGII respondió que no procesó la solicitud, y
    /// reenviar el mismo eNCF es seguro porque la DGII no admite dos comprobantes con el mismo número.
    /// </remarks>
    public static bool EsAmbiguo(int codigoHttp) => codigoHttp == 0;

    /// <summary>Descripción legible de la causa, para el registro de trazabilidad.</summary>
    public static string Describir(int codigoHttp) => codigoHttp switch
    {
        0 => "Sin respuesta de la DGII (red, DNS o tiempo de espera agotado).",
        400 => "La DGII rechazó la solicitud por datos inválidos.",
        401 => "Credenciales de la DGII ausentes o inválidas.",
        403 => "La DGII denegó el acceso al servicio.",
        404 => "Recurso no encontrado en la DGII.",
        409 => "Conflicto: el comprobante ya existe o está en un estado incompatible.",
        422 => "Validación fiscal rechazada por la DGII.",
        429 => "Límite de solicitudes alcanzado en la DGII.",
        500 or 502 or 503 or 504 => "La DGII no está disponible en este momento.",
        _ => $"Respuesta inesperada de la DGII (HTTP {codigoHttp})."
    };
}
