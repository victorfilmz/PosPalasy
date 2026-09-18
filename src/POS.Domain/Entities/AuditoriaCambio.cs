using System;
using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// Registro mínimo de auditoría de cambios de configuración del sistema.
/// </summary>
/// <remarks>
/// No es un sistema de auditoría general: por ahora documenta quién cambió qué valor de
/// configuración, cuándo, de qué valor a cuál y por qué motivo (Fase 3). El registro se escribe en la
/// misma transacción del cambio: no existe un cambio sin su traza.
/// </remarks>
public class AuditoriaCambio : BaseEntity
{
    /// <summary>Nombre de usuario (login) que ejecutó el cambio.</summary>
    public string Usuario { get; set; } = string.Empty;

    /// <summary>Instante del cambio en UTC.</summary>
    public DateTime FechaUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Entidad modificada (ej. "Enterprise").</summary>
    public string Entidad { get; set; } = string.Empty;

    /// <summary>Campo modificado (ej. "PoliticaStock").</summary>
    public string Campo { get; set; } = string.Empty;

    /// <summary>Valor antes del cambio (texto legible, ej. "Permitir").</summary>
    public string ValorAnterior { get; set; } = string.Empty;

    /// <summary>Valor después del cambio.</summary>
    public string ValorNuevo { get; set; } = string.Empty;

    /// <summary>Motivo declarado por quien hizo el cambio (opcional).</summary>
    public string? Motivo { get; set; }
}
