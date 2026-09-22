using System;
using POS.Application.DTOs;
using POS.Domain.Entities;
using POS.Domain.Enums;

namespace POS.Infrastructure.Services.Dgii;

/// <summary>
/// Dueño único de la consolidación del veredicto fiscal de la DGII sobre el estado local del
/// comprobante: la traducción de los estados oficiales (Aceptado / Aceptado Condicional /
/// Rechazado / Anulado), la traza oficial (EstadoDgii, MensajesDgii, SecuenciaUtilizada) y las
/// fechas fiscales. No toca repositorios: el llamador persiste.
/// </summary>
internal static class ConsolidadorResultadoFiscal
{
    /// <summary>
    /// Aplica el veredicto de una recepción RFCE (resultado definitivo en la misma llamada).
    /// </summary>
    public static void ConsolidarRecepcion(ElectronicInvoice invoice, DgiiApiResponse respuesta)
    {
        switch ((respuesta.Estado ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "aceptado":
            case "aceptado condicional":
                // Ambos tienen validez fiscal; el condicional trae observaciones en los mensajes.
                invoice.Estado = EstadoFacturaElectronica.Aceptado;
                invoice.FechaAprobacion = DateTime.UtcNow;
                break;

            case "rechazado":
                invoice.Estado = EstadoFacturaElectronica.Rechazado;
                invoice.MotivoRechazo = respuesta.Mensaje;
                break;

            default:
                // Respuesta sin estado reconocible: queda como recibida y en proceso de verificación.
                invoice.Estado = EstadoFacturaElectronica.EnProceso;
                break;
        }

        invoice.AvanzarEstado(EstadoEmisionECF.ConfirmadaEnvio);
    }

    /// <summary>
    /// Aplica el veredicto de una consulta de resultado (por TrackId o consulta RFCE). Devuelve
    /// true si el veredicto fue definitivo y aplicado; false si la respuesta no fue consultable.
    /// </summary>
    public static bool ConsolidarConsulta(ElectronicInvoice invoice, DgiiApiResponse respuesta)
    {
        if (!respuesta.EsExitoso || string.IsNullOrWhiteSpace(respuesta.Estado))
            return false;

        var nuevoEstado = respuesta.Estado.ToUpperInvariant() switch
        {
            "ACEPTADO" or "1" => EstadoFacturaElectronica.Aceptado,
            "ACEPTADO CONDICIONAL" => EstadoFacturaElectronica.Aceptado,
            "RECHAZADO" or "2" => EstadoFacturaElectronica.Rechazado,
            "ANULADO" or "3" => EstadoFacturaElectronica.Anulado,
            _ => EstadoFacturaElectronica.EnProceso
        };

        // Traza del resultado oficial tal como lo reportó la DGII.
        invoice.EstadoDgii = respuesta.Estado;
        invoice.SecuenciaUtilizada = respuesta.SecuenciaUtilizada;
        if (!string.IsNullOrWhiteSpace(respuesta.Mensaje))
            invoice.MensajesDgii = respuesta.Mensaje;

        invoice.Estado = nuevoEstado;
        if (nuevoEstado == EstadoFacturaElectronica.Aceptado)
        {
            invoice.FechaAprobacion = DateTime.UtcNow;
            // La confirmación resuelve un envío previamente incierto.
            if (invoice.EstadoEmision.PuedeTransicionarA(EstadoEmisionECF.ConfirmadaEnvio))
                invoice.EstadoEmision = EstadoEmisionECF.ConfirmadaEnvio;
        }

        return true;
    }
}
