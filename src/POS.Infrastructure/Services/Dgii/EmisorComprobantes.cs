using System;
using System.IO;
using System.Linq;
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
using POS.Infrastructure.XmlSerialization;

namespace POS.Infrastructure.Services.Dgii;

/// <summary>
/// Dueño de la emisión de comprobantes: preparar el documento local (dentro de la transacción
/// de la venta) y transmitirlo a la DGII con su resultado real.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PrepararYRegistrarAsync"/> construye y persiste el comprobante SIN hablar con la
/// DGII. Es el paso que pertenece a la transacción de la venta: si algo falla, la venta se
/// revierte por completo.
/// </para>
/// <para>
/// <see cref="EnviarAsync"/> transmite un comprobante ya registrado y actualiza su estado con el
/// resultado real. Un fallo aquí NO invalida la venta ya confirmada.
/// </para>
/// La liberación de secuencias tras rechazo (5.5) y la firma en transmisión (5.1) son de sus
/// propios dueños; este emisor las orquesta en los puntos correctos del flujo.
/// </remarks>
internal sealed class EmisorComprobantes
{
    private readonly IXmlSerializer _xmlSerializer;
    private readonly IXmlValidator _xmlValidator;
    private readonly ISecurityCodeGenerator _codigoSeguridad;
    private readonly IDgiiApiClient _dgiiApiClient;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IEmisionDGIIQueueRepository _queueRepository;
    private readonly LiberadorSecuencias _liberador;
    private readonly FirmadorEnTransmision _firmadorEnTransmision;
    private readonly ILogger _logger;
    private readonly string _xsdBasePath;
    private readonly bool _simulador;

