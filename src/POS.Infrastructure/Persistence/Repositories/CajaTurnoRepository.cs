using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

public class CajaTurnoRepository : ICajaTurnoRepository
{
    private readonly POSDbContext _context;

    public CajaTurnoRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<CajaTurno?> GetTurnoActivoAsync(string? cajero = null, CancellationToken ct = default)
    {
        var query = _context.CajaTurnos
            .Include(c => c.Movimientos)
            .Include(c => c.Ventas)
            .Where(c => c.Estado == TurnoCajaEstado.Abierto);

        if (!string.IsNullOrWhiteSpace(cajero))
        {
            query = query.Where(c => c.Cajero == cajero);
        }

        return await query.OrderByDescending(c => c.FechaApertura).FirstOrDefaultAsync(ct);
    }

    public async Task<CajaTurno?> GetTurnoAbiertoDeUsuarioAsync(int usuarioId, CancellationToken ct = default)
    {
        return await _context.CajaTurnos
            .FirstOrDefaultAsync(c => c.Estado == TurnoCajaEstado.Abierto && c.UsuarioId == usuarioId, ct);
    }

    public async Task<int> AcumularVentaAsync(
        int turnoId,
        decimal efectivo,
        decimal tarjeta,
        decimal transferencia,
        decimal total,
        CancellationToken ct = default)
    {
        // Incremento atómico en la base de datos: ni se reescribe el grafo cargado del turno, ni se
        // pierden acumulados por escrituras concurrentes de otros cajeros.
        return await _context.CajaTurnos
            .Where(c => c.Id == turnoId && c.Estado == TurnoCajaEstado.Abierto)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.VentasEfectivo, c => c.VentasEfectivo + efectivo)
                .SetProperty(c => c.VentasTarjeta, c => c.VentasTarjeta + tarjeta)
                .SetProperty(c => c.VentasTransferencia, c => c.VentasTransferencia + transferencia)
                .SetProperty(c => c.TotalVentas, c => c.TotalVentas + total)
                .SetProperty(c => c.CantidadTransacciones, c => c.CantidadTransacciones + 1)
                .SetProperty(c => c.UpdatedAt, _ => DateTime.UtcNow), ct);
    }

    public async Task<CajaTurno?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _context.CajaTurnos
            .Include(c => c.Movimientos)
            .Include(c => c.Ventas)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IEnumerable<CajaTurno>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.CajaTurnos
            .OrderByDescending(c => c.FechaApertura)
            .ToListAsync(ct);
    }

    public async Task<CajaTurno> AddAsync(CajaTurno turno, CancellationToken ct = default)
    {
        await _context.CajaTurnos.AddAsync(turno, ct);
        await _context.SaveChangesAsync(ct);
        return turno;
    }

    public async Task UpdateAsync(CajaTurno turno, CancellationToken ct = default)
    {
        _context.CajaTurnos.Update(turno);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddMovimientoAsync(MovimientoCaja movimiento, CancellationToken ct = default)
    {
        await _context.MovimientosCaja.AddAsync(movimiento, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task RegistrarMovimientoAsync(MovimientoCaja movimiento, CancellationToken ct = default)
    {
        await using var transaccion = await _context.Database.BeginTransactionAsync(ct);

        await _context.MovimientosCaja.AddAsync(movimiento, ct);
        await _context.SaveChangesAsync(ct);

        var esEntrada = movimiento.Tipo == TipoMovimientoCaja.Entrada;

        var filas = await _context.CajaTurnos
            .Where(c => c.Id == movimiento.CajaTurnoId && c.Estado == TurnoCajaEstado.Abierto)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.TotalEntradasEfectivo,
                    c => esEntrada ? c.TotalEntradasEfectivo + movimiento.Monto : c.TotalEntradasEfectivo)
                .SetProperty(c => c.TotalSalidasEfectivo,
                    c => esEntrada ? c.TotalSalidasEfectivo : c.TotalSalidasEfectivo + movimiento.Monto)
                .SetProperty(c => c.UpdatedAt, _ => DateTime.UtcNow), ct);

        if (filas == 0)
        {
            await transaccion.RollbackAsync(ct);
            throw new ReglaDeNegocioException(
                "El turno de caja no está abierto: no se registró el movimiento.",
                "TURNO_CERRADO");
        }

        await transaccion.CommitAsync(ct);
    }

    public async Task<int> CerrarTurnoAsync(
        int turnoId,
        decimal montoRealCierre,
        string? observaciones,
        CancellationToken ct = default)
    {
        var ahora = DateTime.UtcNow;

        // El efectivo esperado se calcula con los valores vigentes en la base de datos, no con los que
        // el navegador cargó: el arqueo no puede quedar desfasado por una venta concurrente.
        return await _context.CajaTurnos
            .Where(c => c.Id == turnoId && c.Estado == TurnoCajaEstado.Abierto)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.MontoRealCierre, montoRealCierre)
                .SetProperty(c => c.Diferencia,
                    c => montoRealCierre - (c.MontoInicial + c.VentasEfectivo + c.TotalEntradasEfectivo - c.TotalSalidasEfectivo))
                .SetProperty(c => c.FechaCierre, ahora)
                .SetProperty(c => c.Estado, TurnoCajaEstado.Cerrado)
                .SetProperty(c => c.Observaciones, observaciones)
                .SetProperty(c => c.UpdatedAt, _ => ahora), ct);
    }
}
