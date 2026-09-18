using System;
using System.Collections.Generic;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Application.CasosDeUso.Ventas;

/// <summary>
/// Solicitud de registro de una venta de punto de venta.
/// </summary>
/// <remarks>
/// El comando NO transporta precios, tasas ni indicadores fiscales: esos datos los resuelve el
/// servidor desde el catálogo. Lo único que aporta el cliente es qué producto, cuánto y con qué
/// descuento, más la intención de cobro y la clave de idempotencia del intento.
/// </remarks>
public sealed class ProcesarVentaCommand
{
    /// <summary>
    /// Clave única del intento lógico de venta. Se genera una vez por carrito y se repite en cada
    /// reintento del cliente (doble clic, refresh, recarga): es la barrera de idempotencia.
    /// </summary>
    public Guid ClaveIdempotencia { get; set; }

    public int? SucursalId { get; set; }
    public int? ClienteId { get; set; }

    /// <summary>RNC del comprador escrito en el POS (obligatorio para Factura de Crédito Fiscal).</summary>
    public string? RNCComprador { get; set; }

    /// <summary>Razón social del comprador; si se omite se usa la del cliente o "Consumidor Final".</summary>
    public string? RazonSocialComprador { get; set; }

    public TipoeCFType TipoeCF { get; set; } = TipoeCFType.FacturaConsumo;
    public TipoPago TipoPago { get; set; } = TipoPago.Contado;

    /// <summary>Forma de pago principal (usada cuando no se detallan pagos mixtos).</summary>
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;

    /// <summary>Efectivo entregado por el cliente; solo aplica al pago único en efectivo.</summary>
    public decimal? MontoRecibido { get; set; }

    public List<ItemVentaCommand> Items { get; set; } = new();

    /// <summary>Pagos mixtos. Si viene vacío se registra un único pago según <see cref="MetodoPago"/>.</summary>
    public List<PagoVentaCommand> Pagos { get; set; } = new();

    /// <summary>Usuario autenticado que registra la venta (lo aporta la capa de presentación).</summary>
    public int UsuarioId { get; set; }

    public string UsuarioNombre { get; set; } = string.Empty;
}

/// <summary>Renglón solicitado: producto del catálogo, cantidad y descuento autorizado.</summary>
public sealed class ItemVentaCommand
{
    public int ProductoId { get; set; }
    public decimal Cantidad { get; set; } = 1m;

    /// <summary>Descuento en pesos sobre el importe bruto de la línea.</summary>
    public decimal Descuento { get; set; }
}

/// <summary>Pago aplicado a la venta.</summary>
public sealed class PagoVentaCommand
{
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
}
