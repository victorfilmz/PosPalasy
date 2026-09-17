using System;
using System.Collections.Generic;
using POS.Application.DTOs;
using POS.Application.Services;
using POS.Application.Validators;
using POS.Domain.Types;
using Xunit;

namespace POS.Domain.Types.Tests;

public class TaxCalculatorTests
{
    private readonly TaxCalculator _calculator = new();

    [Fact]
    public void CalcularLinea_ITBIS18_CalculatesCorrectly()
    {
        var item = new InvoiceItemRequest
        {
            Descripcion = "Articulo Gravado 18%",
            Cantidad = 3,
            PrecioUnitario = 100.00m,
            Descuento = 20.00m,
            IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18
        };

        _calculator.CalcularLinea(item);

        // Subtotal = (3 * 100) - 20 = 280
        Assert.Equal(280.00m, item.Subtotal);
        // ITBIS 18% de 280 = 50.40
        Assert.Equal(50.40m, item.ITBIS);
        // Total = 280 + 50.40 = 330.40
        Assert.Equal(330.40m, item.Total);
    }

    [Fact]
    public void CalcularTotales_MixedItems_ProducesExactBreakdowns()
    {
        var items = new List<InvoiceItemRequest>
        {
            new() { Descripcion = "Item 18%", Cantidad = 1, PrecioUnitario = 1000m, IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18 },
            new() { Descripcion = "Item 16%", Cantidad = 2, PrecioUnitario = 500m, IndicadorFacturacion = IndicadorFacturacionType.ITBIS2_16 },
            new() { Descripcion = "Item Exento", Cantidad = 1, PrecioUnitario = 200m, IndicadorFacturacion = IndicadorFacturacionType.Exento }
        };

        var totales = _calculator.CalcularTotales(items);

        Assert.Equal(2200m, totales.SubTotal);
        Assert.Equal(1000m, totales.MontoGravadoI1);
        Assert.Equal(1000m, totales.MontoGravadoI2);
        Assert.Equal(200m, totales.MontoExento);

        Assert.Equal(180m, totales.TotalITBIS1);
        Assert.Equal(160m, totales.TotalITBIS2);
        Assert.Equal(340m, totales.TotalITBIS);

        Assert.Equal(2540m, totales.Total);
    }
}

public class HashGeneratorTests
{
    private readonly HashGenerator _generator = new();

    [Fact]
    public void Generate_ReturnsSixCharacterBase64()
    {
        var xml = "<ECF><Encabezado><Version>1.0</Version></Encabezado></ECF>";
        var hash = _generator.Generate(xml);

        Assert.NotNull(hash);
        Assert.Equal(6, hash.Length);

        // Mismo contenido genera mismo hash determinista
        var hash2 = _generator.Generate(xml);
        Assert.Equal(hash, hash2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Generate_EmptyXml_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => _generator.Generate(input));
    }
}

public class ValidatorTests
{
    [Fact]
    public void EmitirFacturaValidator_ValidRequest_Passes()
    {
        var request = new ElectronicInvoiceRequest
        {
            eNCF = "E320000000001",
            Emisor = new EmisorRequest
            {
                RNC = "13100000001",
                RazonSocial = "Empresa Dominicana SRL"
            },
            Comprador = new CompradorRequest
            {
                RNC = "101000001"
            },
            Items = new List<InvoiceItemRequest>
            {
                new() { Descripcion = "Producto A", Cantidad = 1, PrecioUnitario = 100m }
            }
        };

        var result = EmitirFacturaValidator.Validar(request);
        Assert.True(result.EsValido);
        Assert.Empty(result.Errores);
    }

    [Fact]
    public void EmitirFacturaValidator_InvalidRncAndEmptyItems_Fails()
    {
        var request = new ElectronicInvoiceRequest
        {
            eNCF = "E32001", // no tiene 13 caracteres
            Emisor = new EmisorRequest
            {
                RNC = "123", // RNC inválido
                RazonSocial = ""
            },
            Items = new List<InvoiceItemRequest>() // Sin ítems
        };

        var result = EmitirFacturaValidator.Validar(request);
        Assert.False(result.EsValido);
        Assert.Contains(result.Errores, e => e.Contains("RNC del emisor"));
        Assert.Contains(result.Errores, e => e.Contains("eNCF"));
        Assert.Contains(result.Errores, e => e.Contains("al menos un ítem"));
    }
}
