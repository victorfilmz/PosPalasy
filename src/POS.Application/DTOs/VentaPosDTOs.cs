using System;
using System.Collections.Generic;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Application.DTOs;

public class PagoFacturaDto
{
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
}

/// <summary>
/// Renglón enviado por el terminal de venta.
/// </summary>
/// <remarks>
/// Solo llega lo que el cajero decide: qué producto, cuánta cantidad y qué descuento autoriza.
/// El precio, el ITBIS, la unidad de medida y la descripción los resuelve el servidor desde el
/// catálogo, de modo que la interfaz no pueda alterar el contenido fiscal ni el importe cobrado.
/// </remarks>
public class VentaPosItemRequest
{
    public int ProductoId { get; set; }
    public decimal Cantidad { get; set; } = 1;
    public decimal Descuento { get; set; }
}

/// <summary>Solicitud de venta del terminal de punto de venta.</summary>
public class VentaPosRequest
{
    /// <summary>
    /// Clave de idempotencia del intento (generada una vez por carrito en el terminal). Protege
    /// contra doble clic, doble POST, recarga de página y reintentos.
    /// </summary>
    public Guid? ClaveIdempotencia { get; set; }

    public int? SucursalId { get; set; }
    public int? ClienteId { get; set; }
    public string? RNCComprador { get; set; }
    public string? RazonSocialComprador { get; set; }
    public TipoeCFType TipoeCF { get; set; } = TipoeCFType.FacturaConsumo;
    public TipoPago TipoPago { get; set; } = TipoPago.Contado;
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public decimal? MontoRecibido { get; set; }
    public List<PagoFacturaDto>? Pagos { get; set; }

    public List<VentaPosItemRequest> Items { get; set; } = new();
}

/// <summary>Resultado de la venta para el terminal.</summary>
public class VentaPosResponse
{
    /// <summary>La venta quedó registrada y cobrada localmente.</summary>
    public bool Exitoso { get; set; }

    /// <summary>La solicitud ya había sido procesada: se devolvió la venta original.</summary>
    public bool Duplicada { get; set; }

    public int VentaId { get; set; }
    public int? ElectronicInvoiceId { get; set; }
    public string eNCF { get; set; } = string.Empty;
    public string? TrackId { get; set; }
    public decimal Total { get; set; }
    public decimal Cambio { get; set; }

    /// <summary>Estado fiscal del comprobante según la DGII.</summary>
    public EstadoFacturaElectronica Estado { get; set; } = EstadoFacturaElectronica.NoEnviado;

    /// <summary>La venta se registró, pero el comprobante aún no fue confirmado por la DGII.</summary>
    public bool EsOfflineDGII { get; set; }

    /// <summary>Código estable del error de negocio (para que la interfaz decida qué mostrar).</summary>
    public string? CodigoError { get; set; }

    public string? Mensaje { get; set; }
}
