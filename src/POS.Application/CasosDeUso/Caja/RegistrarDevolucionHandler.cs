using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using POS.Application.CasosDeUso.Ventas;
using POS.Application.Interfaces;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;

namespace POS.Application.CasosDeUso.Caja;

/// <summary>Comando de devolución (total o parcial) de una venta del POS.</summary>
public sealed class RegistrarDevolucionCommand
{
    public Guid ClaveIdempotencia { get; init; }
    public int VentaId { get; init; }

    /// <summary>Cantidad a devolver por cada línea de la venta (parcial si no cubre todas).</summary>
    public IReadOnlyDictionary<int, decimal> CantidadesPorLinea { get; init; } =
        new Dictionary<int, decimal>();

    public string Motivo { get; init; } = string.Empty;
    public int UsuarioId { get; init; }
    public string UsuarioNombre { get; init; } = string.Empty;
}

/// <summary>Resultado del caso de uso de devolución.</summary>
public sealed class RegistrarDevolucionResult
{
    public bool Exitoso { get; init; }
    public bool Duplicada { get; init; }
    public int DevolucionId { get; init; }
    public int VentaId { get; init; }
    public string eNCFVenta { get; init; } = string.Empty;

    public decimal TotalDevuelto { get; init; }
    public decimal EfectivoDevuelto { get; init; }
    public decimal TarjetaDevuelto { get; init; }
    public decimal TransferenciaDevuelto { get; init; }
    public decimal StockReingresado { get; init; }

    public string? CodigoError { get; init; }
    public string? Mensaje { get; init; }

    public static RegistrarDevolucionResult Error(string codigo, string mensaje) => new()
    {
        Exitoso = false,
        CodigoError = codigo,
        Mensaje = mensaje
    };
}

/// <summary>
/// Registra una devolución de venta con las garantías del módulo de caja (Fase 4):
/// <list type="number">
/// <item><b>Idempotencia</b>: la misma clave devuelve la devolución original (doble clic, reintento).</item>
/// <item><b>Tope de reembolso</b>: nunca se devuelve más de lo cobrado, sumando lo ya devuelto
/// (protegido también contra dos devoluciones concurrentes sobre la misma venta).</item>
/// <item><b>Unidad atómica</b>: devolución, renglones, kardex de reingreso y movimiento de caja en
/// una transacción: se confirman juntos o se revierten juntos.</item>
/// <item><b>Caja honesta</b>: el reembolso entra como movimiento de salida referenciado a la venta y
/// como acumulado del turno abierto del operador; nunca reescribe ventas ya contabilizadas.</item>
/// </list>
/// </summary>
public sealed class RegistrarDevolucionHandler
{
    private readonly IVentaRepository _ventaRepo;
    private readonly IDevolucionRepository _devolucionRepo;
    private readonly IInventarioAlmacenRepository _inventarioRepo;
    private readonly IMovimientoInventarioRepository _movimientoRepo;
    private readonly ICajaTurnoRepository _cajaRepo;
    private readonly IUnidadDeTrabajo _unidadDeTrabajo;

    public RegistrarDevolucionHandler(
        IVentaRepository ventaRepo,
        IDevolucionRepository devolucionRepo,
        IInventarioAlmacenRepository inventarioRepo,
        IMovimientoInventarioRepository movimientoRepo,
        ICajaTurnoRepository cajaRepo,
        IUnidadDeTrabajo unidadDeTrabajo)
    {
        _ventaRepo = ventaRepo;
        _devolucionRepo = devolucionRepo;
        _inventarioRepo = inventarioRepo;
        _movimientoRepo = movimientoRepo;
        _cajaRepo = cajaRepo;
        _unidadDeTrabajo = unidadDeTrabajo;
    }

    public async Task<RegistrarDevolucionResult> EjecutarAsync(
        RegistrarDevolucionCommand command,
        CancellationToken ct = default)
    {
        try
        {
            return await EjecutarDevolucionAsync(command, ct);
        }
        catch (ConflictoDeUnicidadException ex)
            when (ex.Codigo == CodigosConflicto.IdempotenciaDevolucion)
        {
            // Otra petición con la misma clave ganó la carrera: se devuelve aquella devolución.
            var existente = await _devolucionRepo.GetByClaveIdempotenciaAsync(command.ClaveIdempotencia, ct);
            if (existente is null)
                throw;

            return new RegistrarDevolucionResult
            {
                Exitoso = true,
                Duplicada = true,
                DevolucionId = existente.Id,
                VentaId = existente.VentaId,
                TotalDevuelto = existente.TotalDevuelto,
                EfectivoDevuelto = existente.EfectivoDevuelto,
                TarjetaDevuelto = existente.TarjetaDevuelto,
                TransferenciaDevuelto = existente.TransferenciaDevuelto,
                Mensaje = "La devolución ya había sido registrada con esta misma solicitud."
            };
        }
        catch (ReglaDeNegocioException ex)
        {
            return RegistrarDevolucionResult.Error(ex.Codigo, ex.Message);
        }
    }

