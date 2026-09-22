using System;
using System.Collections.Generic;
using System.Linq;
using POS.Application.DTOs;
using POS.Application.Services;
using POS.Domain.Common;
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

    /// <summary>
    /// Construye la solicitud fiscal de la <b>Nota de Crédito e-CF 34</b> que documenta una
    /// devolución registrada (FASE 4).
    /// </summary>
    /// <remarks>
    /// <para>Reglas fiscales que aplica:</para>
    /// <list type="bullet">
    /// <item>Los renglones de la nota son los de la devolución ya prorrateados (monto base, ITBIS
    /// y total por línea). La devolución no recalcula impuestos: hereda el <c>IndicadorFacturacion</c>
    /// y la tasa de cada línea vendida, de modo que la nota revierte EXACTAMENTE lo gravado en el
    /// original. El tipo de comprobante es <see cref="TipoeCFType.NotaCredito"/> con referencia
    /// obligatoria al comprobante modificado (<c>InformacionReferencia</c> del XSD 34).</item>
    /// <item>El tipo de pago es el de la venta original (una nota revierte la transacción tal como
    /// se pactó; no es una nueva negociación comercial).</item>
    /// <item>El prorrateo por línea es la fuente de verdad; el total del desglose puede diferir del
    /// <c>TotalDevuelto</c> de la devolución por centavos de redondeo. El ajuste se aplica a la
    /// primera línea gravada (nunca a una exenta) para que la suma de líneas cuadre con el monto
    /// realmente reembolsado.</item>
    /// <item>Si la venta original fue en moneda extranjera, la nota replica la moneda y el tipo de
    /// cambio (la reversión es en la misma moneda de la transacción).</item>
    /// </list>
    /// </remarks>
    public static ElectronicInvoiceRequest DesdeNotaCreditoDevolucion(
        Devolucion devolucion,
        Venta ventaOriginal,
        Enterprise empresa,
        string eNCFNota,
        string eNCFModificado,
        string fechaNCFModificado,
        int indicadorNotaCredito,
        ITaxCalculator taxCalculator)
    {
        if (devolucion is null) throw new ArgumentNullException(nameof(devolucion));
        if (ventaOriginal is null) throw new ArgumentNullException(nameof(ventaOriginal));
        if (empresa is null) throw new ArgumentNullException(nameof(empresa));
        if (string.IsNullOrWhiteSpace(eNCFNota))
            throw new ReglaDeNegocioException("La nota de crédito exige su e-NCF asignado.", "ENCF_REQUERIDO");
        if (string.IsNullOrWhiteSpace(eNCFModificado))
            throw new ReglaDeNegocioException(
                "La nota de crédito exige el e-NCF del comprobante modificado (referencia fiscal obligatoria del XSD 34).",
                "ENCF_MODIFICADO_REQUERIDO");

        var items = ConstruirLineasNota(devolucion, ventaOriginal);
        var totales = taxCalculator.AgregarTotales(items);

        // La devolución (base + ITBIS prorrateados) es el monto a revertir. Si el redondeo por línea
        // dejó una diferencia de centavos frente al total devuelto, se ajusta la primera línea
        // GRAVADA (nunca una exenta: alteraría la naturaleza fiscal del renglón).
        var diferencia = devolucion.TotalDevuelto - totales.Total;
        if (diferencia != 0m)
        {
            var ajustable = items.FirstOrDefault(i =>
                i.IndicadorFacturacion == IndicadorFacturacionType.ITBIS1_18 ||
                i.IndicadorFacturacion == IndicadorFacturacionType.ITBIS2_16 ||
                i.IndicadorFacturacion == IndicadorFacturacionType.ITBIS3_0)
                ?? throw new ReglaDeNegocioException(
                    "La devolución no contiene líneas gravadas: no es posible cuadrar el redondeo de la nota de crédito.",
                    "NOTA_SIN_LINEA_GRAVADA");

            ajustable.Subtotal += diferencia;
            ajustable.Total += diferencia;
            taxCalculator.CalcularLinea(ajustable);
            totales = taxCalculator.AgregarTotales(items);
        }

        return new ElectronicInvoiceRequest
        {
            TipoeCF = TipoeCFType.NotaCredito,
            eNCF = eNCFNota,
            FechaFactura = FechaDominicana.Today,
            TipoIngresos = TipoIngresosType.IngresosOperaciones,
            TipoPago = ventaOriginal.TipoPago,
            MetodoPago = ventaOriginal.MetodoPago,
            IndicadorNotaCredito = indicadorNotaCredito,
            NCFModificado = eNCFModificado,
            FechaNCFModificado = fechaNCFModificado,
            CodigoModificacion = 3, // Corrige montos del comprobante modificado
            MotivoModificacion = Truncar($"Devolución de venta: {devolucion.Motivo}", 90),
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
            // El comprador es el de la venta original (quien recibió el comprobante modificado).
            Comprador = ventaOriginal.ElectronicInvoice?.RNCComprador is null
                ? new CompradorRequest()
                : new CompradorRequest
                {
                    RNC = ventaOriginal.ElectronicInvoice.RNCComprador,
                    RazonSocial = ventaOriginal.ElectronicInvoice.RazonSocialComprador ?? string.Empty
                },
            Items = items,
            Totales = totales
        };
    }

    /// <summary>
    /// Renglones de la nota a partir de los renglones de la devolución: heredan el tratamiento
    /// fiscal (indicador, tasa, ISC) de la línea vendida correspondiente, de modo que la nota
    /// revierte exactamente el impuesto cobrado en el original.
    /// </summary>
    private static List<InvoiceItemRequest> ConstruirLineasNota(Devolucion devolucion, Venta ventaOriginal)
    {
        var lineasVendidas = ventaOriginal.Items.ToDictionary(l => l.Id);

        return devolucion.Items
            .OrderBy(i => i.Id)
            .Select((item, indice) =>
            {
                // La devolución congela la línea vendida (VentaItemId); si la venta no está cargada
                // con sus líneas (no debería ocurrir: el handler exige GetWithItemsAsync), la nota
                // hereda el tratamiento estándar gravado al 18%.
                var vendida = lineasVendidas.GetValueOrDefault(item.VentaItemId);

                return new InvoiceItemRequest
                {
                    Indice = indice + 1,
                    Codigo = item.ProductoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Descripcion = item.Descripcion,
                    Cantidad = item.Cantidad,
                    UnidadMedida = vendida?.UnidadMedida ?? UnidadMedidaType.Unidad,
                    PrecioUnitario = item.Cantidad == 0m
                        ? 0m
                        : Math.Round(item.MontoBase / item.Cantidad, 4, MidpointRounding.AwayFromZero),
                    IndicadorFacturacion = vendida?.IndicadorFacturacion ?? IndicadorFacturacionType.ITBIS1_18,
                    TasaITBIS = vendida?.TasaITBIS ?? 18m,
                    ITBIS = item.MontoITBIS,
                    ISC = vendida?.CodigoISC,
                    // El ISC es un impuesto distinto del ITBIS: la devolución (FASE 4) prorratea
                    // base e ITBIS, nunca duplica el ITBIS como ISC (inflaría el total de la nota).
                    ISCValue = null,
                    Descuento = 0m,
                    Recargo = 0m,
                    Subtotal = item.MontoBase,
                    Total = item.MontoTotal
                };
            })
            .ToList();
    }

    /// <summary>RazonModificacion: máximo 90 caracteres (AlfNum90ValidationType del XSD 34).</summary>
    private static string Truncar(string texto, int maximo) =>
        texto.Length <= maximo ? texto : texto[..maximo];
}
