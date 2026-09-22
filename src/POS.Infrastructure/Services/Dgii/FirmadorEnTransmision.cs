using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Extensions.Logging;
using POS.Application.DTOs;
using POS.Application.Interfaces;
using POS.Application.Services;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Services.Dgii;

/// <summary>
/// Dueño único de la firma dentro de la transmisión (sub-fase 5.1): en modo REAL el documento DEBE
/// estar firmado antes de transmitirse. Sin certificado válido o con firma no verificable el envío
/// falla ANTES de salir a la red: la ausencia de certificado es recuperable de configuración (el
/// comprobante vuelve a la cola y sale solo al instalarse); una firma que no verifica es un defecto
/// del documento (permanente).
/// </summary>
internal sealed class FirmadorEnTransmision
{
    private readonly IProveedorCertificadoDigital? _proveedorCertificado;
    private readonly IFirmadorComprobanteECF? _firmador;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IEmisionDGIIQueueRepository _queueRepository;
    private readonly ILogger _logger;

    public FirmadorEnTransmision(
        IProveedorCertificadoDigital? proveedorCertificado,
        IFirmadorComprobanteECF? firmador,
        IInvoiceRepository invoiceRepository,
        IEmisionDGIIQueueRepository queueRepository,
        ILogger logger)
    {
        _proveedorCertificado = proveedorCertificado;
        _firmador = firmador;
        _invoiceRepository = invoiceRepository;
        _queueRepository = queueRepository;
        _logger = logger;
    }

    /// <summary>
    /// Resultado de la etapa de firma: exitosa, o el fallo clasificado con su respuesta lista.
    /// </summary>
    public enum ResultadoFirma
    {
        Firmado,
        FallaRecuperable,
        FallaPermanente
    }

    /// <summary>
    /// Garantiza que el comprobante esté firmado para transmitir en modo real. Devuelve el
    /// resultado de la etapa; en fallo, <paramref name="respuestaFallo"/> trae la respuesta lista
    /// y el comprobante/cola ya quedaron persistidos en su estado honesto.
    /// </summary>
    public async Task<(ResultadoFirma Resultado, ElectronicInvoiceResponse? RespuestaFallo)> FirmarSiCorrespondeAsync(
        ElectronicInvoice invoice,
        EmisionDGIIQueue? elementoCola,
        bool simulador,
        System.Threading.CancellationToken cancellationToken)
    {
        if (simulador || ContieneFirmaReal(invoice.XMLContent))
            return (ResultadoFirma.Firmado, null);

        try
        {
            var certificado = _proveedorCertificado?.ObtenerCertificado();
            if (certificado == null || _firmador == null)
                throw new ReglaDeNegocioException(
                    _proveedorCertificado?.DescribirEstado()
                        ?? "No hay proveedor de certificado digital configurado para firmar comprobantes.",
                    "CERTIFICADO_AUSENTE");

            invoice.XMLContent = _firmador.Firmar(invoice.XMLContent, certificado);
            // Transición mejor-esfuerzo: en un reintento desde ErrorTemporal el documento se
            // vuelve a firmar (se persiste el XML firmado, lo esencial), pero la máquina no
            // permite re-entrar a la línea principal; el avance lo dará la transmisión.
            if (invoice.EstadoEmision.PuedeTransicionarA(EstadoEmisionECF.Firmada))
                invoice.AvanzarEstado(EstadoEmisionECF.Firmada);
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

            _logger.LogInformation(
                "e-CF {eNCF} firmado con XML-DSig (certificado {Sujeto}).",
                invoice.eNCF, certificado.SubjectName.Name);

            return (ResultadoFirma.Firmado, null);
        }
        catch (ReglaDeNegocioException ex) when (ex.Codigo is "FIRMA_INVALIDA" or "FIRMA_AUSENTE")
        {
            // El documento se firmó pero su firma no verifica: defecto del documento.
            return (ResultadoFirma.FallaPermanente,
                await FallarFirmaAsync(invoice, elementoCola, ex.Message, esRecuperable: false, cancellationToken));
        }
        catch (ReglaDeNegocioException ex)
        {
            // CERTIFICADO_AUSENTE / SIN_CLAVE_PRIVADA / VENCIDO: se corrige instalando el certificado.
            return (ResultadoFirma.FallaRecuperable,
                await FallarFirmaAsync(invoice, elementoCola, ex.Message, esRecuperable: true, cancellationToken));
        }
        catch (CryptographicException ex)
        {
            return (ResultadoFirma.FallaRecuperable,
                await FallarFirmaAsync(
                    invoice,
                    elementoCola,
                    $"El certificado digital no se pudo usar para firmar (¿contraseña incorrecta o archivo corrupto?): {ex.Message}",
                    esRecuperable: true,
                    cancellationToken));
        }
    }

    private async Task<ElectronicInvoiceResponse> FallarFirmaAsync(
        ElectronicInvoice invoice,
        EmisionDGIIQueue? elementoCola,
        string mensaje,
        bool esRecuperable,
        System.Threading.CancellationToken cancellationToken)
    {
        invoice.FechaUltimoIntentoEnvio = DateTime.UtcNow;
        invoice.Estado = EstadoFacturaElectronica.NoEnviado;

        if (esRecuperable)
        {
            if (invoice.EstadoEmision.PuedeTransicionarA(EstadoEmisionECF.Encolada))
                invoice.EstadoEmision = EstadoEmisionECF.Encolada;
        }
        else
        {
            invoice.AvanzarEstado(EstadoEmisionECF.ErrorFirma);
        }

        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        if (elementoCola != null)
        {
            var proximoIntento = esRecuperable
                ? PoliticaReintentoCola.ProximoIntento(elementoCola.Intentos + 1, DateTime.UtcNow, elementoCola.Id)
                : (DateTime?)null;

            await _queueRepository.MarcarFalloAsync(
                elementoCola.Id, mensaje, null, esRecuperable, proximoIntento, cancellationToken);
        }

        if (esRecuperable)
            _logger.LogWarning("e-CF {eNCF} NO transmitido (falta de certificado; quedará en cola): {Mensaje}", invoice.eNCF, mensaje);
        else
            _logger.LogError("e-CF {eNCF} NO transmitido por fallo de firma: {Mensaje}", invoice.eNCF, mensaje);

        return new ElectronicInvoiceResponse
        {
            Exitoso = false,
            eNCF = invoice.eNCF,
            Estado = invoice.Estado,
            EstadoEmision = invoice.EstadoEmision,
            EsRecuperable = esRecuperable,
            CodigoSeguridadeCF = invoice.XMLHash,
            Mensaje = mensaje
        };
    }

    /// <summary>
    /// Detecta si el XML ya contiene una firma XML-DSig REAL (elemento con contenido). El hueco
    /// estructural vacío emitido por el serializer no cuenta: solo evita firmar dos veces un
    /// documento que ya pasó por el firmador en un intento anterior.
    /// </summary>
    private static bool ContieneFirmaReal(string xml)
    {
        try
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(xml);

            var firmas = doc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
            return firmas.Count > 0 && firmas[0] is XmlElement firma && firma.HasChildNodes;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
