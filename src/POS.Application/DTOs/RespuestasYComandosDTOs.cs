using System;
using System.Collections.Generic;
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

    /// <summary>Estado técnico local del comprobante (creado, validado, firmado, encolado, error…).</summary>
    public EstadoEmisionECF EstadoEmision { get; set; } = EstadoEmisionECF.Creada;

    /// <summary>Código HTTP devuelto por la DGII; null cuando no hubo respuesta alguna.</summary>
    public int? CodigoHttp { get; set; }

    /// <summary>Indica si el fallo admite reintento automático o exige intervención humana.</summary>
    public bool EsRecuperable { get; set; }

    public string? CodigoSeguridadeCF { get; set; }
    public string? Mensaje { get; set; }
    public DateTime FechaRecepcion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Estado fiscal emitido por la DGII en la recepción/resultado (Aceptado, Aceptado Condicional,
    /// Rechazado, En proceso). Es la fuente oficial: puede diferir del estado local consolidado.
    /// </summary>
    public string? EstadoDgii { get; set; }

    /// <summary>Mensajes devueltos por la DGII (motivos de rechazo, observaciones, etc.).</summary>
    public List<string> MensajesDgii { get; set; } = new();

    /// <summary>
    /// Marca oficial de la DGII: true = la secuencia del e-NCF NO puede reutilizarse; false = la
    /// secuencia puede reutilizarse (típico tras rechazo por error correctable). Null si la DGII
    /// no la reportó en esta respuesta.
    /// </summary>
    public bool? SecuenciaUtilizada { get; set; }
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

    /// <summary>e-NCF confirmado por la DGII en la respuesta.</summary>
    public string? eNCF { get; set; }

    /// <summary>
    /// Marca oficial de la DGII: true = la secuencia del e-NCF NO puede reutilizarse; false = la
    /// secuencia puede reutilizarse (típico tras rechazo por error correctable). Null si la DGII
    /// no la reportó en esta respuesta.
    /// </summary>
    public bool? SecuenciaUtilizada { get; set; }
}
