using System;
using POS.Domain.Enums;
using POS.Domain.Types;

namespace POS.Application.DTOs;

public class AnulacionRequest
{
    public string RNCEmisor { get; set; } = string.Empty;
    public TipoeCFType TipoeCF { get; set; } = TipoeCFType.FacturaConsumo;
    public string eNCFDesde { get; set; } = string.Empty;
    public string eNCFHasta { get; set; } = string.Empty;
    public int CantidadSecuencias { get; set; } = 1;
    public int CodigoMotivoAnulacion { get; set; } = 1;
    public string Motivo { get; set; } = string.Empty;
}

public class ElectronicInvoiceResponse
{
    public bool Exitoso { get; set; }
    public string eNCF { get; set; } = string.Empty;
    public string? TrackId { get; set; }
    public EstadoFacturaElectronica Estado { get; set; } = EstadoFacturaElectronica.EnProceso;
    public string? CodigoSeguridadeCF { get; set; }
    public string? Mensaje { get; set; }
    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;
}

public class AnulacionResponse
{
    public bool Exitoso { get; set; }
    public string? TrackId { get; set; }
    public string? Mensaje { get; set; }
    public DateTime FechaProcesamiento { get; set; } = DateTime.UtcNow;
}

public class EstadoFacturaResponse
{
    public string eNCF { get; set; } = string.Empty;
    public string? TrackId { get; set; }
    public EstadoFacturaElectronica Estado { get; set; }
    public string? MensajeDGII { get; set; }
    public string? ARECFXML { get; set; }
    public string? ACECFXML { get; set; }
}

public class DgiiApiResponse
{
    public bool EsExitoso { get; set; }
    public int CodigoHttp { get; set; }
    public string? TrackId { get; set; }
    public string? Estado { get; set; }
    public string? Mensaje { get; set; }
    public string? RawResponse { get; set; }
}
