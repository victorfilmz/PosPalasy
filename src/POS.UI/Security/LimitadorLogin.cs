using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace POS.UI.Security;

/// <summary>
/// Defensa en profundidad del login frente a exposición pública: limita los intentos fallidos
/// por ventana deslizante, tanto por IP (máquina atacante) como por cuenta (diccionario sobre un
/// usuario). Complementa el lockout de 5 intentos por cuenta de <c>Usuario.BloqueadoHasta</c>
/// (que protege la cuenta pero no la máquina) y el antiforgery (que solo un atacante con
/// navegador no salta).
///
/// Sin dependencias externas: contador concurrente con ventana fija. Un ataque distribuido
/// (muchas IPs) lo mitiga el límite por cuenta; un ataque por fuerza bruta desde una IP lo
/// mitiga el límite por IP.
/// </summary>
public sealed class LimitadorLogin
{
    /// <summary>Intentos fallidos permitidos por IP dentro de la ventana.</summary>
    public const int MaxIntentosPorIp = 20;

    /// <summary>Intentos fallidos permitidos por cuenta dentro de la ventana (refuerza el lockout).</summary>
    public const int MaxIntentosPorCuenta = 10;

    /// <summary>Ventana de conteo de intentos fallidos.</summary>
    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(10);

    /// <summary>Cuánto queda bloqueada una IP/cuenta al superar su límite.</summary>
    public static readonly TimeSpan BloqueoAdicional = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, RegistroIntentos> _porIp = new();
    private readonly ConcurrentDictionary<string, RegistroIntentos> _porCuenta = new();

    private sealed class RegistroIntentos
    {
        public readonly List<DateTime> MarcasUtc = new();
        public DateTime BloqueadoHastaUtc;
    }

    /// <summary>
    /// ¿Permite intentarlo? No consume ningún intento: los intentos los marcan los fallos.
    /// </summary>
    public bool Permitir(string? ip, string? cuenta, DateTime ahoraUtc)
    {
        if (_porIp.TryGetValue(ClaveIp(ip), out var regIp) && regIp.BloqueadoHastaUtc > ahoraUtc)
            return false;
        if (_porCuenta.TryGetValue(ClaveCuenta(cuenta), out var regCuenta) && regCuenta.BloqueadoHastaUtc > ahoraUtc)
            return false;
        return true;
    }

    /// <summary>Segundos restantes de bloqueo (0 = no bloqueado). Para responder con espera honesta.</summary>
    public int SegundosRestantes(string? ip, string? cuenta, DateTime ahoraUtc)
    {
        var restantes = 0;
        if (_porIp.TryGetValue(ClaveIp(ip), out var regIp) && regIp.BloqueadoHastaUtc > ahoraUtc)
            restantes = (int)(regIp.BloqueadoHastaUtc - ahoraUtc).TotalSeconds;
        if (_porCuenta.TryGetValue(ClaveCuenta(cuenta), out var regCuenta) && regCuenta.BloqueadoHastaUtc > ahoraUtc)
            restantes = Math.Max(restantes, (int)(regCuenta.BloqueadoHastaUtc - ahoraUtc).TotalSeconds);
        return restantes;
    }

    /// <summary>Registra un intento fallido; al superar el límite activa el bloqueo temporal.</summary>
    public void RegistrarFallo(string? ip, string? cuenta, DateTime ahoraUtc)
    {
        RegistrarFalloEn(_porIp, ClaveIp(ip), MaxIntentosPorIp, ahoraUtc);
        RegistrarFalloEn(_porCuenta, ClaveCuenta(cuenta), MaxIntentosPorCuenta, ahoraUtc);
    }

    /// <summary>Limpia los intentos fallidos de la cuenta tras un inicio de sesión exitoso (no el de la IP).</summary>
    public void RegistrarExito(string? ip, string? cuenta)
    {
        _porCuenta.TryRemove(ClaveCuenta(cuenta), out _);
    }

    /// <summary>Purga de registros viejos (llamado periódicamente para no crecer indefinidamente).</summary>
    public void Purgar(DateTime ahoraUtc)
    {
        PurgarDiccionario(_porIp, ahoraUtc);
        PurgarDiccionario(_porCuenta, ahoraUtc);
    }

    private static void RegistrarFalloEn(
        ConcurrentDictionary<string, RegistroIntentos> diccionario, string clave, int maximo, DateTime ahoraUtc)
    {
        var registro = diccionario.GetOrAdd(clave, _ => new RegistroIntentos());
        lock (registro)
        {
            registro.MarcasUtc.Add(ahoraUtc);
            registro.MarcasUtc.RemoveAll(m => ahoraUtc - m > Ventana);
            if (registro.MarcasUtc.Count >= maximo && registro.BloqueadoHastaUtc <= ahoraUtc)
                registro.BloqueadoHastaUtc = ahoraUtc + BloqueoAdicional;
        }
    }

    private static void PurgarDiccionario(ConcurrentDictionary<string, RegistroIntentos> diccionario, DateTime ahoraUtc)
    {
        foreach (var (clave, registro) in diccionario)
        {
            lock (registro)
            {
                registro.MarcasUtc.RemoveAll(m => ahoraUtc - m > Ventana);
                if (registro.BloqueadoHastaUtc <= ahoraUtc && registro.MarcasUtc.Count == 0)
                    diccionario.TryRemove(clave, out _);
            }
        }
    }

    private static string ClaveIp(string? ip) => string.IsNullOrWhiteSpace(ip) ? "(sin-ip)" : ip!.Trim();

    /// <summary>La cuenta se normaliza en minúsculas: Usuario y usuario comparten límite.</summary>
    private static string ClaveCuenta(string? cuenta) =>
        string.IsNullOrWhiteSpace(cuenta) ? "(sin-cuenta)" : cuenta.Trim().ToLowerInvariant();
}
