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
        Assert.Contains("<RncEmisor>13100000001</RncEmisor>", xml);
        Assert.Contains("<SecuenciaeNCFDesde>E320000000001</SecuenciaeNCFDesde>", xml);
        Assert.Contains("<SecuenciaeNCFHasta>E320000000001</SecuenciaeNCFHasta>", xml);
        Assert.Contains("<CantidadeNCFAnulados>1</CantidadeNCFAnulados>", xml);
        Assert.Contains("<TipoeCF>32</TipoeCF>", xml);
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

    [Fact]
    public void Reporte606_ConstruyeRegistrosDelKardex_ConProveedorYNCF()
    {
        var service = new ReporteFiscalService();
        var entradas = new List<MovimientoInventario>
        {
            new()
            {
                Fecha = new DateTime(2026, 9, 5, 14, 30, 0, DateTimeKind.Utc),
                Tipo = TipoMovimientoInventario.EntradaCompra,
                CostoUnitario = 1250.55m,
                Cantidad = 10,
                ReferenciaDocumento = "B1100000001",
                Proveedor = new Proveedor { RNC = "101000001", RazonSocial = "Distribuidora Mayorista SRL" } // RNC 9 dígitos
            },
            new()
            {
                Fecha = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc),
                Tipo = TipoMovimientoInventario.EntradaCompra,
                CostoUnitario = 80.00m,
                Cantidad = 5,
                Concepto = "Factura Proveedor #B0100000456 — mercancía general",
                Proveedor = new Proveedor { RNC = "00112345678", RazonSocial = "Juan Pérez" } // Cédula
            },
            new()
            {
                Fecha = new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc),
                Tipo = TipoMovimientoInventario.EntradaCompra,
                CostoUnitario = 40.00m,
                Cantidad = 2
                // Sin proveedor ni NCF: compra menor / saldo inicial
            }
        };

        var registros = service.GenerarRegistros606(entradas);

        Assert.Equal(3, registros.Count);

        // Orden por fecha; NCF desde la referencia directa
        Assert.Equal("101000001", registros[0].RNC_Cedula);
        Assert.Equal(1, registros[0].TipoIdentificacion);
        Assert.Equal("B1100000001", registros[0].NCFCompra);
        Assert.Equal("20260905", registros[0].FechaComprobante);
        Assert.Equal(12505.50m, registros[0].MontoFacturado);
        Assert.Equal("Distribuidora Mayorista SRL", registros[0].RazonSocial);

        // NCF extraído del concepto libre; cédula → tipo 2
        Assert.Equal(2, registros[1].TipoIdentificacion);
        Assert.Equal("B0100000456", registros[1].NCFCompra);
        Assert.Equal(400.00m, registros[1].MontoFacturado);

        // Sin proveedor: RNC de referencia y razón social explícita
        Assert.Equal("00000000000", registros[2].RNC_Cedula);
        Assert.Equal("PROVEEDOR NO REGISTRADO", registros[2].RazonSocial);
        Assert.Equal("", registros[2].NCFCompra);

        // Formato TXT oficial: encabezado 606|RNC|PERIODO|CANTIDAD + líneas delimitadas
        var txt = service.GenerarArchivo606Txt("13100000001", 2026, 9, registros);
        Assert.StartsWith("606|13100000001|202609|3", txt);
        Assert.Contains("101000001|1|B1100000001|1|20260905|12505.50|0.00|0.00|0.00|01", txt);
    }

    [Fact]
    public void Reporte606_PeriodoVacio_GeneraSoloEncabezado()
    {
        var service = new ReporteFiscalService();

        var txt = service.GenerarArchivo606Txt("13100000001", 2026, 8, service.GenerarRegistros606(new List<MovimientoInventario>()));

        Assert.Equal("606|13100000001|202608|0\r\n", txt);
    }

    [Fact]
    public void FiltrarPorPeriodo_ExcluyeComprobantesDeOtrosMeses()
    {
        // Bug de cumplimiento: el TXT 607 declara un período y no puede llevar comprobantes
        // de otros meses aunque el repositorio los devuelva.
        var delPeriodo = new ElectronicInvoice
        {
            eNCF = "E320000000010",
            FechaEmision = "15-09-2026",
            Estado = EstadoFacturaElectronica.Aceptado,
            MontoTotal = 118.00m
        };
        var deOtroMes = new ElectronicInvoice
        {
            eNCF = "E320000000011",
            FechaEmision = "20-08-2026",
            Estado = EstadoFacturaElectronica.Aceptado,
            MontoTotal = 100.00m
        };
        var deOtroAnio = new ElectronicInvoice
        {
            eNCF = "E320000000012",
            FechaEmision = "05-09-2025",
            Estado = EstadoFacturaElectronica.Aceptado,
            MontoTotal = 50.00m
        };
        var fechaInvalida = new ElectronicInvoice
        {
            eNCF = "E320000000013",
            FechaEmision = "sin-fecha",
            Estado = EstadoFacturaElectronica.Aceptado,
            MontoTotal = 10.00m
        };

        var filtradas = ReporteFiscalService.FiltrarPorPeriodo(
            new[] { delPeriodo, deOtroMes, deOtroAnio, fechaInvalida }, 2026, 9).ToList();

        var unica = Assert.Single(filtradas);
        Assert.Equal("E320000000010", unica.eNCF);
    }
}
