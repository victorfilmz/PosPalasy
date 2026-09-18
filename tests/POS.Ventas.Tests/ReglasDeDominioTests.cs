using System;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Ventas.Tests;

public class ReglasDeCobroTests
{
    private static Producto Producto(decimal precio = 100m, IndicadorFacturacionType indicador = IndicadorFacturacionType.ITBIS1_18) =>
        new()
        {
            Id = 1,
            Codigo = "P-1",
            Descripcion = "Producto de prueba",
            PrecioUnitario = precio,
            CodigoISC = null,
            IndicadorFacturacion = indicador,
            UnidadMedida = UnidadMedidaType.Unidad,
            EstaActivo = true
        };

    private static Venta VentaCon(decimal cantidad = 1m, decimal descuento = 0m, TipoPago tipoPago = TipoPago.Contado)
    {
        var venta = new Venta { TipoPago = tipoPago, MetodoPago = MetodoPago.Efectivo };
        venta.AddItem(VentaItem.DesdeCatalogo(Producto(), cantidad, descuento));
        return venta;
    }

    [Fact]
    public void Venta_al_contado_sin_pagos_es_rechazada()
    {
        var venta = VentaCon();

        var ex = Assert.Throws<ReglaDeNegocioException>(() => venta.ValidarCobro());
        Assert.Equal("PAGOS_AUSENTES", ex.Codigo);
    }

    [Fact]
    public void Venta_a_credito_sin_pagos_es_valida()
    {
        var venta = VentaCon(tipoPago: TipoPago.Credito);

        var desglose = venta.ValidarCobro();

        Assert.Equal(0m, desglose.TotalRecibido);
        Assert.Equal(0m, desglose.Cambio);
    }

    [Fact]
    public void Pago_menor_al_total_es_rechazado()
    {
        var venta = VentaCon(cantidad: 2m);
        venta.RegistrarPago(MetodoPago.Efectivo, 200m);

        var ex = Assert.Throws<ReglaDeNegocioException>(() => venta.ValidarCobro());
        Assert.Equal("PAGOS_INSUFICIENTES", ex.Codigo);
    }

    [Fact]
    public void Pago_en_efectivo_mayor_al_total_genera_cambio()
    {
        var venta = VentaCon();
        venta.RegistrarPago(MetodoPago.Efectivo, 150m);

        var desglose = venta.ValidarCobro();

        Assert.Equal(118m, venta.Total);
        Assert.Equal(32m, desglose.Cambio);
        Assert.Equal(118m, desglose.EfectivoNetoEnCaja);
        Assert.Equal(150m, venta.MontoRecibido);
    }

    [Fact]
    public void Pago_con_tarjeta_no_puede_exceder_el_total()
    {
        var venta = VentaCon();
        venta.RegistrarPago(MetodoPago.TarjetaDebitoCredito, 200m);

        var ex = Assert.Throws<ReglaDeNegocioException>(() => venta.ValidarCobro());
        Assert.Equal("PAGO_EXCEDE_TOTAL", ex.Codigo);
    }

    [Fact]
    public void El_cambio_solo_puede_provenir_del_efectivo()
    {
        var venta = VentaCon();
        venta.RegistrarPago(MetodoPago.TarjetaDebitoCredito, 118m);

        var desglose = venta.ValidarCobro();

        // Un pago electrónico exacto no genera cambio; y por encima del total se rechaza antes de
        // calcularlo, por lo que el cambio nunca puede salir de la tarjeta.
        Assert.Equal(0m, desglose.Cambio);

        var ventaConExceso = VentaCon();
        ventaConExceso.RegistrarPago(MetodoPago.TarjetaDebitoCredito, 200m);

        var ex = Assert.Throws<ReglaDeNegocioException>(() => ventaConExceso.ValidarCobro());
        Assert.Equal("PAGO_EXCEDE_TOTAL", ex.Codigo);
    }

    [Fact]
    public void Pagos_mixtos_se_desglosan_por_forma_de_pago()
    {
        var venta = VentaCon(cantidad: 2m);            // total 236
        venta.RegistrarPago(MetodoPago.Efectivo, 100m);
        venta.RegistrarPago(MetodoPago.TarjetaDebitoCredito, 136m);

        var desglose = venta.ValidarCobro();

        Assert.Equal(100m, desglose.Efectivo);
        Assert.Equal(136m, desglose.Tarjeta);
        Assert.Equal(0m, desglose.Cambio);
        Assert.Equal(100m, desglose.EfectivoNetoEnCaja);
    }

