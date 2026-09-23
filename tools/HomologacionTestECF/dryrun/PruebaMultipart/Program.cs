// Prueba de aislamiento: multipart real → ¿qué extrajo ExtraerXml del servidor?
using System.Text;

var handler = new HttpClientHandler();
using var http = new HttpClient(handler);

// Construye un multipart idéntico al del DgiiApiClient y lo envía a la DGII falsa.
var xml = "<ECF><Encabezado><eNCF>E320000000001</eNCF></Encabezado><Signature>dato</Signature></ECF>";
using var contenido = new MultipartFormDataContent
{
    { new StringContent(xml, Encoding.UTF8, "text/xml"), "xml", "13100000001E320000000001.xml" }
};
var multipart = await contenido.ReadAsStringAsync();
Console.WriteLine("== multipart tal como sale del cliente ==");
Console.WriteLine(multipart.Replace("\r\n", "\\r\\n\n"));

// Reproduce la extracción del servidor falso.
var inicio = multipart.IndexOf("<?xml", StringComparison.Ordinal);
Console.WriteLine($"\nIndex de '<?xml': {inicio}");
if (inicio >= 0)
{
    var fin = multipart.IndexOf("--", inicio, StringComparison.Ordinal);
    Console.WriteLine($"Index del próximo '--': {fin}");
    Console.WriteLine($"XML extraído: [{multipart[inicio..(fin > inicio ? fin : multipart.Length)].TrimEnd('\r', '\n')}]");
}
