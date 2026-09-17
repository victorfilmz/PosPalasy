using System;
using System.Security.Cryptography;
using System.Text;

namespace POS.Application.Services;

public interface IHashGenerator
{
    /// <summary>
    /// Genera el CodigoSeguridadeCF (primeros 6 caracteres en Base64 del hash SHA-256 del XML firmado).
    /// </summary>
    string Generate(string xmlContent);
}

public class HashGenerator : IHashGenerator
{
    public string Generate(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
            throw new ArgumentException("El contenido XML no puede estar vacío para calcular el hash.", nameof(xmlContent));

        var bytes = Encoding.UTF8.GetBytes(xmlContent.Trim());
        var hashBytes = SHA256.HashData(bytes);
        var base64 = Convert.ToBase64String(hashBytes);

        // DGII especifica exactamente los primeros 6 caracteres en Base64
        return base64[..6];
    }
}
