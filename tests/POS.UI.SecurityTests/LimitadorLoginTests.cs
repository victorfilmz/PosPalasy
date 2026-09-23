using System;
using System.Linq;
using POS.UI.Security;
using Xunit;

namespace POS.UI.SecurityTests;

/// <summary>
/// Rate limiting del login (defensa en profundidad): los límites por IP y por cuenta bloquean
/// temporalmente a quien fuerza el login, la ventana es deslizante, el éxito limpia el contador
/// de la cuenta y un bloqueo activo se rechaza antes de tocar el hasher o la base.
/// </summary>
public class LimitadorLoginTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PorCuenta_AlSuperar10Fallos_BloqueaElDiccionarioSobreEseUsuario()
    {
        var limitador = new LimitadorLogin();

        // 10 fallos sobre 'admin' desde 10 IPs distintas (ataque distribuido por cuenta).
        for (int i = 0; i < LimitadorLogin.MaxIntentosPorCuenta; i++)
            limitador.RegistrarFallo($"203.0.113.{i}", "admin", T0.AddSeconds(i));

        // 'admin' bloqueado desde CUALQUIER IP; otra cuenta desde esas IPs pasa.
        Assert.False(limitador.Permitir("198.51.100.1", "admin", T0.AddMinutes(3)));
        Assert.True(limitador.Permitir("198.51.100.1", "supervisor", T0.AddMinutes(3)));

        // Normalización: 'Admin' y ' admin ' comparten el límite (no se esquiva con mayúsculas).
        Assert.False(limitador.Permitir("198.51.100.1", "Admin", T0.AddMinutes(3)));
        Assert.False(limitador.Permitir("198.51.100.1", " admin ", T0.AddMinutes(3)));
    }

    [Fact]
    public void PorIp_AlSuperar20Fallos_BloqueaCualquierCuentaDesdeEsaIp()
    {
        var limitador = new LimitadorLogin();

        // 20 fallos desde la misma IP (fuerza bruta sobre cuentas distintas).
        for (int i = 0; i < LimitadorLogin.MaxIntentosPorIp; i++)
            limitador.RegistrarFallo("203.0.113.7", "cuenta" + i, T0.AddSeconds(i));

        // Cualquier cuenta desde esa IP está bloqueada; otra IP no le afecta.
        Assert.False(limitador.Permitir("203.0.113.7", "admin", T0.AddMinutes(2)));
        Assert.False(limitador.Permitir("203.0.113.7", "otra", T0.AddMinutes(2)));
        Assert.True(limitador.Permitir("198.51.100.9", "admin", T0.AddMinutes(2)));
        // El bloqueo corre 15 min desde el ÚLTIMO fallo (T0+19s → vence a T0+919s):
        // a T0+2min faltan 799 s.
        Assert.Equal(799, limitador.SegundosRestantes("203.0.113.7", "admin", T0.AddMinutes(2)));
    }

    [Fact]
    public void BloqueoTemporal_ExpiraExactamente15MinutosDespuesDelFalloQueDispara()
    {
        var limitador = new LimitadorLogin();
        for (int i = 0; i < LimitadorLogin.MaxIntentosPorCuenta; i++)
            limitador.RegistrarFallo($"203.0.113.{i}", "admin", T0);

        // Los 10 fallos simultáneos disparan el bloqueo a T0: expira exactamente a T0+15min.
        Assert.False(limitador.Permitir("198.51.100.1", "admin", T0.AddMinutes(15).AddSeconds(-1)));
        Assert.True(limitador.Permitir("198.51.100.1", "admin", T0.AddMinutes(15).AddSeconds(1)));
    }

    [Fact]
    public void VentanaDeslizante_LosFallosViejosNoCuentan()
    {
        var limitador = new LimitadorLogin();

        // 9 fallos hace 9 minutos + 1 ahora: 10 en cuenta → bloquea (por cuenta).
        for (int i = 0; i < LimitadorLogin.MaxIntentosPorCuenta - 1; i++)
            limitador.RegistrarFallo("203.0.113.7", "x", T0.AddMinutes(-9).AddSeconds(i));
        limitador.RegistrarFallo("203.0.113.7", "x", T0);
        Assert.False(limitador.Permitir("203.0.113.7", "x", T0.AddMinutes(1)));

        // Los mismos fallos pero de hace 11 minutos: salieron de la ventana → 1 solo fallo, no bloquea.
        var limitador2 = new LimitadorLogin();
        for (int i = 0; i < LimitadorLogin.MaxIntentosPorCuenta - 1; i++)
            limitador2.RegistrarFallo("203.0.113.8", "x", T0.AddMinutes(-11).AddSeconds(i));
        limitador2.RegistrarFallo("203.0.113.8", "x", T0);
        Assert.True(limitador2.Permitir("203.0.113.8", "x", T0.AddMinutes(1)));
    }

    [Fact]
    public void Exito_LimpiaElContadorDeLaCuentaPeroNoElDeLaIp()
    {
        var limitador = new LimitadorLogin();

        // 9 fallos por cuenta y 9 por IP (misma cuenta, misma IP).
        for (int i = 0; i < LimitadorLogin.MaxIntentosPorCuenta - 1; i++)
            limitador.RegistrarFallo("203.0.113.7", "admin", T0.AddSeconds(i));

        // Un inicio exitoso limpia los intentos fallidos de la cuenta.
        limitador.RegistrarExito("203.0.113.7", "admin");
        Assert.True(limitador.Permitir("203.0.113.7", "admin", T0.AddMinutes(1)));

        // La IP conserva su historial (9 marcas): 11 fallos más la llevan al límite de 20 por IP.
        for (int i = 0; i < 11; i++)
            limitador.RegistrarFallo("203.0.113.7", "otra", T0.AddMinutes(1).AddSeconds(i));
        Assert.False(limitador.Permitir("203.0.113.7", "otra", T0.AddMinutes(2)));
    }

    [Fact]
    public void Purgar_EliminaLosRegistrosVencidos()
    {
        var limitador = new LimitadorLogin();
        limitador.RegistrarFallo("203.0.113.7", "admin", T0);

        limitador.Purgar(T0.AddMinutes(11)); // Fuera de la ventana y sin bloqueo activo

        // Tras purgar, la cuenta parte de cero: 9 fallos más NO bloquean.
        for (int i = 0; i < LimitadorLogin.MaxIntentosPorCuenta - 1; i++)
            limitador.RegistrarFallo("203.0.113.7", "admin", T0.AddMinutes(12).AddSeconds(i));
        Assert.True(limitador.Permitir("203.0.113.7", "admin", T0.AddMinutes(12).AddSeconds(30)));
    }

    [Fact]
    public void LimiteJusto_ElFalloQueAlcanzaElLimiteEsElQueBloquea()
    {
        var limitador = new LimitadorLogin();

        // Con 9 fallos (límite-1) todavía permite.
        for (int i = 0; i < LimitadorLogin.MaxIntentosPorCuenta - 1; i++)
            limitador.RegistrarFallo("203.0.113.7", "admin", T0.AddSeconds(i));
        Assert.True(limitador.Permitir("203.0.113.7", "admin", T0.AddMinutes(1)));

        // El fallo número 10 activa el bloqueo.
        limitador.RegistrarFallo("203.0.113.7", "admin", T0.AddMinutes(1));
        Assert.False(limitador.Permitir("203.0.113.7", "admin", T0.AddMinutes(1).AddSeconds(1)));
    }
}
