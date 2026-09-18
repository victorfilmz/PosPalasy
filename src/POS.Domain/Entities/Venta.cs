using System;
using System.Collections.Generic;
using System.Linq;
using POS.Domain.Common;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Domain.Entities;

/// <summary>
/// Cabecera de venta realizada en el punto de venta (POS).
/// </summary>
public class Venta : BaseEntity
{
    public string NumeroFacturaInterna { get; set; } = string.Empty;

    /// <summary>
    /// Clave de idempotencia del intento de venta. La genera el cliente una sola vez por carrito y
    /// el servidor la usa como barrera única contra doble clic, doble POST, refresh y reintentos.
    /// </summary>
    public Guid ClaveIdempotencia { get; set; } = Guid.NewGuid();

    /// <summary>Usuario que registró la venta (trazabilidad operativa).</summary>
    public string? Usuario { get; set; }

    public int EnterpriseId { get; set; }
    public Enterprise? Enterprise { get; set; }

    public int? SucursalId { get; set; }
    public Sucursal? Sucursal { get; set; }

    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public int? CajaTurnoId { get; set; }
    public CajaTurno? CajaTurno { get; set; }

    public DateTime Fecha { get; set; } = DateTime.UtcNow;
    public TipoPago TipoPago { get; set; } = TipoPago.Contado;
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public TipoeCFType TipoeCF { get; set; } = TipoeCFType.FacturaConsumo;

    // Totales calculados a partir de los items
    public decimal Subtotal { get; set; }
    public decimal TotalDescuento { get; set; }
    public decimal TotalITBIS { get; set; }
    public decimal TotalISC { get; set; }
    public decimal Total { get; set; }

    public string? Notas { get; set; }

    /// <summary>Efectivo recibido del cliente (bruto), para el ticket.</summary>
    public decimal MontoRecibido { get; set; }

    /// <summary>Cambio entregado al cliente. Nunca negativo.</summary>
    public decimal Cambio { get; set; }

    // Colección de renglones / items
    public ICollection<VentaItem> Items { get; set; } = new List<VentaItem>();

    // Desglose de pagos realizados (soporte de pagos mixtos)
    public ICollection<PagoFactura> Pagos { get; set; } = new List<PagoFactura>();

    // Factura electrónica generada (si aplica)
    public ElectronicInvoice? ElectronicInvoice { get; set; }

    public void AddItem(VentaItem item)
    {
        item.NumeroLinea = Items.Count + 1;
        item.Calcular();
        Items.Add(item);
        CalcularTotales();
    }

    public void CalcularTotales()
    {
        Subtotal = Items.Sum(i => i.Subtotal);
        TotalDescuento = Items.Sum(i => i.Descuento);
        TotalITBIS = Items.Sum(i => i.MontoITBIS);
        TotalISC = Items.Sum(i => i.MontoISC);
        Total = Items.Sum(i => i.Total);
    }

    /// <summary>Suma de los pagos registrados en la venta.</summary>
    public decimal TotalPagos => Pagos.Sum(p => p.Monto);

    /// <summary>
    /// Registra un pago. El monto debe ser positivo: un pago en cero o negativo no es un pago.
    /// </summary>
    public void RegistrarPago(MetodoPago metodo, decimal monto, string? referencia = null)
    {
        if (monto <= 0)
            throw new ReglaDeNegocioException(
                $"El monto del pago por {metodo} debe ser mayor que cero.",
                "PAGO_INVALIDO");

        Pagos.Add(new PagoFactura
        {
            MetodoPago = metodo.ToString(),
            Monto = monto,
            Referencia = referencia
        });
    }

    /// <summary>
    /// Valida que el cobro cuaje con el total y devuelve el desglose por forma de pago.
    /// Es la única autoridad sobre el cambio: la UI no decide cuánto se devuelve.
    /// </summary>
    /// <exception cref="ReglaDeNegocioException">Pagos ausentes, insuficientes, excedidos o sin efectivo para el cambio.</exception>
    public DesglosePago ValidarCobro()
    {
        CalcularTotales();

        if (Total <= 0)
            throw new ReglaDeNegocioException(
                "El total de la venta debe ser mayor que cero.",
                "TOTAL_INVALIDO");

        // Venta a crédito: el saldo queda por cobrar, no se exige pago inmediato.
        if (Pagos.Count == 0)
        {
            if (TipoPago != TipoPago.Credito)
                throw new ReglaDeNegocioException(
                    "La venta debe registrar al menos un pago.",
                    "PAGOS_AUSENTES");

            return new DesglosePago(0m, 0m, 0m, 0m, 0m);
        }

        var efectivo = SumarPorMetodo(MetodoPago.Efectivo);
        var tarjeta = SumarPorMetodo(MetodoPago.TarjetaDebitoCredito);
        var transferencia = SumarPorMetodo(MetodoPago.ChequeTransferenciaDeposito);
        var otros = TotalPagos - efectivo - tarjeta - transferencia;

        if (otros > 0)
            throw new ReglaDeNegocioException(
                "La venta incluye una forma de pago que el arqueo de caja todavía no controla. " +
                "Use efectivo, tarjeta o transferencia/cheque.",
                "FORMA_PAGO_NO_SOPORTADA");

        // Solo el efectivo puede exceder el total: es el único que genera cambio.
        foreach (var pago in Pagos.Where(p => p.MetodoPago != MetodoPago.Efectivo.ToString()))
        {
            if (pago.Monto > Total)
                throw new ReglaDeNegocioException(
                    $"El pago por {pago.MetodoPago} (RD$ {pago.Monto:N2}) excede el total de la venta (RD$ {Total:N2}).",
                    "PAGO_EXCEDE_TOTAL");
        }

        if (TotalPagos < Total)
            throw new ReglaDeNegocioException(
                $"Los pagos recibidos (RD$ {TotalPagos:N2}) no cubren el total de la venta (RD$ {Total:N2}).",
                "PAGOS_INSUFICIENTES");

        var cambio = TotalPagos - Total;

        if (cambio > efectivo)
            throw new ReglaDeNegocioException(
                "El cambio debe entregarse en efectivo: registre un pago en efectivo suficiente.",
                "CAMBIO_SIN_EFECTIVO");

        MontoRecibido = efectivo;
        Cambio = cambio;

        return new DesglosePago(efectivo, tarjeta, transferencia, otros, cambio);
    }

