using System;
using System.Threading;
using System.Threading.Tasks;
using POS.Application.CasosDeUso.Ventas;
using POS.Application.DTOs;
using POS.Application.Interfaces;
using POS.Application.Services;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Domain.Types;

namespace POS.Application.CasosDeUso.Facturacion;

/// <summary>
/// Emite la <b>Nota de Crédito Electrónica (e-CF 34)</b> que documenta una devolución registrada
/// (Fase 4), con las mismas garantías transaccionales del caso de uso de venta:
/// <list type="number">
/// <item><b>Idempotencia</b>: una devolución tiene UNA nota de crédito. La verificación corre dentro
/// de la transacción y un índice único sobre <c>ElectronicInvoices.DevolucionId</c> respalda
/// físicamente la regla (dos emisiones concurrentes no pueden ambas materializarse).</item>
/// <item><b>Referencia fiscal obligatoria</b>: la nota lleva <c>InformacionReferencia</c> al
/// comprobante original (eNCF, fecha, código de modificación) e <c>IndicadorNotaCredito</c> según
/// los 30 días de la normativa. Sin esa referencia la DGII no puede vincular la corrección.</item>
/// <item><b>Unidad atómica</b>: reserva del e-NCF de la serie E34, construcción y validación XSD del
/// documento, registro local y outbox de transmisión ocurren en UNA transacción: la nota nace con
/// ruta de transmisión garantizada o no nace.</item>
/// <item><b>Transmisión post-commit</b>: el envío a la DGII ocurre después de confirmar la
/// transacción. Un fallo de red nunca revierte el documento fiscal: la cola lo reintenta.</item>
/// </list>
/// </summary>
/// <remarks>
/// La corrección fiscal central: la devolución NO se documenta con un ANECF (ese comprobante anula
/// RANGOS de secuencias no utilizadas); exige un e-CF 34 que referencie el comprobante original.
/// </remarks>
public sealed class EmitirNotaCreditoDevolucionHandler
{
    private readonly IDevolucionRepository _devolucionRepo;
    private readonly IEnterpriseRepository _empresaRepo;
    private readonly ISecuenciaECFRepository _secuenciaRepo;
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly IEmisionDGIIQueueRepository _queueRepo;
    private readonly IElectronicInvoiceService _invoiceService;
    private readonly ITaxCalculator _taxCalculator;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;

    public EmitirNotaCreditoDevolucionHandler(
        IDevolucionRepository devolucionRepo,
        IEnterpriseRepository empresaRepo,
        ISecuenciaECFRepository secuenciaRepo,
        IInvoiceRepository invoiceRepo,
        IEmisionDGIIQueueRepository queueRepo,
        IElectronicInvoiceService invoiceService,
        ITaxCalculator taxCalculator,
        IUnidadDeTrabajo unidadDeTrabajo)
    {
        _devolucionRepo = devolucionRepo;
        _empresaRepo = empresaRepo;
        _secuenciaRepo = secuenciaRepo;
        _invoiceRepo = invoiceRepo;
        _queueRepo = queueRepo;
        _invoiceService = invoiceService;
        _taxCalculator = taxCalculator;
        _unidadDeTrabajo = unidadDeTrabajo;
    }

    public async Task<NotaCreditoDevolucionResult> EjecutarAsync(
        NotaCreditoDevolucionCommand command,
        CancellationToken ct = default)
    {
        try
        {
            return await EmitirAsync(command, ct);
        }
        catch (ConflictoDeUnicidadException ex)
            when (ex.Codigo == CodigosConflicto.IdempotenciaNotaCredito)
        {
            // El índice único sobre DevolucionId detuvo una emisión duplicada: se devuelve la nota
            // de la devolución (idempotencia de segundo nivel respaldada físicamente).
            var nota = await _invoiceRepo.GetByDevolucionIdAsync(command.DevolucionId, ct);
            if (nota is null)
                throw;

            return new NotaCreditoDevolucionResult
            {
                Exitoso = true,
                Duplicada = true,
                DevolucionId = command.DevolucionId,
                ElectronicInvoiceId = nota.Id,
                eNCF = nota.eNCF,
                eNCFModificado = nota.eNCFModificado ?? string.Empty,
                TotalNota = nota.MontoTotal,
                Xml = nota.XMLContent,
                Mensaje = "La nota de crédito de esta devolución ya había sido emitida."
            };
        }
        catch (ReglaDeNegocioException ex)
        {
            return NotaCreditoDevolucionResult.Error(ex.Codigo, ex.Message);
        }
    }

