using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using POS.Application.DTOs;
using POS.Application.Interfaces;
using POS.Application.Services;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Domain.Types;

namespace POS.Application.CasosDeUso.Ventas;

/// <summary>
/// Caso de uso de registro de una venta de punto de venta.
/// </summary>
/// <remarks>
/// <para>Orden de operaciones y su frontera transaccional:</para>
/// <list type="number">
/// <item>Validación de forma y resolución de precios y datos fiscales desde el catálogo del servidor.</item>
/// <item>Validación del cobro en el dominio (total, pagos, cambio).</item>
/// <item>Transacción local única: secuencia eNCF, venta, inventario, caja, comprobante y outbox.</item>
/// <item>Confirmación (commit).</item>
/// <item>Transmisión a la DGII FUERA de la transacción; su resultado no puede deshacer la venta.</item>
/// </list>
/// <para>
/// Cada intento vuelve a leer sus entidades y reconstruye el agregado. Es deliberado: tras un
/// revertimiento, el contexto no conserva entidades seguidas, y reutilizar objetos ya leídos haría
/// que EF intentara insertar de nuevo entidades que ya existen en la base de datos.
/// </para>
/// </remarks>
public sealed class ProcesarVentaHandler
{
    /// <summary>Reintentos ante colisión de eNCF detectada por la base de datos (carrera de numeración).</summary>
    private const int MaxIntentosAsignacionEncf = 3;

    private readonly IVentaRepository _ventaRepo;
    private readonly IInvoiceRepository _invoiceRepo;
    private readonly IProductoRepository _productoRepo;
    private readonly IClienteRepository _clienteRepo;
    private readonly IEnterpriseRepository _enterpriseRepo;
    private readonly ISecuenciaECFRepository _secuenciaRepo;
    private readonly IInventarioAlmacenRepository _inventarioRepo;
    private readonly IMovimientoInventarioRepository _movimientoRepo;
    private readonly ICajaTurnoRepository _cajaRepo;
    private readonly IEmisionDGIIQueueRepository _queueRepo;
    private readonly IElectronicInvoiceService _invoiceService;
    private readonly ITaxCalculator _taxCalculator;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;

    public ProcesarVentaHandler(
        IVentaRepository ventaRepo,
        IInvoiceRepository invoiceRepo,
        IProductoRepository productoRepo,
        IClienteRepository clienteRepo,
        IEnterpriseRepository enterpriseRepo,
        ISecuenciaECFRepository secuenciaRepo,
        IInventarioAlmacenRepository inventarioRepo,
        IMovimientoInventarioRepository movimientoRepo,
        ICajaTurnoRepository cajaRepo,
        IEmisionDGIIQueueRepository queueRepo,
        IElectronicInvoiceService invoiceService,
        ITaxCalculator taxCalculator,
        IUnidadDeTrabajo unidadDeTrabajo)
    {
        _ventaRepo = ventaRepo;
        _invoiceRepo = invoiceRepo;
        _productoRepo = productoRepo;
        _clienteRepo = clienteRepo;
        _enterpriseRepo = enterpriseRepo;
        _secuenciaRepo = secuenciaRepo;
        _inventarioRepo = inventarioRepo;
        _movimientoRepo = movimientoRepo;
        _cajaRepo = cajaRepo;
        _queueRepo = queueRepo;
        _invoiceService = invoiceService;
        _taxCalculator = taxCalculator;
        _unidadDeTrabajo = unidadDeTrabajo;
    }

    /// <summary>
    /// Registra la venta. Devuelve siempre un resultado tipado: los errores de negocio no se lanzan
    /// como excepción para que la capa de presentación no dependa de mensajes de excepción.
    /// </summary>
    public async Task<ProcesarVentaResult> EjecutarAsync(
        ProcesarVentaCommand command,
        CancellationToken ct = default)
    {
        try
        {
            ProcesarVentaValidator.Validar(command);
            return await EjecutarVentaAsync(command, ct);
        }
        catch (ReglaDeNegocioException ex)
        {
            return ProcesarVentaResult.Error(ex.Codigo, ex.Message);
        }
    }

