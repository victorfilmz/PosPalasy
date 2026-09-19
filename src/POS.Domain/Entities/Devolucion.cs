using System;
using System.Collections.Generic;
using System.Linq;
using POS.Domain.Common;
using POS.Domain.Enums;

namespace POS.Domain.Entities;

/// <summary>
/// Devolución (total o parcial) de una venta registrada en el POS.
/// </summary>
/// <remarks>
/// <para>Reglas del módulo de caja (Fase 4):</para>
/// <list type="bullet">
/// <item>La devolución se registra en el <b>turno abierto del operador</b> que la ejecuta: es la
/// gaveta de la que sale físicamente el efectivo devuelto.</item>
/// <item>Las ventas del turno nunca se reescriben: el efecto del reembolso entra como
/// <see cref="MovimientoCaja"/> de salida con referencia a la venta y como acumulado de
/// <see cref="CajaTurno.TotalDevoluciones"/>. El arqueo de un turno cerrado no se altera.</item>
/// <item>El reembolso reparte el monto devuelto por las mismas formas de pago de la venta
/// (tarjeta y transferencia hasta lo pagado por cada una; el resto, efectivo).</item>
/// <item>El reingreso de existencias y el kardex (<see cref="TipoMovimientoInventario.DevolucionVenta"/>)
/// ocurren en la sucursal de la venta, dentro de la misma transacción.</item>
/// <item>El comprobante fiscal de la devolución (Nota de Crédito e-CF) pertenece a la fase fiscal;
/// mientras no exista, <see cref="RequiereComprobanteFiscal"/> deja la deuda visible.</item>
/// </list>
/// </remarks>
public class Devolucion : BaseEntity
{
    /// <summary>
    /// Clave de idempotencia del intento de devolución: la misma solicitud no puede producir dos
    /// reembolsos (doble clic, doble POST, reintento del cliente).
    /// </summary>
    public Guid ClaveIdempotencia { get; set; } = Guid.NewGuid();

    public int VentaId { get; set; }
    public Venta? Venta { get; set; }

    /// <summary>Turno donde se registra la devolución (de donde sale el efectivo devuelto).</summary>
    public int? CajaTurnoId { get; set; }
    public CajaTurno? CajaTurno { get; set; }

    /// <summary>Operador que ejecutó la devolución (trazabilidad).</summary>
    public string Usuario { get; set; } = string.Empty;

    /// <summary>Motivo obligatorio de la devolución (auditoría y servicio al cliente).</summary>
    public string Motivo { get; set; } = string.Empty;

    public DateTime Fecha { get; set; } = DateTime.UtcNow;

    /// <summary>Total devuelto al cliente (base + ITBIS de las líneas devueltas).</summary>
    public decimal TotalDevuelto { get; set; }

    /// <summary>Parte del reembolso entregada en efectivo de la gaveta.</summary>
    public decimal EfectivoDevuelto { get; set; }

    /// <summary>Parte del reembolso reversada a tarjeta.</summary>
    public decimal TarjetaDevuelto { get; set; }

    /// <summary>Parte del reembolso reversada a transferencia/cheque.</summary>
    public decimal TransferenciaDevuelto { get; set; }

    /// <summary>
    /// La devolución exige su propio comprobante fiscal (Nota de Crédito e-CF, serie e41). Hasta que
    /// la fase fiscal lo implemente, la deuda queda marcada y visible en cada devolución.
    /// </summary>
    public bool RequiereComprobanteFiscal { get; set; } = true;

    public ICollection<DevolucionItem> Items { get; set; } = new List<DevolucionItem>();

    /// <summary>
    /// Construye el renglón de devolución prorrateando los montos de la línea vendida según la
    /// cantidad devuelta. El redondeo (2 decimales, AwayFromZero) se aplica por línea.
    /// </summary>
    public static DevolucionItem CrearItem(VentaItem linea, decimal cantidadDevuelta)
    {
        if (linea is null) throw new ArgumentNullException(nameof(linea));
        if (cantidadDevuelta <= 0)
            throw new ReglaDeNegocioException(
                "La cantidad devuelta debe ser mayor que cero.",
                "CANTIDAD_INVALIDA");
        if (cantidadDevuelta > linea.Cantidad)
            throw new ReglaDeNegocioException(
                $"La cantidad devuelta ({cantidadDevuelta:0.##}) de '{linea.Descripcion}' excede la vendida ({linea.Cantidad:0.##}).",
                "CANTIDAD_EXCEDE_VENDIDA");

        var proporcion = cantidadDevuelta / linea.Cantidad;

        return new DevolucionItem
        {
            VentaItemId = linea.Id,
            ProductoId = linea.ProductoId
                ?? throw new ReglaDeNegocioException(
                    "La línea de venta no está asociada a un producto del catálogo.",
                    "PRODUCTO_REQUERIDO"),
            Descripcion = linea.Descripcion,
            Cantidad = cantidadDevuelta,
            MontoBase = Math.Round(linea.Subtotal * proporcion, 2, MidpointRounding.AwayFromZero),
            MontoITBIS = Math.Round(linea.MontoITBIS * proporcion, 2, MidpointRounding.AwayFromZero),
            MontoTotal = Math.Round((linea.Subtotal + linea.MontoITBIS) * proporcion, 2, MidpointRounding.AwayFromZero)
        };
    }

    /// <summary>
    /// Reparte el total devuelto entre las formas de pago de la venta: primero tarjeta, luego
    /// transferencia (hasta lo pagado por cada una) y el resto en efectivo. El efectivo absorbe
    /// los redondeos y es el único que sale físicamente de la gaveta.
    /// </summary>
    public static (decimal Efectivo, decimal Tarjeta, decimal Transferencia) CalcularReembolso(
        decimal totalDevuelto, Venta venta)
    {
        if (totalDevuelto <= 0)
            throw new ReglaDeNegocioException(
                "El total devuelto debe ser mayor que cero.",
                "DEVOLUCION_INVALIDA");

        decimal pagadoPor(MetodoPago metodo) => venta.Pagos
            .Where(p => p.MetodoPago == metodo.ToString())
            .Sum(p => p.Monto);

        var restante = totalDevuelto;

        var tarjeta = Math.Min(pagadoPor(MetodoPago.TarjetaDebitoCredito), restante);
        restante -= tarjeta;

        var transferencia = Math.Min(pagadoPor(MetodoPago.ChequeTransferenciaDeposito), restante);
        restante -= transferencia;

        var efectivo = restante;

        if (efectivo < 0)
            throw new ReglaDeNegocioException(
                "El desglose de reembolso produjo un efectivo negativo.",
                "DEVOLUCION_INVALIDA");

        return (efectivo, tarjeta, transferencia);
    }
}

/// <summary>Renglón devuelto de una venta, con los montos prorrateados de la línea original.</summary>
public class DevolucionItem : BaseEntity
{
    public int DevolucionId { get; set; }
    public Devolucion? Devolucion { get; set; }

    /// <summary>Línea de la venta original (snapshot, sin FK de borrado en cascada).</summary>
    public int VentaItemId { get; set; }

    public int ProductoId { get; set; }
    public Producto? Producto { get; set; }

    /// <summary>Descripción congelada de la línea vendida.</summary>
    public string Descripcion { get; set; } = string.Empty;

    public decimal Cantidad { get; set; }

    /// <summary>Base devuelta (sin ITBIS), prorrateada de la línea original.</summary>
    public decimal MontoBase { get; set; }

    public decimal MontoITBIS { get; set; }

    /// <summary>Total devuelto del renglón (MontoBase + MontoITBIS).</summary>
    public decimal MontoTotal { get; set; }
}
