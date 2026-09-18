using System.Collections.Generic;
using System.Linq;
using POS.Application.DTOs;
using POS.Application.Services;
using POS.Domain.Entities;
using POS.Domain.Types;

namespace POS.Application.CasosDeUso.Ventas;

/// <summary>
/// Construye la solicitud de comprobante electrónico a partir de una venta ya calculada por el
/// dominio. Es el único punto de traducción entre las líneas de la venta y las líneas del documento
/// fiscal, de modo que los importes del POS, los de la base de datos y los del XML son los mismos.
/// </summary>
public static class ConstructorSolicitudECF
{
    public static ElectronicInvoiceRequest DesdeVenta(
        Venta venta,
        Enterprise empresa,
        string eNCF,
        CompradorRequest comprador,
        ITaxCalculator taxCalculator)
    {
        var items = ConstruirLineas(venta);
        var totales = taxCalculator.AgregarTotales(items);

        return new ElectronicInvoiceRequest
        {
            TipoeCF = venta.TipoeCF,
            eNCF = eNCF,
            FechaFactura = FechaDominicana.Today,
            TipoIngresos = TipoIngresosType.IngresosOperaciones,
            TipoPago = venta.TipoPago,
            MetodoPago = venta.MetodoPago,
            Emisor = new EmisorRequest
            {
                RNC = empresa.RNC,
                RazonSocial = empresa.RazonSocial,
                NombreComercial = empresa.NombreComercial,
                Direccion = empresa.Direccion,
                Telefono = empresa.Telefono,
                Email = empresa.Email,
                CodigoProvincia = empresa.CodigoProvincia,
                CodigoMunicipio = empresa.CodigoMunicipio
            },
            Comprador = comprador,
            Items = items,
            Totales = totales
        };
    }

    /// <summary>
    /// Traduce las líneas del dominio a líneas fiscales. No recalcula importes: copia lo que el
    /// dominio ya calculó con los precios del catálogo.
    /// </summary>
    public static List<InvoiceItemRequest> ConstruirLineas(Venta venta)
    {
        return venta.Items
            .OrderBy(i => i.NumeroLinea)
            .Select((linea, indice) => new InvoiceItemRequest
            {
                Indice = indice + 1,
                Codigo = linea.CodigoProducto ?? linea.ProductoId?.ToString() ?? string.Empty,
                Descripcion = linea.Descripcion,
                Cantidad = linea.Cantidad,
                UnidadMedida = linea.UnidadMedida,
                PrecioUnitario = linea.PrecioUnitario,
                IndicadorFacturacion = linea.IndicadorFacturacion,
                TasaITBIS = linea.TasaITBIS,
                ITBIS = linea.MontoITBIS,
                ISC = linea.CodigoISC,
                ISCValue = linea.MontoISC,
                Descuento = linea.Descuento,
                Recargo = 0m,
                Subtotal = linea.Subtotal,
                Total = linea.Total
            })
            .ToList();
    }
}
