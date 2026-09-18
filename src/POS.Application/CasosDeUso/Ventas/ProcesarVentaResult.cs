using POS.Domain.Enums;

namespace POS.Application.CasosDeUso.Ventas;

/// <summary>
/// Resultado del caso de uso de venta. Separa con precisión tres cosas que antes se mezclaban:
/// si la venta quedó registrada, en qué estado técnico está el comprobante y qué respondió la DGII.
/// </summary>
public sealed class ProcesarVentaResult
{
    /// <summary>La venta quedó registrada y cobrada localmente (transacción confirmada).</summary>
    public bool Exitoso { get; init; }

    /// <summary>La venta ya existía con la misma clave de idempotencia: se devolvió la original.</summary>
    public bool Duplicada { get; init; }

    public int VentaId { get; init; }
    public int? ElectronicInvoiceId { get; init; }
    public string eNCF { get; init; } = string.Empty;
    public string? TrackId { get; init; }

    public decimal Total { get; init; }
    public decimal Cambio { get; init; }

    /// <summary>Estado fiscal según la DGII.</summary>
    public EstadoFacturaElectronica EstadoFiscal { get; init; } = EstadoFacturaElectronica.NoEnviado;

    /// <summary>Estado técnico local de la emisión.</summary>
    public EstadoEmisionECF EstadoEmision { get; init; } = EstadoEmisionECF.Creada;

    /// <summary>La venta se registró, pero el comprobante aún no fue confirmado por la DGII.</summary>
    public bool EsOfflineDGII { get; init; }

    /// <summary>Código estable del error de negocio (null si la venta se registró).</summary>
    public string? CodigoError { get; init; }

    public string? Mensaje { get; init; }

    public static ProcesarVentaResult Error(string codigo, string mensaje) => new()
    {
        Exitoso = false,
        CodigoError = codigo,
        Mensaje = mensaje
    };
}

/// <summary>Códigos de conflicto de unicidad compartidos entre infraestructura y aplicación.</summary>
public static class CodigosConflicto
{
    /// <summary>Ya existe una venta con la misma clave de idempotencia.</summary>
    public const string IdempotenciaVenta = "IDEMPOTENCIA_VENTA";

    /// <summary>El eNCF asignado ya existe (carrera de secuencia detectada por la base de datos).</summary>
    public const string EncfDuplicado = "ENCF_DUPLICADO";

    /// <summary>Otro conflicto de unicidad no clasificado.</summary>
    public const string Generico = "CONFLICTO_UNICIDAD";
}