    private async Task<ProcesarVentaResult> EjecutarVentaAsync(
        ProcesarVentaCommand command,
        CancellationToken ct)
    {
        // Camino rápido de idempotencia: la misma solicitud devuelve la venta ya registrada.
        var yaRegistrada = await _ventaRepo.GetByClaveIdempotenciaAsync(command.ClaveIdempotencia, ct);
        if (yaRegistrada != null)
            return await ResultadoDuplicadoAsync(yaRegistrada, ct);

        for (var intento = 1; intento <= MaxIntentosAsignacionEncf; intento++)
        {
            try
            {
                var registro = await _unidadDeTrabajo.EnTransaccionAsync(
                    token => EjecutarIntentoAsync(command, token),
                    ct);

                // A partir de aquí la venta está confirmada: la transmisión no puede deshacerla.
                return await TransmitirYResponderAsync(registro.Venta, registro.Comprobante, ct);
            }
            catch (ConflictoDeUnicidadException ex) when (ex.Codigo == CodigosConflicto.IdempotenciaVenta)
            {
                // Otra petición con la misma clave ganó la carrera: se devuelve aquella venta.
                var ganadora = await _ventaRepo.GetByClaveIdempotenciaAsync(command.ClaveIdempotencia, ct);
                if (ganadora == null)
                    throw;

                return await ResultadoDuplicadoAsync(ganadora, ct);
            }
            catch (ConflictoDeUnicidadException ex)
                when (ex.Codigo == CodigosConflicto.EncfDuplicado && intento < MaxIntentosAsignacionEncf)
            {
                // Carrera de numeración: la asignación nueva se resolverá en el siguiente intento.
            }
        }

        throw new ReglaDeNegocioException(
            "No fue posible asignar un número de comprobante único. Reintente la venta.",
            "ENCF_NO_DISPONIBLE");
    }

    /// <summary>
    /// Un intento completo: lecturas, validaciones y escrituras. Todo lo que ocurre aquí se confirma
    /// o se revierte como una sola unidad: no existe venta sin comprobante, ni comprobante sin kardex,
    /// ni caja sin venta.
    /// </summary>
    private async Task<RegistroLocal> EjecutarIntentoAsync(
        ProcesarVentaCommand command,
        CancellationToken token)
    {
        // a) Contexto de la operación: empresa emisora y turno de caja del usuario autenticado.
        var empresa = await _enterpriseRepo.GetDefaultAsync(token)
            ?? throw new ReglaDeNegocioException(
                "No hay una empresa configurada en el sistema.",
                "EMPRESA_NO_CONFIGURADA");

        var turno = await _cajaRepo.GetTurnoAbiertoDeUsuarioAsync(command.UsuarioId, token)
            ?? throw new ReglaDeNegocioException(
                "Debe abrir su turno de caja antes de registrar ventas.",
                "SIN_TURNO_DE_CAJA");

        if (command.SucursalId.HasValue && command.SucursalId.Value != turno.SucursalId)
            throw new ReglaDeNegocioException(
                "La venta debe registrarse en la sucursal del turno de caja abierto.",
                "SUCURSAL_FUERA_DE_TURNO");

        // b) El catálogo del servidor manda: precio, ITBIS, unidad, descripción y costo.
        var productos = await CargarProductosAsync(command, token);

        var venta = new Venta
        {
            ClaveIdempotencia = command.ClaveIdempotencia,
            Usuario = command.UsuarioNombre,
            EnterpriseId = empresa.Id,
            SucursalId = turno.SucursalId,
            CajaTurnoId = turno.Id,
            ClienteId = command.ClienteId,
            Fecha = DateTime.UtcNow,
            TipoPago = command.TipoPago,
            MetodoPago = command.MetodoPago,
            TipoeCF = command.TipoeCF
        };

        foreach (var item in command.Items)
        {
            venta.AddItem(VentaItem.DesdeCatalogo(productos[item.ProductoId], item.Cantidad, item.Descuento));
        }

        RegistrarPagos(venta, command);

        // c) El cobro se valida en el dominio: total, pagos y cambio no los decide la interfaz.
        var desglose = venta.ValidarCobro();

        var comprador = await ResolverCompradorAsync(command, token);

        // d) Numeración fiscal dentro de la transacción (un revertimiento libera el número).
        var eNCF = await _secuenciaRepo.AsignarSiguienteENCFAsync(command.TipoeCF, token);
        venta.NumeroFacturaInterna = $"FAC-{eNCF}";

        // e) Venta y líneas.
        await _ventaRepo.AddAsync(venta, token);

        // f) Inventario: descuento atómico y kardex con existencias exactas.
        await MoverInventarioAsync(venta, turno, empresa, productos, command, eNCF, token);

        // g) Caja: acumulación atómica en el turno (sin reescribir el grafo de la caja).
        var filasTurno = await _cajaRepo.AcumularVentaAsync(
            turno.Id,
            desglose.EfectivoNetoEnCaja,
            desglose.Tarjeta,
            desglose.Transferencia,
            venta.Total,
            token);

        if (filasTurno == 0)
            throw new ReglaDeNegocioException(
                "El turno de caja fue cerrado durante la venta. La operación se canceló por completo.",
                "TURNO_CERRADO");

        // h) Comprobante registrado localmente (sin transmitir todavía).
        var solicitud = ConstructorSolicitudECF.DesdeVenta(venta, empresa, eNCF, comprador, _taxCalculator);
        var comprobante = await _invoiceService.PrepararYRegistrarAsync(
            new PrepararComprobanteCommand(solicitud, venta.Id),
            token);

        // i) Outbox: la fila nace en la misma transacción, de modo que un cierre abrupto del proceso
        //    no deja un comprobante sin ruta de transmisión.
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

        return new RegistroLocal(venta, comprobante);
    }

