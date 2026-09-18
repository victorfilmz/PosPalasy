using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Common;
using POS.Domain.Entities;
using POS.Domain.Repositories;
using POS.Domain.Types;

namespace POS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de la numeración fiscal (eNCF).
/// </summary>
/// <remarks>
/// La asignación se apoya en tres barreras (Fase 3):
/// <list type="number">
/// <item><b>Bloqueo de fila en SQL Server (UPDLOCK, ROWLOCK)</b> sobre la fila de la serie: los
/// emisores se serializan en el punto fiscal y el número se libera si la venta revierte (sin huecos).
/// Funciona con cualquier cantidad de instancias de la aplicación; no hay contadores en memoria.</item>
/// <item>Concurrencia optimista sobre la fila de la serie (red de seguridad para proveedores sin
/// sugerencias de bloqueo, como SQLite en pruebas): si otra venta incrementó el contador en el
/// intervalo, EF no actualiza ninguna fila y se repite la lectura.</item>
/// <item>Índice único sobre <c>ElectronicInvoices.eNCF</c>: aun con un fallo de las barreras
/// anteriores, la base de datos impide que dos comprobantes compartan número.</item>
/// </list>
/// La asignación se realiza dentro de la transacción de la venta.
/// </remarks>
public sealed class SecuenciaECFRepository : ISecuenciaECFRepository
{
    private const int MaxIntentosAsignacion = 5;

    private readonly POSDbContext _context;

    public SecuenciaECFRepository(POSDbContext context)
    {
        _context = context;
    }

    public async Task<string> AsignarSiguienteENCFAsync(TipoeCFType tipo, CancellationToken ct = default)
    {
        var serie = SecuenciaECF.SerieDe(tipo);

        for (var intento = 1; intento <= MaxIntentosAsignacion; intento++)
        {
            // SQL Server: lectura con bloqueo de la fila de la serie hasta el fin de la transacción:
            // el siguiente emisor espera aquí en lugar de perder la carrera y reintentar a ciegas.
            // Otros proveedores (SQLite en pruebas): lectura plana + camino optimista.
            var consulta = _context.Database.IsSqlServer()
                ? _context.SecuenciasECF.FromSqlInterpolated(
                    $"SELECT * FROM [SecuenciasECF] WITH (UPDLOCK, ROWLOCK) WHERE [Serie] = {serie}")
                : _context.SecuenciasECF.Where(s => s.Serie == serie);

            var secuencia = await consulta.FirstOrDefaultAsync(ct);

            if (secuencia == null)
            {
                secuencia = await CrearSerieAsync(tipo, serie, ct);
                if (secuencia == null)
                    continue; // Otra transacción la creó: se reintenta leyéndola.
            }

            // Segundo control, independiente del contador: el mayor número realmente emitido de la
            // serie. Cubre bases restauradas o secuencias desincronizadas y evita entregar un número
            // que ya está en uso (defensa en profundidad frente al índice único).
            var ultimoEmitido = await ObtenerUltimoEmitidoAsync(serie, ct);
            var baseNumeracion = Math.Max(secuencia.Ultimo, ultimoEmitido);

            if (secuencia.HastaAutorizado.HasValue && baseNumeracion >= secuencia.HastaAutorizado.Value)
                throw new ReglaDeNegocioException(
                    $"La serie {serie} agotó el rango autorizado por la DGII " +
                    $"({secuencia.DesdeAutorizado ?? 1}-{secuencia.HastaAutorizado}). " +
                    "Solicite nuevas secuencias antes de seguir facturando.",
                    "SECUENCIA_AGOTADA");

            var siguiente = baseNumeracion + 1;
            secuencia.Ultimo = siguiente;
            secuencia.Version = Guid.NewGuid();
            secuencia.FechaActualizacion = DateTime.UtcNow;

            try
            {
                await _context.SaveChangesAsync(ct);
                return SecuenciaECF.Formatear(serie, siguiente);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Otra venta incrementó la serie en el intervalo: se descarta y se reintenta.
                _context.Entry(secuencia).State = EntityState.Detached;
            }
            catch (DbUpdateException ex) when (MapeadorErroresEF.EsViolacionDeUnicidad(ex))
            {
                _context.Entry(secuencia).State = EntityState.Detached;
            }
        }

        throw new ReglaDeNegocioException(
            "La numeración fiscal está saturada y no fue posible asignar un número único. Reintente la venta.",
            "SECUENCIA_NO_DISPONIBLE");
    }

    public async Task<SecuenciaECF?> ObtenerAsync(string serie, CancellationToken ct = default)
    {
        return await _context.SecuenciasECF
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Serie == serie, ct);
    }

    public async Task<SecuenciaECF> AsegurarSerieAsync(
        TipoeCFType tipo,
        long desde = 1,
        long? hasta = null,
        CancellationToken ct = default)
    {
        if (desde < 1)
            throw new ReglaDeNegocioException(
                "El rango autorizado debe comenzar en 1 o más.",
                "RANGO_INVALIDO");

        if (hasta.HasValue && hasta.Value < desde)
            throw new ReglaDeNegocioException(
                "El final del rango autorizado no puede ser menor que su inicio.",
                "RANGO_INVALIDO");

        var serie = SecuenciaECF.SerieDe(tipo);
        var secuencia = await _context.SecuenciasECF.FirstOrDefaultAsync(s => s.Serie == serie, ct);

        if (secuencia == null)
        {
            secuencia = new SecuenciaECF
            {
                Serie = serie,
                TipoECF = tipo,
                // Ultimo = desde - 1 para que la primera asignación entregue exactamente "desde".
                Ultimo = desde - 1,
                DesdeAutorizado = desde,
                HastaAutorizado = hasta,
                FechaActualizacion = DateTime.UtcNow
            };

            await _context.SecuenciasECF.AddAsync(secuencia, ct);
        }
        else
        {
            secuencia.DesdeAutorizado = desde;
            secuencia.HastaAutorizado = hasta;
            secuencia.FechaActualizacion = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);
        return secuencia;
    }

    /// <summary>Mayor secuencia realmente emitida para una serie (0 si no hay comprobantes).</summary>
    private async Task<long> ObtenerUltimoEmitidoAsync(string serie, CancellationToken ct)
    {
        var ultimo = await _context.ElectronicInvoices
            .Where(i => i.eNCF.StartsWith(serie))
            .OrderByDescending(i => i.eNCF)
            .Select(i => i.eNCF)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(ultimo) || ultimo.Length <= serie.Length)
            return 0L;

        return long.TryParse(ultimo[serie.Length..], out var numero) ? numero : 0L;
    }

    private async Task<SecuenciaECF?> CrearSerieAsync(TipoeCFType tipo, string serie, CancellationToken ct)
    {
        var nueva = new SecuenciaECF
        {
            Serie = serie,
            TipoECF = tipo,
            Ultimo = 0,
            DesdeAutorizado = 1,
            HastaAutorizado = null,
            FechaActualizacion = DateTime.UtcNow
        };

        await _context.SecuenciasECF.AddAsync(nueva, ct);

        try
        {
            await _context.SaveChangesAsync(ct);
            return nueva;
        }
        catch (DbUpdateException ex) when (MapeadorErroresEF.EsViolacionDeUnicidad(ex))
        {
            _context.Entry(nueva).State = EntityState.Detached;
            return null;
        }
    }
}
