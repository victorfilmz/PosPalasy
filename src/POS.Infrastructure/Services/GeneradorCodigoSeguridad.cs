using System.Security.Cryptography;

namespace POS.Infrastructure.Services;

/// <summary>Contrato del generador del código de seguridad del e-CF.</summary>
public interface ISecurityCodeGenerator
{
    /// <summary>Genera un código de seguridad de 6 caracteres alfanuméricos mayúsculas.</summary>
    string Generar();
}

/// <summary>
/// Generador del <c>CodigoSeguridadeCF</c>: 6 caracteres alfanuméricos asignados por comprobante.
/// </summary>
/// <remarks>
/// El XSD oficial del RFCE exige exactamente 6 caracteres ("Hash generado en factura de consumo
/// original") y la API de consulta lo usa como parámetro <c>Cod_Seguridad_eCF</c>. No es un hash
/// derivado del documento: es un código por comprobante que la DGII cruza con el RNC y el eNCF.
/// Se genera con el generador criptográfico del sistema (no aleatoriedad predecible) y se persiste
/// junto al comprobante: es el dato que el emisor debe poder reportar después de la emisión.
/// </remarks>
public sealed class GeneradorCodigoSeguridad : ISecurityCodeGenerator
{
    /// <summary>Alfabeto sin caracteres ambiguos (sin 0/O, 1/I/L) para lectura y soporte telefónico.</summary>
    private const string Alfabeto = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string Generar()
    {
        Span<char> codigo = stackalloc char[6];
        Span<byte> aleatorio = stackalloc byte[6];

        RandomNumberGenerator.Fill(aleatorio);

        for (var i = 0; i < codigo.Length; i++)
            codigo[i] = Alfabeto[aleatorio[i] % Alfabeto.Length];

        return new string(codigo);
    }
}
