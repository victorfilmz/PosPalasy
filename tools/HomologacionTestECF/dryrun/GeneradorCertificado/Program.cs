// Generador de certificado de prueba para el DRY-RUN del verificador de homologación.
// Ejecutar: dotnet run --project tools/HomologacionTestECF/dryrun/GeneradorCertificado
// NO es apto para la DGII: solo para ensayar el verificador contra el servidor falso.
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

var ruta = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "PosPalasy", "certificados", "emisor.pfx");
const string password = "ensayo-homologacion";

Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);

using var rsa = RSA.Create(2048);
var req = new CertificateRequest(
    "CN=Ensayo Homologacion, O=PosPalasy", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

File.WriteAllBytes(ruta, cert.Export(X509ContentType.Pfx, password));
Console.WriteLine($"Certificado de prueba generado: {ruta}");
Console.WriteLine($"Contraseña: {password} (vence en 30 días — solo para dry-run, NO para la DGII)");
