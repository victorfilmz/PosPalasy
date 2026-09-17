using System;
using System.Collections.Generic;
using POS.Application.DTOs;
using POS.Domain.Types;

namespace POS.Application.Validators;

public class ValidationResult
{
    public bool EsValido => Errores.Count == 0;
    public List<string> Errores { get; } = new();

    public void AgregarError(string mensaje) => Errores.Add(mensaje);
}

public static class EmitirFacturaValidator
{
    public static ValidationResult Validar(ElectronicInvoiceRequest request)
    {
        var result = new ValidationResult();

        if (request == null)
        {
            result.AgregarError("El request no puede ser nulo.");
            return result;
        }

        // Emisor
        if (string.IsNullOrWhiteSpace(request.Emisor?.RNC) || !RNC.IsValid(request.Emisor.RNC))
            result.AgregarError("El RNC del emisor es obligatorio y debe tener 9 u 11 dígitos numéricos.");

        if (string.IsNullOrWhiteSpace(request.Emisor?.RazonSocial))
            result.AgregarError("La razón social del emisor es obligatoria.");

        // Comprador (si tiene RNC, validarlo)
        if (!string.IsNullOrWhiteSpace(request.Comprador?.RNC) && !RNC.IsValid(request.Comprador.RNC))
            result.AgregarError("El RNC del comprador debe tener 9 u 11 dígitos numéricos si se especifica.");

        // eNCF
        if (string.IsNullOrWhiteSpace(request.eNCF) || !eNCF.IsValid(request.eNCF))
            result.AgregarError($"El eNCF '{request.eNCF}' es inválido. Debe tener exactamente 13 caracteres alfanuméricos.");

        // Items
        if (request.Items == null || request.Items.Count == 0)
        {
            result.AgregarError("La factura debe tener al menos un ítem.");
        }
        else
        {
            for (int i = 0; i < request.Items.Count; i++)
            {
                var item = request.Items[i];
                var linea = i + 1;

                if (string.IsNullOrWhiteSpace(item.Descripcion))
                    result.AgregarError($"Línea {linea}: La descripción del ítem es requerida.");

                if (item.Cantidad <= 0)
                    result.AgregarError($"Línea {linea}: La cantidad debe ser mayor que cero.");

                if (item.PrecioUnitario <= 0)
                    result.AgregarError($"Línea {linea}: El precio unitario debe ser mayor que cero.");

                if (item.Descuento < 0)
                    result.AgregarError($"Línea {linea}: El descuento no puede ser negativo.");
            }
        }

        return result;
    }
}

public static class AnulacionValidator
{
    public static ValidationResult Validar(AnulacionRequest request)
    {
        var result = new ValidationResult();

        if (request == null)
        {
            result.AgregarError("El request de anulación no puede ser nulo.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(request.RNCEmisor) || !RNC.IsValid(request.RNCEmisor))
            result.AgregarError("El RNC del emisor es obligatorio y debe tener 9 u 11 dígitos numéricos.");

        if (string.IsNullOrWhiteSpace(request.eNCFDesde) || !eNCF.IsValid(request.eNCFDesde))
            result.AgregarError("El eNCF inicial (Desde) es obligatorio y debe tener 13 caracteres alfanuméricos.");

        if (string.IsNullOrWhiteSpace(request.eNCFHasta) || !eNCF.IsValid(request.eNCFHasta))
            result.AgregarError("El eNCF final (Hasta) es obligatorio y debe tener 13 caracteres alfanuméricos.");

        if (request.CantidadSecuencias <= 0)
            result.AgregarError("La cantidad de secuencias a anular debe ser mayor que cero.");

        if (string.IsNullOrWhiteSpace(request.Motivo))
            result.AgregarError("El motivo de anulación es obligatorio.");

        return result;
    }
}