    private async Task<NotaCreditoDevolucionResult> EmitirAsync(
        NotaCreditoDevolucionCommand command,
        CancellationToken ct)
    {
        if (command.DevolucionId <= 0)
            return NotaCreditoDevolucionResult.Error(
                "DEVOLUCION_NO_INDICADA",
                "Indique la devolución que origina la nota de crédito.");

        var devolucion = await _devolucionRepo.GetByIdAsync(command.DevolucionId, ct);
        if (devolucion is null)
            return NotaCreditoDevolucionResult.Error(
                "DEVOLUCION_NO_ENCONTRADA",
                $"La devolución #{command.DevolucionId} no existe.");

        if (devolucion.Venta is null)
            return NotaCreditoDevolucionResult.Error(
                "DEVOLUCION_SIN_VENTA",
                $"La devolución #{devolucion.Id} no tiene la venta de origen cargada.");

        // Camino rápido de idempotencia (la verificación canónica corre dentro de la transacción).
        var yaEmitida = await _invoiceRepo.GetByDevolucionIdAsync(devolucion.Id, ct);
        if (yaEmitida is not null)
            return RespuestaDeExistente(devolucion, yaEmitida);

        var empresa = await _empresaRepo.GetDefaultAsync(ct)
            ?? throw new ReglaDeNegocioException(
                "No hay empresa emisora configurada.",
                "EMPRESA_NO_CONFIGURADA");

        var comprobanteOriginal = devolucion.Venta.ElectronicInvoice
            ?? throw new ReglaDeNegocioException(
                $"La venta #{devolucion.VentaId} no tiene comprobante fiscal: no hay referencia para la nota de crédito.",
                "VENTA_SIN_COMPROBANTE");

        // Indicador fiscal: 0 si la nota se emite dentro de los 30 días del comprobante original;
        // 1 en caso contrario. Es decisión normativa, no de pantalla.
        var fechaOriginal = FechaDominicana.Parse(comprobanteOriginal.FechaEmision);
        var diasTranscurridos = FechaDominicana.Today.Value.DayNumber - fechaOriginal.Value.DayNumber;
        var indicadorNotaCredito = diasTranscurridos <= 30 ? 0 : 1;

        var registro = await _unidadDeTrabajo.EnTransaccionAsync(async token =>
        {
            // Serialización sobre la fila de la devolución: dos emisiones concurrentes quedan en
            // fila aquí y la segunda ve la nota de la primera. El índice único sobre DevolucionId
            // es el respaldo físico si algo escapa a este ancla.
            await _devolucionRepo.AnclarParaNotaCreditoAsync(devolucion.Id, token);

            var existente = await _invoiceRepo.GetByDevolucionIdAsync(devolucion.Id, token);
            if (existente is not null)
                return new RegistroNota(existente.Id, existente.eNCF, existente.XMLContent, Duplicada: true);

            // Reserva de la secuencia fiscal E34 dentro de la transacción (unicidad garantizada
            // por el repositorio; una carrera revierte todo el lote y puede reintentarse).
            var eNCF = await _secuenciaRepo.AsignarSiguienteENCFAsync(TipoeCFType.NotaCredito, token);

            var solicitud = ConstructorSolicitudECF.DesdeNotaCreditoDevolucion(
                devolucion,
                devolucion.Venta,
                empresa,
                eNCF,
                comprobanteOriginal.eNCF,
                comprobanteOriginal.FechaEmision,
                indicadorNotaCredito,
                _taxCalculator);

            // Registro local con validación XSD y código de seguridad, en la MISMA transacción.
            var comprobante = await _invoiceService.PrepararYRegistrarAsync(
                new PrepararComprobanteCommand(
                    solicitud,
                    VentaId: devolucion.VentaId,
                    DevolucionId: devolucion.Id),
                token);

            // Outbox: la ruta de transmisión nace en la misma transacción que el documento; un
            // cierre abrupto del proceso no deja una nota sin camino hacia la DGII.
            await _queueRepo.AddAsync(new EmisionDGIIQueue
            {
                FacturaId = comprobante.ElectronicInvoiceId,
                eNCF = eNCF,
                XmlFirmado = comprobante.Xml,
                Intentos = 0,
                EnviadoExitosamente = false,
                Estado = EstadoColaDGII.Pendiente,
                FechaRegistro = DateTime.UtcNow
            }, token);

            // La devolución queda saldada fiscalmente en cuanto su nota existe: la aceptación de la
            // DGII es del pipeline de transmisión (cola + consultas) y no reabre la devolución.
            devolucion.RequiereComprobanteFiscal = false;
            await _devolucionRepo.UpdateAsync(devolucion, token);

            return new RegistroNota(
                comprobante.ElectronicInvoiceId,
                comprobante.eNCF,
                comprobante.Xml,
                Duplicada: false);
        }, ct);

        // Transmisión DESPUÉS del commit: un fallo de red no revierte el documento fiscal; la cola
        // de emisión lo reintenta con espera progresiva.
        var mensajeTransmision = await TransmitirAsync(registro.ElectronicInvoiceId, ct);

        return new NotaCreditoDevolucionResult
        {
            Exitoso = true,
            Duplicada = registro.Duplicada,
            DevolucionId = devolucion.Id,
            ElectronicInvoiceId = registro.ElectronicInvoiceId,
            eNCF = registro.ENCF,
            eNCFModificado = comprobanteOriginal.eNCF,
            TotalNota = devolucion.TotalDevuelto,
            Xml = registro.Xml,
            Mensaje = registro.Duplicada
                ? "La nota de crédito de esta devolución ya había sido emitida."
                : $"Nota de crédito {registro.ENCF} emitida por RD$ {devolucion.TotalDevuelto:N2}. {mensajeTransmision}"
        };
    }

