using System;
using POS.Domain.Common;
using POS.Domain.Types;

namespace POS.Domain.Entities;

/// <summary>
/// Representa una anulación de e-NCF ante la DGII (esquema ANECF).
/// </summary>
public class Anulacion : BaseEntity
{
    public string RNCEmisor { get; set; } = string.Empty;
    public TipoeCFType TipoeCF { get; set; } = TipoeCFType.FacturaConsumo;
    public string eNCFDesde { get; set; } = string.Empty;
    public string eNCFHasta { get; set; } = string.Empty;
    public int CantidadSecuencias { get; set; } = 1;

    public int CodigoMotivoAnulacion { get; set; } // 1=Deterioro, 2=Extravío, 3=Error de impresión, etc.
    public string Motivo { get; set; } = string.Empty;

    public string XMLContent { get; set; } = string.Empty;
    public string? TrackId { get; set; }
    public DateTime FechaSolicitud { get; set; } = DateTime.UtcNow;
    public DateTime? FechaRespuesta { get; set; }
    public bool Aprobada { get; set; } = false;
    public string? MensajeRespuesta { get; set; }
}

/// <summary>
/// Parámetros de configuración del sistema (URLs DGII, certificado, etc.).
/// </summary>
public class Configuracion : BaseEntity
{
    public string Clave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
}
