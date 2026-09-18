using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Application.Interfaces;

namespace POS.Infrastructure.Persistence;

/// <summary>
/// Unidad de trabajo sobre <see cref="POSDbContext"/>.
/// </summary>
/// <remarks>
/// Dos garantías importantes para la operación del POS:
/// <list type="bullet">
/// <item>Si la operación falla, se revierte la transacción y se limpia el seguimiento de entidades:
/// un contexto que quedó con entidades a medio insertar no puede reutilizarse para reintentar.</item>
/// <item>Se usa la estrategia de ejecución del proveedor, de modo que los reintentos por fallos
/// transitorios de conexión respeten la frontera transaccional.</item>
/// </list>
/// </remarks>
public sealed class UnidadDeTrabajo : IUnidadDeTrabajo
{
    private readonly POSDbContext _context;

    public UnidadDeTrabajo(POSDbContext context)
    {
        _context = context;
    }

    public Task<T> EnTransaccionAsync<T>(
        Func<CancellationToken, Task<T>> operacion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);

        var estrategia = _context.Database.CreateExecutionStrategy();

        return estrategia.ExecuteAsync(async token =>
        {
            await using var transaccion = await _context.Database.BeginTransactionAsync(token);

            try
            {
                var resultado = await operacion(token);
                await transaccion.CommitAsync(token);
                return resultado;
            }
            catch (DbUpdateException ex) when (MapeadorErroresEF.EsViolacionDeUnicidad(ex))
            {
                await RevertirAsync(transaccion);
                throw MapeadorErroresEF.Traducir(ex);
            }
            catch
            {
                await RevertirAsync(transaccion);
                throw;
            }
        }, ct);
    }

    public Task EnTransaccionAsync(Func<CancellationToken, Task> operacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operacion);

        return EnTransaccionAsync(async token =>
        {
            await operacion(token);
            return true;
        }, ct);
    }

    private async Task RevertirAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaccion)
    {
        try
        {
            await transaccion.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Si la conexión ya se perdió, la transacción se descarta igualmente al liberar el contexto.
        }
        finally
        {
            // Un fallo no debe dejar entidades seguidas: el llamador puede reintentar desde cero.
            _context.ChangeTracker.Clear();
        }
    }
}
