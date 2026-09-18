namespace POS.Domain.Common;

/// <summary>
/// Colisión con una restricción de unicidad de la base de datos (clave de idempotencia, eNCF,
/// usuario). La infraestructura la traduce desde el error del proveedor para que la capa de
/// aplicación pueda reaccionar sin conocer EF Core.
/// </summary>
public class ConflictoDeUnicidadException : ReglaDeNegocioException
{
    public ConflictoDeUnicidadException(string mensaje, string codigo = "CONFLICTO_UNICIDAD")
        : base(mensaje, codigo)
    {
    }
}
