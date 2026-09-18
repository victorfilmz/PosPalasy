using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;
using POS.Domain.Repositories;

namespace POS.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio EF Core de usuarios del sistema.
/// </summary>
public class UsuarioRepository : IUsuarioRepository
{
    private readonly POSDbContext _context;

    public UsuarioRepository(POSDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Usuario?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<Usuario?> GetByNombreUsuarioAsync(string nombreUsuario, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nombreUsuario)) return null;

        var normalizado = nombreUsuario.Trim().ToLower();
        return await _context.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario.ToLower() == normalizado, ct);
    }

    public async Task<IEnumerable<Usuario>> GetAllAsync(CancellationToken ct = default) =>
        await _context.Usuarios.OrderBy(u => u.NombreUsuario).ToListAsync(ct);

    public async Task<bool> ExisteAlgunoAsync(CancellationToken ct = default) =>
        await _context.Usuarios.AnyAsync(ct);

    public async Task<bool> ExisteNombreUsuarioAsync(string nombreUsuario, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(nombreUsuario)) return false;

        var normalizado = nombreUsuario.Trim().ToLower();
        return await _context.Usuarios.AnyAsync(u => u.NombreUsuario.ToLower() == normalizado, ct);
    }

    public async Task<Usuario> AddAsync(Usuario usuario, CancellationToken ct = default)
    {
        await _context.Usuarios.AddAsync(usuario, ct);
        await _context.SaveChangesAsync(ct);
        return usuario;
    }

    public async Task UpdateAsync(Usuario usuario, CancellationToken ct = default)
    {
        usuario.UpdatedAt = DateTime.UtcNow;
        _context.Usuarios.Update(usuario);
        await _context.SaveChangesAsync(ct);
    }
}
