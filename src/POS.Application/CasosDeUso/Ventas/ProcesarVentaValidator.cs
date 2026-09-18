using System;
using System.Linq;
using POS.Domain.Common;
using POS.Domain.Types;

namespace POS.Application.CasosDeUso.Ventas;

/// <summary>
/// Validación de forma del comando de venta. Todo lo que dependa de datos del catálogo (existencias,
/// precio, estado del producto) se valida con los datos reales en el manejador, no aquí.
/// </summary>
public static class ProcesarVentaValidator
{
    public static void Validar(ProcesarVentaCommand? command)
    {
        if (command == null)
            throw new ReglaDeNegocioException("La solicitud de venta es nula.", "SOLICITUD_NULA");

        if (command.ClaveIdempotencia == Guid.Empty)
            throw new ReglaDeNegocioException(
                "La solicitud no incluye clave de idempotencia: no se puede garantizar que la venta no se duplique.",
                "CLAVE_IDEMPOTENCIA_REQUERIDA");

        if (command.UsuarioId <= 0 || string.IsNullOrWhiteSpace(command.UsuarioNombre))
            throw new ReglaDeNegocioException(
                "No se pudo identificar al usuario que registra la venta.",
                "USUARIO_REQUERIDO");

        if (command.Items == null || command.Items.Count == 0)
            throw new ReglaDeNegocioException("El carrito de compras no contiene productos.", "CARRITO_VACIO");

        foreach (var item in command.Items)
        {
            if (item.ProductoId <= 0)
                throw new ReglaDeNegocioException(
                    "Toda línea de la venta debe referirse a un producto del catálogo.",
                    "PRODUCTO_REQUERIDO");

            if (item.Cantidad <= 0)
                throw new ReglaDeNegocioException(
                    "La cantidad de cada línea debe ser mayor que cero.",
                    "CANTIDAD_INVALIDA");

            if (item.Descuento < 0)
                throw new ReglaDeNegocioException(
                    "El descuento no puede ser negativo.",
                    "DESCUENTO_INVALIDO");
        }

        var repetidos = command.Items
            .GroupBy(i => i.ProductoId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (repetidos.Count > 0)
            throw new ReglaDeNegocioException(
                $"El carrito envió el producto #{string.Join(", #", repetidos)} más de una vez. " +
                "Consolide la cantidad en una sola línea antes de cobrar.",
                "PRODUCTO_REPETIDO");

        if (command.TipoeCF != TipoeCFType.FacturaCreditoFiscal && command.TipoeCF != TipoeCFType.FacturaConsumo)
            throw new ReglaDeNegocioException(
                "El terminal de punto de venta solo emite Factura de Crédito Fiscal (31) o Factura de Consumo (32). " +
                "Las notas de crédito y débito se emiten desde el módulo de facturación.",
                "TIPO_ECF_NO_SOPORTADO");

        // La Factura de Crédito Fiscal exige identificar al comprador: sin RNC la DGII la rechaza.
        if (command.TipoeCF == TipoeCFType.FacturaCreditoFiscal)
        {
            if (string.IsNullOrWhiteSpace(command.RNCComprador))
                throw new ReglaDeNegocioException(
                    "La Factura de Crédito Fiscal (e-CF 31) requiere el RNC del comprador.",
                    "RNC_COMPRADOR_REQUERIDO");

            if (!RNC.IsValid(command.RNCComprador))
                throw new ReglaDeNegocioException(
                    "El RNC del comprador no es válido: debe tener 9 u 11 dígitos numéricos.",
                    "RNC_COMPRADOR_INVALIDO");
        }

        foreach (var pago in command.Pagos ?? Enumerable.Empty<PagoVentaCommand>())
        {
            if (pago.Monto <= 0)
                throw new ReglaDeNegocioException(
                    "Cada pago debe tener un monto mayor que cero.",
                    "PAGO_INVALIDO");
        }

        if (command.MontoRecibido is < 0)
            throw new ReglaDeNegocioException(
                "El efectivo recibido no puede ser negativo.",
                "MONTO_RECIBIDO_INVALIDO");
    }
}
