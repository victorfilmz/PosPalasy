using System;
using System.Collections.Generic;
using POS.Application.DTOs;
using POS.Application.Services;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;
using Xunit;

namespace POS.Domain.Types.Tests;

public class PosWorkflowTests
{
    private readonly TaxCalculator _taxCalculator = new();

    [Fact]
    public void PosSale_CalculatesCorrectTaxesAndTotals_ForMixedCart()
    {
        // Arrange: Carrito con ITBIS 18%, ITBIS 16% y Exento
        var items = new List<InvoiceItemRequest>
        {
            new()
            {
                Indice = 1,
                Descripcion = "Refresco Cola 500ml",
                Cantidad = 2,
                PrecioUnitario = 50.00m, // Subtotal = 100, ITBIS 18% = 18.00
                IndicadorFacturacion = IndicadorFacturacionType.ITBIS1_18
            },
            new()
            {
                Indice = 2,
                Descripcion = "Leche Evaporada",
                Cantidad = 1,
                PrecioUnitario = 100.00m, // Subtotal = 100, ITBIS 16% = 16.00
                IndicadorFacturacion = IndicadorFacturacionType.ITBIS2_16
            },
            new()
            {
                Indice = 3,
                Descripcion = "Arroz Blanco 10 Lbs",
                Cantidad = 1,
                PrecioUnitario = 350.00m, // Subtotal = 350, Exento
                IndicadorFacturacion = IndicadorFacturacionType.Exento
            }
        };

        // Act
        var totales = _taxCalculator.CalcularTotales(items);

        // Assert
        Assert.Equal(100.00m, totales.MontoGravadoI1);
        Assert.Equal(18.00m, totales.TotalITBIS1);
        Assert.Equal(100.00m, totales.MontoGravadoI2);
        Assert.Equal(16.00m, totales.TotalITBIS2);
        Assert.Equal(350.00m, totales.MontoExento);
        Assert.Equal(34.00m, totales.TotalITBIS);
        // Total = 100 + 18 + 100 + 16 + 350 = 584.00
        Assert.Equal(584.00m, totales.Total);
    }

    [Fact]
    public void PosSale_CalculatesCorrectChange_WhenCashReceivedIsGreater()
    {
        // Arrange
        var total = 584.00m;
        var recibido = 1000.00m;

        // Act
        var cambio = Math.Max(0, recibido - total);

        // Assert
        Assert.Equal(416.00m, cambio);
    }

    [Fact]
    public void PosSale_GeneratesValidENCFSequence_ForECF32AndECF31()
    {
        // Test secuencia E32
        var ultimoEncfE32 = "E320000000005";
        var secPart32 = ultimoEncfE32[3..];
        var num32 = long.Parse(secPart32);
        var nuevoE32 = $"E32{num32 + 1:D10}";
        Assert.Equal("E320000000006", nuevoE32);
        Assert.True(eNCF.EsValido(nuevoE32));

        // Test secuencia E31
        var ultimoEncfE31 = "E310000000042";
        var secPart31 = ultimoEncfE31[3..];
        var num31 = long.Parse(secPart31);
        var nuevoE31 = $"E31{num31 + 1:D10}";
        Assert.Equal("E310000000043", nuevoE31);
        Assert.True(eNCF.EsValido(nuevoE31));
    }

    [Fact]
    public void DgiiQrCode_GeneratesNorma012020Url_WithExactParameters()
    {
        // Arrange
        var rncEmisor = "13100000001";
        var rncComprador = "101000001";
        var encf = "E320000000001";
        var fecha = "16-09-2026";
        var monto = 1180.00m;
        var itbis = 180.00m;
        var hash = "aB3dE=";

        // Act
        var qrUrl = DgiiQrHelper.GenerarUrlConsultaDgii(rncEmisor, rncComprador, encf, fecha, monto, itbis, hash);

        // Assert
        Assert.StartsWith("https://fc.dgii.gov.do/ecf/consultatimbre", qrUrl);
        Assert.Contains("RncEmisor=13100000001", qrUrl);
        Assert.Contains("RncComprador=101000001", qrUrl);
        Assert.Contains("ENCF=E320000000001", qrUrl);
        Assert.Contains("FechaEmision=16-09-2026", qrUrl);
        Assert.Contains("MontoTotal=1180.00", qrUrl);
        Assert.Contains("TotalITBIS=180.00", qrUrl);
        Assert.Contains("CodigoSeguridad=aB3dE%3D", qrUrl);
    }

    [Fact]
    public void Anulacion_ValidatesAndGeneratesANECF_Structure()
    {
        // Arrange
        var request = new AnulacionRequest
        {
            RNCEmisor = "13100000001",
            TipoeCF = TipoeCFType.FacturaConsumo,
            eNCFDesde = "E320000000001",
            eNCFHasta = "E320000000001",
            CantidadSecuencias = 1,
            CodigoMotivoAnulacion = 5,
            Motivo = "Devolución total del cliente por cambio de producto"
        };

        // Act
        var valResult = POS.Application.Validators.AnulacionValidator.Validar(request);
        var xmlSerializer = new POS.Infrastructure.XmlSerialization.XmlSerializer();
        var xml = xmlSerializer.SerializeAnulacion(request);

        // Assert
        Assert.True(valResult.EsValido);
        Assert.Contains("<ANECF>", xml);
        Assert.Contains("<RNCEmisor>13100000001</RNCEmisor>", xml);
        Assert.Contains("<eNCFDesde>E320000000001</eNCFDesde>", xml);
        Assert.Contains("<eNCFHasta>E320000000001</eNCFHasta>", xml);
        Assert.Contains("<CantidadSecuencias>1</CantidadSecuencias>", xml);
        Assert.Contains("<CodigoMotivoAnulacion>5</CodigoMotivoAnulacion>", xml);
    }