    private decimal SumarPorMetodo(MetodoPago metodo) =>
        Pagos.Where(p => p.MetodoPago == metodo.ToString()).Sum(p => p.Monto);
}

/// <summary>
/// Renglón / Item individual de una venta en el POS.
/// </summary>
public class VentaItem : BaseEntity
{
    public int VentaId { get; set; }
    public Venta? Venta { get; set; }

    public int? ProductoId { get; set; }
    public Producto? Producto { get; set; }

    /// <summary>Código del producto en el catálogo (snapshot fiscal de la línea).</summary>
    public string? CodigoProducto { get; set; }

    public int NumeroLinea { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; } = 1;
    public decimal PrecioUnitario { get; set; }
    public decimal Descuento { get; set; }

    public IndicadorFacturacionType IndicadorFacturacion { get; set; } = IndicadorFacturacionType.ITBIS1_18;

    /// <summary>Unidad de medida fiscal del renglón, tomada del catálogo al momento de la venta.</summary>
    public UnidadMedidaType UnidadMedida { get; set; } = UnidadMedidaType.Unidad;

    /// <summary>Naturaleza del renglón (bien o servicio) según el catálogo.</summary>
    public IndicadorBienoServicioType IndicadorBienoServicio { get; set; } = IndicadorBienoServicioType.Bien;

    public decimal TasaITBIS { get; set; } = 18.0m;
    public decimal MontoITBIS { get; set; }

    public string? CodigoISC { get; set; }
    public decimal MontoISC { get; set; }

    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }

    /// <summary>
    /// Construye un renglón tomando del catálogo la descripción, el precio, el indicador fiscal, la
    /// unidad de medida y el ISC. El cliente solo aporta cantidad y descuento: el servidor es la
    /// autoridad sobre todo lo que tiene efecto fiscal o económico.
    /// </summary>
    public static VentaItem DesdeCatalogo(Producto producto, decimal cantidad, decimal descuento = 0m)
    {
        if (producto == null) throw new ArgumentNullException(nameof(producto));

        if (!producto.EstaActivo)
            throw new ReglaDeNegocioException(
                $"El producto '{producto.Descripcion}' está inactivo y no puede venderse.",
                "PRODUCTO_INACTIVO");

        if (cantidad <= 0)
            throw new ReglaDeNegocioException(
                $"La cantidad de '{producto.Descripcion}' debe ser mayor que cero.",
                "CANTIDAD_INVALIDA");

        if (descuento < 0)
            throw new ReglaDeNegocioException(
                $"El descuento de '{producto.Descripcion}' no puede ser negativo.",
                "DESCUENTO_INVALIDO");

        var bruto = Math.Round(cantidad * producto.PrecioUnitario, 2, MidpointRounding.AwayFromZero);

        if (descuento > bruto)
            throw new ReglaDeNegocioException(
                $"El descuento (RD$ {descuento:N2}) de '{producto.Descripcion}' no puede superar el importe de la línea (RD$ {bruto:N2}).",
                "DESCUENTO_EXCEDE_LINEA");

        var renglon = new VentaItem
        {
            ProductoId = producto.Id,
            CodigoProducto = producto.Codigo,
            Descripcion = producto.Descripcion,
            Cantidad = cantidad,
            PrecioUnitario = producto.PrecioUnitario,
            Descuento = descuento,
            IndicadorFacturacion = producto.IndicadorFacturacion,
            UnidadMedida = producto.UnidadMedida,
            IndicadorBienoServicio = producto.IndicadorBienoServicio,
            CodigoISC = producto.CodigoISC
        };

        // Un renglón siempre sale calculado: quien lo reciba no tiene que acordarse de llamar a
        // Calcular() para que los impuestos existan (una línea sin ITBIS es un error fiscal).
        renglon.Calcular();

        return renglon;
    }

    public void Calcular()
    {
        TasaITBIS = IndicadorFacturacionHelper.ObtenerTasaITBIS(IndicadorFacturacion);

        // Subtotal = (Cantidad * PrecioUnitario) - Descuento
        var bruto = Math.Round(Cantidad * PrecioUnitario, 2, MidpointRounding.AwayFromZero);
        Subtotal = Math.Max(0, bruto - Descuento);

        // ITBIS por ítem
        MontoITBIS = Math.Round(Subtotal * (TasaITBIS / 100m), 2, MidpointRounding.AwayFromZero);

        // Total línea
        Total = Subtotal + MontoITBIS + MontoISC;
    }
}
