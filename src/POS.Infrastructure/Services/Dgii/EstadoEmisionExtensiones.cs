using POS.Domain.Entities;
using POS.Domain.Enums;

namespace POS.Infrastructure.Services.Dgii;

/// <summary>
/// Dueño único de las transiciones de la máquina de estados de emisión. Repetir el mismo estado
/// (un segundo intento con la misma conclusión) no es una transición inválida: lo que la máquina
/// prohíbe es retroceder o reabrir un estado terminal.
/// </summary>
internal static class EstadoEmisionExtensiones
{
    /// <summary>Avanza el estado técnico de emisión validando la transición.</summary>
    public static void AvanzarEstado(this ElectronicInvoice invoice, EstadoEmisionECF nuevoEstado)
    {
        if (invoice.EstadoEmision == nuevoEstado)
            return;

        invoice.EstadoEmision = invoice.EstadoEmision.ValidarTransicion(nuevoEstado);
    }
}
