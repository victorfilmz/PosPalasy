using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Infrastructure.Persistence;

namespace POS.Infrastructure.Services.Dgii;

/// <summary>
/// Dueño único de la regla de la sub-fase 5.5: al confirmarse un rechazo con
/// <c>secuenciaUtilizada=false</c> (o sin marca explícita: la DGII solo envía true cuando consumió
/// el número), la secuencia del comprobante rechazado vuelve al pool de reutilización, con
/// auditoría en la misma operación. <c>secuenciaUtilizada=true</c> NUNCA libera.
/// </summary>
/// <remarks>
/// Los cuatro puntos donde la DGII confirma un rechazo (recepción RFCE, rechazo en recepción HTTP,
/// consulta e-CF por TrackId y consulta RFCE) llaman a este mismo dueño de la regla.
/// </remarks>
internal sealed class LiberadorSecuencias
{
    private readonly ISecuenciaLibreRepository? _secuenciasLibresRepo;
    private readonly IAuditoriaRepository? _auditoriaRepository;
    private readonly ILogger _logger;

    public LiberadorSecuencias(
        ISecuenciaLibreRepository? secuenciasLibresRepo,
        IAuditoriaRepository? auditoriaRepository,
        ILogger logger)
    {
        _secuenciasLibresRepo = secuenciasLibresRepo;
        _auditoriaRepository = auditoriaRepository;
        _logger = logger;
    }

    /// <summary>
    /// Libera la secuencia del comprobante al pool si el resultado recibido es un rechazo
    /// corregible. Sin repositorio configurado es una no-operación (compatibilidad con pruebas).
    /// </summary>
    public async Task LiberarSiCorrespondeAsync(
        ElectronicInvoice invoice,
        bool? secuenciaUtilizada,
        CancellationToken cancellationToken)
    {
        if (_secuenciasLibresRepo is null)
            return;

        if (invoice.Estado != EstadoFacturaElectronica.Rechazado)
            return;

        // true = la DGII consumió el número: NUNCA se libera.
        if (secuenciaUtilizada == true)
            return;

        var serie = SecuenciaECF.SerieDe(invoice.TipoeCF);
        var numero = PoliticaRecepcion.ParsearSecuencia(serie, invoice.eNCF);
        if (numero is null)
            return;

        var libre = await _secuenciasLibresRepo.LiberarAsync(
            serie,
            numero.Value,
            invoice.Id,
            invoice.MotivoRechazo ?? invoice.MensajesDgii ?? string.Empty,
            liberadaPor: "sistema",
            ct: cancellationToken);

        _logger.LogInformation(
            "Secuencia {ENCF} devuelta al pool tras rechazo (fila {FilaId}). Motivo DGII: {Motivo}",
            libre.ENCF,
            libre.Id,
            libre.MotivoRechazo);

        if (_auditoriaRepository is not null)
        {
            await _auditoriaRepository.RegistrarAsync(new AuditoriaCambio
            {
                Usuario = "sistema",
                Entidad = "ElectronicInvoice",
                Campo = "SecuenciaUtilizada",
                ValorAnterior = invoice.eNCF,
                ValorNuevo = "liberada (secuenciaUtilizada=" + (secuenciaUtilizada?.ToString() ?? "null") + ")",
                Motivo = Truncar(invoice.MotivoRechazo ?? invoice.MensajesDgii ?? "Rechazo DGII", 500)
            }, cancellationToken);
        }
    }

    private static string Truncar(string texto, int maximo) =>
        texto.Length <= maximo ? texto : texto[..maximo];
}
