using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using POS.Application.DTOs;
using POS.Application.Validators;
using POS.Domain.Types;

namespace POS.Application.Interfaces;

public class EmitirFacturaCommand
{
    public ElectronicInvoiceRequest Request { get; set; } = new();
    public int? VentaId { get; set; }
}

/// <summary>
/// Contrato orquestador principal para la emisión, anulación, contingencia y consulta de e-CF ante la DGII.
/// </summary>
public interface IElectronicInvoiceService
{
    /// <summary>
    /// Construye el comprobante y lo registra LOCALMENTE (XML, hash, persistencia) SIN transmitirlo.
    /// Es el paso que pertenece a la transacción de la venta: la comunicación con la DGII ocurre
    /// después del commit, a través de <see cref="EnviarAsync"/>.
    /// </summary>
    Task<ComprobantePreparado> PrepararYRegistrarAsync(
        PrepararComprobanteCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transmite a la DGII un comprobante ya registrado y actualiza su estado local con el
    /// resultado real. Un fallo aquí NO invalida la venta ya confirmada.
    /// </summary>
    Task<ElectronicInvoiceResponse> EnviarAsync(
        int electronicInvoiceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Emisión completa (registrar + transmitir) en una sola llamada. Se conserva para la emisión
    /// manual desde facturación; el flujo de venta de POS usa las dos operaciones separadas.
    /// </summary>
    Task<ElectronicInvoiceResponse> EmitirAsync(
        EmitirFacturaCommand command,
        CancellationToken cancellationToken = default);

    Task<AnulacionResponse> AnularAsync(
        AnulacionRequest request,
        CancellationToken cancellationToken = default);

    Task<EstadoFacturaResponse> ConsultarEstadoAsync(
        string eNCF,
        CancellationToken cancellationToken = default);

    Task<EstadoFacturaResponse> ConsultarEstadoPorTrackIdAsync(
        string trackId,
        CancellationToken cancellationToken = default);

    Task<string> GenerarXmlAsync(ElectronicInvoiceRequest request);

    Task<ValidationResult> ValidarXmlAsync(string xmlContent, TipoeCFType tipo = TipoeCFType.FacturaConsumo);

    string GenerarHash(string xmlContent);

    Task<ElectronicInvoiceResponse> ReenviarAsync(
        string eNCF,
        CancellationToken cancellationToken = default);
}
