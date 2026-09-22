using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.DTOs;
using POS.Application.Interfaces;
using POS.Application.Services;
using POS.Application.Validators;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Domain.Types;
using POS.Infrastructure.DGII;
using POS.Infrastructure.Services.Dgii;
using POS.Infrastructure.XmlSerialization;

namespace POS.Infrastructure.Services;

/// <summary>
/// Fachada del pipeline fiscal ante la DGII (contrato <see cref="IElectronicInvoiceService"/>).
/// Orquesta los dueños de cada responsabilidad, que viven bajo <c>Services/Dgii</c>:
/// <list type="bullet">
/// <item><see cref="EmisorComprobantes"/>: preparar el comprobante dentro de la transacción de la
/// venta y transmitirlo con su resultado real.</item>
/// <item><see cref="FirmadorEnTransmision"/>: firma XML-DSig en transmisión (5.1).</item>
/// <item><see cref="ConsolidadorResultadoFiscal"/>: consolidación del veredicto fiscal.</item>
/// <item><see cref="LiberadorSecuencias"/>: liberación de secuencias tras rechazo (5.5).</item>
/// <item><see cref="AnuladorDeRangos"/>: anulación de rangos de secuencias (ANECF).</item>
/// <item><see cref="PoliticaRecepcion"/>: regla de recepción RFCE/e-CF (RD$250,000).</item>
/// </list>
/// Esta clase conserva las consultas de estado y la generación simple de XML/hash, que son
/// orquestación pura sobre los dueños anteriores.
/// </summary>
public class DgiiElectronicInvoiceService : IElectronicInvoiceService
{
    private readonly IXmlSerializer _xmlSerializer;
    private readonly IHashGenerator _hashGenerator;
    private readonly IDgiiApiClient _dgiiApiClient;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly EmisorComprobantes _emisor;
    private readonly AnuladorDeRangos _anulador;
    private readonly LiberadorSecuencias _liberador;

