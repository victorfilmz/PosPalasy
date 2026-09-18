using System;
using System.Threading;
using System.Threading.Tasks;

namespace POS.Application.Interfaces;

/// <summary>
/// Frontera transaccional del sistema. La capa de aplicación define unidades de trabajo atómicas
/// sin conocer el proveedor de datos; la infraestructura decide cómo se abre y se confirma.
/// </summary>
/// <remarks>
/// Regla de diseño: la comunicación con servicios externos (DGII) NUNCA ocurre dentro de la
/// transacción. Primero se persiste el hecho local y se confirma; después se intenta la transmisión.
/// </remarks>
public interface IUnidadDeTrabajo
{
    /// <summary>
    /// Ejecuta la operación dentro de una transacción: si termina sin excepción se confirma; ante
    /// cualquier excepción se revierte por completo y la excepción se propaga.
    /// </summary>
    Task<T> EnTransaccionAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default);

    /// <summary>Variante sin valor de retorno.</summary>
    Task EnTransaccionAsync(Func<CancellationToken, Task> operacion, CancellationToken ct = default);
}
