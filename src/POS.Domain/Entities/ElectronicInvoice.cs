using System;
using System.Collections.Generic;
using POS.Domain.Common;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Domain.Entities;

/// <summary>
/// Entidad que representa un Comprobante Fiscal Electrónico (e-CF) emitido y enviado a la DGII.
/// </summary>
public class ElectronicInvoice : BaseEntity
{
    public int? VentaId { get; set; }
    public Venta? Venta { get; set; }

    public TipoeCFType TipoeCF { get; set; } = TipoeCFType.FacturaConsumo;
    public string eNCF { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";

    // Emisor
    public string RNCEmisor { get; set; } = string.Empty;
    public string RazonSocialEmisor { get; set; } = string.Empty;

    // Comprador
    public string? RNCComprador { get; set; }
    public string? RazonSocialComprador { get; set; }

    // Fecha y Clasificación DGII
    public string FechaEmision { get; set; } = string.Empty; // Formato DD-MM-AAAA
    public TipoIngresosType TipoIngresos { get; set; } = TipoIngresosType.IngresosOperaciones;
    public TipoPago TipoPago { get; set; } = TipoPago.Contado;

    // Desglose de Bases Imponibles
    public decimal MontoGravadoTotal { get; set; }
    public decimal MontoGravadoI1 { get; set; } // Base 18%
    public decimal MontoGravadoI2 { get; set; } // Base 16%
    public decimal MontoGravadoI3 { get; set; } // Base 0%
    public decimal MontoExento { get; set; }

    // Desglose de ITBIS
    public decimal TotalITBIS { get; set; }
    public decimal TotalITBIS1 { get; set; } // Monto 18%
    public decimal TotalITBIS2 { get; set; } // Monto 16%
    public decimal TotalITBIS3 { get; set; } // Monto 0%

    // Impuestos Adicionales / ISC
    public decimal MontoImpuestoAdicional { get; set; }

    // Monto Total de la Factura
    public decimal MontoTotal { get; set; }

    // Trazabilidad y Seguridad DGII
    public string XMLContent { get; set; } = string.Empty;
    public string XMLHash { get; set; } = string.Empty; // CodigoSeguridadeCF (6 caracteres)
    public string? TrackId { get; set; }

    /// <summary>
    /// Estado fiscal según la DGII. Nace como <see cref="EstadoFacturaElectronica.NoEnviado"/>: un
    /// comprobante recién registrado no puede declararse "en proceso" sin haber sido transmitido.
    /// </summary>
    public EstadoFacturaElectronica Estado { get; set; } = EstadoFacturaElectronica.NoEnviado;

    /// <summary>Estado del eje técnico local de emisión (creado, validado, firmado, encolado…).</summary>
    public EstadoEmisionECF EstadoEmision { get; set; } = EstadoEmisionECF.Creada;

    /// <summary>Último intento de transmisión a la DGII (trazabilidad operativa).</summary>
    public DateTime? FechaUltimoIntentoEnvio { get; set; }

    /// <summary>Código HTTP del último intento de transmisión; null si no hubo respuesta.</summary>
    public int? UltimoCodigoHttp { get; set; }

    public DateTime? FechaEnvio { get; set; }
    public DateTime? FechaAprobacion { get; set; }
    public DateTime? FechaAnulacion { get; set; }

    public string? MotivoRechazo { get; set; }
    public string? MotivoAnulacion { get; set; }

    // Acuses de Recibo y Aprobación Comercial
    public string? ARECFXML { get; set; }
    public string? ACECFXML { get; set; }

    // Items fiscales
    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}

/// <summary>
/// Item individual dentro del e-CF según la estructura del XSD DGII.
/// </summary>
public class InvoiceItem : BaseEntity
{
    public int ElectronicInvoiceId { get; set; }
    public ElectronicInvoice? ElectronicInvoice { get; set; }

    public int NumeroLinea { get; set; }
    public IndicadorFacturacionType IndicadorFacturacion { get; set; } = IndicadorFacturacionType.ITBIS1_18;
    public IndicadorBienoServicioType IndicadorBienoServicio { get; set; } = IndicadorBienoServicioType.Bien;

    public string NombreItem { get; set; } = string.Empty;
    public string? DescripcionItem { get; set; }
    public decimal CantidadItem { get; set; }
    public UnidadMedidaType UnidadMedida { get; set; } = UnidadMedidaType.Unidad;

    public decimal PrecioUnitarioItem { get; set; }
    public decimal? DescuentoMonto { get; set; }
    public decimal? RecargoMonto { get; set; }

    public decimal Subtotal { get; set; }
    public decimal MontoITBIS { get; set; }
    public string? CodigoISC { get; set; }
    public decimal? MontoISC { get; set; }
    public decimal MontoItem { get; set; } // Total de la línea
}