    /// <summary>Intenta la transmisión inmediata; cualquier fallo queda en manos de la cola.</summary>
    private async Task<string> TransmitirAsync(int electronicInvoiceId, CancellationToken ct)
    {
        try
        {
            var envio = await _invoiceService.EnviarAsync(electronicInvoiceId, ct);
            return envio.Exitoso
                ? "La nota fue registrada en la cola de transmisión a la DGII."
                : $"La nota quedó en cola de emisión para reintentar la transmisión ({envio.Mensaje}).";
        }
        catch (ReglaDeNegocioException ex)
        {
            return $"La nota quedó en cola de emisión para reintentar la transmisión ({ex.Message}).";
        }
    }

    private static NotaCreditoDevolucionResult RespuestaDeExistente(
        Devolucion devolucion,
        ElectronicInvoice nota) => new()
    {
        Exitoso = true,
        Duplicada = true,
        DevolucionId = devolucion.Id,
        ElectronicInvoiceId = nota.Id,
        eNCF = nota.eNCF,
        eNCFModificado = nota.eNCFModificado ?? string.Empty,
        TotalNota = nota.MontoTotal,
        Xml = nota.XMLContent,
        Mensaje = "La nota de crédito de esta devolución ya había sido emitida."
    };

    /// <summary>Resultado interno de la transacción de registro (común a nueva y duplicada).</summary>
    private sealed record RegistroNota(int ElectronicInvoiceId, string ENCF, string Xml, bool Duplicada);
}