    public EmisorComprobantes(
        IXmlSerializer xmlSerializer,
        IXmlValidator xmlValidator,
        ISecurityCodeGenerator codigoSeguridad,
        IDgiiApiClient dgiiApiClient,
        IInvoiceRepository invoiceRepository,
        IEmisionDGIIQueueRepository queueRepository,
        LiberadorSecuencias liberador,
        FirmadorEnTransmision firmadorEnTransmision,
        ILogger logger,
        string xsdBasePath,
        bool simulador)
    {
        _xmlSerializer = xmlSerializer ?? throw new ArgumentNullException(nameof(xmlSerializer));
        _xmlValidator = xmlValidator ?? throw new ArgumentNullException(nameof(xmlValidator));
        _codigoSeguridad = codigoSeguridad ?? throw new ArgumentNullException(nameof(codigoSeguridad));
        _dgiiApiClient = dgiiApiClient ?? throw new ArgumentNullException(nameof(dgiiApiClient));
        _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
        _queueRepository = queueRepository ?? throw new ArgumentNullException(nameof(queueRepository));
        _liberador = liberador ?? throw new ArgumentNullException(nameof(liberador));
        _firmadorEnTransmision = firmadorEnTransmision ?? throw new ArgumentNullException(nameof(firmadorEnTransmision));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _xsdBasePath = xsdBasePath ?? throw new ArgumentNullException(nameof(xsdBasePath));
        _simulador = simulador;
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
            DevolucionId = command.DevolucionId,
            // Referencia fiscal al comprobante modificado (notas de crédito/débito): la traza
            // local replica la InformacionReferencia del XML.
            eNCFModificado = req.NCFModificado,
            CodigoModificacion = req.CodigoModificacion,
            FechaNCFModificado = req.FechaNCFModificado,
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
        // XsdValidado (la firma llega en transmisión; el hueco ds:Signature ya está en el documento).
        invoiceEntity.AvanzarEstado(EstadoEmisionECF.XsdValidado);

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
    /// Clasificación de la respuesta: éxito ⇒ EnProceso (esperando resultado fiscal; el RFCE
    /// consolida de inmediato); sin respuesta o 5xx/429 ⇒ envío incierto con reintento progresivo;
    /// credenciales (401/403) ⇒ error permanente sin marcar el documento como rechazado; rechazo de
    /// validación (400/404/409/422) ⇒ Rechazado y secuencia candidata a liberarse (5.5).
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

        // Ventana de contingencia (6.1): un comprobante cuya contingencia de 30 días venció NO se
        // transmite — el reglamento exige anular el rango (ANECF) y reemitir con nueva secuencia.
        if (!ServicioContingencia.PuedeTransmitirse(invoice, DateTime.UtcNow))
            throw new ReglaDeNegocioException(
                $"El comprobante {invoice.eNCF} venció su ventana de contingencia " +
                $"({invoice.ContingenciaHastaUtc:dd-MM-yyyy}): anule el rango (ANECF) y reemita con nueva secuencia.",
                "CONTINGENCIA_VENCIDA");

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

        // Firma XML-DSig (5.1): en modo REAL el documento DEBE estar firmado antes de transmitirse.
        // Un fallo de firma nunca sale a la red; el colaborador persiste el estado honesto.
        var (resultadoFirma, respuestaFallo) = await _firmadorEnTransmision.FirmarSiCorrespondeAsync(
            invoice, elementoCola, _simulador, cancellationToken);
        if (resultadoFirma != FirmadorEnTransmision.ResultadoFirma.Firmado)
            return respuestaFallo!;

        // La transmisión se declara iniciada ANTES de salir a la red: si el proceso muere durante la
        // llamada, el estado refleja que el documento pudo haber llegado a la DGII.
        if (invoice.EstadoEmision.PuedeTransicionarA(EstadoEmisionECF.Enviada))
        {
            invoice.EstadoEmision = EstadoEmisionECF.Enviada;
            await _invoiceRepository.UpdateAsync(invoice, cancellationToken);
        }

        // Transmisión real (5.3): multipart con el nombre de archivo oficial RNC+eNCF.xml
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
            if (PoliticaRecepcion.EsRFCE(invoice))
            {
                // La recepción RFCE entrega el RESULTADO FISCAL definitivo en la misma respuesta
                // (Aceptado / Aceptado Condicional / Rechazado): se consolida de inmediato.
                ConsolidadorResultadoFiscal.ConsolidarRecepcion(invoice, respuesta);
                // Sub-fase 5.5: rechazo corregible ⇒ la secuencia vuelve al pool de reutilización.
                await _liberador.LiberarSiCorrespondeAsync(
                    invoice, respuesta.SecuenciaUtilizada, cancellationToken);
            }
            else
            {
                // La recepción e-CF entrega TrackId con estado "En proceso": el resultado fiscal
                // llega por la consulta de resultado (polling con ConsultarEstadoPorTrackIdAsync).
                invoice.TrackId = respuesta.TrackId;
                invoice.Estado = EstadoFacturaElectronica.EnProceso;
                invoice.AvanzarEstado(EstadoEmisionECF.ConfirmadaEnvio);
            }
        }
        else if (incierto)
        {
            // Sin respuesta alguna: pudo haber sido recibido. Se resuelve consultando, no reenviando a ciegas.
            invoice.Estado = EstadoFacturaElectronica.PendienteReenvio;
            invoice.AvanzarEstado(EstadoEmisionECF.EnvioIncierto);
        }
        else if (respuesta.CodigoHttp is 401 or 403)
        {
            // Problema de credenciales del emisor, no del documento.
            invoice.Estado = EstadoFacturaElectronica.NoEnviado;
            invoice.AvanzarEstado(EstadoEmisionECF.ErrorPermanente);
        }
        else if (recuperable)
        {
            // La DGII no procesó la solicitud: se reintenta con espera progresiva.
            invoice.Estado = EstadoFacturaElectronica.PendienteReenvio;
            invoice.AvanzarEstado(EstadoEmisionECF.ErrorTemporal);
        }
        else
        {
            invoice.Estado = EstadoFacturaElectronica.Rechazado;
            invoice.MotivoRechazo = respuesta.Mensaje;
            invoice.AvanzarEstado(EstadoEmisionECF.ErrorPermanente);
            // Sub-fase 5.5: rechazo en recepción ⇒ la DGII no consumió el número; sin marca
            // explícita (respuesta sin cuerpo fiscal) se interpreta como secuencia NO utilizada.
            await _liberador.LiberarSiCorrespondeAsync(
                invoice, respuesta.SecuenciaUtilizada, cancellationToken);
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
    /// Valida un XML de comprobante contra el esquema oficial de su TIPO, con la misma resolución
    /// de ruta que usa el resto del pipeline (output de la aplicación primero).
    /// </summary>
    public Task<ValidationResult> ValidarXmlAsync(string xmlContent, TipoeCFType tipo)
    {
        var rutaXsd = MapaXsdComprobante.ResolverRuta(tipo)
            ?? Path.Combine(_xsdBasePath, MapaXsdComprobante.ArchivoDe(tipo));

        return Task.FromResult(_xmlValidator.Validate(xmlContent, rutaXsd));
    }
}