    private async Task MoverInventarioAsync(
        Venta venta,
        CajaTurno turno,
        Enterprise empresa,
        IReadOnlyDictionary<int, Producto> productos,
        ProcesarVentaCommand command,
        string eNCF,
        CancellationToken token)
    {
        foreach (var linea in venta.Items)
        {
            var productoId = linea.ProductoId
                ?? throw new ReglaDeNegocioException(
                    "La línea de venta no está asociada a un producto del catálogo.",
                    "PRODUCTO_REQUERIDO");

            var resultado = await _inventarioRepo.DescontarStockAsync(
                productoId,
                turno.SucursalId,
                linea.Cantidad,
                empresa.PoliticaStock != PoliticaStock.Bloquear,
                token);

            if (!resultado.Aplicado)
            {
                var disponible = resultado.ExistenciaRegistrada
                    ? $"Existencia disponible: {resultado.StockResultante:0.##}"
                    : "El producto no tiene existencias registradas en esta sucursal.";

                throw new ReglaDeNegocioException(
                    $"Stock insuficiente para '{linea.Descripcion}'. {disponible}, solicitada: {linea.Cantidad:0.##}.",
                    "STOCK_INSUFICIENTE");
            }

            // Políticas Permitir y Advertir pueden vender por encima de la existencia: la venta debe
            // quedar marcada para revisión, no escondido. La política la impone el servidor.
            var sinExistenciaSuficiente = resultado.StockResultante < 0m
                || !resultado.ExistenciaRegistrada;

            await _movimientoRepo.AddAsync(new MovimientoInventario
            {
                ProductoId = productoId,
                SucursalId = turno.SucursalId,
                Tipo = TipoMovimientoInventario.VentaPOS,
                Cantidad = linea.Cantidad,
                StockAnterior = resultado.StockResultante + linea.Cantidad,
                StockNuevo = resultado.StockResultante,
                CostoUnitario = productos[productoId].CostoUnitario,
                Concepto = sinExistenciaSuficiente && empresa.PoliticaStock == PoliticaStock.Advertir
                    ? $"ADVERTENCIA de stock: salida POS e-CF {eNCF} (política {empresa.PoliticaStock})"
                    : $"Salida POS e-CF {eNCF}",
                ReferenciaDocumento = eNCF,
                RequiereRevision = sinExistenciaSuficiente && empresa.PoliticaStock == PoliticaStock.Advertir,
                Fecha = DateTime.UtcNow,
                Usuario = command.UsuarioNombre
            }, token);

            if (sinExistenciaSuficiente && empresa.PoliticaStock == PoliticaStock.Advertir)
                venta.RequiereRevisionStock = true;
        }
    }

