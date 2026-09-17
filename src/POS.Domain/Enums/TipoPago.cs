namespace POS.Domain.Enums;

/// <summary>
/// Tipo de pago del comprobante fiscal (1=Contado, 2=Crédito, 3=Gratuito).
/// </summary>
public enum TipoPago : int
{
    Contado = 1,
    Credito = 2,
    Gratuito = 3
}

/// <summary>
/// Formas o métodos de pago aceptados por la DGII.
/// </summary>
public enum MetodoPago : int
{
    Efectivo = 1,
    ChequeTransferenciaDeposito = 2,
    TarjetaDebitoCredito = 3,
    VentaACredito = 4,
    BonosCertificados = 5,
    Permuta = 6,
    NotaCredito = 7,
    OtrasFormas = 8
}
