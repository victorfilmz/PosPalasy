using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using POS.Application.DTOs;
using POS.Application.Validators;

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

    Task<ValidationResult> ValidarXmlAsync(string xmlContent);

    string GenerarHash(string xmlContent);

    Task<ElectronicInvoiceResponse> ReenviarAsync(
        string eNCF,
        CancellationToken cancellationToken = default);
}
