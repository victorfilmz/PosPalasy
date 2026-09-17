using System;
using POS.Domain.Common;

namespace POS.Domain.Entities;

/// <summary>
/// Cola de emisión asíncrona y contingencia offline ante la DGII.
/// Permite facturar localmente sin demora y sincronizar en segundo plano.
/// </summary>
public class EmisionDGIIQueue : BaseEntity
{
    public int FacturaId { get; set; }
    public string eNCF { get; set; } = string.Empty;
    public string XmlFirmado { get; set; } = string.Empty;
    public int Intentos { get; set; } = 0;
    public string? UltimoError { get; set; }
    public bool EnviadoExitosamente { get; set; } = false;
    public DateTime FechaRegistro { get; set; } = DateTime.UtcNow;
    public DateTime? FechaUltimoIntento { get; set; }
}
