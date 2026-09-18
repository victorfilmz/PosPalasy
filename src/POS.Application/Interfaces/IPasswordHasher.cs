namespace POS.Application.Interfaces;

/// <summary>Resultado de verificar una contraseña contra su hash almacenado.</summary>
public enum ResultadoVerificacionPassword
{
    /// <summary>La contraseña no corresponde al hash.</summary>
    Fallida = 0,

    /// <summary>La contraseña es correcta y el hash está vigente.</summary>
    Correcta = 1,

    /// <summary>
    /// La contraseña es correcta pero el hash se generó con parámetros obsoletos
    /// (menos iteraciones o formato anterior) y debe regenerarse.
    /// </summary>
    CorrectaRequiereRehash = 2
}

/// <summary>
/// Servicio de hashing de contraseñas. La implementación usa un algoritmo de derivación de clave
/// con sal aleatoria por contraseña (nunca hash reversible ni texto plano) y reporta si el hash
/// almacenado quedó obsoleto, para poder migrarlo sin invalidar la contraseña del usuario.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Genera el hash de una contraseña en claro.</summary>
    string Hash(string passwordEnClaro);

    /// <summary>
    /// Verifica una contraseña contra un hash almacenado.
    /// Nunca lanza: un hash nulo, vacío o con formato desconocido se reporta como fallido.
    /// </summary>
    ResultadoVerificacionPassword Verify(string? passwordHash, string passwordEnClaro);
}