    private async Task<RegistrarDevolucionResult> EjecutarDevolucionAsync(
        RegistrarDevolucionCommand command,
        CancellationToken ct)
    {
        // Camino rápido de idempotencia.
        var yaRegistrada = await _devolucionRepo.GetByClaveIdempotenciaAsync(command.ClaveIdempotencia, ct);
        if (yaRegistrada is not null)
            return RespuestaDeExistente(yaRegistrada, duplicada: true);

        if (command.VentaId <= 0)
            return RegistrarDevolucionResult.Error("VENTA_NO_INDICADA", "Indique la venta a devolver.");

        if (string.IsNullOrWhiteSpace(command.Motivo))
            return RegistrarDevolucionResult.Error("MOTIVO_REQUERIDO", "La devolución exige un motivo.");

        var venta = await _ventaRepo.GetWithItemsAsync(command.VentaId, ct);
        if (venta is null)
            return RegistrarDevolucionResult.Error(
                "VENTA_NO_ENCONTRADA",
                $"La venta #{command.VentaId} no existe.");

        var comprobante = venta.ElectronicInvoice;
        var eNCFVenta = comprobante?.eNCF ?? string.Empty;

        // Sin comprobante no hay devolución registrable: la devolución fiscal parte de un documento
        // emitido. Una venta sin e-CF se revisa con soporte; se registra la deuda en pantalla.
        if (comprobante is null)
            return RegistrarDevolucionResult.Error(
                "VENTA_SIN_COMPROBANTE",
                "La venta no tiene comprobante fiscal registrado; no es posible devolverla por esta vía.");

        // b) Unidad atómica: devolución + renglones + kardex + caja. El tope de reembolso se evalúa
        //    DENTRO de la transacción: dos devoluciones concurrentes sobre la misma venta se
        //    serializan aquí y la segunda ve lo ya devuelto por la primera (nunca se duplica).
        var devolucion = await _unidadDeTrabajo.EnTransaccionAsync(async token =>
        {
            // Serialización sobre la fila de la venta ANTES de leer lo ya devuelto: sin este ancla,
            // dos devoluciones concurrentes con claves distintas leerían ambas "0 devuelto" y
            // duplicarían el reembolso (la segunda esperaría aquí el commit de la primera).
            await _devolucionRepo.AnclarVentaParaDevolucionAsync(venta.Id, token);

            var turno = await _cajaRepo.GetTurnoAbiertoDeUsuarioAsync(command.UsuarioId, token)
                ?? throw new ReglaDeNegocioException(
                    "Debe abrir su turno de caja antes de registrar devoluciones.",
                    "SIN_TURNO_DE_CAJA");

            var yaDevueltas = await _devolucionRepo.GetByVentaIdAsync(venta.Id, token);
            var itemsDevolucion = ConstruirItems(venta, command, yaDevueltas);

            if (itemsDevolucion.Count == 0)
                throw new ReglaDeNegocioException(
                    "No hay cantidades pendientes de devolver en esta venta.",
                    "DEVOLUCION_VACIA");

            var totalDevuelto = itemsDevolucion.Sum(i => i.MontoTotal);
            var (efectivo, tarjeta, transferencia) = Devolucion.CalcularReembolso(totalDevuelto, venta);

            var nueva = new Devolucion
            {
                ClaveIdempotencia = command.ClaveIdempotencia,
                VentaId = venta.Id,
                CajaTurnoId = turno.Id,
                Usuario = command.UsuarioNombre,
                Motivo = command.Motivo.Trim(),
                Fecha = DateTime.UtcNow,
                TotalDevuelto = totalDevuelto,
                EfectivoDevuelto = efectivo,
                TarjetaDevuelto = tarjeta,
                TransferenciaDevuelto = transferencia,
                RequiereComprobanteFiscal = true
            };

            foreach (var item in itemsDevolucion)
                nueva.Items.Add(item);

            await _devolucionRepo.AddAsync(nueva, token);

            // Reingreso de existencias y kardex en la sucursal de la venta (o del turno si la venta no la registró).
            var sucursalId = venta.SucursalId ?? turno.SucursalId;
            await ReingresarInventarioAsync(nueva, sucursalId, command.UsuarioNombre, token);

            // Caja: movimiento de salida referenciado a la venta + acumulado del turno, ambos dentro
            // de la transacción ambiente. 0 filas en el acumulado => el turno fue cerrado: se revierte todo.
            // Solo el efectivo toca la gaveta: un reembolso 100% tarjeta/transferencia no deja
            // movimiento de caja (un movimiento de RD$ 0.00 es ruido en el arqueo).
            if (efectivo > 0)
            {
                var movimientoCaja = new MovimientoCaja
                {
                    CajaTurnoId = turno.Id,
                    Tipo = TipoMovimientoCaja.Salida,
                    Monto = efectivo,
                    Concepto = $"Devolución de venta {eNCFVenta} (motivo: {nueva.Motivo})",
                    Fecha = DateTime.UtcNow,
                    Usuario = command.UsuarioNombre,
                    VentaId = venta.Id
                };

                await _cajaRepo.AddMovimientoAsync(movimientoCaja, token);
            }

            var filas = await _cajaRepo.AcumularDevolucionAsync(turno.Id, totalDevuelto, token);
            if (filas == 0)
                throw new ReglaDeNegocioException(
                    "El turno de caja no está abierto: la devolución se canceló por completo.",
                    "TURNO_CERRADO");

            return nueva;
        }, ct);

        return RespuestaDeExistente(devolucion, duplicada: false, eNCFVenta);
    }

