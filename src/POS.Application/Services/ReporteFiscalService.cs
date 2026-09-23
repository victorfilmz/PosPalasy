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

    /// <summary>
    /// Libro de compras 606 desde el kardex: cada entrada por compra (EntradaCompra) del período,
    /// con el proveedor y el NCF declarado en el concepto/referencia. El concepto de la entrada
    /// "Factura Proveedor #NCF" es la fuente del NCF cuando la referencia no lo trae.
    /// </summary>
    public List<Registro606Dto> GenerarRegistros606(IEnumerable<MovimientoInventario> entradasCompra)
    {
        var lista = new List<Registro606Dto>();
        int sec = 1;

        foreach (var m in entradasCompra.OrderBy(m => m.Fecha).ThenBy(m => m.Id))
        {
            var rnc = m.Proveedor?.RNC?.Trim() ?? "";
            int tipoId = rnc.Length == 11 ? 2 // Cédula
                : rnc.Length == 9 ? 1          // RNC Empresa
                : !string.IsNullOrEmpty(rnc) ? 3 // Pasaporte / Exterior
                : 1;                            // Sin proveedor: RNC de referencia 000-0000000-0

            var reg = new Registro606Dto
            {
                Secuencia = sec++,
                RNC_Cedula = string.IsNullOrWhiteSpace(rnc) ? "00000000000" : rnc,
                TipoIdentificacion = tipoId,
                RazonSocial = string.IsNullOrWhiteSpace(m.Proveedor?.RazonSocial)
                    ? "PROVEEDOR NO REGISTRADO"
                    : m.Proveedor!.RazonSocial.Trim(),
                NCFCompra = ExtraerNCF(m.ReferenciaDocumento) ?? ExtraerNCF(m.Concepto) ?? "",
                FechaComprobante = m.Fecha.ToString("yyyyMMdd"),
                MontoFacturado = Math.Round(m.CostoUnitario * m.Cantidad, 2),
                ITBISFacturado = 0 // El costo del kardex no separa ITBIS; ajustable cuando el módulo de compras lo registre
            };

            lista.Add(reg);
        }

        return lista;
    }

    public string GenerarArchivo606Txt(string rncEmisor, int anio, int mes, List<Registro606Dto> registros)
    {
        var sb = new StringBuilder();
        var periodo = $"{anio:D4}{mes:D2}";

        // Encabezado formato oficial DGII 606:
        // 606|RNC|PERIODO|CANTIDAD_REGISTROS
        sb.AppendLine($"606|{rncEmisor.Trim()}|{periodo}|{registros.Count}");

        foreach (var reg in registros)
        {
            sb.AppendLine(reg.ToDgiiDelimitedLine());
        }

        return sb.ToString();
    }

    /// <summary>Extrae un NCF de un texto libre: tradicional (B + 10 dígitos) o electrónico (E + 11 dígitos).</summary>
    private static string? ExtraerNCF(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(texto, @"\b[BE][0-9]{10,11}\b");
        return match.Success ? match.Value : null;
    }
}
