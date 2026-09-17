using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using POS.Application.DTOs;
using POS.Application.Interfaces;
using POS.Domain.Entities;
using POS.Domain.Enums;

namespace POS.Application.Services;

public class ReporteFiscalService : IReporteFiscalService
{
    public List<Registro607Dto> GenerarRegistros607(IEnumerable<ElectronicInvoice> facturas)
    {
        var lista = new List<Registro607Dto>();
        int sec = 1;

        // Solo facturas no rechazadas ni anuladas
        var facturasValidas = facturas
            .Where(f => f.Estado != EstadoFacturaElectronica.Rechazado && f.Estado != EstadoFacturaElectronica.Anulado)
            .OrderBy(f => f.FechaEmision)
            .ThenBy(f => f.eNCF);

        foreach (var f in facturasValidas)
        {
            var rnc = f.RNCComprador?.Trim() ?? "";
            int tipoId = 1;
            if (rnc.Length == 11) tipoId = 2; // Cédula
            else if (rnc.Length == 9) tipoId = 1; // RNC Empresa
            else if (!string.IsNullOrEmpty(rnc)) tipoId = 3; // Pasaporte / Otros

            // Convertir fecha de DD-MM-AAAA a YYYYMMDD
            string fecha607 = f.FechaEmision;
            var parts = f.FechaEmision.Split('-');
            if (parts.Length == 3)
            {
                fecha607 = $"{parts[2]}{parts[1]}{parts[0]}";
            }

            var reg = new Registro607Dto
            {
                Secuencia = sec++,
                RNC_Cedula = rnc,
                TipoIdentificacion = tipoId,
                RazonSocial = string.IsNullOrWhiteSpace(f.RazonSocialComprador) ? "Consumidor Final" : f.RazonSocialComprador,
                eNCF = f.eNCF,
                TipoIngreso = ((int)f.TipoIngresos).ToString("D2"),
                FechaComprobante = fecha607,
                MontoFacturado = f.MontoGravadoTotal + f.MontoExento,
                ITBISFacturado = f.TotalITBIS,
                ISC = f.MontoImpuestoAdicional
            };

            // Clasificación por forma de pago
            if (f.TipoPago == TipoPago.Credito)
            {
                reg.VentaCredito = f.MontoTotal;
            }
            else
            {
                reg.Efectivo = f.MontoTotal;
            }

            lista.Add(reg);
        }

        return lista;
    }

    public ResumenItbisMensualDto GenerarResumenItbis(int anio, int mes, IEnumerable<ElectronicInvoice> facturas)
    {
        var validas = facturas
            .Where(f => f.Estado != EstadoFacturaElectronica.Rechazado && f.Estado != EstadoFacturaElectronica.Anulado)
            .ToList();

        var resumen = new ResumenItbisMensualDto
        {
            Anio = anio,
            Mes = mes,
            CantidadComprobantes = validas.Count,
            BaseGravada18 = validas.Sum(f => f.MontoGravadoI1),
            ITBIS18 = validas.Sum(f => f.TotalITBIS1),
            BaseGravada16 = validas.Sum(f => f.MontoGravadoI2),
            ITBIS16 = validas.Sum(f => f.TotalITBIS2),
            BaseExenta = validas.Sum(f => f.MontoExento),
            TotalImpuestoAdicionalISC = validas.Sum(f => f.MontoImpuestoAdicional)
        };

        return resumen;
    }

    public string GenerarArchivo607Txt(string rncEmisor, int anio, int mes, List<Registro607Dto> registros)
    {
        var sb = new StringBuilder();
        var periodo = $"{anio:D4}{mes:D2}";

        // Encabezado formato oficial DGII 607:
        // 607|RNC|PERIODO|CANTIDAD_REGISTROS
        sb.AppendLine($"607|{rncEmisor.Trim()}|{periodo}|{registros.Count}");

        foreach (var reg in registros)
        {
            sb.AppendLine(reg.ToDgiiDelimitedLine());
        }

        return sb.ToString();
    }
}
