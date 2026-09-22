using System;
using Microsoft.EntityFrameworkCore;
using POS.Application.CasosDeUso.Ventas;
using POS.Domain.Common;

namespace POS.Infrastructure.Persistence;

/// <summary>
/// Traduce los errores de la base de datos a excepciones que la capa de aplicación entiende, sin
/// que ésta conozca EF Core ni el proveedor. Las violaciones de unicidad de nuestros índices son
/// conflictos de negocio esperados (idempotencia, numeración fiscal), no fallos técnicos.
/// </summary>
internal static class MapeadorErroresEF
{
    /// <summary>
    /// Convierte una <see cref="DbUpdateException"/> en la excepción de dominio correspondiente.
    /// Si no reconoce la causa, devuelve la excepción original para que no se oculten fallos reales.
    /// </summary>
    public static Exception Traducir(DbUpdateException ex)
    {
        var detalle = Describir(ex);

        if (Coincide(detalle, "ClaveIdempotencia"))
            return new ConflictoDeUnicidadException(
                "La venta ya fue registrada con esta misma solicitud.",
                CodigosConflicto.IdempotenciaVenta);

        if (Coincide(detalle, "ElectronicInvoices.eNCF") || Coincide(detalle, "IX_ElectronicInvoices_eNCF"))
            return new ConflictoDeUnicidadException(
                "El número de comprobante asignado ya existe: otra venta lo tomó primero.",
                CodigosConflicto.EncfDuplicado);

        if (Coincide(detalle, "IX_ElectronicInvoices_DevolucionId") || Coincide(detalle, "ElectronicInvoices.DevolucionId"))
            return new ConflictoDeUnicidadException(
                "La devolución ya tiene su nota de crédito emitida.",
                CodigosConflicto.IdempotenciaNotaCredito);

        if (Coincide(detalle, "SecuenciasECF") || Coincide(detalle, "IX_SecuenciasECF_Serie"))
            return new ConflictoDeUnicidadException(
                "La serie de comprobantes ya está registrada en la base de datos.",
                CodigosConflicto.Generico);

        if (Coincide(detalle, "Usuarios.NombreUsuario"))
            return new ConflictoDeUnicidadException(
                "Ya existe un usuario con ese nombre.",
                CodigosConflicto.Generico);

        return ex;
    }

    private static bool Coincide(string detalle, string patron) =>
        detalle.Contains(patron, StringComparison.OrdinalIgnoreCase);

    /// <summary>Concatena los mensajes de toda la cadena de excepciones internas del proveedor.</summary>
    private static string Describir(Exception ex)
    {
        var mensaje = ex.Message;
        var actual = ex.InnerException;

        while (actual != null)
        {
            mensaje += " | " + actual.Message;
            actual = actual.InnerException;
        }

        return mensaje;
    }

    /// <summary>Indica si la excepción (o alguna interna) corresponde a una violación de unicidad.</summary>
    public static bool EsViolacionDeUnicidad(DbUpdateException ex)
    {
        var detalle = Describir(ex);

        return detalle.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || detalle.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            || detalle.Contains("Violation of UNIQUE KEY", StringComparison.OrdinalIgnoreCase)
            || detalle.Contains("2601", StringComparison.Ordinal)
            || detalle.Contains("2627", StringComparison.Ordinal);
    }
}