    [Fact]
    public void Forma_de_pago_no_controlada_por_el_arqueo_es_rechazada()
    {
        var venta = VentaCon();
        venta.RegistrarPago(MetodoPago.BonosCertificados, 118m);

        var ex = Assert.Throws<ReglaDeNegocioException>(() => venta.ValidarCobro());
        Assert.Equal("FORMA_PAGO_NO_SOPORTADA", ex.Codigo);
    }

    [Fact]
    public void Un_pago_en_cero_no_es_un_pago()
    {
        var venta = VentaCon();

        var ex = Assert.Throws<ReglaDeNegocioException>(() => venta.RegistrarPago(MetodoPago.Efectivo, 0m));
        Assert.Equal("PAGO_INVALIDO", ex.Codigo);
    }

    [Fact]
    public void Producto_inactivo_no_puede_venderse()
    {
        var producto = Producto();
        producto.EstaActivo = false;

        var ex = Assert.Throws<ReglaDeNegocioException>(() => VentaItem.DesdeCatalogo(producto, 1m));
        Assert.Equal("PRODUCTO_INACTIVO", ex.Codigo);
    }

    [Fact]
    public void Descuento_mayor_que_la_linea_es_rechazado()
    {
        var ex = Assert.Throws<ReglaDeNegocioException>(() => VentaItem.DesdeCatalogo(Producto(), 1m, 150m));
        Assert.Equal("DESCUENTO_EXCEDE_LINEA", ex.Codigo);
    }

    [Fact]
    public void Cantidad_cero_o_negativa_es_rechazada()
    {
        var ex = Assert.Throws<ReglaDeNegocioException>(() => VentaItem.DesdeCatalogo(Producto(), 0m));
        Assert.Equal("CANTIDAD_INVALIDA", ex.Codigo);
    }

    [Fact]
    public void El_renglon_toma_del_catalogo_todos_los_datos_fiscales()
    {
        var producto = Producto(precio: 75m, indicador: IndicadorFacturacionType.ITBIS2_16);
        producto.UnidadMedida = UnidadMedidaType.Lata;
        producto.IndicadorBienoServicio = IndicadorBienoServicioType.Servicio;
        producto.CodigoISC = "023";

        var renglon = VentaItem.DesdeCatalogo(producto, 2m);

        Assert.Equal(75m, renglon.PrecioUnitario);
        Assert.Equal(IndicadorFacturacionType.ITBIS2_16, renglon.IndicadorFacturacion);
        Assert.Equal(UnidadMedidaType.Lata, renglon.UnidadMedida);
        Assert.Equal(IndicadorBienoServicioType.Servicio, renglon.IndicadorBienoServicio);
        Assert.Equal("023", renglon.CodigoISC);
        Assert.Equal("P-1", renglon.CodigoProducto);
        Assert.Equal(16m, renglon.TasaITBIS);
        Assert.Equal(24m, renglon.MontoITBIS);   // 150 × 16 %
        Assert.Equal(174m, renglon.Total);
    }
}

public class EstadosDeEmisionTests
{
    [Fact]
    public void No_se_puede_retroceder_en_la_linea_de_emision()
    {
        Assert.False(EstadoEmisionECF.Firmada.PuedeTransicionarA(EstadoEmisionECF.XmlGenerado));
        Assert.False(EstadoEmisionECF.Enviada.PuedeTransicionarA(EstadoEmisionECF.Encolada));
        Assert.False(EstadoEmisionECF.ConfirmadaEnvio.PuedeTransicionarA(EstadoEmisionECF.Enviada));
    }

    [Fact]
    public void Se_admite_avanzar_saltando_los_pasos_aun_no_implementados()
    {
        // La validación XSD y la firma todavía no forman parte del flujo; la máquina permite avanzar
        // pero nunca retroceder, de modo que al incorporarlas no cambia ninguna regla.
        Assert.True(EstadoEmisionECF.XmlGenerado.PuedeTransicionarA(EstadoEmisionECF.Enviada));
        Assert.True(EstadoEmisionECF.XmlGenerado.PuedeTransicionarA(EstadoEmisionECF.ConfirmadaEnvio));
    }

    [Fact]
    public void Un_estado_terminal_no_se_reabre()
    {
        foreach (var destino in Enum.GetValues<EstadoEmisionECF>())
        {
            Assert.False(EstadoEmisionECF.ConfirmadaEnvio.PuedeTransicionarA(destino));
            Assert.False(EstadoEmisionECF.ErrorXsd.PuedeTransicionarA(destino));
            Assert.False(EstadoEmisionECF.ErrorFirma.PuedeTransicionarA(destino));
            Assert.False(EstadoEmisionECF.ErrorPermanente.PuedeTransicionarA(destino));
        }
    }

