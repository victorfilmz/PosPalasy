using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using POS.Application.DTOs;
using POS.Domain.Types;

namespace POS.Infrastructure.XmlSerialization;

public interface IXmlSerializer
{
    string Serialize(ElectronicInvoiceRequest request);
    string SerializeAnulacion(AnulacionRequest request);
}

/// <summary>
/// Serializador de comprobantes fiscales electrónicos (e-CF) usando LINQ to XML (XDocument / XElement)
/// conforme a los esquemas XSD oficiales de la DGII de la República Dominicana.
/// </summary>
public class XmlSerializer : IXmlSerializer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Serialize(ElectronicInvoiceRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("ECF",
                CrearEncabezado(request),
                CrearDetallesItems(request)
            )
        );

        return doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
    }

    private XElement CrearEncabezado(ElectronicInvoiceRequest req)
    {
        var emisor = req.Emisor;
        var comprador = req.Comprador;
        var totales = req.Totales;

        var encabezado = new XElement("Encabezado",
            new XElement("Version", req.Version),
            
            // IdDoc
            new XElement("IdDoc",
                new XElement("TipoeCF", (int)req.TipoeCF),
                new XElement("eNCF", req.eNCF),
                new XElement("TipoIngresos", TipoIngresosFormatter.ToString(req.TipoIngresos)),
                new XElement("TipoPago", (int)req.TipoPago),
                CrearOpcional("FechaLimitePago", req.FechaVencimiento?.ToString("dd-MM-yyyy")),
                CrearOpcional("TerminoPago", req.PlazoCredito)
            ),

            // Emisor
            new XElement("Emisor",
                new XElement("RNCEmisor", emisor.RNC),
                new XElement("RazonSocialEmisor", emisor.RazonSocial),
                CrearOpcional("NombreComercial", emisor.NombreComercial),
                CrearOpcional("Sucursal", emisor.Sucursal),
                new XElement("DireccionEmisor", emisor.Direccion),
                CrearOpcional("Municipio", emisor.CodigoMunicipio),
                CrearOpcional("Provincia", emisor.CodigoProvincia),
                CrearOpcional("TelefonoEmisor", emisor.Telefono),
                CrearOpcional("CorreoEmisor", emisor.Email),
                CrearOpcional("WebSite", emisor.SitioWeb),
                new XElement("FechaEmision", req.FechaFactura.ToXmlString())
            ),

            // Comprador
            new XElement("Comprador",
                CrearOpcional("RNCComprador", comprador.RNC),
                CrearOpcional("IdentificadorExtranjero", comprador.Identificacion),
                new XElement("RazonSocialComprador", comprador.RazonSocial),
                CrearOpcional("DireccionComprador", comprador.Direccion),
                CrearOpcional("MunicipioComprador", comprador.CodigoMunicipio),
                CrearOpcional("ProvinciaComprador", comprador.CodigoProvincia),
                CrearOpcional("TelefonoAdicional", comprador.Telefono),
                CrearOpcional("CorreoComprador", comprador.Email)
            ),

            // Totales
            new XElement("Totales",
                CrearOpcional("MontoGravadoTotal", FormatearDecimal(totales.MontoGravadoTotal)),
                CrearOpcional("MontoGravadoI1", FormatearDecimal(totales.MontoGravadoI1)),
                CrearOpcional("MontoGravadoI2", FormatearDecimal(totales.MontoGravadoI2)),
                CrearOpcional("MontoGravadoI3", FormatearDecimal(totales.MontoGravadoI3)),
                CrearOpcional("MontoExento", FormatearDecimal(totales.MontoExento)),
                CrearOpcional("TotalITBIS", FormatearDecimal(totales.TotalITBIS)),
                CrearOpcional("TotalITBIS1", FormatearDecimal(totales.TotalITBIS1)),
                CrearOpcional("TotalITBIS2", FormatearDecimal(totales.TotalITBIS2)),
                CrearOpcional("TotalITBIS3", FormatearDecimal(totales.TotalITBIS3)),
                CrearOpcional("MontoImpuestoAdicional", totales.TotalISC > 0 ? FormatearDecimal(totales.TotalISC) : null),
                new XElement("MontoTotal", FormatearDecimal(totales.Total))
            )
        );

        return encabezado;
    }

    private XElement CrearDetallesItems(ElectronicInvoiceRequest req)
    {
        var itemsElement = new XElement("DetallesItems");

        for (int i = 0; i < req.Items.Count; i++)
        {
            var item = req.Items[i];
            var itemEl = new XElement("Item",
                new XElement("NumeroLinea", i + 1),
                new XElement("IndicadorFacturacion", (int)item.IndicadorFacturacion),
                new XElement("NombreItem", item.Descripcion),
                CrearOpcional("DescripcionItem", item.Descripcion),
                new XElement("IndicadorBienoServicio", 1), // 1 = Bien, 2 = Servicio
                new XElement("CantidadItem", item.Cantidad.ToString("F2", Inv)),
                new XElement("UnidadMedida", (int)item.UnidadMedida),
                new XElement("PrecioUnitarioItem", item.PrecioUnitario.ToString("F4", Inv)),
                CrearOpcional("DescuentoMonto", item.Descuento > 0 ? FormatearDecimal(item.Descuento) : null),
                CrearOpcional("RecargoMonto", item.Recargo > 0 ? FormatearDecimal(item.Recargo) : null),
                new XElement("MontoItem", FormatearDecimal(item.Subtotal))
            );

            itemsElement.Add(itemEl);
        }

        return itemsElement;
    }

    public string SerializeAnulacion(AnulacionRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("ANECF",
                new XElement("RNCEmisor", request.RNCEmisor),
                new XElement("TipoeCF", (int)request.TipoeCF),
                new XElement("eNCFDesde", request.eNCFDesde),
                new XElement("eNCFHasta", request.eNCFHasta),
                new XElement("CantidadSecuencias", request.CantidadSecuencias),
                new XElement("CodigoMotivoAnulacion", request.CodigoMotivoAnulacion),
                new XElement("Motivo", request.Motivo),
                new XElement("FechaHoraAnulacion", DateTime.UtcNow.ToString("dd-MM-yyyy HH:mm:ss"))
            )
        );

        return doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement? CrearOpcional(string nombre, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        return new XElement(nombre, valor);
    }

    private static string FormatearDecimal(decimal valor) => valor.ToString("F2", Inv);
}
