using System;
using POS.Domain.Common;
using POS.Domain.Types;

namespace POS.Domain.Entities;

/// <summary>
/// Contador autorizado de secuencias de eNCF por serie. Es la autoridad de numeración fiscal:
/// cada asignación debe ser atómica y quedar dentro de la transacción de la venta, de modo que un
/// rollback libere el número en lugar de quemarlo.
/// </summary>
public class SecuenciaECF : BaseEntity
{
    /// <summary>Serie de 3 caracteres asignada por la DGII (E31, E32, E33, E34, E41…).</summary>
    public string Serie { get; set; } = string.Empty;

    /// <summary>Tipo de comprobante al que pertenece la serie.</summary>
    public TipoeCFType TipoECF { get; set; }

    /// <summary>Último número asignado (no el siguiente).</summary>
    public long Ultimo { get; set; }

    /// <summary>Rango autorizado por la DGII (inclusive). Null = sin límite configurado.</summary>
    public long? DesdeAutorizado { get; set; }
    public long? HastaAutorizado { get; set; }

    /// <summary>Token de concurrencia optimista: cambia en cada asignación para detectar carreras.</summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    /// <summary>Serie derivada del tipo de comprobante según la codificación de la DGII (E + tipo).</summary>
    public static string SerieDe(TipoeCFType tipo) => $"E{(int)tipo:D2}";

    /// <summary>Formatea la secuencia con el ancho fiscal obligatorio (serie + 10 dígitos = 13).</summary>
    public static string Formatear(string serie, long secuencia) => $"{serie}{secuencia:D10}";
}
