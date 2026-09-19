using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace POS.UI.Controllers;

/// <summary>
/// Formulario de devolución de venta (adaptador HTTP del caso de uso). El navegador no decide
/// montos: aporta la venta, el motivo y (opcionalmente) las cantidades parciales por línea.
/// </summary>
public class RegistrarDevolucionForm
{
    [HiddenInput]
    public Guid? ClaveIdempotencia { get; set; }

    public int VentaId { get; set; }

    [BindProperty(Name = "Motivo")]
    public string? Motivo { get; set; }

    /// <summary>Cantidades parciales por línea (Id de VentaItem -> cantidad). Null = devolución total.</summary>
    public Dictionary<int, decimal>? Cantidades { get; set; }
}
