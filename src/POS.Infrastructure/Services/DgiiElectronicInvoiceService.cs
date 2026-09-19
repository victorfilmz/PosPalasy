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
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Domain.Types;
using POS.Infrastructure.DGII;
using System.Xml;
using POS.Infrastructure.XmlSerialization;

namespace POS.Infrastructure.Services;

/// <summary>
/// Servicio de comprobantes electrónicos.
/// </summary>
/// <remarks>
/// <para>
/// Separación de responsabilidades que exige la operación real del POS:
/// </para>
/// <list type="bullet">
/// <item><see cref="PrepararYRegistrarAsync"/> construye y persiste el comprobante SIN hablar con la
/// DGII. Es el paso que pertenece a la transacción de la venta.</item>
/// <item><see cref="EnviarAsync"/> transmite un comprobante ya registrado y actualiza su estado con
/// la respuesta real. Nunca se ejecuta dentro de la transacción de la venta.</item>
/// </list>
/// <para>
/// Estado de la construcción del documento (FASE 5, sub-fase 5.0): el XML se construye con el hueco
/// estructural de la firma (ds:Signature) exigido por el XSD, se valida contra el esquema oficial
/// del tipo del comprobante (transición a XsdValidado; ErrorXsd si no valida) y se le asigna el
/// código de seguridad de 6 caracteres por comprobante.
/// </para>
/// <para>
/// Firma (FASE 5, sub-fase 5.1): en modo REAL el documento se firma con XML-DSig justo antes de
/// transmitirse (transición a Firmada; ErrorFirma permanente si falta el certificado o la firma no
/// verifica). En modo simulador se transmite sin firma para no exigir certificado en desarrollo.
/// </para>
/// <para>
/// Transmisión (FASE 5, sub-fase 5.3): multipart con el nombre oficial RNC+eNCF.xml y endpoint
/// según la regla de los RD$250,000 (e-CF completo en ecf.dgii.gov.do; resumen RFCE en
/// fc.dgii.gov.do para factura de consumo menor). El resultado fiscal de la DGII (Aceptado,
/// Aceptado Condicional, Rechazado, mensajes y secuenciaUtilizada) se persiste en el comprobante.
/// </para>
/// </remarks>
public class DgiiElectronicInvoiceService : IElectronicInvoiceService
{
    private readonly IXmlSerializer _xmlSerializer;
    private readonly ISecurityCodeGenerator _codigoSeguridad;
    private readonly IProveedorCertificadoDigital? _proveedorCertificado;
    private readonly IFirmadorComprobanteECF? _firmador;
    private readonly bool _simulador;
    private readonly IXmlValidator _xmlValidator;
    private readonly IHashGenerator _hashGenerator;
    private readonly IDgiiApiClient _dgiiApiClient;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IAnulacionRepository _anulacionRepository;
    private readonly IEmisionDGIIQueueRepository _queueRepository;
    private readonly ILogger<DgiiElectronicInvoiceService> _logger;
    private readonly string _xsdBasePath;

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
        DgiiConfig? dgiiConfig = null)
    {
        _xmlSerializer = xmlSerializer ?? throw new ArgumentNullException(nameof(xmlSerializer));
        _xmlValidator = xmlValidator ?? throw new ArgumentNullException(nameof(xmlValidator));
        _hashGenerator = hashGenerator ?? throw new ArgumentNullException(nameof(hashGenerator));
        _dgiiApiClient = dgiiApiClient ?? throw new ArgumentNullException(nameof(dgiiApiClient));
        _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
        _anulacionRepository = anulacionRepository ?? throw new ArgumentNullException(nameof(anulacionRepository));
        _queueRepository = queueRepository ?? throw new ArgumentNullException(nameof(queueRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _codigoSeguridad = codigoSeguridad ?? new GeneradorCodigoSeguridad();
        _proveedorCertificado = proveedorCertificado;
        _firmador = firmador;
        _simulador = dgiiConfig?.ModoSimulador ?? false;

        _xsdBasePath = xsdBasePath ?? Path.Combine(AppContext.BaseDirectory, "documentacion xsd");
    }

    public Task<string> GenerarXmlAsync(ElectronicInvoiceRequest request)
    {
        var xml = _xmlSerializer.Serialize(request);
        return Task.FromResult(xml);
    }

    public Task<ValidationResult> ValidarXmlAsync(string xmlContent, TipoeCFType tipo = TipoeCFType.FacturaConsumo)
    {
        var rutaXsd = MapaXsdComprobante.ResolverRuta(tipo)
            ?? Path.Combine(_xsdBasePath, MapaXsdComprobante.ArchivoDe(tipo));

        var result = _xmlValidator.Validate(xmlContent, rutaXsd);
        return Task.FromResult(result);
    }

    public string GenerarHash(string xmlContent)
    {
        return _hashGenerator.Generate(xmlContent);
    }

    /// <summary>
    /// Construye el XML, calcula el código de seguridad y persiste el comprobante como
    /// <see cref="EstadoFacturaElectronica.NoEnviado"/>. No hay ninguna llamada de red aquí: si algo
    /// falla, la transacción de la venta se revierte por completo.
    /// </summary>
    public async Task<ComprobantePreparado> PrepararYRegistrarAsync(
        PrepararComprobanteCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));

        var req = command.Request
            ?? throw new ReglaDeNegocioException(
                "La solicitud del comprobante es nula.",
                "SOLICITUD_INVALIDA");

        var valResult = EmitirFacturaValidator.Validar(req);
        if (!valResult.EsValido)
            throw new ReglaDeNegocioException(
                string.Join(" | ", valResult.Errores),
                "COMPROBANTE_INVALIDO");

        // 1. Construcción del documento local: incluye el hueco estructural de la firma (ds:Signature)
        //    exigido por el XSD; el documento nace estructuralmente completo.
        var xml = _xmlSerializer.Serialize(req);

        // 2. Validación contra el esquema oficial del TIPO del comprobante (no un XSD único).
        var validacion = await ValidarXmlAsync(xml, req.TipoeCF);
        if (!validacion.EsValido)
            throw new ReglaDeNegocioException(
                "El comprobante no valida contra el esquema XSD oficial: " +
                    string.Join(" | ", validacion.Errores.Take(5)),
                "ERROR_XSD");

        // 3. Código de seguridad: 6 caracteres asignados por comprobante (no derivados del XML).
        var hash = _codigoSeguridad.Generar();
        req.CodigoSeguridadeCF = hash;

        // 4. Persistencia del comprobante sin transmitir.
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
            TrackId = null,
            Estado = EstadoFacturaElectronica.NoEnviado,
            EstadoEmision = EstadoEmisionECF.Creada,
            FechaEnvio = null
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
                // El total de la línea incluye sus impuestos: es la única cifra coherente con el total
                // del comprobante (la correspondencia exacta con "MontoItem" del XSD se ajusta en la
                // fase de XML/XSD).
                MontoItem = itm.Total
            });
        }

        // El documento nació, se validó contra el XSD oficial y se persistió: el estado honesto es
        // XsdValidado (la firma llega en 5.1; el hueco ds:Signature ya está en el documento).
        AvanzarEstado(invoiceEntity, EstadoEmisionECF.XsdValidado);

        await _invoiceRepository.AddAsync(invoiceEntity, cancellationToken);

        _logger.LogInformation(
            "Comprobante {eNCF} registrado localmente (Id {Id}) con estado {EstadoEmision}; pendiente de transmisión a la DGII.",
            invoiceEntity.eNCF,
            invoiceEntity.Id,
            invoiceEntity.EstadoEmision);

        return new ComprobantePreparado(
            invoiceEntity.Id,
            invoiceEntity.eNCF,
            invoiceEntity.EstadoEmision,
            invoiceEntity.Estado,
            xml);
    }

    /// <summary>
    /// Transmite un comprobante ya registrado y guarda el resultado real.
    /// </summary>
    /// <remarks>
    /// Clasificación de la respuesta: éxito ⇒ EnProceso (esperando resultado fiscal); sin respuesta
    /// o 5xx/429 ⇒ envío incierto con reintento progresivo; credenciales (401/403) ⇒ error permanente
    /// sin marcar el documento como rechazado; rechazo de validación (400/404/409/422) ⇒ Rechazado.
    /// </remarks>
    public async Task<ElectronicInvoiceResponse> EnviarAsync(
        int electronicInvoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await _invoiceRepository.GetByIdAsync(electronicInvoiceId, cancellationToken)
            ?? throw new ReglaDeNegocioException(
                $"El comprobante #{electronicInvoiceId} no existe.",
                "COMPROBANTE_NO_ENCONTRADO");

        // Guardas de idempotencia fiscal: no se reenvía lo ya aceptado ni lo anulado.
        if (invoice.Estado == EstadoFacturaElectronica.Aceptado)
        {
            return new ElectronicInvoiceResponse
            {
                Exitoso = true,
                eNCF = invoice.eNCF,
                TrackId = invoice.TrackId,
                Estado = invoice.Estado,
                EstadoEmision = invoice.EstadoEmision,
                CodigoSeguridadeCF = invoice.XMLHash,
                Mensaje = "El comprobante ya fue aceptado por la DGII; no se reenvió."
            };
        }

        if (invoice.Estado == EstadoFacturaElectronica.Anulado)
            throw new ReglaDeNegocioException(
                "Un comprobante anulado no puede transmitirse.",
                "COMPROBANTE_ANULADO");

        // Exclusión mutua de la transmisión: quien transmite debe poseer el lease del elemento de cola.
        // Así el envío inmediato de la venta y el trabajador en segundo plano nunca envían el mismo
        // comprobante a la vez, y un envío abandonado se retoma cuando el lease vence.
        var elementoCola = await _queueRepository.GetPendientePorFacturaAsync(invoice.Id, cancellationToken);
        if (elementoCola != null)
        {
            var reclamado = await _queueRepository.ReclamarAsync(
                elementoCola.Id,
                $"envio-{Guid.NewGuid():N}",
                DateTime.UtcNow,
                cancellationToken);

            if (!reclamado)
            {
                return new ElectronicInvoiceResponse
                {
                    Exitoso = false,
                    eNCF = invoice.eNCF,
                    Estado = invoice.Estado,
                    EstadoEmision = invoice.EstadoEmision,
                    EsRecuperable = true,
                    Mensaje = "El comprobante ya está siendo transmitido por otro proceso; su estado se confirmará en breve."
                };
            }
        }

        // Firma XML-DSig (sub-fase 5.1): en modo REAL el documento DEBE estar firmado antes de
        // transmitirse. Sin certificado válido o con firma no verificable => ErrorFirma permanente:
        // nada sale a la red. En modo simulador se transmite sin firma (el simulador no valida
        // XML-DSig); los comprobantes ya firmados no se firman dos veces.
        if (!_simulador && !ContieneFirmaReal(invoice.XMLContent))
        {
            // Sin certificado utilizable no hay envío; pero la AUSENCIA de certificado es un problema
            // de configuración, no del documento: vuelve a la cola con reintento progresivo (al
            // instalarse el certificado el comprobante sale solo). Un documento cuya firma no
            // verifica es ErrorFirma PERMANENTE (defecto del documento, no del entorno).
            async Task<ElectronicInvoiceResponse> FallarFirmaAsync(string mensaje, bool esRecuperable)
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
                    AvanzarEstado(invoice, EstadoEmisionECF.ErrorFirma);
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
                    AvanzarEstado(invoice, EstadoEmisionECF.Firmada);
                await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

                _logger.LogInformation(
                    "e-CF {eNCF} firmado con XML-DSig (certificado {Sujeto}).",
                    invoice.eNCF, certificado.SubjectName.Name);
            }
            catch (ReglaDeNegocioException ex) when (ex.Codigo is "FIRMA_INVALIDA" or "FIRMA_AUSENTE")
            {
                // El documento se firmó pero su firma no verifica: defecto del documento.
                return await FallarFirmaAsync(ex.Message, esRecuperable: false);
            }
            catch (ReglaDeNegocioException ex)
            {
                // CERTIFICADO_AUSENTE / SIN_CLAVE_PRIVADA / VENCIDO: se corrige instalando el certificado.
                return await FallarFirmaAsync(ex.Message, esRecuperable: true);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                return await FallarFirmaAsync(
                    $"El certificado digital no se pudo usar para firmar (¿contraseña incorrecta o archivo corrupto?): {ex.Message}",
                    esRecuperable: true);
            }
        }

        // La transmisión se declara iniciada ANTES de salir a la red: si el proceso muere durante la
        // llamada, el estado refleja que el documento pudo haber llegado a la DGII.
        if (invoice.EstadoEmision.PuedeTransicionarA(EstadoEmisionECF.Enviada))
        {
            invoice.EstadoEmision = EstadoEmisionECF.Enviada;
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }

        // Transmisión real (sub-fase 5.3): multipart con el nombre de archivo oficial RNC+eNCF.xml
        // y endpoint según la regla de los RD$250,000.
        var respuesta = await _dgiiApiClient.EnviarFacturaAsync(
            invoice.XMLContent,
            DgiiApiClient.CrearNombreArchivoXml(invoice.RNCEmisor, invoice.eNCF),
            invoice.TipoeCF == TipoeCFType.FacturaConsumo,
            invoice.MontoTotal,
            cancellationToken);

        var recuperable = ClasificadorErroresDGII.EsRecuperable(respuesta.CodigoHttp, respuesta.EsExitoso);
        var incierto = !respuesta.EsExitoso && ClasificadorErroresDGII.EsAmbiguo(respuesta.CodigoHttp);

        invoice.UltimoCodigoHttp = respuesta.CodigoHttp;
        invoice.FechaUltimoIntentoEnvio = DateTime.UtcNow;
        invoice.EstadoDgii = respuesta.Estado;
        invoice.MensajesDgii = respuesta.Mensaje;
        invoice.SecuenciaUtilizada = respuesta.SecuenciaUtilizada;

        if (respuesta.EsExitoso)
        {
            invoice.FechaEnvio = DateTime.UtcNow;
            if (EsRFCE(invoice))
            {
                // La recepción RFCE entrega el RESULTADO FISCAL definitivo en la misma respuesta
                // (Aceptado / Aceptado Condicional / Rechazado): se consolida de inmediato.
                ConsolidarEstadoFiscal(invoice, respuesta);
            }
            else
            {
                // La recepción e-CF entrega TrackId con estado "En proceso": el resultado fiscal
                // llega por la consulta de resultado (polling con ConsultarEstadoPorTrackIdAsync).
                invoice.TrackId = respuesta.TrackId;
                invoice.Estado = EstadoFacturaElectronica.EnProceso;
                AvanzarEstado(invoice, EstadoEmisionECF.ConfirmadaEnvio);
            }
        }
        else if (incierto)
        {
            // Sin respuesta alguna: pudo haber sido recibido. Se resuelve consultando, no reenviando a ciegas.
            invoice.Estado = EstadoFacturaElectronica.PendienteReenvio;
            AvanzarEstado(invoice, EstadoEmisionECF.EnvioIncierto);
        }
        else if (respuesta.CodigoHttp is 401 or 403)
        {
            // Problema de credenciales del emisor, no del documento.
            invoice.Estado = EstadoFacturaElectronica.NoEnviado;
            AvanzarEstado(invoice, EstadoEmisionECF.ErrorPermanente);
        }
        else if (recuperable)
        {
            // La DGII no procesó la solicitud: se reintenta con espera progresiva.
            invoice.Estado = EstadoFacturaElectronica.PendienteReenvio;
            AvanzarEstado(invoice, EstadoEmisionECF.ErrorTemporal);
        }
        else
        {
            invoice.Estado = EstadoFacturaElectronica.Rechazado;
            invoice.MotivoRechazo = respuesta.Mensaje;
            AvanzarEstado(invoice, EstadoEmisionECF.ErrorPermanente);
        }

        await _invoiceRepository.UpdateAsync(invoice, cancellationToken);

        // La fila de la cola refleja el mismo resultado: si se confirmó, deja de ser candidata.
        if (elementoCola != null)
        {
            if (respuesta.EsExitoso)
            {
                await _queueRepository.MarcarEnviadoAsync(elementoCola.Id, respuesta.TrackId, respuesta.CodigoHttp, cancellationToken);
            }
            else
            {
                var proximoIntento = recuperable
                    ? PoliticaReintentoCola.ProximoIntento(
                        elementoCola.Intentos + 1,
                        DateTime.UtcNow,
                        elementoCola.Id)
                    : (DateTime?)null;

                await _queueRepository.MarcarFalloAsync(
                    elementoCola.Id,
                    respuesta.Mensaje ?? ClasificadorErroresDGII.Describir(respuesta.CodigoHttp),
                    respuesta.CodigoHttp,
                    recuperable,
                    proximoIntento,
                    cancellationToken);
            }
        }

        if (respuesta.EsExitoso)
        {
            _logger.LogInformation(
                "e-CF {eNCF} recibido por la DGII. TrackId: {TrackId}",
                invoice.eNCF,
                respuesta.TrackId);
        }
        else
        {
            _logger.LogWarning(
                "e-CF {eNCF} no confirmado por la DGII (HTTP {Codigo}, recuperable: {Recuperable}): {Mensaje}",
                invoice.eNCF,
                respuesta.CodigoHttp,
                recuperable,
                respuesta.Mensaje);
        }

        return new ElectronicInvoiceResponse
        {
            Exitoso = respuesta.EsExitoso,
            eNCF = invoice.eNCF,
            TrackId = respuesta.TrackId,
            Estado = invoice.Estado,
            EstadoEmision = invoice.EstadoEmision,
            CodigoHttp = respuesta.CodigoHttp,
            EsRecuperable = recuperable,
            CodigoSeguridadeCF = invoice.XMLHash,
            Mensaje = respuesta.Mensaje ?? ClasificadorErroresDGII.Describir(respuesta.CodigoHttp)
        };
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

        // 2. Enviar a DGII: multipart con el nombre oficial RNC+eNCF.xml al endpoint de anulación de rangos
        var dgiiResp = await _dgiiApiClient.EnviarAnulacionAsync(
            xml,
            DgiiApiClient.CrearNombreArchivoXml(request.RNCEmisor, request.eNCFDesde),
            cancellationToken);

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

        // RFCE: la DGII no emitió TrackId; la consulta se hace por RNC/eNCF/código de seguridad.
        if (EsRFCE(invoice) && !string.IsNullOrWhiteSpace(invoice.TrackId) && invoice.Estado == EstadoFacturaElectronica.EnProceso)
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

        if (invoice != null && dgiiResp.EsExitoso && !string.IsNullOrWhiteSpace(dgiiResp.Estado))
        {
            var nuevoEstado = dgiiResp.Estado.ToUpperInvariant() switch
            {
                "ACEPTADO" or "1" => EstadoFacturaElectronica.Aceptado,
                "ACEPTADO CONDICIONAL" => EstadoFacturaElectronica.Aceptado,
                "RECHAZADO" or "2" => EstadoFacturaElectronica.Rechazado,
                "ANULADO" or "3" => EstadoFacturaElectronica.Anulado,
                _ => EstadoFacturaElectronica.EnProceso
            };

            // Traza del resultado oficial tal como lo reportó la DGII.
            invoice.EstadoDgii = dgiiResp.Estado;
            invoice.SecuenciaUtilizada = dgiiResp.SecuenciaUtilizada;
            if (!string.IsNullOrWhiteSpace(dgiiResp.Mensaje))
                invoice.MensajesDgii = dgiiResp.Mensaje;

            invoice.Estado = nuevoEstado;
            if (nuevoEstado == EstadoFacturaElectronica.Aceptado)
            {
                invoice.FechaAprobacion = DateTime.UtcNow;
                // La confirmación resuelve un envío previamente incierto.
                if (invoice.EstadoEmision.PuedeTransicionarA(EstadoEmisionECF.ConfirmadaEnvio))
                    invoice.EstadoEmision = EstadoEmisionECF.ConfirmadaEnvio;
            }

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

        if (dgiiResp.EsExitoso && !string.IsNullOrWhiteSpace(dgiiResp.Estado))
        {
            var nuevoEstado = dgiiResp.Estado.ToUpperInvariant() switch
            {
                "ACEPTADO" or "1" => EstadoFacturaElectronica.Aceptado,
                "ACEPTADO CONDICIONAL" => EstadoFacturaElectronica.Aceptado,
                "RECHAZADO" or "2" => EstadoFacturaElectronica.Rechazado,
                _ => EstadoFacturaElectronica.EnProceso
            };

            invoice.EstadoDgii = dgiiResp.Estado;
            invoice.SecuenciaUtilizada = dgiiResp.SecuenciaUtilizada;
            if (!string.IsNullOrWhiteSpace(dgiiResp.Mensaje))
                invoice.MensajesDgii = dgiiResp.Mensaje;

            invoice.Estado = nuevoEstado;
            if (nuevoEstado == EstadoFacturaElectronica.Aceptado)
            {
                invoice.FechaAprobacion = DateTime.UtcNow;
                if (invoice.EstadoEmision.PuedeTransicionarA(EstadoEmisionECF.ConfirmadaEnvio))
                    invoice.EstadoEmision = EstadoEmisionECF.ConfirmadaEnvio;
            }

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
    /// Reintento de transmisión para una factura localizada por su eNCF. Delega en
    /// <see cref="EnviarAsync"/> para que exista una única ruta de transmisión y de actualización
    /// de estado.
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

    /// <summary>
    /// La recepción RFCE (facturas de consumo &lt; RD$250,000) responde con el resultado fiscal
    /// definitivo en la misma llamada, sin TrackId.
    /// </summary>
    private static bool EsRFCE(ElectronicInvoice invoice) =>
        invoice.TipoeCF == TipoeCFType.FacturaConsumo && invoice.MontoTotal < DgiiApiClient.UmbralECF;

    /// <summary>
    /// Consolida el resultado fiscal de la DGII en el estado local: Aceptado (1), Aceptado
    /// Condicional (2), Rechazado (3). Solo afecta comprobantes con recepción exitosa; los fallos
    /// de transporte se tratan por la clasificación HTTP habitual.
    /// </summary>
    private void ConsolidarEstadoFiscal(ElectronicInvoice invoice, DgiiApiResponse respuesta)
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

        AvanzarEstado(invoice, EstadoEmisionECF.ConfirmadaEnvio);
    }

    /// <summary>Traduce el código de estado de la consulta RFCE (0/1/2) a texto oficial.</summary>
    private static string EstadoFiscalDeConsultaRFCE(int codigo) => codigo switch
    {
        1 => "Aceptado",
        2 => "Rechazado",
        _ => "No encontrado"
    };

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

    private static void AvanzarEstado(ElectronicInvoice invoice, EstadoEmisionECF nuevoEstado)
    {
        // Repetir el mismo resultado (un segundo intento con la misma conclusión) no es una transición
        // inválida: lo que la máquina prohíbe es retroceder o reabrir un estado terminal.
        if (invoice.EstadoEmision == nuevoEstado)
            return;

        invoice.EstadoEmision = invoice.EstadoEmision.ValidarTransicion(nuevoEstado);
    }
}
