using System;
using System.Collections.Generic;
using System.Linq;
using POS.Application.DTOs;
using POS.Domain.Types;

namespace POS.Application.Services;

public interface ITaxCalculator
{
    void CalcularLinea(InvoiceItemRequest item);
    TotalesRequest CalcularTotales(IEnumerable<InvoiceItemRequest> items);
}

/// <summary>
/// Calculador oficial de impuestos para comprobantes fiscales de la DGII.
/// El cálculo de ITBIS e ISC se realiza a nivel de línea y luego se totaliza.
/// </summary>
public class TaxCalculator : ITaxCalculator
{
    public void CalcularLinea(InvoiceItemRequest item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        item.TasaITBIS = IndicadorFacturacionHelper.ObtenerTasaITBIS(item.IndicadorFacturacion);

        var bruto = Math.Round(item.Cantidad * item.PrecioUnitario, 2, MidpointRounding.AwayFromZero);
        item.Subtotal = Math.Max(0, bruto - item.Descuento + item.Recargo);

        // ITBIS
        item.ITBIS = Math.Round(item.Subtotal * (item.TasaITBIS / 100m), 2, MidpointRounding.AwayFromZero);

        // Total línea
        var isc = item.ISCValue ?? 0m;
        item.Total = item.Subtotal + item.ITBIS + isc;
    }

    public TotalesRequest CalcularTotales(IEnumerable<InvoiceItemRequest> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        var itemList = items.ToList();
        foreach (var item in itemList)
        {
            CalcularLinea(item);
        }

        var totales = new TotalesRequest
        {
            SubTotal = itemList.Sum(i => i.Subtotal),
            TotalDescuentos = itemList.Sum(i => i.Descuento),
            TotalRecargos = itemList.Sum(i => i.Recargo),

            // Bases imponibles desglosadas
            MontoGravadoI1 = itemList.Where(i => i.IndicadorFacturacion == IndicadorFacturacionType.ITBIS1_18 || i.IndicadorFacturacion == IndicadorFacturacionType.NoFacturable18).Sum(i => i.Subtotal),
            MontoGravadoI2 = itemList.Where(i => i.IndicadorFacturacion == IndicadorFacturacionType.ITBIS2_16).Sum(i => i.Subtotal),
            MontoGravadoI3 = itemList.Where(i => i.IndicadorFacturacion == IndicadorFacturacionType.ITBIS3_0).Sum(i => i.Subtotal),
            MontoExento = itemList.Where(i => i.IndicadorFacturacion == IndicadorFacturacionType.Exento).Sum(i => i.Subtotal),

            // ITBIS desglosado
            TotalITBIS1 = itemList.Where(i => i.IndicadorFacturacion == IndicadorFacturacionType.ITBIS1_18 || i.IndicadorFacturacion == IndicadorFacturacionType.NoFacturable18).Sum(i => i.ITBIS),
            TotalITBIS2 = itemList.Where(i => i.IndicadorFacturacion == IndicadorFacturacionType.ITBIS2_16).Sum(i => i.ITBIS),
            TotalITBIS3 = itemList.Where(i => i.IndicadorFacturacion == IndicadorFacturacionType.ITBIS3_0).Sum(i => i.ITBIS),

            // ISC
            TotalISC = itemList.Sum(i => i.ISCValue ?? 0m)
        };

        totales.MontoGravadoTotal = totales.MontoGravadoI1 + totales.MontoGravadoI2 + totales.MontoGravadoI3;
        totales.TotalITBIS = totales.TotalITBIS1 + totales.TotalITBIS2 + totales.TotalITBIS3;
        totales.Total = totales.SubTotal + totales.TotalITBIS + totales.TotalISC;

        return totales;
    }
}
