using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// Secuencia de e-NCF devuelta al pool tras un rechazo de la DGII con
/// <c>secuenciaUtilizada=false</c> (sub-fase 5.5). Es una fila de HUECO: la asignación de la
/// numeración fiscal la consume ANTES de avanzar el contador de la serie, de modo que la secuencia
/// liberada se reutiliza exactamente una vez y la numeración no quema números innecesariamente.
/// </summary>
/// <remarks>
/// La DGII confirma con <c>secuenciaUtilizada</c> qué secuencias pueden reutilizarse (KB:
/// false = "PUEDE reutilizarse, útil cuando fue rechazado por errores correctables"). El comprobante
/// rechazado CONSERVA su e-NCF con fines de trazabilidad; la unicidad de la numeración vigente se
/// protege con el índice único filtrado sobre <c>ElectronicInvoices.eNCF</c>, que excluye los
/// rechazados. Cada fila nace con su auditoría y muere al ser consumida.
/// </remarks>
public class SecuenciaLibre : BaseEntity
{
    /// <summary>Serie de la secuencia devuelta (E31, E32, E34…).</summary>
    public string Serie { get; set; } = string.Empty;

    /// <summary>Número de secuencia liberado (sin la serie; la fila completa es el e-NCF).</summary>
    public long Numero { get; set; }

    /// <summary>e-NCF completo de la secuencia liberada (serie + número con ancho fiscal).</summary>
    public string ENCF { get; set; } = string.Empty;

    /// <summary>Comprobante rechazado que liberó la secuencia (trazabilidad fiscal).</summary>
    public int FacturaRechazadaId { get; set; }

    /// <summary>Fecha de la liberación (UTC).</summary>
    public DateTime FechaLiberacionUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Usuario o proceso que liberó la secuencia (auditoría operativa).</summary>
    public string LiberadaPor { get; set; } = "sistema";

    /// <summary>Motivo declarado por la DGII en el rechazo (auditoría fiscal).</summary>
    public string MotivoRechazo { get; set; } = string.Empty;

    /// <summary>
    /// True cuando la asignación ya consumió la secuencia. La marca (y no el borrado) ocurre dentro
    /// de la transacción del emisor: un rollback de la venta devuelve la fila al pool junto con todo
    /// lo demás, y el historial de consumo queda auditable.
    /// </summary>
    public bool Consumida { get; set; }

    /// <summary>Instante del consumo (UTC); null mientras la secuencia siga disponible.</summary>
    public DateTime? FechaConsumoUtc { get; set; }

    /// <summary>e-NCF formateado de una secuencia libre.</summary>
    public static string Formatear(string serie, long numero) => $"{serie}{numero:D10}";
}
