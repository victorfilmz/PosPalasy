using System;

namespace POS.Domain.Common;

/// <summary>
/// Violación de una regla de negocio (no un fallo técnico). El caso de uso que la invoca la traduce
/// a una respuesta tipada para el usuario; nunca debe confundirse con un error de infraestructura.
/// </summary>
public class ReglaDeNegocioException : Exception
{
    public ReglaDeNegocioException(string mensaje, string codigo = "REGLA_NEGOCIO")
        : base(mensaje)
    {
        Codigo = codigo;
    }

    /// <summary>Código estable para que la capa de presentación no dependa del texto del mensaje.</summary>
    public string Codigo { get; }
}
