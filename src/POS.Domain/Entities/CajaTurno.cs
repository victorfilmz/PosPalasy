using System;
using System.Collections.Generic;
using POS.Domain.Common;
using POS.Domain.Enums;

namespace POS.Domain.Entities;

/// <summary>
/// Representa un turno de caja de un cajero (apertura, movimientos, ventas y cierre / arqueo).
/// </summary>
public class CajaTurno : BaseEntity
{
    public int SucursalId { get; set; }
    public string Cajero { get; set; } = "Cajero Principal";

    /// <summary>
    /// Usuario autenticado que abrió el turno. Es la referencia fiable para imputar las ventas al
    /// arqueo correcto (el nombre escrito a mano no es verificable).
    /// </summary>
    public int? UsuarioId { get; set; }

    /// <summary>Nombre de usuario (login) del responsable del turno.</summary>
    public string? UsuarioNombre { get; set; }
    public DateTime FechaApertura { get; set; } = DateTime.UtcNow;
    public DateTime? FechaCierre { get; set; }
    public TurnoCajaEstado Estado { get; set; } = TurnoCajaEstado.Abierto;

    // Fondos y Ventas
    public decimal MontoInicial { get; set; } // Fondo de caja para cambio
    public decimal VentasEfectivo { get; set; }
    public decimal VentasTarjeta { get; set; }
    public decimal VentasTransferencia { get; set; }
    public decimal TotalVentas { get; set; }
    public int CantidadTransacciones { get; set; }

    // Movimientos adicionales de efectivo
    public decimal TotalEntradasEfectivo { get; set; }
    public decimal TotalSalidasEfectivo { get; set; }

    // Arqueo y Cierre
    public decimal? MontoRealCierre { get; set; } // Efectivo físico contado al cierre
    public decimal? Diferencia { get; set; } // MontoRealCierre - EfectivoEsperado

    public string? Observaciones { get; set; }

    // Colecciones de navegación
    public ICollection<MovimientoCaja> Movimientos { get; set; } = new List<MovimientoCaja>();
    public ICollection<Venta> Ventas { get; set; } = new List<Venta>();

    /// <summary>
    /// Total de efectivo que debería haber en caja físicamente.
    /// Efectivo Esperado = MontoInicial + VentasEfectivo + Entradas - Salidas.
    /// </summary>
    public decimal EfectivoEsperado =>
        MontoInicial + VentasEfectivo + TotalEntradasEfectivo - TotalSalidasEfectivo;
}

/// <summary>
/// Movimiento puntual de entrada o salida de efectivo durante el turno (ej. pago rápido de flete, recarga de cambio).
/// </summary>
public class MovimientoCaja : BaseEntity
{
    public int CajaTurnoId { get; set; }
    public CajaTurno? CajaTurno { get; set; }

    public TipoMovimientoCaja Tipo { get; set; } = TipoMovimientoCaja.Entrada;
    public decimal Monto { get; set; }
    public string Concepto { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
