using System;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using POS.Application.Validators;

namespace POS.Infrastructure.XmlSerialization;

public interface IXmlValidator
{
    ValidationResult Validate(string xmlContent, string xsdPath);
}

/// <summary>
/// Validador de documentos XML contra esquemas XSD oficiales de DGII utilizando XmlSchemaSet y XmlReader nativos.
/// </summary>
public class XmlValidator : IXmlValidator
{
    public ValidationResult Validate(string xmlContent, string xsdPath)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
            throw new ArgumentException("El contenido XML no puede estar vacío.", nameof(xmlContent));

        if (!File.Exists(xsdPath))
            throw new FileNotFoundException($"El archivo de esquema XSD no fue encontrado en la ruta: '{xsdPath}'", xsdPath);

        var result = new ValidationResult();

        try
        {
            var schemas = new XmlSchemaSet();
            schemas.Add(null, xsdPath);
            schemas.Compile();

            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                Schemas = schemas
            };

            settings.ValidationEventHandler += (sender, args) =>
            {
                if (args.Severity == XmlSeverityType.Error)
                {
                    result.AgregarError($"Error XSD (Línea {args.Exception.LineNumber}, Pos {args.Exception.LinePosition}): {args.Message}");
                }
            };

            using var stringReader = new StringReader(xmlContent);
            using var reader = XmlReader.Create(stringReader, settings);

            while (reader.Read()) { }
        }
        catch (XmlSchemaValidationException ex)
        {
            result.AgregarError($"Fallo de validación de esquema: {ex.Message}");
        }
        catch (XmlException ex)
        {
            result.AgregarError($"XML mal formado: {ex.Message}");
        }
        catch (Exception ex)
        {
            result.AgregarError($"Error inesperado durante validación XSD: {ex.Message}");
        }

        return result;
    }
}
