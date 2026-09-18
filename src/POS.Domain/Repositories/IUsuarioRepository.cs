using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using POS.Domain.Entities;

namespace POS.Domain.Repositories;

/// <summary>
/// Repositorio de usuarios del sistema (autenticación, autorización y administración de cuentas).
/// </summary>
public interface IUsuarioRepository
{
    Task<Usuario?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Usuario?> GetByNombreUsuarioAsync(string nombreUsuario, CancellationToken ct = default);

    Task<IEnumerable<Usuario>> GetAllAsync(CancellationToken ct = default);

    Task<bool> ExisteAlgunoAsync(CancellationToken ct = default);

    Task<bool> ExisteNombreUsuarioAsync(string nombreUsuario, CancellationToken ct = default);

    Task<Usuario> AddAsync(Usuario usuario, CancellationToken ct = default);

    Task UpdateAsync(Usuario usuario, CancellationToken ct = default);
}
