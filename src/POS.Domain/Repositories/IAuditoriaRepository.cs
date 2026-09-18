using POS.Domain.Entities;

namespace POS.Domain.Repositories;

/// <summary>
/// Registro de auditoría de cambios de configuración. Mínimo necesario para dejar traza de quién
/// cambió valores sensibles de operación (política de stock, ambiente, etc.).
/// </summary>
public interface IAuditoriaRepository
{
    /// <summary>Registra un cambio de configuración. Debe llamarse en la misma transacción del cambio.</summary>
    Task RegistrarAsync(AuditoriaCambio cambio, CancellationToken ct = default);
}
