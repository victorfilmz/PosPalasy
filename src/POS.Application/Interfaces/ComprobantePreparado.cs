using POS.Application.DTOs;
using POS.Domain.Enums;

namespace POS.Application.Interfaces;

/// <summary>
/// Resultado de registrar un comprobante en la base local, antes de cualquier transmisión.
/// Permite separar "comprobante generado y persistido" de "comprobante aceptado por la DGII".
/// </summary>
/// <param name="ElectronicInvoiceId">Identificador local del comprobante.</param>
/// <param name="eNCF">Número de comprobante fiscal asignado.</param>
/// <param name="EstadoEmision">Estado del eje técnico local (creado, validado, firmado…).</param>
/// <param name="EstadoFiscal">Estado fiscal: sin enviar mientras no exista transmisión.</param>
/// <param name="Xml">Contenido del comprobante tal como se persistió localmente.</param>
public sealed record ComprobantePreparado(
    int ElectronicInvoiceId,
    string eNCF,
    EstadoEmisionECF EstadoEmision,
    EstadoFacturaElectronica EstadoFiscal,
    string Xml);

/// <summary>
/// Datos completos para construir y registrar localmente un comprobante electrónico.
/// </summary>
/// <param name="Request">Datos fiscales del comprobante (totales ya calculados por el dominio).</param>
/// <param name="VentaId">Venta de origen, si el comprobante nace de una venta de POS.</param>
public sealed record PrepararComprobanteCommand(ElectronicInvoiceRequest Request, int? VentaId);
