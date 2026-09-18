using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using POS.Application.Interfaces;
using POS.Application.Security;
using POS.Domain.Entities;
using POS.Domain.Enums;
using POS.Domain.Repositories;
using POS.Infrastructure.Security;

namespace POS.UI.SecurityTests;

/// <summary>
/// Pruebas del caso de uso de autenticación: credenciales, bloqueo por intentos fallidos,
/// cuentas inactivas, cambio de contraseña propia y mensajes que no permiten enumerar usuarios.
/// </summary>
public class AutenticacionServiceTests
{
    private const string PasswordValida = "Contrasena-Segura-2026!";
    private static readonly DateTime Ahora = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);

    private readonly IdentityPasswordHasher _hasher = new();
    private readonly RepositorioUsuariosEnMemoria _repositorio = new();

    private AutenticacionService CrearServicio() => new(_repositorio, _hasher);

    [Fact]
    public async Task Autenticar_ConCredencialesCorrectas_RegistraAccesoYDevuelveUsuario()
    {
        var usuario = _repositorio.Agregar(CrearUsuario("cajero"));

        var resultado = await CrearServicio().AutenticarAsync("cajero", PasswordValida, Ahora);

        Assert.True(resultado.Exito);
        Assert.Equal(usuario.Id, resultado.Usuario!.Id);
        Assert.Equal(Ahora, usuario.UltimoAcceso);
        Assert.Equal(0, usuario.IntentosFallidos);
    }

    [Fact]
    public async Task Autenticar_DistingueMayusculasEnElUsuarioPeroNoFallaPorEspacios()
    {
        _repositorio.Agregar(CrearUsuario("cajero"));

        var resultado = await CrearServicio().AutenticarAsync("  CAJERO  ", PasswordValida, Ahora);

        Assert.True(resultado.Exito);
    }

    [Fact]
    public async Task Autenticar_ConUsuarioInexistenteYConPasswordIncorrecta_DaElMismoMensaje()
    {
        _repositorio.Agregar(CrearUsuario("cajero"));

        var servicios = CrearServicio();

        var inexistente = await servicios.AutenticarAsync("no-existe", PasswordValida, Ahora);
        var incorrecta = await servicios.AutenticarAsync("cajero", "Otra-Contrasena-2026!", Ahora);

        Assert.False(inexistente.Exito);
        Assert.False(incorrecta.Exito);
        Assert.Equal(MotivoFalloAutenticacion.CredencialesInvalidas, inexistente.Motivo);
        Assert.Equal(MotivoFalloAutenticacion.CredencialesInvalidas, incorrecta.Motivo);
        Assert.Equal(inexistente.Mensaje, incorrecta.Mensaje);
    }

    [Fact]
    public async Task Autenticar_TrasCincoFallos_BloqueaLaCuentaAunqueLaPasswordSeaCorrecta()
    {
        var usuario = _repositorio.Agregar(CrearUsuario("cajero"));
        var servicios = CrearServicio();

        for (var intento = 0; intento < Usuario.MaxIntentosFallidos; intento++)
            await servicios.AutenticarAsync("cajero", "Otra-Contrasena-2026!", Ahora);

        var conPasswordCorrecta = await servicios.AutenticarAsync("cajero", PasswordValida, Ahora);

        Assert.False(conPasswordCorrecta.Exito);
        Assert.Equal(MotivoFalloAutenticacion.UsuarioBloqueado, conPasswordCorrecta.Motivo);
        Assert.True(usuario.EstaBloqueada(Ahora));

        // Vencido el bloqueo, la contraseña correcta vuelve a funcionar.
        var despues = Ahora.Add(Usuario.DuracionBloqueo).AddSeconds(1);
        var liberado = await servicios.AutenticarAsync("cajero", PasswordValida, despues);

        Assert.True(liberado.Exito);
    }

    [Fact]
    public async Task Autenticar_ConCuentaInactiva_NoPermiteElAcceso()
    {
        var usuario = CrearUsuario("cajero");
        usuario.EstaActivo = false;
        _repositorio.Agregar(usuario);

        var resultado = await CrearServicio().AutenticarAsync("cajero", PasswordValida, Ahora);

        Assert.False(resultado.Exito);
        Assert.Equal(MotivoFalloAutenticacion.UsuarioInactivo, resultado.Motivo);
    }

    [Fact]
    public async Task CambiarPassword_ConPasswordActualIncorrecta_EsRechazado()
    {
        var usuario = _repositorio.Agregar(CrearUsuario("cajero"));

        var resultado = await CrearServicio().CambiarPasswordAsync(usuario.Id, "Incorrecta-2026!", "Nueva-Contrasena-2026!", Ahora);

        Assert.False(resultado.Exito);
        Assert.Contains(resultado.Errores, e => e.Contains("actual", StringComparison.OrdinalIgnoreCase));
        Assert.True(_hasher.Verify(usuario.PasswordHash, PasswordValida) != ResultadoVerificacionPassword.Fallida);
    }

    [Fact]
    public async Task CambiarPassword_ConPasswordDebil_EsRechazadoYNoModificaElHash()
    {
        var usuario = _repositorio.Agregar(CrearUsuario("cajero"));
        var hashOriginal = usuario.PasswordHash;

        var resultado = await CrearServicio().CambiarPasswordAsync(usuario.Id, PasswordValida, "corta", Ahora);

        Assert.False(resultado.Exito);
        Assert.Equal(hashOriginal, usuario.PasswordHash);
    }

    [Fact]
    public async Task CambiarPassword_ConPasswordNuevaIgualALaActual_EsRechazado()
    {
        var usuario = _repositorio.Agregar(CrearUsuario("cajero"));

        var resultado = await CrearServicio().CambiarPasswordAsync(usuario.Id, PasswordValida, PasswordValida, Ahora);

        Assert.False(resultado.Exito);
        Assert.Contains(resultado.Errores, e => e.Contains("distinta", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CambiarPassword_Valido_ActualizaElHashYLiberaElCambioObligatorio()
    {
        var usuario = _repositorio.Agregar(CrearUsuario("cajero"));
        usuario.DebeCambiarPassword = true;

        const string nueva = "Nueva-Contrasena-2026!";
        var resultado = await CrearServicio().CambiarPasswordAsync(usuario.Id, PasswordValida, nueva, Ahora);

        Assert.True(resultado.Exito);
        Assert.False(usuario.DebeCambiarPassword);
        Assert.Equal(ResultadoVerificacionPassword.Correcta, _hasher.Verify(usuario.PasswordHash, nueva));
        Assert.Equal(ResultadoVerificacionPassword.Fallida, _hasher.Verify(usuario.PasswordHash, PasswordValida));
    }

    [Fact]
    public async Task CambiarPassword_DeUsuarioInexistente_EsRechazado()
    {
        var resultado = await CrearServicio().CambiarPasswordAsync(999, PasswordValida, "Nueva-Contrasena-2026!", Ahora);

        Assert.False(resultado.Exito);
        Assert.Contains(resultado.Errores, e => e.Contains("no existe", StringComparison.OrdinalIgnoreCase));
    }

    private Usuario CrearUsuario(string nombreUsuario)
    {
        var usuario = new Usuario
        {
            NombreUsuario = nombreUsuario,
            NombreCompleto = "Usuario de pruebas",
            Rol = RolUsuario.Cajero,
            EstaActivo = true
        };

        usuario.EstablecerPassword(_hasher.Hash(PasswordValida));
        return usuario;
    }

    /// <summary>Repositorio en memoria: aísla el caso de uso sin depender de la base de datos.</summary>
    private sealed class RepositorioUsuariosEnMemoria : IUsuarioRepository
    {
        private readonly List<Usuario> _usuarios = new();
        private int _siguienteId = 1;

        public Usuario Agregar(Usuario usuario)
        {
            usuario.Id = _siguienteId++;
            _usuarios.Add(usuario);
            return usuario;
        }

        public Task<Usuario?> GetByIdAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(_usuarios.FirstOrDefault(u => u.Id == id));

        public Task<Usuario?> GetByNombreUsuarioAsync(string nombreUsuario, CancellationToken ct = default) =>
            Task.FromResult(_usuarios.FirstOrDefault(u =>
                string.Equals(u.NombreUsuario, nombreUsuario.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<IEnumerable<Usuario>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IEnumerable<Usuario>>(_usuarios);

        public Task<bool> ExisteAlgunoAsync(CancellationToken ct = default) =>
            Task.FromResult(_usuarios.Count > 0);

        public Task<bool> ExisteNombreUsuarioAsync(string nombreUsuario, CancellationToken ct = default) =>
            Task.FromResult(_usuarios.Any(u =>
                string.Equals(u.NombreUsuario, nombreUsuario.Trim(), StringComparison.OrdinalIgnoreCase)));

        public Task<Usuario> AddAsync(Usuario usuario, CancellationToken ct = default) =>
            Task.FromResult(Agregar(usuario));

        public Task UpdateAsync(Usuario usuario, CancellationToken ct = default) => Task.CompletedTask;
    }
}
