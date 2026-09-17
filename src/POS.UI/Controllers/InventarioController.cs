using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using POS.Application.DTOs;
using POS.Domain.Entities;
using POS.Domain.Repositories;
using POS.Domain.Types;

namespace POS.UI.Controllers;

public class InventarioController : Controller
{
    private readonly IProductoRepository _productoRepo;
    private readonly IInventarioAlmacenRepository _inventarioRepo;
    private readonly ISucursalRepository _sucursalRepo;
    private readonly IProveedorRepository _proveedorRepo;
    private readonly IMovimientoInventarioRepository _movimientoRepo;
    private readonly IEnterpriseRepository _enterpriseRepo;

    public InventarioController(
        IProductoRepository productoRepo,
        IInventarioAlmacenRepository inventarioRepo,
        ISucursalRepository sucursalRepo,
        IProveedorRepository proveedorRepo,
        IMovimientoInventarioRepository movimientoRepo,
        IEnterpriseRepository enterpriseRepo)
    {
        _productoRepo = productoRepo;
        _inventarioRepo = inventarioRepo;
        _sucursalRepo = sucursalRepo;
        _proveedorRepo = proveedorRepo;
        _movimientoRepo = movimientoRepo;
        _enterpriseRepo = enterpriseRepo;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? q, string? categoria, string? estado, int? sucursalId)
    {
        var sucursales = (await _sucursalRepo.GetAllActiveAsync()).ToList();
        var allProducts = (await _productoRepo.GetAllAsync()).ToList();

        // Determinar sucursal seleccionada (null o <=0 = Sucursal Principal por defecto si existe)
        int selectedSucursalId = sucursalId ?? (sucursales.FirstOrDefault()?.Id ?? 1);
        var sucursalActual = sucursales.FirstOrDefault(s => s.Id == selectedSucursalId);
        string nombreSucursal = sucursalActual?.Nombre ?? "Sucursal Principal";

        // Cargar stock por sucursal o consolidado
        if (selectedSucursalId > 0)
        {
            var inventarios = (await _inventarioRepo.GetBySucursalAsync(selectedSucursalId)).ToDictionary(i => i.ProductoId);
            foreach (var p in allProducts)
            {
                if (inventarios.TryGetValue(p.Id, out var inv))
                {
                    p.StockActual = inv.StockActual;
                    p.StockMinimo = inv.StockMinimo;
                }
                else
                {
                    p.StockActual = 0m;
                    p.StockMinimo = 5m;
                }
            }
        }
        else
        {
            // Consolidado de todas las sucursales
            var allInv = (await _inventarioRepo.GetAllAsync()).ToList();
            var invGrouped = allInv.GroupBy(i => i.ProductoId)
                .ToDictionary(g => g.Key, g => new { StockTotal = g.Sum(x => x.StockActual), Minimo = g.Average(x => x.StockMinimo) });

            foreach (var p in allProducts)
            {
                if (invGrouped.TryGetValue(p.Id, out var inv))
                {
                    p.StockActual = inv.StockTotal;
                    p.StockMinimo = inv.Minimo;
                }
                else
                {
                    p.StockActual = 0m;
                    p.StockMinimo = 5m;
                }
            }
            nombreSucursal = "Todas las Sucursales (Consolidado)";
        }

        // Categorías únicas para filtro
        var categorias = allProducts
            .Select(p => p.Categoria)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        ViewBag.Sucursales = sucursales;
        ViewBag.SucursalSeleccionadaId = selectedSucursalId;
        ViewBag.NombreSucursalActual = nombreSucursal;
        ViewBag.Categorias = categorias;
        ViewBag.FiltroQuery = q;
        ViewBag.FiltroCategoria = categoria;
        ViewBag.FiltroEstado = estado;

        // Métricas de cabecera
        ViewBag.TotalProductos = allProducts.Count;
        ViewBag.ValorInventarioCosto = allProducts.Sum(p => p.StockActual * p.CostoUnitario);
        ViewBag.ValorInventarioVenta = allProducts.Sum(p => p.StockActual * p.PrecioUnitario);
        ViewBag.BajoStockCount = allProducts.Count(p => p.StockActual > 0 && p.StockActual <= p.StockMinimo);
        ViewBag.AgotadosCount = allProducts.Count(p => p.StockActual <= 0);

        // Filtrado en memoria
        var query = allProducts.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(p => 
                p.Codigo.ToLowerInvariant().Contains(term) || 
                p.Descripcion.ToLowerInvariant().Contains(term) ||
                p.Categoria.ToLowerInvariant().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(categoria) && categoria != "Todas")
        {
            query = query.Where(p => p.Categoria.Equals(categoria, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(estado))
        {
            switch (estado.ToLowerInvariant())
            {
                case "agotado":
                    query = query.Where(p => p.StockActual <= 0);
                    break;
                case "bajo":
                    query = query.Where(p => p.StockActual > 0 && p.StockActual <= p.StockMinimo);
                    break;
                case "normal":
                    query = query.Where(p => p.StockActual > p.StockMinimo);
                    break;
                case "inactivo":
                    query = query.Where(p => !p.EstaActivo);
                    break;
            }
        }

        var dtoList = new List<ProductoInventarioDto>();
        foreach (var p in query.OrderBy(p => p.Descripcion))
        {
            var hasVentas = await _productoRepo.HasVentasAsync(p.Id);
            dtoList.Add(new ProductoInventarioDto
            {
                Id = p.Id,
                Codigo = p.Codigo,
                Descripcion = p.Descripcion,
                Categoria = p.Categoria,
                PrecioUnitario = p.PrecioUnitario,
                CostoUnitario = p.CostoUnitario,
                StockActual = p.StockActual,
                StockMinimo = p.StockMinimo,
                SucursalId = selectedSucursalId,
                NombreSucursal = nombreSucursal,
                TieneLotes = p.TieneLotes,
                IndicadorFacturacion = p.IndicadorFacturacion,
                EstaActivo = p.EstaActivo,
                TieneVentasHistoricas = hasVentas
            });
        }

        return View(dtoList);
    }

    [HttpGet]
    public async Task<IActionResult> Crear()
    {
        var allProducts = await _productoRepo.GetAllAsync();
        ViewBag.CategoriasExistentes = allProducts
            .Select(p => p.Categoria)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        var sucursales = await _sucursalRepo.GetAllActiveAsync();
        ViewBag.Sucursales = sucursales;

        return View(new CrearProductoDto());
    }

    [HttpPost]
    public async Task<IActionResult> Crear(CrearProductoDto model)
    {
        if (string.IsNullOrWhiteSpace(model.Codigo) || string.IsNullOrWhiteSpace(model.Descripcion))
        {
            TempData["Error"] = "El código y el nombre/descripción del producto son obligatorios.";
            ViewBag.Sucursales = await _sucursalRepo.GetAllActiveAsync();
            return View(model);
        }

        if (model.PrecioUnitario <= 0)
        {
            TempData["Error"] = "El precio unitario de venta debe ser mayor a 0.";
            ViewBag.Sucursales = await _sucursalRepo.GetAllActiveAsync();
            return View(model);
        }

        var existe = await _productoRepo.GetByCodigoAsync(model.Codigo.Trim());
        if (existe != null)
        {
            TempData["Error"] = $"Ya existe un producto registrado con el código '{model.Codigo.Trim()}'.";
            ViewBag.Sucursales = await _sucursalRepo.GetAllActiveAsync();
            return View(model);
        }

        var producto = new Producto
        {
            Codigo = model.Codigo.Trim(),
            Descripcion = model.Descripcion.Trim(),
            Categoria = string.IsNullOrWhiteSpace(model.Categoria) ? "General" : model.Categoria.Trim(),
            PrecioUnitario = model.PrecioUnitario,
            CostoUnitario = model.CostoUnitario >= 0 ? model.CostoUnitario : 0m,
            TieneLotes = model.TieneLotes,
            IndicadorFacturacion = model.IndicadorFacturacion,
            EstaActivo = true
        };

        await _productoRepo.AddAsync(producto);

        // Asignar stock en almacén para la sucursal indicada o principal
        var targetSucursalId = model.SucursalId ?? (await _sucursalRepo.GetPrincipalAsync())?.Id ?? 1;
        var stockInicial = model.StockInicial >= 0 ? model.StockInicial : 0m;
        var stockMinimo = model.StockMinimo >= 0 ? model.StockMinimo : 5m;

        await _inventarioRepo.SetStockAsync(producto.Id, targetSucursalId, stockInicial, stockMinimo);

        // Si se especificó stock inicial, registrar entrada en el Kardex
        if (stockInicial > 0)
        {
            await _movimientoRepo.AddAsync(new MovimientoInventario
            {
                ProductoId = producto.Id,
                SucursalId = targetSucursalId,
                Tipo = TipoMovimientoInventario.EntradaCompra,
                Cantidad = stockInicial,
                StockAnterior = 0,
                StockNuevo = stockInicial,
                CostoUnitario = producto.CostoUnitario,
                Concepto = "Apertura de inventario (Stock inicial)",
                Fecha = DateTime.UtcNow
            });
        }

        TempData["Mensaje"] = $"Producto '{producto.Descripcion}' creado y asignado al almacén de la sucursal exitosamente.";
        return RedirectToAction(nameof(Index), new { sucursalId = targetSucursalId });
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int id, int? sucursalId)
    {
        var producto = await _productoRepo.GetByIdAsync(id);
        if (producto == null)
        {
            TempData["Error"] = "El producto no fue encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var sucursales = (await _sucursalRepo.GetAllActiveAsync()).ToList();
        int targetSucursalId = sucursalId ?? (sucursales.FirstOrDefault()?.Id ?? 1);

        var inv = await _inventarioRepo.GetByProductAndSucursalAsync(id, targetSucursalId);
        decimal stockActual = inv?.StockActual ?? 0m;
        decimal stockMinimo = inv?.StockMinimo ?? 5m;

        var allProducts = await _productoRepo.GetAllAsync();
        ViewBag.CategoriasExistentes = allProducts
            .Select(p => p.Categoria)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        ViewBag.Sucursales = sucursales;
        ViewBag.SucursalSeleccionadaId = targetSucursalId;
        ViewBag.StockActual = stockActual;

        var dto = new EditarProductoDto
        {
            Id = producto.Id,
            Codigo = producto.Codigo,
            Descripcion = producto.Descripcion,
            Categoria = producto.Categoria,
            PrecioUnitario = producto.PrecioUnitario,
            CostoUnitario = producto.CostoUnitario,
            StockMinimo = stockMinimo,
            SucursalId = targetSucursalId,
            TieneLotes = producto.TieneLotes,
            IndicadorFacturacion = producto.IndicadorFacturacion,
            EstaActivo = producto.EstaActivo
        };

        return View(dto);
    }

    [HttpPost]
    public async Task<IActionResult> Editar(EditarProductoDto model)
    {
        if (string.IsNullOrWhiteSpace(model.Codigo) || string.IsNullOrWhiteSpace(model.Descripcion))
        {
            TempData["Error"] = "El código y la descripción son campos requeridos.";
            return View(model);
        }

        var producto = await _productoRepo.GetByIdAsync(model.Id);
        if (producto == null)
        {
            TempData["Error"] = "El producto no existe.";
            return RedirectToAction(nameof(Index));
        }

        // Validar unicidad de código
        var otro = await _productoRepo.GetByCodigoAsync(model.Codigo.Trim());
        if (otro != null && otro.Id != model.Id)
        {
            TempData["Error"] = $"El código '{model.Codigo}' ya está asignado a otro producto ({otro.Descripcion}).";
            return View(model);
        }

        producto.Codigo = model.Codigo.Trim();
        producto.Descripcion = model.Descripcion.Trim();
        producto.Categoria = string.IsNullOrWhiteSpace(model.Categoria) ? "General" : model.Categoria.Trim();
        producto.PrecioUnitario = model.PrecioUnitario;
        producto.CostoUnitario = model.CostoUnitario >= 0 ? model.CostoUnitario : 0m;
        producto.TieneLotes = model.TieneLotes;
        producto.IndicadorFacturacion = model.IndicadorFacturacion;
        producto.EstaActivo = model.EstaActivo;

        await _productoRepo.UpdateAsync(producto);

        // Actualizar stock mínimo en el almacén de la sucursal
        if (model.SucursalId.HasValue && model.SucursalId.Value > 0)
        {
            var inv = await _inventarioRepo.GetByProductAndSucursalAsync(model.Id, model.SucursalId.Value);
            var stockAct = inv?.StockActual ?? 0m;
            await _inventarioRepo.SetStockAsync(model.Id, model.SucursalId.Value, stockAct, model.StockMinimo);
        }

        TempData["Mensaje"] = $"Producto '{producto.Descripcion}' actualizado exitosamente.";
        return RedirectToAction(nameof(Index), new { sucursalId = model.SucursalId });
    }

    [HttpPost]
    public async Task<IActionResult> Eliminar(int id)
    {
        var producto = await _productoRepo.GetByIdAsync(id);
        if (producto == null)
        {
            TempData["Error"] = "Producto no encontrado.";
            return RedirectToAction(nameof(Index));
        }

        var hasVentas = await _productoRepo.HasVentasAsync(id);
        if (hasVentas)
        {
            // Baja Lógica Fiscal para proteger integridad DGII
            producto.EstaActivo = false;
            await _productoRepo.UpdateAsync(producto);

            TempData["Mensaje"] = $"El producto '{producto.Descripcion}' tiene ventas históricas asociadas a comprobantes e-CF DGII. Ha sido desactivado del catálogo para preservar los reportes fiscales.";
        }
        else
        {
            // Borrado físico directo
            await _productoRepo.DeleteAsync(id);
            TempData["Mensaje"] = $"El producto '{producto.Descripcion}' ha sido eliminado definitivamente del inventario.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> AjustarStock(AjusteStockDto model)
    {
        if (model.Cantidad <= 0)
        {
            TempData["Error"] = "La cantidad para el ajuste debe ser mayor a 0.";
            return RedirectToAction(nameof(Index));
        }

        var producto = await _productoRepo.GetByIdAsync(model.ProductoId);
        if (producto == null)
        {
            TempData["Error"] = "El producto a ajustar no existe.";
            return RedirectToAction(nameof(Index));
        }

        var targetSucursalId = model.SucursalId ?? (await _sucursalRepo.GetPrincipalAsync())?.Id ?? 1;

        decimal delta = model.TipoMovimiento switch
        {
            TipoMovimientoInventario.EntradaCompra => model.Cantidad,
            TipoMovimientoInventario.DevolucionVenta => model.Cantidad,
            TipoMovimientoInventario.AjusteManual => model.Cantidad,
            TipoMovimientoInventario.SalidaMerma => -model.Cantidad,
            TipoMovimientoInventario.SalidaVencimiento => -model.Cantidad,
            _ => model.Cantidad
        };

        var stockAnterior = await _inventarioRepo.GetStockAsync(model.ProductoId, targetSucursalId);
        var stockResultante = stockAnterior + delta;

        await _inventarioRepo.SetStockAsync(model.ProductoId, targetSucursalId, stockResultante);

        await _movimientoRepo.AddAsync(new MovimientoInventario
        {
            ProductoId = producto.Id,
            SucursalId = targetSucursalId,
            ProveedorId = model.ProveedorId,
            NumeroLote = model.NumeroLote,
            FechaVencimiento = model.FechaVencimiento,
            Tipo = model.TipoMovimiento,
            Cantidad = model.Cantidad,
            StockAnterior = stockAnterior,
            StockNuevo = stockResultante,
            CostoUnitario = producto.CostoUnitario,
            Concepto = string.IsNullOrWhiteSpace(model.Concepto) ? "Ajuste manual de inventario" : model.Concepto.Trim(),
            Fecha = DateTime.UtcNow
        });

        TempData["Mensaje"] = $"Ajuste registrado exitosamente. Nuevo stock de '{producto.Descripcion}': {stockResultante.ToString("0.##")}";
        return RedirectToAction(nameof(Index), new { sucursalId = targetSucursalId });
    }

    [HttpGet]
    public async Task<IActionResult> Kardex(int? productoId, int? sucursalId)
    {
        var allProducts = (await _productoRepo.GetAllAsync()).OrderBy(p => p.Descripcion).ToList();
        var sucursales = (await _sucursalRepo.GetAllActiveAsync()).ToList();

        ViewBag.Productos = allProducts;
        ViewBag.Sucursales = sucursales;
        ViewBag.ProductoSeleccionadoId = productoId;
        ViewBag.SucursalSeleccionadaId = sucursalId;

        IEnumerable<MovimientoInventario> movimientos;
        if (sucursalId.HasValue && sucursalId.Value > 0)
        {
            var movsSucursal = await _movimientoRepo.GetBySucursalIdAsync(sucursalId.Value, 200);
            if (productoId.HasValue && productoId.Value > 0)
            {
                movimientos = movsSucursal.Where(m => m.ProductoId == productoId.Value);
            }
            else
            {
                movimientos = movsSucursal;
            }
        }
        else if (productoId.HasValue && productoId.Value > 0)
        {
            movimientos = await _movimientoRepo.GetByProductoIdAsync(productoId.Value);
            ViewBag.ProductoNombre = allProducts.FirstOrDefault(p => p.Id == productoId.Value)?.Descripcion;
        }
        else
        {
            movimientos = await _movimientoRepo.GetUltimosMovimientosAsync(200);
        }

        var productMap = allProducts.ToDictionary(p => p.Id);
        var sucursalMap = sucursales.ToDictionary(s => s.Id);

        var dtoList = movimientos.Select(m => new KardexItemDto
        {
            Id = m.Id,
            Fecha = m.Fecha,
            ProductoId = m.ProductoId,
            CodigoProducto = productMap.TryGetValue(m.ProductoId, out var prod) ? prod.Codigo : "N/A",
            DescripcionProducto = productMap.TryGetValue(m.ProductoId, out prod) ? prod.Descripcion : "Producto Eliminado",
            SucursalId = m.SucursalId,
            NombreSucursal = sucursalMap.TryGetValue(m.SucursalId, out var suc) ? suc.Nombre : "Casa Matriz",
            ProveedorNombre = m.Proveedor?.RazonSocial,
            NumeroLote = m.NumeroLote,
            TipoMovimiento = m.Tipo,
            Cantidad = m.Cantidad,
            StockAnterior = m.StockAnterior,
            StockResultante = m.StockNuevo,
            Concepto = m.Concepto,
            ReferenciaDocumento = m.ReferenciaDocumento
        }).ToList();

        return View(dtoList);
    }
}
