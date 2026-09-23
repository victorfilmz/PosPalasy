using System;
using POS.Domain.Entities;
using POS.Domain.Enums;

namespace POS.Application.Services;

/// <summary>
/// Comando para declarar contingencia sobre un comprobante emitido sin transmisión inmediata.
/// </summary>
public record DeclararContingenciaCommand(
    int ElectronicInvoiceId,
    TipoContingenciaDgii Tipo,
    DateTime AhoraUtc);

/// <summary>Resultado de la declaración de contingencia.</summary>
public record ContingenciaDeclarada(
    bool Exitoso,
    string Mensaje,
    DateTime? VentanaHastaUtc = null);

/// <summary>
/// Régimen de contingencia (FASE 6.1): declara el TIPO oficial (1–5) y la ventana normativa de
/// 30 días sobre un comprobante que nació sin transmisión (DGII caída, certificado ausente, etc.).
/// Los XSD e-CF v1.0 no llevan campo XML de contingencia: el marcado es metadato local y el
/// comprobante se transmite después como e-CF normal dentro de su ventana.
/// </summary>
public static class ServicioContingencia
{
    /// <summary>
    /// Declara contingencia sobre un comprobante pendiente de transmisión. Idempotente: si el
    /// comprobante ya tiene contingencia declarada, no la sobrescribe (trazabilidad normativa).
    /// </summary>
    public static ContingenciaDeclarada Declarar(ElectronicInvoice invoice, DeclararContingenciaCommand cmd)
    {
        if (invoice is null) throw new ArgumentNullException(nameof(invoice));

        if (invoice.Estado is EstadoFacturaElectronica.Aceptado or EstadoFacturaElectronica.EnProceso)
            return new ContingenciaDeclarada(false,
                $"El comprobante {invoice.eNCF} ya fue transmitido a la DGII; no aplica contingencia.");

        if (invoice.Estado is EstadoFacturaElectronica.Anulado or EstadoFacturaElectronica.Rechazado)
            return new ContingenciaDeclarada(false,
                $"El comprobante {invoice.eNCF} está {invoice.Estado}; no aplica contingencia.");

        if (!Enum.IsDefined(cmd.Tipo))
            return new ContingenciaDeclarada(false, "Tipo de contingencia inválido (1–5).");

        if (invoice.TipoContingencia.HasValue)
        {
            // Idempotencia: la primera declaración manda (la ventana normativa corre desde entonces).
            return new ContingenciaDeclarada(true,
                $"La contingencia ya estaba declarada ({invoice.TipoContingencia}); no se sobrescribe.",
                invoice.ContingenciaHastaUtc);
        }

        invoice.TipoContingencia = cmd.Tipo;
        invoice.ContingenciaDesdeUtc = cmd.AhoraUtc;
        invoice.ContingenciaHastaUtc = RegimenContingencia.FinDeVentana(cmd.AhoraUtc);

        return new ContingenciaDeclarada(true,
            $"Contingencia {cmd.Tipo} declarada para {invoice.eNCF}; ventana de transmisión hasta " +
            $"{invoice.ContingenciaHastaUtc:dd-MM-yyyy HH:mm} UTC.",
            invoice.ContingenciaHastaUtc);
    }

    /// <summary>
    /// ¿Puede este comprobante transmitirse dentro de su ventana de contingencia? Sin contingencia
    /// declarada, siempre puede (flujo normal).
    /// </summary>
    public static bool PuedeTransmitirse(ElectronicInvoice invoice, DateTime ahoraUtc) =>
        !invoice.ContingenciaDesdeUtc.HasValue || RegimenContingencia.VentanaAbierta(invoice.ContingenciaDesdeUtc.Value, ahoraUtc);
}