    [Fact]
    public void Un_envio_incierto_solo_se_resuelve_confirmando_la_recepcion()
    {
        Assert.True(EstadoEmisionECF.EnvioIncierto.PuedeTransicionarA(EstadoEmisionECF.ConfirmadaEnvio));
        Assert.False(EstadoEmisionECF.EnvioIncierto.PuedeTransicionarA(EstadoEmisionECF.Enviada));
        Assert.False(EstadoEmisionECF.EnvioIncierto.PuedeTransicionarA(EstadoEmisionECF.Encolada));
    }

    [Fact]
    public void Un_fallo_recuperable_puede_volver_a_intentarse_y_un_permanente_no()
    {
        Assert.True(EstadoEmisionECF.ErrorTemporal.PuedeTransicionarA(EstadoEmisionECF.Encolada));
        Assert.True(EstadoEmisionECF.ErrorTemporal.PuedeTransicionarA(EstadoEmisionECF.Enviada));
        Assert.False(EstadoEmisionECF.ErrorPermanente.PuedeTransicionarA(EstadoEmisionECF.Encolada));
    }

    [Fact]
    public void Una_transicion_invalida_lanza_error_de_negocio()
    {
        var ex = Assert.Throws<ReglaDeNegocioException>(() =>
            EstadoEmisionECF.ConfirmadaEnvio.ValidarTransicion(EstadoEmisionECF.Enviada));

        Assert.Equal("TRANSICION_EMISION_INVALIDA", ex.Codigo);
    }
}

public class PoliticaDeReintentoTests
{
    [Fact]
    public void La_espera_crece_con_los_intentos_y_se_limita_al_tope()
    {
        var ahora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var primerIntento = PoliticaReintentoCola.ProximoIntento(1, ahora, semillaJitter: 0);
        var tercerIntento = PoliticaReintentoCola.ProximoIntento(3, ahora, semillaJitter: 0);
        var intentoAvanzado = PoliticaReintentoCola.ProximoIntento(10, ahora, semillaJitter: 0);

        Assert.Equal(30, (primerIntento - ahora).TotalSeconds, 1);
        Assert.Equal(120, (tercerIntento - ahora).TotalSeconds, 1);
        Assert.True((intentoAvanzado - ahora) <= PoliticaReintentoCola.EsperaMaxima);
    }

    [Fact]
    public void El_jitter_es_estable_por_documento_y_distinto_entre_documentos()
    {
        var ahora = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var una = PoliticaReintentoCola.ProximoIntento(3, ahora, semillaJitter: 10);
        var otraVez = PoliticaReintentoCola.ProximoIntento(3, ahora, semillaJitter: 10);
        var distinta = PoliticaReintentoCola.ProximoIntento(3, ahora, semillaJitter: 55);

        Assert.Equal(una, otraVez);
        Assert.NotEqual(una, distinta);
    }
}

public class ClasificadorErroresTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(429, true)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(400, false)]
    [InlineData(422, false)]
    public void Un_fallo_se_reintenta_solo_cuando_puede_resolverse_reintentando(int codigoHttp, bool recuperable)
    {
        Assert.Equal(recuperable, POS.Application.Services.ClasificadorErroresDGII.EsRecuperable(codigoHttp, false));
    }

    [Fact]
    public void Una_respuesta_exitosa_nunca_es_recuperable()
    {
        Assert.False(POS.Application.Services.ClasificadorErroresDGII.EsRecuperable(200, true));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(500, false)]
    [InlineData(503, false)]
    [InlineData(429, false)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    public void Solo_la_ausencia_total_de_respuesta_deja_el_envio_en_estado_incierto(int codigoHttp, bool incierto)
    {
        Assert.Equal(incierto, POS.Application.Services.ClasificadorErroresDGII.EsAmbiguo(codigoHttp));
    }
}

public class SecuenciaFiscalTests
{
    [Fact]
    public void La_serie_se_deriva_del_tipo_de_comprobante()
    {
        Assert.Equal("E31", SecuenciaECF.SerieDe(TipoeCFType.FacturaCreditoFiscal));
        Assert.Equal("E32", SecuenciaECF.SerieDe(TipoeCFType.FacturaConsumo));
        Assert.Equal("E33", SecuenciaECF.SerieDe(TipoeCFType.NotaDebito));
        Assert.Equal("E34", SecuenciaECF.SerieDe(TipoeCFType.NotaCredito));
        Assert.Equal("E41", SecuenciaECF.SerieDe(TipoeCFType.Compras));
    }

    [Fact]
    public void El_numero_de_comprobante_tiene_trece_caracteres()
    {
        var numero = SecuenciaECF.Formatear("E32", 1);

        Assert.Equal("E320000000001", numero);
        Assert.Equal(13, numero.Length);
        Assert.True(POS.Domain.Types.eNCF.IsValid(numero));
    }
}