    /// <summary>
    /// Transmite el comprobante ya confirmado. Un fallo aquí deja la venta intacta y el documento en
    /// la cola: la respuesta informa el estado real en lugar de fingir un envío exitoso.
    /// </summary>
    private async Task<ProcesarVentaResult> TransmitirYResponderAsync(
        Venta venta,
        ComprobantePreparado comprobante,
        CancellationToken ct)
    {
        ElectronicInvoiceResponse envio;
        try
        {
            envio = await _invoiceService.EnviarAsync(comprobante.ElectronicInvoiceId, ct);
        }
        catch (Exception ex)
        {
            // Contingencia de la DGII (6.1): el comprobante nació sin transmisión. Se declara la
            // contingencia oficial (FallaPlataformaDgii) con su ventana normativa de 30 días; la
            // cola reintenta sola. La venta queda intacta.
            var comprobanteCaído = await _invoiceRepo.GetByIdAsync(comprobante.ElectronicInvoiceId, ct);
            DateTime? ventanaHasta = null;
            if (comprobanteCaído != null)
            {
                var declaracion = ServicioContingencia.Declarar(comprobanteCaído,
                    new DeclararContingenciaCommand(comprobante.ElectronicInvoiceId,
                        TipoContingenciaDgii.FallaPlataformaDgii, DateTime.UtcNow));
                ventanaHasta = declaracion.VentanaHastaUtc;
                await _invoiceRepo.UpdateAsync(comprobanteCaído, ct);
            }

            envio = new ElectronicInvoiceResponse
            {
                Exitoso = false,
                eNCF = comprobante.eNCF,
                Estado = EstadoFacturaElectronica.PendienteReenvio,
                EstadoEmision = EstadoEmisionECF.Encolada,
                EsRecuperable = true,
                Mensaje = $"La venta quedó registrada. Contingencia declarada ante la DGII" +
                          (ventanaHasta.HasValue ? $" (ventana de transmisión hasta {ventanaHasta:dd-MM-yyyy})." : ".") +
                          $" Detalle: {ex.Message}"
            };
        }

        return new ProcesarVentaResult
        {
            Exitoso = true,
            VentaId = venta.Id,
            ElectronicInvoiceId = comprobante.ElectronicInvoiceId,
            eNCF = comprobante.eNCF,
            TrackId = envio.TrackId,
            Total = venta.Total,
            Cambio = venta.Cambio,
            EstadoFiscal = envio.Estado,
            EstadoEmision = envio.EstadoEmision,
            EsOfflineDGII = !envio.Exitoso,
            Mensaje = envio.Exitoso
                ? envio.Mensaje
                : string.IsNullOrWhiteSpace(envio.Mensaje)
                    ? "La venta quedó registrada y el comprobante está en cola para transmisión a la DGII."
                    : envio.Mensaje
        };
    }

