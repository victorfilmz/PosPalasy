using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;
using POS.Domain.Types;

namespace POS.Domain.Repositories;

/// <summary>
/// Contrato de asignación de secuencias fiscales (eNCF). La implementación es responsable de
/// garantizar la unicidad bajo concurrencia: la numeración fiscal no admite duplicados.
/// </summary>
public interface ISecuenciaECFRepository
{
    /// <summary>
    /// Reserva y devuelve el siguiente eNCF de la serie indicada. Debe ejecutarse dentro de la
    /// transacción de la venta y resolver las carreras con reintento acotado.
    /// </summary>
    /// <exception cref="Common.ReglaDeNegocioException">
    /// Si la serie no tiene secuencia configurada, si se agotó el rango autorizado o si no fue
    /// posible asignar un número tras varios intentos por concurrencia.
    /// </exception>
    Task<string> AsignarSiguienteENCFAsync(TipoeCFType tipo, CancellationToken ct = default);

    /// <summary>Estado actual de una serie (para configuración y diagnóstico).</summary>
    Task<SecuenciaECF?> ObtenerAsync(string serie, CancellationToken ct = default);

    /// <summary>Asegura que la serie existe con el rango autorizado indicado.</summary>
    Task<SecuenciaECF> AsegurarSerieAsync(
        TipoeCFType tipo,
        long desde = 1,
        long? hasta = null,
        CancellationToken ct = default);
}
