using System;
using System.Collections.Generic;

namespace POS.Application.DTOs;

/// <summary>
/// Representa una fila del Formato 607 (Libro de Ventas de Bienes y Servicios) oficial de la DGII.
/// </summary>
public class Registro607Dto
{
    public int Secuencia { get; set; }
    public string RNC_Cedula { get; set; } = string.Empty;
    public int TipoIdentificacion { get; set; } = 1; // 1=RNC, 2=Cédula, 3=Pasaporte
    public string RazonSocial { get; set; } = string.Empty;
    public string eNCF { get; set; } = string.Empty;
    public string? NCFModificado { get; set; }
    public string TipoIngreso { get; set; } = "01"; // 01=Ingresos por operaciones
    public string FechaComprobante { get; set; } = string.Empty; // YYYYMMDD
    public string? FechaRetencion { get; set; }
    public decimal MontoFacturado { get; set; } // Monto neto gravado + exento
    public decimal ITBISFacturado { get; set; }
    public decimal ITBISRetenido { get; set; }
    public decimal ITBISPercibido { get; set; }
    public decimal RetencionRenta { get; set; }
    public decimal ISRPercibido { get; set; }
    public decimal ISC { get; set; }
    public decimal OtrosImpuestos { get; set; }
    public decimal PropinaLegal { get; set; }
    public decimal Efectivo { get; set; }
    public decimal ChequeTransferencia { get; set; }
    public decimal TarjetaDebitoCredito { get; set; }
    public decimal VentaCredito { get; set; }
    public decimal BonosCertificados { get; set; }
    public decimal Permuta { get; set; }
    public decimal OtrasFormasVenta { get; set; }

    /// <summary>
    /// Genera la línea delimitada por tuberías '|' requerida por la macro/herramienta de envío de la DGII.
    /// </summary>
    public string ToDgiiDelimitedLine()
    {
        return $"{RNC_Cedula}|{TipoIdentificacion}|{eNCF}|{NCFModificado ?? ""}|{TipoIngreso}|{FechaComprobante}|{FechaRetencion ?? ""}|" +
               $"{MontoFacturado:F2}|{ITBISFacturado:F2}|{ITBISRetenido:F2}|{ITBISPercibido:F2}|{RetencionRenta:F2}|{ISRPercibido:F2}|" +
               $"{ISC:F2}|{OtrosImpuestos:F2}|{PropinaLegal:F2}|{Efectivo:F2}|{ChequeTransferencia:F2}|{TarjetaDebitoCredito:F2}|" +
               $"{VentaCredito:F2}|{BonosCertificados:F2}|{Permuta:F2}|{OtrasFormasVenta:F2}";
    }
}

/// <summary>
/// Resumen fiscal consolidado para la declaración jurada mensual del ITBIS (Formulario IT-1).
/// </summary>
public class ResumenItbisMensualDto
{
    public int Anio { get; set; }
    public int Mes { get; set; }
    public int CantidadComprobantes { get; set; }

    public decimal BaseGravada18 { get; set; }
    public decimal ITBIS18 { get; set; }

    public decimal BaseGravada16 { get; set; }
    public decimal ITBIS16 { get; set; }

    public decimal BaseExenta { get; set; }
    public decimal TotalImpuestoAdicionalISC { get; set; }

    public decimal TotalVentasNetas => BaseGravada18 + BaseGravada16 + BaseExenta;
    public decimal TotalITBISDevengado => ITBIS18 + ITBIS16;
    public decimal TotalGeneralFacturado => TotalVentasNetas + TotalITBISDevengado + TotalImpuestoAdicionalISC;
}
