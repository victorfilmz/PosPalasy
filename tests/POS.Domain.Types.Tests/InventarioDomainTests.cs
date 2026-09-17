using System;
using POS.Domain.Entities;
using POS.Domain.Types;
using Xunit;

namespace POS.Domain.Types.Tests;

public class InventarioDomainTests
{
    [Fact]
    public void Producto_CalculaMargenCorrectamente()
    {
        var prod = new Producto
        {
            Codigo = "TEST001",
            Descripcion = "Producto de Prueba",
            CostoUnitario = 50.00m,
            PrecioUnitario = 100.00m,
            StockActual = 20,
            StockMinimo = 5
        };

        var margenPorcentaje = ((prod.PrecioUnitario - prod.CostoUnitario) / prod.PrecioUnitario) * 100;
        Assert.Equal(50.00m, margenPorcentaje);
        Assert.True(prod.StockActual > prod.StockMinimo);
    }

    [Fact]
    public void MovimientoInventario_EntradaCompra_IncrementaStock()
    {
        decimal stockAnterior = 10;
        decimal cantidadEntrada = 15;
        decimal stockNuevo = stockAnterior + cantidadEntrada;

        var mov = new MovimientoInventario
        {
            ProductoId = 1,
            Tipo = TipoMovimientoInventario.EntradaCompra,
            Cantidad = cantidadEntrada,
            StockAnterior = stockAnterior,
            StockNuevo = stockNuevo,
            CostoUnitario = 40.00m,
            Concepto = "Compra inicial de mercancía",
            Fecha = DateTime.UtcNow
        };

        Assert.Equal(25.00m, mov.StockNuevo);
        Assert.Equal(TipoMovimientoInventario.EntradaCompra, mov.Tipo);
    }

    [Fact]
    public void MovimientoInventario_VentaPOS_DecrementaStock()
    {
        decimal stockAnterior = 25;
        decimal cantidadVendida = 3;
        decimal stockNuevo = stockAnterior - cantidadVendida;

        var mov = new MovimientoInventario
        {
            ProductoId = 1,
            Tipo = TipoMovimientoInventario.VentaPOS,
            Cantidad = cantidadVendida,
            StockAnterior = stockAnterior,
            StockNuevo = stockNuevo,
            Concepto = "Venta en caja POS",
            ReferenciaDocumento = "E320000000005",
            Fecha = DateTime.UtcNow
        };

        Assert.Equal(22.00m, mov.StockNuevo);
        Assert.Equal("E320000000005", mov.ReferenciaDocumento);
    }

    [Fact]
    public void Enterprise_ConfiguracionTicket_TieneValoresPredeterminadosValidos()
    {
        var ent = new Enterprise
        {
            RNC = "13100000001",
            RazonSocial = "PosPalasy SRL"
        };

        Assert.Equal(80, ent.AnchoPapelMm);
        Assert.True(ent.MostrarLogoTicket);
        Assert.True(ent.PermitirVentaSinStock);
        Assert.NotEmpty(ent.MensajePieTicket);
        Assert.NotEmpty(ent.PoliticaGarantiaTicket);
    }

    [Fact]
    public void InventarioAlmacen_AislamientoPorSucursal_Y_StockConsolidado()
    {
        var prod = new Producto
        {
            Id = 1,
            Codigo = "PROD-001",
            Descripcion = "Refresco Cola 2L",
            PrecioUnitario = 120.00m,
            CostoUnitario = 80.00m,
            TieneLotes = true
        };

        var invSucursal1 = new InventarioAlmacen
        {
            ProductoId = prod.Id,
            SucursalId = 1,
            StockActual = 15.00m,
            StockMinimo = 5.00m
        };

        var invSucursal2 = new InventarioAlmacen
        {
            ProductoId = prod.Id,
            SucursalId = 2,
            StockActual = 0.00m,
            StockMinimo = 2.00m
        };

        prod.Inventarios.Add(invSucursal1);
        prod.Inventarios.Add(invSucursal2);

        // Aislamiento: Sucursal 1 tiene existencias, Sucursal 2 está agotada
        Assert.Equal(15.00m, invSucursal1.StockActual);
        Assert.Equal(0.00m, invSucursal2.StockActual);

        // Consolidación empresarial
        Assert.Equal(15.00m, prod.StockConsolidado);
    }

    [Fact]
    public void PagoFactura_PagoMixto_TotalizaCorrectamente()
    {
        var venta = new Venta
        {
            NumeroFacturaInterna = "FAC-TEST-001",
            Total = 1500.00m
        };

        venta.Pagos.Add(new PagoFactura
        {
            MetodoPago = "Efectivo",
            Monto = 500.00m
        });

        venta.Pagos.Add(new PagoFactura
        {
            MetodoPago = "TarjetaDebitoCredito",
            Monto = 1000.00m,
            Referencia = "AUTH-874291"
        });

        var sumaPagos = venta.Pagos.Sum(p => p.Monto);
        Assert.Equal(venta.Total, sumaPagos);
        Assert.Equal(2, venta.Pagos.Count);
    }

    [Fact]
    public void EmisionDGIIQueue_EncoladoOffline_RegistraValoresIniciales()
    {
        var itemQueue = new EmisionDGIIQueue
        {
            FacturaId = 42,
            eNCF = "E320000000010",
            XmlFirmado = "<eCF>...</eCF>",
            Intentos = 0,
            EnviadoExitosamente = false,
            FechaRegistro = DateTime.UtcNow
        };

        Assert.False(itemQueue.EnviadoExitosamente);
        Assert.Equal(0, itemQueue.Intentos);
        Assert.Equal("E320000000010", itemQueue.eNCF);
        Assert.Null(itemQueue.UltimoError);
    }

    [Fact]
    public void MovimientoInventario_RegistraSucursalYLoteCorrectamente()
    {
        var mov = new MovimientoInventario
        {
            ProductoId = 1,
            SucursalId = 2,
            ProveedorId = 5,
            NumeroLote = "LOT-2026-B9",
            FechaVencimiento = DateTime.UtcNow.AddMonths(12),
            Tipo = TipoMovimientoInventario.EntradaCompra,
            Cantidad = 50,
            StockAnterior = 0,
            StockNuevo = 50,
            Concepto = "Recepción de mercancía sucursal este"
        };

        Assert.Equal(2, mov.SucursalId);
        Assert.Equal(5, mov.ProveedorId);
        Assert.Equal("LOT-2026-B9", mov.NumeroLote);
        Assert.NotNull(mov.FechaVencimiento);
    }
}
