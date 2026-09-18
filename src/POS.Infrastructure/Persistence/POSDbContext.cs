using Microsoft.EntityFrameworkCore;
using POS.Domain.Entities;

namespace POS.Infrastructure.Persistence;

public class POSDbContext : DbContext
{
    public POSDbContext(DbContextOptions<POSDbContext> options) : base(options) { }

    public DbSet<Enterprise> Enterprises => Set<Enterprise>();
    public DbSet<Sucursal> Sucursales => Set<Sucursal>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Venta> Ventas => Set<Venta>();
    public DbSet<VentaItem> VentaItems => Set<VentaItem>();
    public DbSet<ElectronicInvoice> ElectronicInvoices => Set<ElectronicInvoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Anulacion> Anulaciones => Set<Anulacion>();
    public DbSet<Configuracion> Configuraciones => Set<Configuracion>();
    public DbSet<CajaTurno> CajaTurnos => Set<CajaTurno>();
    public DbSet<MovimientoCaja> MovimientosCaja => Set<MovimientoCaja>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<InventarioAlmacen> InventariosAlmacen => Set<InventarioAlmacen>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<PagoFactura> PagosFactura => Set<PagoFactura>();
    public DbSet<EmisionDGIIQueue> EmisionesDGIIQueue => Set<EmisionDGIIQueue>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<SecuenciaECF> SecuenciasECF => Set<SecuenciaECF>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Usuario (seguridad)
        modelBuilder.Entity<Usuario>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.NombreUsuario).HasMaxLength(60).IsRequired();
            b.Property(u => u.NombreCompleto).HasMaxLength(150).IsRequired();
            b.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
            b.Property(u => u.Rol).HasConversion<int>();
            b.HasIndex(u => u.NombreUsuario).IsUnique();
        });

        // SecuenciaECF (numeración fiscal autorizada)
        modelBuilder.Entity<SecuenciaECF>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Serie).HasMaxLength(3).IsRequired();
            b.Property(s => s.TipoECF).HasConversion<int>();
            b.Property(s => s.Version).IsConcurrencyToken();
            b.HasIndex(s => s.Serie).IsUnique();
        });

        // Enterprise
        modelBuilder.Entity<Enterprise>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.RNC).HasMaxLength(11).IsRequired();
            b.Property(e => e.RazonSocial).HasMaxLength(150).IsRequired();
            b.Property(e => e.MensajeEncabezadoExtra).HasMaxLength(200);
            b.Property(e => e.MensajePieTicket).HasMaxLength(250);
            b.Property(e => e.PoliticaGarantiaTicket).HasMaxLength(500);
            b.HasIndex(e => e.RNC);
        });

        // Sucursal
        modelBuilder.Entity<Sucursal>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.CodigoSucursal).HasMaxLength(20).IsRequired();
            b.Property(s => s.Nombre).HasMaxLength(150).IsRequired();
            b.HasOne(s => s.Enterprise)
                .WithMany(e => e.Sucursales)
                .HasForeignKey(s => s.EnterpriseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cliente
        modelBuilder.Entity<Cliente>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.RNC).HasMaxLength(11);
            b.Property(c => c.Identificacion).HasMaxLength(20);
            b.Property(c => c.RazonSocial).HasMaxLength(150).IsRequired();
            b.HasIndex(c => c.RNC);
        });

        // Producto
        modelBuilder.Entity<Producto>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Codigo).HasMaxLength(50).IsRequired();
            b.Property(p => p.Descripcion).HasMaxLength(200).IsRequired();
            b.Property(p => p.Categoria).HasMaxLength(100).HasDefaultValue("General");
            b.Property(p => p.PrecioUnitario).HasPrecision(18, 4);
            b.Property(p => p.CostoUnitario).HasPrecision(18, 4);
            b.Property(p => p.TieneLotes).HasDefaultValue(false);
            b.Property(p => p.TasaISC).HasPrecision(18, 2);
            b.HasIndex(p => p.Codigo);
        });

        // InventarioAlmacen
        modelBuilder.Entity<InventarioAlmacen>(b =>
        {
            b.HasKey(ia => ia.Id);
            b.Property(ia => ia.StockActual).HasPrecision(18, 2);
            b.Property(ia => ia.StockMinimo).HasPrecision(18, 2);
            b.HasIndex(ia => new { ia.ProductoId, ia.SucursalId }).IsUnique();

            b.HasOne(ia => ia.Producto)
                .WithMany(p => p.Inventarios)
                .HasForeignKey(ia => ia.ProductoId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(ia => ia.Sucursal)
                .WithMany(s => s.Inventarios)
                .HasForeignKey(ia => ia.SucursalId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Proveedor
        modelBuilder.Entity<Proveedor>(b =>
        {
            b.HasKey(pr => pr.Id);
            b.Property(pr => pr.RNC).HasMaxLength(11).IsRequired();
            b.Property(pr => pr.RazonSocial).HasMaxLength(150).IsRequired();
            b.Property(pr => pr.Telefono).HasMaxLength(20);
            b.Property(pr => pr.Email).HasMaxLength(100);
            b.Property(pr => pr.Direccion).HasMaxLength(200);
            b.HasIndex(pr => pr.RNC);
        });

        // PagoFactura
        modelBuilder.Entity<PagoFactura>(b =>
        {
            b.HasKey(pf => pf.Id);
            b.Property(pf => pf.MetodoPago).HasMaxLength(50).IsRequired();
            b.Property(pf => pf.Monto).HasPrecision(18, 2);
            b.Property(pf => pf.Referencia).HasMaxLength(100);

            b.HasOne(pf => pf.Venta)
                .WithMany(v => v.Pagos)
                .HasForeignKey(pf => pf.VentaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EmisionDGIIQueue
        modelBuilder.Entity<EmisionDGIIQueue>(b =>
        {
            b.HasKey(q => q.Id);
            b.Property(q => q.eNCF).HasMaxLength(20).IsRequired();
            b.Property(q => q.XmlFirmado).IsRequired();
            b.Property(q => q.UltimoError).HasMaxLength(500);
            b.Property(q => q.Estado).HasConversion<int>();
            b.Property(q => q.LeaseToken).HasMaxLength(100);
            b.Property(q => q.TrackId).HasMaxLength(100);
            b.HasIndex(q => q.FacturaId);
            b.HasIndex(q => q.EnviadoExitosamente);
            // Índice de elegibilidad: el trabajador busca pendientes por estado y ventana de reintento.
            b.HasIndex(q => new { q.EnviadoExitosamente, q.Estado, q.ProximoIntentoUtc });
        });

        // MovimientoInventario
        modelBuilder.Entity<MovimientoInventario>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.Cantidad).HasPrecision(18, 2);
            b.Property(m => m.StockAnterior).HasPrecision(18, 2);
            b.Property(m => m.StockNuevo).HasPrecision(18, 2);
            b.Property(m => m.CostoUnitario).HasPrecision(18, 4);
            b.Property(m => m.NumeroLote).HasMaxLength(50);
            b.Property(m => m.Concepto).HasMaxLength(250).IsRequired();
            b.Property(m => m.ReferenciaDocumento).HasMaxLength(100);
            b.Property(m => m.Usuario).HasMaxLength(100);

            b.HasOne(m => m.Producto)
                .WithMany()
                .HasForeignKey(m => m.ProductoId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(m => m.Sucursal)
                .WithMany()
                .HasForeignKey(m => m.SucursalId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(m => m.Proveedor)
                .WithMany()
                .HasForeignKey(m => m.ProveedorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Venta
        modelBuilder.Entity<Venta>(b =>
        {
            b.HasKey(v => v.Id);
            b.Property(v => v.NumeroFacturaInterna).HasMaxLength(50).IsRequired();
            b.Property(v => v.Usuario).HasMaxLength(100);
            b.Property(v => v.Subtotal).HasPrecision(18, 2);
            b.Property(v => v.TotalDescuento).HasPrecision(18, 2);
            b.Property(v => v.TotalITBIS).HasPrecision(18, 2);
            b.Property(v => v.TotalISC).HasPrecision(18, 2);
            b.Property(v => v.Total).HasPrecision(18, 2);
            b.Property(v => v.MontoRecibido).HasPrecision(18, 2);
            b.Property(v => v.Cambio).HasPrecision(18, 2);

            // Barrera de idempotencia: una misma solicitud no puede producir dos ventas.
            b.HasIndex(v => v.ClaveIdempotencia).IsUnique();

            b.HasOne(v => v.Enterprise)
                .WithMany()
                .HasForeignKey(v => v.EnterpriseId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(v => v.Cliente)
                .WithMany()
                .HasForeignKey(v => v.ClienteId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // VentaItem
        modelBuilder.Entity<VentaItem>(b =>
        {
            b.HasKey(vi => vi.Id);
            b.Property(vi => vi.CodigoProducto).HasMaxLength(50);
            b.Property(vi => vi.Descripcion).HasMaxLength(200).IsRequired();
            b.Property(vi => vi.UnidadMedida).HasConversion<int>();
            b.Property(vi => vi.IndicadorBienoServicio).HasConversion<int>();
            b.Property(vi => vi.Cantidad).HasPrecision(18, 2);
            b.Property(vi => vi.PrecioUnitario).HasPrecision(18, 4);
            b.Property(vi => vi.Descuento).HasPrecision(18, 2);
            b.Property(vi => vi.TasaITBIS).HasPrecision(5, 2);
            b.Property(vi => vi.MontoITBIS).HasPrecision(18, 2);
            b.Property(vi => vi.MontoISC).HasPrecision(18, 2);
            b.Property(vi => vi.Subtotal).HasPrecision(18, 2);
            b.Property(vi => vi.Total).HasPrecision(18, 2);

            b.HasOne(vi => vi.Venta)
                .WithMany(v => v.Items)
                .HasForeignKey(vi => vi.VentaId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ElectronicInvoice
        modelBuilder.Entity<ElectronicInvoice>(b =>
        {
            b.HasKey(ei => ei.Id);
            b.Property(ei => ei.eNCF).HasMaxLength(13).IsRequired();
            b.Property(ei => ei.RNCEmisor).HasMaxLength(11).IsRequired();
            b.Property(ei => ei.RNCComprador).HasMaxLength(11);
            b.Property(ei => ei.XMLHash).HasMaxLength(6).IsRequired();
            b.Property(ei => ei.TrackId).HasMaxLength(100);
            b.Property(ei => ei.EstadoEmision).HasConversion<int>();

            // Montos y bases
            b.Property(ei => ei.MontoGravadoTotal).HasPrecision(18, 2);
            b.Property(ei => ei.MontoGravadoI1).HasPrecision(18, 2);
            b.Property(ei => ei.MontoGravadoI2).HasPrecision(18, 2);
            b.Property(ei => ei.MontoGravadoI3).HasPrecision(18, 2);
            b.Property(ei => ei.MontoExento).HasPrecision(18, 2);
            b.Property(ei => ei.TotalITBIS).HasPrecision(18, 2);
            b.Property(ei => ei.TotalITBIS1).HasPrecision(18, 2);
            b.Property(ei => ei.TotalITBIS2).HasPrecision(18, 2);
            b.Property(ei => ei.TotalITBIS3).HasPrecision(18, 2);
            b.Property(ei => ei.MontoImpuestoAdicional).HasPrecision(18, 2);
            b.Property(ei => ei.MontoTotal).HasPrecision(18, 2);

            b.HasIndex(ei => ei.eNCF).IsUnique();
            b.HasIndex(ei => ei.Estado);
            b.HasIndex(ei => ei.RNCEmisor);

            b.HasOne(ei => ei.Venta)
                .WithOne(v => v.ElectronicInvoice)
                .HasForeignKey<ElectronicInvoice>(ei => ei.VentaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // InvoiceItem
        modelBuilder.Entity<InvoiceItem>(b =>
        {
            b.HasKey(ii => ii.Id);
            b.Property(ii => ii.NombreItem).HasMaxLength(100).IsRequired();
            b.Property(ii => ii.CantidadItem).HasPrecision(18, 2);
            b.Property(ii => ii.PrecioUnitarioItem).HasPrecision(18, 4);
            b.Property(ii => ii.DescuentoMonto).HasPrecision(18, 2);
            b.Property(ii => ii.RecargoMonto).HasPrecision(18, 2);
            b.Property(ii => ii.Subtotal).HasPrecision(18, 2);
            b.Property(ii => ii.MontoITBIS).HasPrecision(18, 2);
            b.Property(ii => ii.MontoISC).HasPrecision(18, 2);
            b.Property(ii => ii.MontoItem).HasPrecision(18, 2);

            b.HasOne(ii => ii.ElectronicInvoice)
                .WithMany(ei => ei.Items)
                .HasForeignKey(ii => ii.ElectronicInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Anulacion
        modelBuilder.Entity<Anulacion>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.RNCEmisor).HasMaxLength(11).IsRequired();
            b.Property(a => a.eNCFDesde).HasMaxLength(13).IsRequired();
            b.Property(a => a.eNCFHasta).HasMaxLength(13).IsRequired();
            b.Property(a => a.TrackId).HasMaxLength(100);
        });

        // CajaTurno
        modelBuilder.Entity<CajaTurno>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Cajero).HasMaxLength(100).IsRequired();
            b.Property(c => c.UsuarioNombre).HasMaxLength(100);
            // Un usuario no puede tener dos turnos abiertos a la vez.
            b.HasIndex(c => new { c.UsuarioId, c.Estado });
            b.Property(c => c.MontoInicial).HasPrecision(18, 2);
            b.Property(c => c.VentasEfectivo).HasPrecision(18, 2);
            b.Property(c => c.VentasTarjeta).HasPrecision(18, 2);
            b.Property(c => c.VentasTransferencia).HasPrecision(18, 2);
            b.Property(c => c.TotalVentas).HasPrecision(18, 2);
            b.Property(c => c.TotalEntradasEfectivo).HasPrecision(18, 2);
            b.Property(c => c.TotalSalidasEfectivo).HasPrecision(18, 2);
            b.Property(c => c.MontoRealCierre).HasPrecision(18, 2);
            b.Property(c => c.Diferencia).HasPrecision(18, 2);
            b.Property(c => c.Observaciones).HasMaxLength(500);

            b.HasMany(c => c.Movimientos)
                .WithOne(m => m.CajaTurno)
                .HasForeignKey(m => m.CajaTurnoId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(c => c.Ventas)
                .WithOne(v => v.CajaTurno)
                .HasForeignKey(v => v.CajaTurnoId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // MovimientoCaja
        modelBuilder.Entity<MovimientoCaja>(b =>
        {
            b.HasKey(m => m.Id);
            b.Property(m => m.Monto).HasPrecision(18, 2);
            b.Property(m => m.Concepto).HasMaxLength(250).IsRequired();
        });
    }
}
