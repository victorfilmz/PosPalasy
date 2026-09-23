namespace POS.Domain.Enums;

/// <summary>
/// Tipos de contingencia reconocidos por la DGII para la emisión de comprobantes sin transmisión
/// inmediata (reglamento e-CF). El e-CF emitido en contingencia es un e-CF NORMAL: los XSD v1.0
/// no llevan campo XML de contingencia; el tipo se declara al momento de la transmisión diferida.
/// </summary>
public enum TipoContingenciaDgii : int
{
    /// <summary>Falla técnica en sistemas del emisor.</summary>
    FallaSistemaEmisor = 1,

    /// <summary>Falla en la entrega de insumos (papel, tinta, energía del punto de emisión).</summary>
    FallaEntregaInsumos = 2,

    /// <summary>Falla de la plataforma tecnológica de la DGII (indisponibilidad de recepción).</summary>
    FallaPlataformaDgii = 3,

    /// <summary>Corte o indisponibilidad del certificado digital del emisor.</summary>
    CorteCertificadoDigital = 4,

    /// <summary>Recorte de energía eléctrica del punto de emisión.</summary>
    RecorteEnergiaElectrica = 5
}

/// <summary>
/// Régimen de contingencia del reglamento e-CF: plazos y helpers de validez.
/// </summary>
/// <remarks>
/// Ventana normativa: una contingencia declarada vence al día 30 calendario; el comprobante emitido
/// bajo ella debe transmitirse con la fecha de inicio de la contingencia como fecha de emisión
/// fiscal. Si vence, el comprobante NO puede transmitirse: debe anularse el rango (ANECF) y
/// reemitirse con nueva secuencia.
/// </remarks>
public static class RegimenContingencia
{
    /// <summary>Ventana máxima normativa de una contingencia declarada (días calendario).</summary>
    public const int VentanaDias = 30;

    /// <summary>Último día de la ventana de transmisión (inicio + 30 días calendario).</summary>
    public static DateTime FinDeVentana(DateTime inicioUtc) => inicioUtc.AddDays(VentanaDias);

    /// <summary>La ventana de una contingencia iniciada en <paramref name="inicioUtc"/> sigue abierta.</summary>
    public static bool VentanaAbierta(DateTime inicioUtc, DateTime ahoraUtc) =>
        ahoraUtc <= FinDeVentana(inicioUtc);
}
