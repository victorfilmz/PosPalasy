using System;

namespace POS.Domain.Common;

/// <summary>
/// Clase base para todas las entidades del dominio con auditoría básica.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
