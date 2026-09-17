$base = "http://localhost:5099"

Write-Host "1. Creando producto temporal sin ventas..."
$bodyTemp = @{
    Codigo = "999TEST001"
    Descripcion = "Producto Temporal Para Borrar"
    Categoria = "Pruebas"
    IndicadorFacturacion = 1
    CostoUnitario = "10.00"
    PrecioUnitario = "20.00"
    StockInicial = "5.00"
    StockMinimo = "1.00"
}
$rCreate = Invoke-WebRequest -Uri "$base/Inventario/Crear" -Method Post -Body $bodyTemp -UseBasicParsing

$prods = Invoke-RestMethod -Uri "$base/Pos/BuscarProductos?query=Temporal"
$tempId = $prods[0].id
Write-Host "Producto temporal creado con ID: $tempId"

Write-Host "`n2. Eliminando producto sin ventas (debe ser borrado fisico)..."
$rDel1 = Invoke-WebRequest -Uri "$base/Inventario/Eliminar" -Method Post -Body @{ id = $tempId } -UseBasicParsing
Write-Host "Status eliminacion: $($rDel1.StatusCode)"

$prodsAfter = Invoke-RestMethod -Uri "$base/Pos/BuscarProductos?query=Temporal"
if ($prodsAfter.Count -eq 0) {
    Write-Host "EXITO: El producto sin ventas fue eliminado fisicamente del sistema!"
} else {
    Write-Host "ALERTA: El producto aun existe."
}

Write-Host "`n3. Intentando eliminar producto con ventas historicas e-CF (ID: 7)..."
$rDel2 = Invoke-WebRequest -Uri "$base/Inventario/Eliminar" -Method Post -Body @{ id = 7 } -UseBasicParsing
Write-Host "Status eliminacion fiscal: $($rDel2.StatusCode)"

# Verificar que ya no aparece en el POS (porque EstaActivo = false) pero sigue en la base de datos
$prodsActive = Invoke-RestMethod -Uri "$base/Pos/BuscarProductos?query=Tropical"
if ($prodsActive.Count -eq 0) {
    Write-Host "EXITO: El producto con ventas fue DESACTIVADO del POS para proteger comprobantes DGII!"
} else {
    Write-Host "ALERTA: El producto sigue activo en POS."
}
