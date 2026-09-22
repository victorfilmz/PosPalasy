using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.DTOs;
using POS.Application.Services;
using POS.Application.Validators;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Infrastructure.DGII;
using POS.Infrastructure.XmlSerialization;

namespace POS.Infrastructure.Services.Dgii;

/// <summary>
/// Dueño de la anulación de RANGOS de secuencias no utilizadas (ANECF). El XML se valida contra
/// el XSD oficial ANECF v.1.0 antes de transmitir; el veredicto de la DGII se consolida
/// HONESTAMENTE: <c>Exitoso</c> refleja la aceptación de la DGII (no solo el registro local), y el
/// comprobante local solo se marca <c>Anulado</c> cuando la DGII aceptó la solicitud.
/// </summary>
internal sealed class AnuladorDeRangos
{
    private readonly IXmlSerializer _xmlSerializer;
    private readonly IXmlValidator _xmlValidator;
    private readonly IHashGenerator _hashGenerator;
    private readonly IDgiiApiClient _dgiiApiClient;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IAnulacionRepository _anulacionRepository;
    private readonly ILogger _logger;
    private readonly string _xsdBasePath;

    public AnuladorDeRangos(
        IXmlSerializer xmlSerializer,
        IXmlValidator xmlValidator,
        IHashGenerator hashGenerator,
        IDgiiApiClient dgiiApiClient,
        IInvoiceRepository invoiceRepository,
        IAnulacionRepository anulacionRepository,
        ILogger logger,
        string xsdBasePath)
    {
        _xmlSerializer = xmlSerializer ?? throw new ArgumentNullException(nameof(xmlSerializer));
        _xmlValidator = xmlValidator ?? throw new ArgumentNullException(nameof(xmlValidator));
        _hashGenerator = hashGenerator ?? throw new ArgumentNullException(nameof(hashGenerator));
        _dgiiApiClient = dgiiApiClient ?? throw new ArgumentNullException(nameof(dgiiApiClient));
        _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
        _anulacionRepository = anulacionRepository ?? throw new ArgumentNullException(nameof(anulacionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _xsdBasePath = xsdBasePath ?? throw new ArgumentNullException(nameof(xsdBasePath));
    }

    public async Task<AnulacionResponse> AnularAsync(AnulacionRequest request, CancellationToken cancellationToken = default)
    {
        var valResult = AnulacionValidator.Validar(request);
        if (!valResult.EsValido)
        {
            return new AnulacionResponse
            {
                Exitoso = false,
                Mensaje = string.Join(" | ", valResult.Errores)
            };
        }

        // 1. Serializar XML de Anulación (ANECF) y validarlo contra el XSD oficial.
        var xml = _xmlSerializer.SerializeAnulacion(request);
        var validacion = _xmlValidator.Validate(xml, RutaXsdAnulacion());
        if (!validacion.EsValido)
        {
            _logger.LogError(
                "ANECF inválido contra el XSD oficial: {Errores}",
                string.Join(" | ", validacion.Errores));
            return new AnulacionResponse
            {
                Exitoso = false,
                Mensaje = "El XML de anulación no cumple el esquema oficial ANECF: " +
                    string.Join(" | ", validacion.Errores)
            };
        }

        var hash = _hashGenerator.Generate(xml);

        // 2. Enviar a DGII: multipart con el nombre oficial RNC+eNCF.xml al endpoint de anulación de rangos
        var dgiiResp = await _dgiiApiClient.EnviarAnulacionAsync(
            xml,
            DgiiApiClient.CrearNombreArchivoXml(request.RNCEmisor, request.eNCFDesde),
            cancellationToken);

        // 3. Persistir registro de Anulación (con el resultado fiscal honesto de la DGII).
        var anulacionEntity = new Anulacion
        {
            RNCEmisor = request.RNCEmisor,
            TipoeCF = request.TipoeCF,
            eNCFDesde = request.eNCFDesde,
            eNCFHasta = request.eNCFHasta,
            CantidadSecuencias = request.CantidadSecuencias,
            CodigoMotivoAnulacion = request.CodigoMotivoAnulacion,
            Motivo = request.Motivo,
            XMLContent = xml,
            TrackId = dgiiResp.TrackId,
            Aprobada = dgiiResp.EsExitoso,
            MensajeRespuesta = dgiiResp.Mensaje,
            FechaSolicitud = DateTime.UtcNow,
            FechaRespuesta = dgiiResp.EsExitoso ? DateTime.UtcNow : null
        };

        await _anulacionRepository.AddAsync(anulacionEntity, cancellationToken);

        // 4. Los comprobantes cuya secuencia cae dentro del rango anulado quedan Anulados cuando la
        // DGII ACEPTÓ la solicitud; con rechazo o fallo de transmisión conservan su estado fiscal
        // vigente y el intento queda auditable en el registro de anulación.
        if (dgiiResp.EsExitoso)
        {
            var enRango = await _invoiceRepository.GetByRangoENCFAsync(
                request.eNCFDesde, request.eNCFHasta, cancellationToken);

            foreach (var invoice in enRango.Where(i => i.Estado != EstadoFacturaElectronica.Anulado))
            {
                invoice.Estado = EstadoFacturaElectronica.Anulado;
                invoice.MotivoAnulacion = request.Motivo;
                invoice.FechaAnulacion = DateTime.UtcNow;
                await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
            }
        }

        return new AnulacionResponse
        {
            Exitoso = dgiiResp.EsExitoso,
            TrackId = dgiiResp.TrackId,
            Mensaje = dgiiResp.EsExitoso
                ? "Anulación procesada y aceptada por la DGII."
                : $"Anulación rechazada o con fallo de transmisión: {dgiiResp.Mensaje}"
        };
    }

    /// <summary>
    /// Ruta del XSD oficial de la anulación de rangos (ANECF v.1.0), con la misma resolución que
    /// los comprobantes: output de la aplicación primero y directorio de datos como respaldo.
    /// </summary>
    private string RutaXsdAnulacion()
    {
        const string nombre = "ANECF v.1.0.xsd";

        var candidatas = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "documentacion xsd", nombre),
            Path.Combine(_xsdBasePath, nombre)
        };

        var ruta = candidatas.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException(
                $"No se encontró el XSD oficial de anulación ({nombre}). Revisar la copia al output.");

        return ruta;
    }
}
