using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Application.DTOs;
using POS.Application.Interfaces;
using POS.Application.Services;
using POS.Application.Validators;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Infrastructure.DGII;
using POS.Infrastructure.XmlSerialization;

namespace POS.Infrastructure.Services;

public class DgiiElectronicInvoiceService : IElectronicInvoiceService
{
    private readonly IXmlSerializer _xmlSerializer;
    private readonly IXmlValidator _xmlValidator;
    private readonly IHashGenerator _hashGenerator;
    private readonly IDgiiApiClient _dgiiApiClient;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IAnulacionRepository _anulacionRepository;
    private readonly ILogger<DgiiElectronicInvoiceService> _logger;
    private readonly string _xsdBasePath;

    public DgiiElectronicInvoiceService(
        IXmlSerializer xmlSerializer,
        IXmlValidator xmlValidator,
        IHashGenerator hashGenerator,
        IDgiiApiClient dgiiApiClient,
        IInvoiceRepository invoiceRepository,
        IAnulacionRepository anulacionRepository,
        ILogger<DgiiElectronicInvoiceService> logger,
        string? xsdBasePath = null)
    {
        _xmlSerializer = xmlSerializer ?? throw new ArgumentNullException(nameof(xmlSerializer));
        _xmlValidator = xmlValidator ?? throw new ArgumentNullException(nameof(xmlValidator));
        _hashGenerator = hashGenerator ?? throw new ArgumentNullException(nameof(hashGenerator));
        _dgiiApiClient = dgiiApiClient ?? throw new ArgumentNullException(nameof(dgiiApiClient));
        _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
        _anulacionRepository = anulacionRepository ?? throw new ArgumentNullException(nameof(anulacionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _xsdBasePath = xsdBasePath ?? Path.Combine(AppContext.BaseDirectory, "documentacion xsd");
    }

    public Task<string> GenerarXmlAsync(ElectronicInvoiceRequest request)
    {
        var xml = _xmlSerializer.Serialize(request);
        return Task.FromResult(xml);
    }

    public Task<ValidationResult> ValidarXmlAsync(string xmlContent)
    {
        var xsdPath = Path.Combine(_xsdBasePath, "e-CF 32 v.1.0.xsd");
        if (!File.Exists(xsdPath))
        {
            // Intentar ruta relativa desde raíz del proyecto
            xsdPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "documentacion xsd", "e-CF 32 v.1.0.xsd"));
        }

        var result = _xmlValidator.Validate(xmlContent, xsdPath);
        return Task.FromResult(result);
    }

    public string GenerarHash(string xmlContent)
    {
        return _hashGenerator.Generate(xmlContent);
    }

