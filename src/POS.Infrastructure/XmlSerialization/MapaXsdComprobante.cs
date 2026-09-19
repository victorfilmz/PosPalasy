using POS.Domain.Common;
using POS.Domain.Types;

namespace POS.Infrastructure.XmlSerialization;

/// <summary>
/// Mapa oficial <c>TipoeCF → XSD</c> para la validación de esquema del comprobante.
/// </summary>
/// <remarks>
/// Los XSD viven en <c>documentacion xsd/</c> del repositorio y se copian al directorio de salida
/// de la aplicación (referencia <c>None/CopyToOutputDirectory</c> del csproj). La validación usa el
/// esquema del tipo del comprobante, no un archivo único: cada tipo tiene su propio contrato.
/// La resolución prueba primero el directorio de salida y luego la raíz del contenido (escenarios
/// de desarrollo y de publicación).
/// </remarks>
public static class MapaXsdComprobante
{
    private static readonly Dictionary<TipoeCFType, string> ArchivosPorTipo = new()
    {
        [TipoeCFType.FacturaCreditoFiscal] = "e-CF 31 v.1.0.xsd",
        [TipoeCFType.FacturaConsumo] = "e-CF 32 v.1.0.xsd",
        [TipoeCFType.NotaDebito] = "e-CF 33 v.1.0.xsd",
        [TipoeCFType.NotaCredito] = "e-CF 34 v.1.0.xsd",
        [TipoeCFType.Compras] = "e-CF 41 v.1.0.xsd",
        [TipoeCFType.GastosMenores] = "e-CF 43 v.1.0.xsd",
        [TipoeCFType.RegimenesEspeciales] = "e-CF 44 v.1.0.xsd",
        [TipoeCFType.Gubernamental] = "e-CF 45 v.1.0.xsd",
        [TipoeCFType.Exportaciones] = "e-CF 46 v.1.0.xsd",
        [TipoeCFType.PagosAlExterior] = "e-CF 47 v.1.0.xsd"
    };

    /// <summary>Nombre del archivo XSD oficial del tipo indicado.</summary>
    public static string ArchivoDe(TipoeCFType tipo) =>
        ArchivosPorTipo.TryGetValue(tipo, out var archivo)
            ? archivo
            : throw new ReglaDeNegocioException(
                $"El tipo de comprobante {tipo} no tiene XSD registrado en el mapa.",
                "TIPO_ECF_SIN_XSD");

    /// <summary>
    /// Ruta absoluta del XSD del tipo indicado, buscando en los directorios estándar.
    /// Devuelve null si el archivo no existe en ninguna ubicación conocida.
    /// </summary>
    public static string? ResolverRuta(TipoeCFType tipo)
    {
        var nombre = ArchivoDe(tipo);

        var candidatas = new[]
        {
            // Directorio de salida de la aplicación (copia al compilar).
            Path.Combine(AppContext.BaseDirectory, "documentacion xsd", nombre),
            // Raíz del contenido en desarrollo.
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "documentacion xsd", nombre)),
            // Directorio de trabajo de la aplicación publicada.
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), nombre))
        };

        return candidatas.FirstOrDefault(File.Exists);
    }
}
