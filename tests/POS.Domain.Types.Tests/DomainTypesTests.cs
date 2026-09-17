using System;
using POS.Domain.Types;
using Xunit;

namespace POS.Domain.Types.Tests;

public class RNCTests
{
    [Theory]
    [InlineData("101000001", true, false)] // 9 dígitos (persona física)
    [InlineData("13100000001", false, true)] // 11 dígitos (persona moral / empresa)
    public void Constructor_ValidRNC_Succeeds(string input, bool expectedFisica, bool expectedEmpresa)
    {
        var rnc = new RNC(input);

        Assert.Equal(input, rnc.Value);
        Assert.Equal(expectedFisica, rnc.IsPersonaFisica);
        Assert.Equal(expectedEmpresa, rnc.IsEmpresa);
        Assert.Equal(expectedFisica, rnc.EsPersonaFisica);
        Assert.Equal(expectedEmpresa, rnc.EsEmpresa);
        Assert.True(RNC.IsValid(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345678")]     // 8 dígitos
    [InlineData("1234567890")]   // 10 dígitos
    [InlineData("123456789012")] // 12 dígitos
    [InlineData("10100000A")]   // Letras
    public void Constructor_InvalidRNC_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => new RNC(input));
        Assert.False(RNC.IsValid(input));
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var rnc1 = new RNC("101000001");
        var rnc2 = new RNC("101000001");
        var rnc3 = new RNC("13100000001");

        Assert.True(rnc1 == rnc2);
        Assert.False(rnc1 != rnc2);
        Assert.True(rnc1.Equals(rnc2));
        Assert.Equal(rnc1.GetHashCode(), rnc2.GetHashCode());

        Assert.False(rnc1 == rnc3);
        Assert.True(rnc1 != rnc3);
    }
}

public class eNCFTests
{
    [Fact]
    public void Constructor_Valid13Chars_SetsSerieAndSecuencial()
    {
        var encf = new eNCF("E310000000001");

        Assert.Equal("E310000000001", encf.Value);
        Assert.Equal("E", encf.Serie);
        Assert.Equal("310000000001", encf.Secuencial);
        Assert.True(eNCF.IsValid("E310000000001"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("E31000000001")]   // 12 chars
    [InlineData("E3100000000001")] // 14 chars
    [InlineData("E3100000000-1")] // Caracter inválido
    public void Constructor_InvalidENCF_ThrowsArgumentException(string input)
    {
        Assert.Throws<ArgumentException>(() => new eNCF(input));
        Assert.False(eNCF.IsValid(input));
    }

    [Fact]
    public void Generate_CreatesValidENCF()
    {
        var generated = eNCF.Generate("E", 125);

        Assert.Equal("E000000000125", generated.Value);
        Assert.Equal("E", generated.Serie);
    }

    [Fact]
    public void Incrementar_IncrementsSequence()
    {
        var next = eNCFSecuencia.Incrementar("E000000000001");
        Assert.Equal("E000000000002", next);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var a = new eNCF("E310000000001");
        var b = new eNCF("e310000000001"); // normalizado a mayúsculas
        var c = new eNCF("E320000000001");

        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a != c);
    }
}

public class FechaDominicanaTests
{
    [Fact]
    public void Parse_ValidFormat_ReturnsCorrectDate()
    {
        var fecha = FechaDominicana.Parse("16-09-2026");

        Assert.Equal(new DateOnly(2026, 9, 16), fecha.Value);
        Assert.Equal("16-09-2026", fecha.ToXmlString());
        Assert.Equal("16-09-2026", fecha.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026-09-16")] // Formato ISO, no dominicano
    [InlineData("32-01-2026")] // Día fuera de rango
    [InlineData("16/09/2026")] // Separador incorrecto
    public void Parse_InvalidFormat_ThrowsException(string input)
    {
        Assert.ThrowsAny<Exception>(() => FechaDominicana.Parse(input));
    }

    [Fact]
    public void Equality_SameDate_AreEqual()
    {
        var f1 = new FechaDominicana(new DateOnly(2026, 9, 16));
        var f2 = FechaDominicana.Parse("16-09-2026");
        var f3 = FechaDominicana.Parse("17-09-2026");

        Assert.True(f1 == f2);
        Assert.False(f1 != f2);
        Assert.True(f1 != f3);
    }
}

public class EnumsAndTypesTests
{
    [Fact]
    public void TipoeCFType_ContainsExpectedCodes()
    {
        Assert.Equal(31, (int)TipoeCFType.FacturaCreditoFiscal);
        Assert.Equal(32, (int)TipoeCFType.FacturaConsumo);
        Assert.Equal(33, (int)TipoeCFType.NotaDebito);
        Assert.Equal(34, (int)TipoeCFType.NotaCredito);
        Assert.Equal(41, (int)TipoeCFType.Compras);
        Assert.Equal(47, (int)TipoeCFType.PagosAlExterior);
    }

    [Fact]
    public void TipoIngresosFormatter_RoundTripsCorrectly()
    {
        var str = TipoIngresosFormatter.ToString(TipoIngresosType.IngresosOperaciones);
        Assert.Equal("01", str);

        var parsed = TipoIngresosFormatter.FromString("01");
        Assert.Equal(TipoIngresosType.IngresosOperaciones, parsed);
    }

    [Fact]
    public void TipoMonedaFormatter_SupportsDOP_USD_EUR_AndCNY()
    {
        Assert.Equal("DOP", TipoMonedaFormatter.ToString(TipoMonedaType.DOP));
        Assert.Equal("USD", TipoMonedaFormatter.ToString(TipoMonedaType.USD));
        Assert.Equal("EUR", TipoMonedaFormatter.ToString(TipoMonedaType.EUR));
        Assert.Equal("CHY", TipoMonedaFormatter.ToString(TipoMonedaType.CNY));

        Assert.Equal(TipoMonedaType.DOP, TipoMonedaFormatter.FromString("DOP"));
        Assert.Equal(TipoMonedaType.USD, TipoMonedaFormatter.FromString("USD"));
        Assert.Equal(TipoMonedaType.CNY, TipoMonedaFormatter.FromString("CHY"));
    }

    [Theory]
    [InlineData(IndicadorFacturacionType.ITBIS1_18, 18.0)]
    [InlineData(IndicadorFacturacionType.ITBIS2_16, 16.0)]
    [InlineData(IndicadorFacturacionType.ITBIS3_0, 0.0)]
    [InlineData(IndicadorFacturacionType.Exento, 0.0)]
    [InlineData(IndicadorFacturacionType.NoFacturable18, 18.0)]
    public void IndicadorFacturacionHelper_CalculatesCorrectRates(IndicadorFacturacionType indicador, decimal expectedRate)
    {
        var tasa = IndicadorFacturacionHelper.ObtenerTasaITBIS(indicador);
        Assert.Equal(expectedRate, tasa);
    }

    [Fact]
    public void UnidadMedida_Covers62Codes()
    {
        for (int i = 1; i <= 62; i++)
        {
            var unidad = UnidadMedidaFormatter.FromCode(i);
            var desc = UnidadMedidaFormatter.GetDescripcion(unidad);
            var abrev = UnidadMedidaFormatter.GetAbreviatura(unidad);

            Assert.False(string.IsNullOrWhiteSpace(desc));
            Assert.False(string.IsNullOrWhiteSpace(abrev));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => UnidadMedidaFormatter.FromCode(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnidadMedidaFormatter.FromCode(63));
    }

    [Fact]
    public void ISC_Covers39Codes()
    {
        for (int i = 1; i <= 39; i++)
        {
            var codigoStr = i.ToString("000");
            Assert.True(ISC.EsValido(codigoStr));

            var tipo = ISC.FromCodigoString(codigoStr);
            var detalle = ISC.ObtenerDetalle(tipo);

            Assert.Equal(codigoStr, detalle.Codigo);
            Assert.False(string.IsNullOrWhiteSpace(detalle.Descripcion));
        }

        Assert.False(ISC.EsValido("000"));
        Assert.False(ISC.EsValido("040"));
    }

    [Fact]
    public void ProvinciaMunicipio_ValidCode_ParsesCorrectly()
    {
        var pmProvincia = new ProvinciaMunicipio("010000");
        Assert.Equal("01", pmProvincia.CodigoProvincia);
        Assert.True(pmProvincia.EsProvincia);
        Assert.False(pmProvincia.EsMunicipio);
        Assert.Equal("Distrito Nacional", ProvinciaMunicipioHelper.ObtenerNombreProvincia(pmProvincia));

        var pmMunicipio = new ProvinciaMunicipio("010100");
        Assert.Equal("01", pmMunicipio.CodigoProvincia);
        Assert.False(pmMunicipio.EsProvincia);
        Assert.True(pmMunicipio.EsMunicipio);

        Assert.True(new ProvinciaMunicipio("010000") == pmProvincia);
        Assert.False(pmMunicipio == pmProvincia);
    }

    [Fact]
    public void VersionType_DefaultsToOneZero()
    {
        var v = new VersionType();
        Assert.Equal(1.0m, v.Value);
        Assert.Equal("1.0", v.ToString());

        var rfc = new VersionRfcType();
        Assert.Equal(1.0m, rfc.Value);
        Assert.Equal("1.0", rfc.ToString());
    }
}
