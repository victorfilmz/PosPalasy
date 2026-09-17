using System;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;
using Xunit;

namespace POS.Domain.Types.Tests;

public class DomainEntitiesTests
{
    [Fact]
    public void Venta_CalcularTotales_AccuratelySumsItems()
    {
        var venta = new Venta
        {
            NumeroFacturaInterna = "FAC-001",
            TipoPago = TipoPago.Contado,
            MetodoPago = MetodoPago.Efectivo
        };

        // Item 1: 2 unidades a 100.00 DOP, ITBIS 18% (General) -> Subtotal 200, ITBIS 36, Total 236
        var item1 = new VentaItem
        {
            Descripcion = "Refresco",
            Cantidad = 2,
            PrecioUnitario = 100.00m,
            IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18
        };

        // Item 2: 1 unidad a 50.00 DOP con 10.00 de descuento, ITBIS 16% (Reducido) -> Subtotal 40, ITBIS 6.40, Total 46.40
        var item2 = new VentaItem
        {
            Descripcion = "Yogurt",
            Cantidad = 1,
            PrecioUnitario = 50.00m,
            Descuento = 10.00m,
            IndicadorFacturacion = IndicadorFacturacionType.ITBIS2_16
        };

        // Item 3: 5 unidades a 20.00 DOP, Exento de ITBIS -> Subtotal 100, ITBIS 0, Total 100
        var item3 = new VentaItem
        {
            Descripcion = "Pan de agua",
            Cantidad = 5,
            PrecioUnitario = 20.00m,
            IndicadorFacturacion = IndicadorFacturacionType.Exento
        };

        venta.AddItem(item1);
        venta.AddItem(item2);
        venta.AddItem(item3);

        Assert.Equal(340.00m, venta.Subtotal);
        Assert.Equal(10.00m, venta.TotalDescuento);
        Assert.Equal(42.40m, venta.TotalITBIS);
        Assert.Equal(0.00m, venta.TotalISC);
        Assert.Equal(382.40m, venta.Total);
    }

    [Fact]
    public void ElectronicInvoice_Properties_InitializedProperly()
    {
        var invoice = new ElectronicInvoice
        {
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCF = "E320000000001",
            RNCEmisor = "13100000001",
            RazonSocialEmisor = "POS Palasy SRL",
            RNCComprador = "101000001",
            RazonSocialComprador = "Consumidor Final",
            FechaEmision = "16-09-2026",
            MontoTotal = 382.40m,
            TotalITBIS = 42.40m,
            XMLContent = "<e-CF>...</e-CF>",
            XMLHash = "Ab12Cd",
            Estado = EstadoFacturaElectronica.EnProceso
        };

        Assert.Equal("E320000000001", invoice.eNCF);
        Assert.Equal("1.0", invoice.Version);
        Assert.Equal(EstadoFacturaElectronica.EnProceso, invoice.Estado);
        Assert.Equal("Ab12Cd", invoice.XMLHash);
    }

    [Fact]
    public void Anulacion_Initialization_IsValid()
    {
        var anulacion = new Anulacion
        {
            RNCEmisor = "13100000001",
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCFDesde = "E320000000001",
            eNCFHasta = "E320000000005",
            CantidadSecuencias = 5,
            CodigoMotivoAnulacion = 1,
            Motivo = "Deterioro de comprobante"
        };

        Assert.Equal(5, anulacion.CantidadSecuencias);
        Assert.False(anulacion.Aprobada);
    }
}
