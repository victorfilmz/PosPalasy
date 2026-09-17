namespace POS.Domain.Types;

/// <summary>
/// Tipo de comprobante fiscal electrónico.
/// 31=Factura de Crédito Fiscal, 32=Factura de Consumo,
/// 33=Nota de Débito, 34=Nota de Crédito, 41=Compras,
/// 43=Gastos Menores, 44=Regímenes Especiales, 45=Gubernamental,
/// 46=Exportaciones, 47=Pagos al Exterior
/// </summary>
public enum TipoeCFType : int
{
    /// <summary>Factura de Crédito Fiscal Electrónica</summary>
    FacturaCreditoFiscal = 31,

    /// <summary>Factura de Consumo Electrónica</summary>
    FacturaConsumo = 32,

    /// <summary>Nota de Débito Electrónica</summary>
    NotaDebito = 33,

    /// <summary>Nota de Crédito Electrónica</summary>
    NotaCredito = 34,

    /// <summary>Compras Electrónicas</summary>
    Compras = 41,

    /// <summary>Gastos Menores Electrónico</summary>
    GastosMenores = 43,

    /// <summary>Regímenes Especiales Electrónico</summary>
    RegimenesEspeciales = 44,

    /// <summary>Gubernamental Electrónico</summary>
    Gubernamental = 45,

    /// <summary>Comprobante de Exportaciones Electrónico</summary>
    Exportaciones = 46,

    /// <summary>Comprobante para Pagos al Exterior Electrónico</summary>
    PagosAlExterior = 47
}

/// <summary>
/// Tipo de comprobante para ANECF (anulación).
/// </summary>
public enum CFType : int
{
    FacturaCreditoFiscal = 31,
    FacturaConsumo = 32,
    NotaDebito = 33,
    NotaCredito = 34,
    Compras = 41,
    GastosMenores = 43,
    RegimenesEspeciales = 44,
    Gubernamental = 45,
    Exportaciones = 46,
    PagosAlExterior = 47
}
