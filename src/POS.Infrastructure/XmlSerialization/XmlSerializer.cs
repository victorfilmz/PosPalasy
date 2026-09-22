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

    /// <summary>Namespace XML-DSig del bloque de firma que reserva el XSD.</summary>
    private static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";

    public string Serialize(ElectronicInvoiceRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        // e-CF 34 (Nota de Crédito): el XSD exige InformacionReferencia tras DetallesItems y antes
        // de FechaHoraFirma. Es una referencia obligatoria al comprobante modificado: sin ella la
        // DGII no puede vincular la corrección con su original.
        var esNotaCredito = request.TipoeCF == TipoeCFType.NotaCredito
            && !string.IsNullOrWhiteSpace(request.NCFModificado);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("ECF",
                CrearEncabezado(request),
                CrearDetallesItems(request),
                esNotaCredito ? CrearInformacionReferencia(request) : null,
                // FechaHoraFirma es obligatoria en el XSD (patrón dd-MM-yyyy HH:mm:ss).
                new XElement("FechaHoraFirma", DateTime.UtcNow.ToString("dd-MM-yyyy HH:mm:ss", Inv)),
                // El XSD exige exactamente un elemento tras FechaHoraFirma: el espacio reservado
                // para la firma XML-DSig (ds:Signature). Se emite VACÍO desde la construcción para
                // que el documento sea estructuralmente completo; la firma (sub-fase 5.1) lo llena.
                new XElement(Ds + "Signature")
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
            
            // IdDoc — la secuencia del XSD 34 exige IndicadorNotaCredito INMEDIATAMENTE después de
            // eNCF (antes de TipoIngresos/TipoPago): la posición es parte del contrato del esquema.
            new XElement("IdDoc",
                new XElement("TipoeCF", (int)req.TipoeCF),
                new XElement("eNCF", req.eNCF),
                // Nota de crédito (e-CF 34): IndicadorNotaCredito es OBLIGATORIO en el IdDoc.
                // 0 = emitida dentro de los 30 días del comprobante modificado; 1 = después.
                req.TipoeCF == TipoeCFType.NotaCredito && req.IndicadorNotaCredito.HasValue
                    ? new XElement("IndicadorNotaCredito", req.IndicadorNotaCredito.Value)
                    : null,
                // El tipo 31 (crédito fiscal) exige la fecha de vencimiento de la secuencia del eNCF:
                // la normativa le da 6 meses de vigencia desde la emisión.
                req.TipoeCF == TipoeCFType.FacturaCreditoFiscal
                    ? new XElement("FechaVencimientoSecuencia", req.FechaFactura.Value.AddMonths(6).ToString("dd-MM-yyyy", Inv))
                    : null,
                new XElement("TipoIngresos", TipoIngresosFormatter.ToString(req.TipoIngresos)),
                new XElement("TipoPago", (int)req.TipoPago),
                CrearOpcional("FechaLimitePago", req.FechaVencimiento?.ToString("dd-MM-yyyy", Inv)),
                CrearOpcional("TerminoPago", req.PlazoCredito)
            ),

            // Emisor
            new XElement("Emisor",
                new XElement("RNCEmisor", emisor.RNC),
                new XElement("RazonSocialEmisor", emisor.RazonSocial),
                CrearOpcional("NombreComercial", emisor.NombreComercial),
                CrearOpcional("Sucursal", emisor.Sucursal),
                new XElement("DireccionEmisor", emisor.Direccion),
                CrearOpcional("Municipio", NormalizarCodigoTerritorial(emisor.CodigoMunicipio)),
                CrearOpcional("Provincia", NormalizarCodigoTerritorial(emisor.CodigoProvincia)),
                // El XSD exige el teléfono dentro de TablaTelefonoEmisor (1..3 repeticiones).
                string.IsNullOrWhiteSpace(emisor.Telefono)
                    ? null
                    : new XElement("TablaTelefonoEmisor",
                        new XElement("TelefonoEmisor", emisor.Telefono)),
                CrearOpcional("CorreoEmisor", emisor.Email),
                CrearOpcional("WebSite", emisor.SitioWeb),
                new XElement("FechaEmision", req.FechaFactura.ToXmlString())
            ),

            // Comprador: el elemento es obligatorio pero TODOS sus campos son opcionales en el XSD;
            // solo se emiten los que tengan valor (un elemento vacío viola los patrones).
            new XElement("Comprador",
                CrearOpcional("RNCComprador", comprador.RNC),
                CrearOpcional("IdentificadorExtranjero", comprador.Identificacion),
                CrearOpcional("RazonSocialComprador", comprador.RazonSocial),
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
                // Orden exigido por la secuencia del XSD: IndicadorBienoServicio ANTES de DescripcionItem.
                new XElement("IndicadorBienoServicio", 1), // 1 = Bien, 2 = Servicio
                CrearOpcional("DescripcionItem", item.Descripcion),
                new XElement("CantidadItem", item.Cantidad.ToString("F2", Inv)),
                new XElement("UnidadMedida", (int)item.UnidadMedida),
                new XElement("PrecioUnitarioItem", item.PrecioUnitario.ToString("F4", Inv)),
                CrearOpcional("DescuentoMonto", item.Descuento > 0 ? FormatearDecimal(item.Descuento) : null),
                CrearOpcional("RecargoMonto", item.Recargo > 0 ? FormatearDecimal(item.Recargo) : null),
                // MontoItem es el total de la línea CON sus impuestos: única cifra coherente con
                // MontoTotal del comprobante (suma de líneas con impuestos).
                new XElement("MontoItem", FormatearDecimal(item.Total))
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

    /// <summary>
    /// InformacionReferencia del e-CF 34: referencia al comprobante modificado. Orden exigido por
    /// la secuencia del XSD: NCFModificado → FechaNCFModificado → CodigoModificacion →
    /// RazonModificacion. NCFModificado admite 11..19 caracteres (e-NCF de 13 o NCF antiguo).
    /// </summary>
    private static XElement CrearInformacionReferencia(ElectronicInvoiceRequest req)
    {
        return new XElement("InformacionReferencia",
            new XElement("NCFModificado", req.NCFModificado),
            CrearOpcional("FechaNCFModificado", req.FechaNCFModificado),
            new XElement("CodigoModificacion", (req.CodigoModificacion ?? 3).ToString(Inv)),
            CrearOpcional("RazonModificacion", req.MotivoModificacion)
        );
    }

    private static XElement? CrearOpcional(string nombre, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        return new XElement(nombre, valor);
    }

    private static string FormatearDecimal(decimal valor) => valor.ToString("F2", Inv);

    /// <summary>
    /// Normaliza códigos territoriales al formato de 6 dígitos del XSD (ProvinciaMunicipioType).
    /// El sistema almacena la provincia como 2 dígitos ("01"); el XSD exige "010000". Los códigos
    /// de municipio ya nacen de 6 dígitos y pasan intactos.
    /// </summary>
    private static string? NormalizarCodigoTerritorial(string? codigo)
    {
        if (string.IsNullOrWhiteSpace(codigo)) return null;

        return codigo.Length == 2 ? codigo + "0000" : codigo;
    }
}