    public DgiiElectronicInvoiceService(
        IXmlSerializer xmlSerializer,
        IXmlValidator xmlValidator,
        IHashGenerator hashGenerator,
        IDgiiApiClient dgiiApiClient,
        IInvoiceRepository invoiceRepository,
        IAnulacionRepository anulacionRepository,
        IEmisionDGIIQueueRepository queueRepository,
        ILogger<DgiiElectronicInvoiceService> logger,
        string? xsdBasePath = null,
        ISecurityCodeGenerator? codigoSeguridad = null,
        IProveedorCertificadoDigital? proveedorCertificado = null,
        IFirmadorComprobanteECF? firmador = null,
        DgiiConfig? dgiiConfig = null,
        ISecuenciaLibreRepository? secuenciasLibresRepository = null,
        IAuditoriaRepository? auditoriaRepository = null)
    {
        _xmlSerializer = xmlSerializer ?? throw new ArgumentNullException(nameof(xmlSerializer));
        _hashGenerator = hashGenerator ?? throw new ArgumentNullException(nameof(hashGenerator));
        _dgiiApiClient = dgiiApiClient ?? throw new ArgumentNullException(nameof(dgiiApiClient));
        _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));

        var basePath = xsdBasePath ?? Path.Combine(AppContext.BaseDirectory, "documentacion xsd");
        var simulador = dgiiConfig?.ModoSimulador ?? false;

        _liberador = new LiberadorSecuencias(secuenciasLibresRepository, auditoriaRepository, logger);
        var firmadorEnTransmision = new FirmadorEnTransmision(
            proveedorCertificado, firmador, invoiceRepository, queueRepository, logger);

        _emisor = new EmisorComprobantes(
            xmlSerializer,
            xmlValidator,
            codigoSeguridad ?? new GeneradorCodigoSeguridad(),
            dgiiApiClient,
            invoiceRepository,
            queueRepository,
            _liberador,
            firmadorEnTransmision,
            logger,
            basePath,
            simulador);

        _anulador = new AnuladorDeRangos(
            xmlSerializer,
            xmlValidator,
            hashGenerator,
            dgiiApiClient,
            invoiceRepository,
            anulacionRepository,
            logger,
            basePath);
    }

    public Task<string> GenerarXmlAsync(ElectronicInvoiceRequest request)
    {
        var xml = _xmlSerializer.Serialize(request);
        return Task.FromResult(xml);
    }

    public Task<ValidationResult> ValidarXmlAsync(string xmlContent, TipoeCFType tipo = TipoeCFType.FacturaConsumo)
    {
        return _emisor.ValidarXmlAsync(xmlContent, tipo);
    }

    public string GenerarHash(string xmlContent)
    {
        return _hashGenerator.Generate(xmlContent);
    }

    public Task<ComprobantePreparado> PrepararYRegistrarAsync(
        PrepararComprobanteCommand command,
        CancellationToken cancellationToken = default)
    {
        return _emisor.PrepararYRegistrarAsync(command, cancellationToken);
    }

    public Task<ElectronicInvoiceResponse> EnviarAsync(
        int electronicInvoiceId,
        CancellationToken cancellationToken = default)
    {
        return _emisor.EnviarAsync(electronicInvoiceId, cancellationToken);
    }

    /// <summary>
    /// Emisión completa (registrar y transmitir) en una sola operación, para la emisión manual desde
    /// el módulo de facturación.
    /// </summary>
    public async Task<ElectronicInvoiceResponse> EmitirAsync(
        EmitirFacturaCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        var preparado = await PrepararYRegistrarAsync(
            new PrepararComprobanteCommand(command.Request, command.VentaId),
            cancellationToken);

        return await EnviarAsync(preparado.ElectronicInvoiceId, cancellationToken);
    }

    public Task<AnulacionResponse> AnularAsync(AnulacionRequest request, CancellationToken cancellationToken = default)
    {
        return _anulador.AnularAsync(request, cancellationToken);
    }

    public async Task<EstadoFacturaResponse> ConsultarEstadoAsync(string eNCF, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByENCFAsync(eNCF, cancellationToken);
        if (invoice == null)
        {
            return new EstadoFacturaResponse
            {
                eNCF = eNCF,
                Estado = EstadoFacturaElectronica.Rechazado,
                MensajeDGII = "Factura no encontrada en base de datos local."
            };
        }

        // RFCE: la DGII no emitió TrackId; la consulta se hace por RNC/eNCF/código de seguridad.
        if (PoliticaRecepcion.EsRFCE(invoice) && !string.IsNullOrWhiteSpace(invoice.TrackId) && invoice.Estado == EstadoFacturaElectronica.EnProceso)
        {
            return await ConsultarRFCEAsync(invoice, cancellationToken);
        }

        // e-CF: la consulta de resultado es por TrackId.
        if (!string.IsNullOrWhiteSpace(invoice.TrackId) && invoice.Estado == EstadoFacturaElectronica.EnProceso)
        {
            return await ConsultarEstadoPorTrackIdAsync(invoice.TrackId, cancellationToken);
        }

        return new EstadoFacturaResponse
        {
            eNCF = invoice.eNCF,
            TrackId = invoice.TrackId,
            Estado = invoice.Estado,
            ARECFXML = invoice.ARECFXML,
            ACECFXML = invoice.ACECFXML
        };
    }

    public async Task<EstadoFacturaResponse> ConsultarEstadoPorTrackIdAsync(string trackId, CancellationToken cancellationToken = default)
    {
        var dgiiResp = await _dgiiApiClient.ConsultarEstadoAsync(trackId, cancellationToken);
        var invoice = await _invoiceRepository.GetByTrackIdAsync(trackId, cancellationToken);

        if (invoice != null && ConsolidadorResultadoFiscal.ConsolidarConsulta(invoice, dgiiResp))
        {
            // Sub-fase 5.5: la consulta de resultado puede traer el rechazo definitivo.
            await _liberador.LiberarSiCorrespondeAsync(
                invoice, dgiiResp.SecuenciaUtilizada, cancellationToken);

            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }

        return new EstadoFacturaResponse
        {
            eNCF = invoice?.eNCF ?? string.Empty,
            TrackId = trackId,
            Estado = invoice?.Estado ?? EstadoFacturaElectronica.EnProceso,
            MensajeDGII = dgiiResp.Mensaje
        };
    }

    /// <summary>
    /// Consulta el resumen RFCE de un comprobante de consumo (&lt; RD$250,000) por RNC emisor,
    /// e-NCF y código de seguridad, y consolida el resultado oficial (0=No encontrado, 1=Aceptado,
    /// 2=Rechazado).
    /// </summary>
    private async Task<EstadoFacturaResponse> ConsultarRFCEAsync(
        ElectronicInvoice invoice, CancellationToken cancellationToken)
    {
        var dgiiResp = await _dgiiApiClient.ConsultarRFCEAsync(
            invoice.RNCEmisor,
            invoice.eNCF,
            invoice.XMLHash,
            cancellationToken);

        if (ConsolidadorResultadoFiscal.ConsolidarConsulta(invoice, dgiiResp))
        {
            // Sub-fase 5.5: la consulta RFCE puede traer el rechazo definitivo.
            await _liberador.LiberarSiCorrespondeAsync(
                invoice, dgiiResp.SecuenciaUtilizada, cancellationToken);

            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }

        return new EstadoFacturaResponse
        {
            eNCF = invoice.eNCF,
            TrackId = null,
            Estado = invoice.Estado,
            MensajeDGII = dgiiResp.Mensaje
        };
    }

    /// <summary>
    /// Reintento de transmisión para una factura localizada por su eNCF. Delega en el emisor para
    /// que exista una única ruta de transmisión y de actualización de estado.
    /// </summary>
    public async Task<ElectronicInvoiceResponse> ReenviarAsync(string eNCF, CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByENCFAsync(eNCF, cancellationToken);
        if (invoice == null)
        {
            return new ElectronicInvoiceResponse
            {
                Exitoso = false,
                eNCF = eNCF,
                Mensaje = "Factura no encontrada para reenvío."
            };
        }

        return await EnviarAsync(invoice.Id, cancellationToken);
    }
}