    private static RegistrarDevolucionResult RespuestaDeExistente(
        Devolucion devolucion,
        bool duplicada,
        string? eNCFVenta = null) => new()
    {
        Exitoso = true,
        Duplicada = duplicada,
        DevolucionId = devolucion.Id,
        VentaId = devolucion.VentaId,
        eNCFVenta = eNCFVenta
            ?? devolucion.Venta?.ElectronicInvoice?.eNCF
            ?? string.Empty,
        TotalDevuelto = devolucion.TotalDevuelto,
        EfectivoDevuelto = devolucion.EfectivoDevuelto,
        TarjetaDevuelto = devolucion.TarjetaDevuelto,
        TransferenciaDevuelto = devolucion.TransferenciaDevuelto,
        StockReingresado = devolucion.Items.Sum(i => i.Cantidad),
        Mensaje = duplicada
            ? "La devolución ya había sido registrada con esta misma solicitud."
            : "Devolución registrada: el efectivo salió de la caja y el stock reingresó."
    };

    /// <summary>
    /// Construye los renglones a devolver a partir de las cantidades pedidas, restando lo ya
    /// devuelto en intentos previos. Rechaza cualquier intento de devolver más de lo vendido.
    /// </summary>
    private static List<DevolucionItem> ConstruirItems(
        Venta venta,
        RegistrarDevolucionCommand command,
        List<Devolucion> yaDevueltas)
    {
        var items = new List<DevolucionItem>();

        foreach (var linea in venta.Items.OrderBy(i => i.NumeroLinea))
        {
            if (linea.ProductoId is null)
                continue;

            var yaDevuelta = yaDevueltas
                .SelectMany(d => d.Items)
                .Where(i => i.VentaItemId == linea.Id)
                .Sum(i => i.Cantidad);

            var pendiente = linea.Cantidad - yaDevuelta;

            // Sin cantidad explícita la intención es "devolver esta línea": se devuelve todo lo
            // PENDIENTE, no la cantidad original (una venta ya parcialmente devuelta no se excede).
            var pedida = command.CantidadesPorLinea.TryGetValue(linea.Id, out var cantidad)
                ? cantidad
                : pendiente;

            if (pedida <= 0)
                continue;

            if (pedida > pendiente + 0.0000000001m)
                throw new ReglaDeNegocioException(
                    $"La devolución de '{linea.Descripcion}' excede la cantidad pendiente (vendida {linea.Cantidad:0.##}, ya devuelta {yaDevuelta:0.##}).",
                    "CANTIDAD_EXCEDE_VENDIDA");

            items.Add(Devolucion.CrearItem(linea, pedida));
        }

        return items;
    }

    private async Task ReingresarInventarioAsync(
        Devolucion devolucion,
        int sucursalId,
        string usuario,
        CancellationToken token)
    {
        foreach (var item in devolucion.Items)
        {
            var resultado = await _inventarioRepo.ReingresarStockAsync(
                item.ProductoId,
                sucursalId,
                item.Cantidad,
                token);

            await _movimientoRepo.AddAsync(new MovimientoInventario
            {
                ProductoId = item.ProductoId,
                SucursalId = sucursalId,
                Tipo = TipoMovimientoInventario.DevolucionVenta,
                Cantidad = item.Cantidad,
                StockAnterior = resultado.StockResultante - item.Cantidad,
                StockNuevo = resultado.StockResultante,
                Concepto = $"Devolución de venta ({devolucion.Motivo})",
                ReferenciaDocumento = devolucion.Id > 0 ? $"DEV-{devolucion.Id}" : null,
                Fecha = DateTime.UtcNow,
                Usuario = usuario
            }, token);
        }
    }
}
