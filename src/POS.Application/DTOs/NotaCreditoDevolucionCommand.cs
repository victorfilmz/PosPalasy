using POS.Domain.Types;

namespace POS.Application.DTOs;

/// <summary>
/// Comando de emisión de la <b>Nota de Crédito Electrónica (e-CF 34)</b> que documenta una
/// devolución de venta. Lleva la referencia fiscal obligatoria al comprobante modificado
/// (<c>InformacionReferencia</c> del XSD e-CF 34): sin ella la DGII no puede vincular la corrección
/// con el original.
/// </summary>
/// <remarks>
/// La nota se construye desde la <see cref="POS.Domain.Entities.Devolucion"/> registrada (FASE 4):
/// los renglones y montos ya están prorrateados y el tope de reembolso ya fue verificado. Este
/// comando no repite esa aritmética; la consume.
/// </remarks>
public class NotaCreditoDevolucionCommand
{
    public int DevolucionId { get; init; }

    /// <summary>Usuario que autoriza la emisión (trazabilidad y bitácora).</summary>
    public string UsuarioNombre { get; init; } = string.Empty;

    /// <summary>Código de modificación (1=Anula, 2=Corrige texto, 3=Corrige montos).</summary>
    public int CodigoModificacion { get; init; } = 3;
}

/// <summary>Resultado del caso de uso de emisión de la nota de crédito de devolución.</summary>
public class NotaCreditoDevolucionResult
{
    public bool Exitoso { get; init; }
    public bool Duplicada { get; init; }

    public int DevolucionId { get; init; }
    public int ElectronicInvoiceId { get; init; }

    /// <summary>e-NCF de la nota de crédito emitida (serie E34).</summary>
    public string eNCF { get; init; } = string.Empty;

    /// <summary>e-NCF del comprobante original modificado (trazabilidad fiscal).</summary>
    public string eNCFModificado { get; init; } = string.Empty;

    public decimal TotalNota { get; init; }
    public string Xml { get; init; } = string.Empty;

    public string? CodigoError { get; init; }
    public string? Mensaje { get; init; }

    public static NotaCreditoDevolucionResult Error(string codigo, string mensaje) => new()
    {
        Exitoso = false,
        CodigoError = codigo,
        Mensaje = mensaje
    };
}