    public async Task<ElectronicInvoiceResponse> EmitirAsync(EmitirFacturaCommand command, CancellationToken cancellationToken = default)
    {
        var req = command.Request;

        // 1. Validación de reglas de negocio en memoria
        var valResult = EmitirFacturaValidator.Validar(req);
        if (!valResult.EsValido)
        {
            return new ElectronicInvoiceResponse
            {
                Exitoso = false,
                eNCF = req.eNCF,
                Estado = EstadoFacturaElectronica.Rechazado,
                Mensaje = string.Join(" | ", valResult.Errores)
            };
        }

        // 2. Serialización XML
        var xml = _xmlSerializer.Serialize(req);

        // 3. Generación del Hash de seguridad (6 caracteres)
        var hash = _hashGenerator.Generate(xml);
        req.CodigoSeguridadeCF = hash;

        // 4. Codificar a Base64
        var xmlBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));

        // 5. Envío a DGII vía REST API JSON
        var dgiiResp = await _dgiiApiClient.EnviarFacturaAsync(xmlBase64, hash, cancellationToken);

        var estado = dgiiResp.EsExitoso
            ? EstadoFacturaElectronica.EnProceso
            : EstadoFacturaElectronica.PendienteReenvio; // Contingencia / Reenvío

        // 6. Persistencia en Base de Datos
        var invoiceEntity = new ElectronicInvoice
        {
            VentaId = command.VentaId,
            TipoeCF = req.TipoeCF,
            eNCF = req.eNCF,
            Version = req.Version,
            RNCEmisor = req.Emisor.RNC,
            RazonSocialEmisor = req.Emisor.RazonSocial,
            RNCComprador = req.Comprador?.RNC,
            RazonSocialComprador = req.Comprador?.RazonSocial,
            FechaEmision = req.FechaFactura.ToXmlString(),
            TipoIngresos = req.TipoIngresos,
            TipoPago = req.TipoPago,
            MontoGravadoTotal = req.Totales.MontoGravadoTotal,
            MontoGravadoI1 = req.Totales.MontoGravadoI1,
            MontoGravadoI2 = req.Totales.MontoGravadoI2,
            MontoGravadoI3 = req.Totales.MontoGravadoI3,
            MontoExento = req.Totales.MontoExento,
            TotalITBIS = req.Totales.TotalITBIS,
            TotalITBIS1 = req.Totales.TotalITBIS1,
            TotalITBIS2 = req.Totales.TotalITBIS2,
            TotalITBIS3 = req.Totales.TotalITBIS3,
            MontoImpuestoAdicional = req.Totales.TotalISC,
            MontoTotal = req.Totales.Total,
            XMLContent = xml,
            XMLHash = hash,
            TrackId = dgiiResp.TrackId,
            Estado = estado,
            FechaEnvio = DateTime.UtcNow
        };

        foreach (var itm in req.Items)
        {
            invoiceEntity.Items.Add(new InvoiceItem
            {
                NumeroLinea = itm.Indice,
                IndicadorFacturacion = itm.IndicadorFacturacion,
                NombreItem = itm.Descripcion,
                CantidadItem = itm.Cantidad,
                UnidadMedida = itm.UnidadMedida,
                PrecioUnitarioItem = itm.PrecioUnitario,
                DescuentoMonto = itm.Descuento,
                RecargoMonto = itm.Recargo,
                Subtotal = itm.Subtotal,
                MontoITBIS = itm.ITBIS,
                CodigoISC = itm.ISC,
                MontoISC = itm.ISCValue,
                MontoItem = itm.Total
            });
        }

        await _invoiceRepository.AddAsync(invoiceEntity, cancellationToken);

        return new ElectronicInvoiceResponse
        {
            Exitoso = dgiiResp.EsExitoso,
            eNCF = req.eNCF,
            TrackId = dgiiResp.TrackId,
            Estado = estado,
            CodigoSeguridadeCF = hash,
            Mensaje = dgiiResp.Mensaje
        };
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

        // 1. Serializar XML de Anulación (ANECF)
        var xml = _xmlSerializer.SerializeAnulacion(request);
        var hash = _hashGenerator.Generate(xml);
        var xmlBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(xml));

        // 2. Enviar a DGII vía REST JSON
        var dgiiResp = await _dgiiApiClient.EnviarAnulacionAsync(xmlBase64, hash, cancellationToken);

        // 3. Persistir registro de Anulación
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

        // 4. Actualizar factura electrónica correspondiente si existe localmente
        var invoice = await _invoiceRepository.GetByENCFAsync(request.eNCFDesde, cancellationToken);
        if (invoice != null)
        {
            invoice.Estado = EstadoFacturaElectronica.Anulado;
            invoice.MotivoAnulacion = request.Motivo;
            invoice.FechaAnulacion = DateTime.UtcNow;
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }

        return new AnulacionResponse
        {
            Exitoso = true,
            TrackId = dgiiResp.TrackId,
            Mensaje = dgiiResp.EsExitoso 
                ? "Anulación procesada y aceptada por la DGII." 
                : $"Anulación registrada localmente. Pendiente confirmación DGII: {dgiiResp.Mensaje}"
        };
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

        if (invoice != null && dgiiResp.EsExitoso && !string.IsNullOrWhiteSpace(dgiiResp.Estado))
        {
            var nuevoEstado = dgiiResp.Estado.ToUpperInvariant() switch
            {
                "ACEPTADO" or "1" => EstadoFacturaElectronica.Aceptado,
                "RECHAZADO" or "2" => EstadoFacturaElectronica.Rechazado,
                "ANULADO" or "3" => EstadoFacturaElectronica.Anulado,
                _ => EstadoFacturaElectronica.EnProceso
            };

            invoice.Estado = nuevoEstado;
            if (nuevoEstado == EstadoFacturaElectronica.Aceptado)
                invoice.FechaAprobacion = DateTime.UtcNow;

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

        var xmlBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(invoice.XMLContent));
        var dgiiResp = await _dgiiApiClient.EnviarFacturaAsync(xmlBase64, invoice.XMLHash, cancellationToken);

        if (dgiiResp.EsExitoso)
        {
            invoice.TrackId = dgiiResp.TrackId;
            invoice.Estado = EstadoFacturaElectronica.EnProceso;
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }

        return new ElectronicInvoiceResponse
        {
            Exitoso = dgiiResp.EsExitoso,
            eNCF = eNCF,
            TrackId = dgiiResp.TrackId,
            Estado = invoice.Estado,
            Mensaje = dgiiResp.Mensaje
        };
    }
}
