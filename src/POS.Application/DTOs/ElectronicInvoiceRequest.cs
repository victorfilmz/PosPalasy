using System;
using System.Collections.Generic;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Application.DTOs;

/// <summary>
/// DTO completo con los datos necesarios para construir, firmar y enviar un e-CF 32 / e-CF 31 a la DGII.
/// </summary>
public class ElectronicInvoiceRequest
{
    // Envelope / Control
    public string Version { get; set; } = "1.0";
    public string Semilla { get; set; } = "Semilla v.1.0";
    public string CodigoSeguridadeCF { get; set; } = string.Empty; // Hash de 6 caracteres

    // Encabezado
    public TipoeCFType TipoeCF { get; set; } = TipoeCFType.FacturaConsumo;
    public string eNCF { get; set; } = string.Empty;
    public int NumeroConsecutivo { get; set; } = 1;
    public string FechaHoraFirma { get; set; } = string.Empty;

    // Fechas
    public FechaDominicana FechaFactura { get; set; } = FechaDominicana.Today;
    public DateTime? FechaVencimiento { get; set; }

    // Clasificación DGII
    public TipoIngresosType TipoIngresos { get; set; } = TipoIngresosType.IngresosOperaciones;
    public TipoPago TipoPago { get; set; } = TipoPago.Contado;
    public MetodoPago MetodoPago { get; set; } = MetodoPago.Efectivo;
    public string? PlazoCredito { get; set; }

    // Participantes
    public EmisorRequest Emisor { get; set; } = new();
    public CompradorRequest Comprador { get; set; } = new();

    // Renglones y Totales
    public List<InvoiceItemRequest> Items { get; set; } = new();
    public TotalesRequest Totales { get; set; } = new();

    // Moneda
    public string Moneda { get; set; } = "DOP";
    public TipoMonedaType? MonedaExtranjera { get; set; }
    public decimal? TipoCambio { get; set; }

    // Referencia fiscal al comprobante modificado (nota de crédito e-CF 34 / débito e-CF 33):
    // IndicadorNotaCredito es obligatorio en el IdDoc del tipo 34 (0 = dentro de 30 días).
    public int? IndicadorNotaCredito { get; set; }
    public string? FechaNCFModificado { get; set; }
    public decimal? TotalEnMonedaExtranjera { get; set; }

    // Información de referencia (para Notas de Crédito/Débito). CodigoModificacion según el XSD 34:
    // 1=Anula, 2=Corrige texto, 3=Corrige montos, 4=Reemplazo de contingencia.
    public int? CodigoModificacion { get; set; }
    public string? NCFModificado { get; set; }
    public string? MotivoModificacion { get; set; }

    public bool EsFacturaElectronica { get; set; } = true;
}