    /// <summary>Respuesta de una venta ya existente (reintento del cliente sobre la misma clave).</summary>
    private async Task<ProcesarVentaResult> ResultadoDuplicadoAsync(Venta venta, CancellationToken ct)
    {
        var comprobante = await _invoiceRepo.GetByVentaIdAsync(venta.Id, ct);

        return new ProcesarVentaResult
        {
            Exitoso = true,
            Duplicada = true,
            VentaId = venta.Id,
            ElectronicInvoiceId = comprobante?.Id,
            eNCF = comprobante?.eNCF ?? string.Empty,
            TrackId = comprobante?.TrackId,
            Total = venta.Total,
            Cambio = venta.Cambio,
            EstadoFiscal = comprobante?.Estado ?? EstadoFacturaElectronica.NoEnviado,
            EstadoEmision = comprobante?.EstadoEmision ?? EstadoEmisionECF.Creada,
            EsOfflineDGII = comprobante == null
                || comprobante.Estado == EstadoFacturaElectronica.PendienteReenvio
                || comprobante.Estado == EstadoFacturaElectronica.NoEnviado,
            Mensaje = "La venta ya había sido registrada con esta misma solicitud; se devolvió el comprobante original."
        };
    }

    private async Task<IReadOnlyDictionary<int, Producto>> CargarProductosAsync(
        ProcesarVentaCommand command,
        CancellationToken ct)
    {
        var solicitados = command.Items.Select(i => i.ProductoId).Distinct().ToList();
        var encontrados = (await _productoRepo.GetByIdsAsync(solicitados, ct)).ToDictionary(p => p.Id);

        var faltantes = solicitados.Where(id => !encontrados.ContainsKey(id)).ToList();
        if (faltantes.Count > 0)
            throw new ReglaDeNegocioException(
                $"Los productos #{string.Join(", #", faltantes)} no existen en el catálogo.",
                "PRODUCTO_NO_ENCONTRADO");

        return encontrados;
    }

    /// <summary>
    /// Registra los pagos: si el cliente detalla el desglose se respeta tal cual; si no, se registra
    /// un único pago con el método indicado. Un pago electrónico nunca puede exceder el total.
    /// </summary>
    private static void RegistrarPagos(Venta venta, ProcesarVentaCommand command)
    {
        if (command.Pagos is { Count: > 0 })
        {
            foreach (var pago in command.Pagos)
            {
                venta.RegistrarPago(pago.MetodoPago, pago.Monto, pago.Referencia);
            }

            return;
        }

        var total = venta.Total;
        var monto = command.MetodoPago == MetodoPago.Efectivo
            ? command.MontoRecibido ?? total
            : total;

        venta.RegistrarPago(command.MetodoPago, monto);
    }

    /// <summary>
    /// Determina los datos del comprador. Prioridad: lo que el cajero escribió (RNC explícito) y, en
    /// su defecto, el registro del cliente seleccionado; el nombre por defecto es "Consumidor Final".
    /// </summary>
    private async Task<CompradorRequest> ResolverCompradorAsync(
        ProcesarVentaCommand command,
        CancellationToken ct)
    {
        Cliente? cliente = null;
        if (command.ClienteId is > 0)
        {
            cliente = await _clienteRepo.GetByIdAsync(command.ClienteId.Value, ct);
            if (cliente == null)
                throw new ReglaDeNegocioException(
                    $"El cliente #{command.ClienteId} no existe.",
                    "CLIENTE_NO_ENCONTRADO");
        }

        var rnc = string.IsNullOrWhiteSpace(command.RNCComprador)
            ? cliente?.RNC
            : command.RNCComprador.Trim();

        var razonSocial = string.IsNullOrWhiteSpace(command.RazonSocialComprador)
            ? cliente?.RazonSocial
            : command.RazonSocialComprador.Trim();

        return new CompradorRequest
        {
            RNC = rnc,
            RazonSocial = string.IsNullOrWhiteSpace(razonSocial) ? "Consumidor Final" : razonSocial,
            Direccion = cliente?.Direccion,
            CodigoProvincia = cliente?.CodigoProvincia,
            CodigoMunicipio = cliente?.CodigoMunicipio,
            Telefono = cliente?.Telefono,
            Email = cliente?.Email
        };
    }

    /// <summary>Resultado de un intento: la venta persistida y su comprobante registrado.</summary>
    private sealed record RegistroLocal(Venta Venta, ComprobantePreparado Comprobante);
}
