using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

public interface ICajaTurnoRepository
{
    Task<CajaTurno?> GetTurnoActivoAsync(string? cajero = null, CancellationToken ct = default);

    /// <summary>
    /// Turno abierto del usuario autenticado. Es la referencia fiable para imputar una venta al
    /// arqueo correcto, con independencia del nombre escrito a mano al abrir la caja.
    /// </summary>
    Task<CajaTurno?> GetTurnoAbiertoDeUsuarioAsync(int usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Acumula una venta en los totales del turno con una sentencia atómica de incremento
    /// (evita el update del grafo completo y las pérdidas por escritura concurrente).
    /// Devuelve las filas afectadas: 0 indica que el turno ya no está abierto.
    /// </summary>
    Task<int> AcumularVentaAsync(
        int turnoId,
        decimal efectivo,
        decimal tarjeta,
        decimal transferencia,
        decimal total,
        CancellationToken ct = default);
    Task<CajaTurno?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IEnumerable<CajaTurno>> GetAllAsync(CancellationToken ct = default);
    Task<CajaTurno> AddAsync(CajaTurno turno, CancellationToken ct = default);
    Task UpdateAsync(CajaTurno turno, CancellationToken ct = default);
    Task AddMovimientoAsync(MovimientoCaja movimiento, CancellationToken ct = default);

    /// <summary>
    /// Registra un movimiento de efectivo y su efecto en los totales del turno en una sola unidad,
    /// sin reescribir el grafo cargado del turno. Devuelve 0 si el turno ya no está abierto.
    /// </summary>
    Task RegistrarMovimientoAsync(MovimientoCaja movimiento, CancellationToken ct = default);

    /// <summary>
    /// Cierra el turno calculando el arqueo en la propia base de datos (así el esperado no depende de
    /// datos leídos antes). Devuelve 0 si el turno ya estaba cerrado.
    /// </summary>
    Task<int> CerrarTurnoAsync(
        int turnoId,
        decimal montoRealCierre,
        string? observaciones,
        CancellationToken ct = default);
}
