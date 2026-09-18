using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using POS.Application.Interfaces;
using POS.Application.Security;
using POS.Domain.Repositories;
using POS.Application.Validators;
using POS.Domain.Entities;
using POS.Infrastructure.Security;

namespace POS.UI.SecurityTests;

/// <summary>
/// Pruebas de las reglas de seguridad de cuentas: hashing no reversible con sal, política de
/// contraseñas y bloqueo por intentos fallidos.
/// </summary>
public class ContrasenasYCuentasTests
{
    private readonly IdentityPasswordHasher _hasher = new();

    [Fact]
    public void Hash_NoAlmacenaLaContrasenaEnClaroYPermiteVerificarla()
    {
        const string password = "Contrasena-Segura-2026!";

        var hash = _hasher.Hash(password);

        Assert.NotEqual(password, hash);
        Assert.DoesNotContain(password, hash, StringComparison.Ordinal);
        Assert.Equal(ResultadoVerificacionPassword.Correcta, _hasher.Verify(hash, password));
        Assert.Equal(ResultadoVerificacionPassword.Fallida, _hasher.Verify(hash, "Otra-Contrasena-2026!"));
    }

    [Fact]
    public void Hash_UsaSalAleatoria_PorLoQueDosHashesDeLaMismaContrasenaDifieren()
    {
        const string password = "Contrasena-Segura-2026!";

        var primero = _hasher.Hash(password);
        var segundo = _hasher.Hash(password);

        Assert.NotEqual(primero, segundo);
        Assert.NotEqual(ResultadoVerificacionPassword.Fallida, _hasher.Verify(primero, password));
        Assert.NotEqual(ResultadoVerificacionPassword.Fallida, _hasher.Verify(segundo, password));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-un-hash-valido")]
    [InlineData("1.2")]
    [InlineData("1.###.###")]
    public void Verify_ConHashInvalido_DevuelveFallidaSinLanzarExcepcion(string hashInvalido)
    {
        Assert.Equal(ResultadoVerificacionPassword.Fallida, _hasher.Verify(hashInvalido, "Contrasena-Segura-2026!"));
    }

    [Theory]
    [InlineData("corta1!A")]
    [InlineData("sinmayusculas-2026!")]
    [InlineData("SINMINUSCULAS-2026!")]
    [InlineData("SinDigitos-abcdef!")]
    [InlineData("SinEspeciales2026abc")]
    [InlineData("Con Espacios-2026!")]
    public void PasswordPolicy_RechazaContrasenasDebiles(string password)
    {
        Assert.False(PasswordPolicy.Validar(password, "admin").EsValido);
    }

    [Fact]
    public void PasswordPolicy_RechazaContrasenaQueContieneElNombreDeUsuario()
    {
        var resultado = PasswordPolicy.Validar("Admin-PosPalasy-2026!", "admin");

        Assert.False(resultado.EsValido);
    }

    [Fact]
    public void PasswordPolicy_AceptaContrasenaFuerte()
    {
        Assert.True(PasswordPolicy.Validar("Clave-Robusta-2026!", "admin").EsValido);
    }

    [Fact]
    public void GenerarAleatoria_CumpleLaPoliticaYEsDistintaCadaVez()
    {
        var primera = PasswordPolicy.GenerarAleatoria();
        var segunda = PasswordPolicy.GenerarAleatoria();

        Assert.True(PasswordPolicy.Validar(primera).EsValido);
        Assert.True(PasswordPolicy.Validar(segunda).EsValido);
        Assert.NotEqual(primera, segunda);
    }

    [Fact]
    public void Usuario_SeBloqueaTrasElMaximoDeIntentosYLiberaAlVencerElPlazo()
    {
        var usuario = new Usuario { NombreUsuario = "cajero" };
        usuario.EstablecerPassword("hash");

        var ahora = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < Usuario.MaxIntentosFallidos; i++)
            usuario.RegistrarIntentoFallido(ahora);

        Assert.True(usuario.EstaBloqueada(ahora));
        Assert.False(usuario.PuedeIniciarSesion(ahora));

        // Dentro del bloqueo sigue bloqueada.
        Assert.False(usuario.PuedeIniciarSesion(ahora.AddMinutes(10)));

        // Vencido el bloqueo, la cuenta vuelve a permitir acceso.
        var despues = ahora.Add(Usuario.DuracionBloqueo).AddSeconds(1);
        Assert.False(usuario.EstaBloqueada(despues));
        Assert.True(usuario.PuedeIniciarSesion(despues));
    }

    [Fact]
    public void Usuario_InactivoOSinPassword_NoPuedeIniciarSesion()
    {
        var ahora = DateTime.UtcNow;

        var inactivo = new Usuario { NombreUsuario = "baja", EstaActivo = false };
        inactivo.EstablecerPassword("hash");
        Assert.False(inactivo.PuedeIniciarSesion(ahora));

        var sinPassword = new Usuario { NombreUsuario = "nuevo" };
        Assert.False(sinPassword.PuedeIniciarSesion(ahora));
    }

    [Fact]
    public void EstablecerPassword_LimpiaBloqueoYDeudaDeCambio()
    {
        var usuario = new Usuario { NombreUsuario = "cajero", DebeCambiarPassword = true };
        var ahora = DateTime.UtcNow;

        for (var i = 0; i < Usuario.MaxIntentosFallidos; i++)
            usuario.RegistrarIntentoFallido(ahora);

        usuario.EstablecerPassword("hash-nuevo");

        Assert.Null(usuario.BloqueadoHasta);
        Assert.Equal(0, usuario.IntentosFallidos);
        Assert.False(usuario.DebeCambiarPassword);
    }
}