    [Fact]
    public void CajaTurno_CalculatesEfectivoEsperado_AndArqueoDiferencia()
    {
        // Arrange: Turno con fondo inicial de 2,000, ventas en efectivo 5,500, entrada 500 y salida 200
        var turno = new CajaTurno
        {
            Cajero = "Juan Perez",
            MontoInicial = 2000.00m,
            VentasEfectivo = 5500.00m,
            VentasTarjeta = 3200.00m,
            VentasTransferencia = 1500.00m,
            TotalVentas = 10200.00m,
            CantidadTransacciones = 8,
            TotalEntradasEfectivo = 500.00m,
            TotalSalidasEfectivo = 200.00m
        };

        // Act & Assert 1: Efectivo Esperado = 2000 + 5500 + 500 - 200 = 7800.00
        Assert.Equal(7800.00m, turno.EfectivoEsperado);

        // Act & Assert 2: Arqueo exacto (cuadrado)
        decimal conteoFisicoExacto = 7800.00m;
        decimal diffCuadrado = conteoFisicoExacto - turno.EfectivoEsperado;
        Assert.Equal(0.00m, diffCuadrado);

        // Act & Assert 3: Arqueo con sobrante (+150)
        decimal conteoSobrante = 7950.00m;
        decimal diffSobrante = conteoSobrante - turno.EfectivoEsperado;
        Assert.Equal(150.00m, diffSobrante);

        // Act & Assert 4: Arqueo con faltante (-100)
        decimal conteoFaltante = 7700.00m;
        decimal diffFaltante = conteoFaltante - turno.EfectivoEsperado;
        Assert.Equal(-100.00m, diffFaltante);
    }

    [Fact]
    public void ReporteFiscalService_GeneratesValid607AndItbisResumen()
    {
        // Arrange
        var service = new ReporteFiscalService();
        var facturas = new List<ElectronicInvoice>
        {
            new()
            {
                eNCF = "E310000000001",
                RNCEmisor = "13100000001",
                RNCComprador = "101000001", // RNC 9 dígitos -> Tipo 1
                RazonSocialComprador = "Distribuidora Nacional SRL",
                FechaEmision = "16-09-2026",
                TipoIngresos = TipoIngresosType.IngresosOperaciones,
                TipoPago = TipoPago.Contado,
                MontoGravadoTotal = 1000.00m,
                MontoGravadoI1 = 1000.00m,
                TotalITBIS = 180.00m,
                TotalITBIS1 = 180.00m,
                MontoTotal = 1180.00m,
                Estado = EstadoFacturaElectronica.Aceptado
            },
            new()
            {
                eNCF = "E320000000002",
                RNCEmisor = "13100000001",
                RNCComprador = "00112345678", // Cédula 11 dígitos -> Tipo 2
                RazonSocialComprador = "Pedro Martinez",
                FechaEmision = "16-09-2026",
                TipoIngresos = TipoIngresosType.IngresosOperaciones,
                TipoPago = TipoPago.Credito,
                MontoGravadoTotal = 500.00m,
                MontoGravadoI2 = 500.00m, // 16%
                TotalITBIS = 80.00m,
                TotalITBIS2 = 80.00m,
                MontoTotal = 580.00m,
                Estado = EstadoFacturaElectronica.EnProceso
            },
            new()
            {
                eNCF = "E320000000003",
                RNCEmisor = "13100000001",
                FechaEmision = "16-09-2026",
                MontoTotal = 500.00m,
                Estado = EstadoFacturaElectronica.Anulado // Excluida del 607
            }
        };

        // Act 1: Registros 607
        var registros = service.GenerarRegistros607(facturas);

        // Assert 1: Solo 2 facturas válidas procesadas
        Assert.Equal(2, registros.Count);
        Assert.Equal("101000001", registros[0].RNC_Cedula);
        Assert.Equal(1, registros[0].TipoIdentificacion);
        Assert.Equal("20260916", registros[0].FechaComprobante);
        Assert.Equal(1000.00m, registros[0].MontoFacturado);
        Assert.Equal(180.00m, registros[0].ITBISFacturado);

        Assert.Equal("00112345678", registros[1].RNC_Cedula);
        Assert.Equal(2, registros[1].TipoIdentificacion);
        Assert.Equal(580.00m, registros[1].VentaCredito);

        // Act 2: Formato TXT
        var txt = service.GenerarArchivo607Txt("13100000001", 2026, 9, registros);
        Assert.StartsWith("607|13100000001|202609|2", txt);

        // Act 3: Resumen ITBIS IT-1
        var itbis = service.GenerarResumenItbis(2026, 9, facturas);
        Assert.Equal(1000.00m, itbis.BaseGravada18);
        Assert.Equal(180.00m, itbis.ITBIS18);
        Assert.Equal(500.00m, itbis.BaseGravada16);
        Assert.Equal(80.00m, itbis.ITBIS16);
        Assert.Equal(260.00m, itbis.TotalITBISDevengado);
    }
}
