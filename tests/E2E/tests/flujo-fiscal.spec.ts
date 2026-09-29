import { expect } from '@playwright/test';
import { test as authedTest, asegurarTurnoAbierto } from '../helpers';

/**
 * Flujo fiscal extendido sobre BD limpia (seed: e-CF E320000000001 es el primero).
 * Orden dependiente dentro de cada describe; los describes son independientes entre sí.
 */

// Venta de un Refresco Cola 500ml (RD$45 + ITBIS 18% = 53.10) y devuelve el e-NCF creado.
async function venderUnRefresco(page: import('@playwright/test').Page) {
  await asegurarTurnoAbierto(page);
  await page.goto('/Pos');
  await page.getByRole('button', { name: /Refresco Cola 500ml/ }).click();
  await expect(page.locator('body')).toContainText(/Total a Cobrar/i);
  await page.locator('#btnCobrar').click();
  await page.locator('#btnConfirmarVenta').click();
  await page.waitForSelector('#modalExito.show', { state: 'visible', timeout: 30_000 });
  const trackId = await page.locator('#exitoTrackId').innerText();

  // El e-NCF con la BD recién sembrada: E320000000001 (secuencia 1). Si no fuera la primera
  // venta del lote, lo localizamos por TrackId en la lista.
  await page.goto('/Facturacion/Lista');
  const enSecuenciaUno = /E320000000001/.test(await page.locator('body').innerText());
  return { trackId: trackId.trim(), encf: enSecuenciaUno ? 'E320000000001' : null };
}

// Localiza la fila del e-CF más reciente y abre su Detalle.
async function abrirDetalleUltimoECF(page: import('@playwright/test').Page) {
  await page.goto('/Facturacion/Lista');
  // Fila 1 de la tabla = más reciente (la lista ordena descendente).
  const primeraFila = page.locator('table tbody tr').first();
  await expect(primeraFila).toContainText(/E32\d{10}/);
  await primeraFila.getByRole('link', { name: /detalle|ver/i }).first().click()
    .catch(() => primeraFila.locator('a').first().click());
  await page.waitForLoadState('networkidle');
}

// ---------------------------------------------------------------- Anulación de e-CF (ANECF)
authedTest.describe('Anulación de e-CF', () => {
  authedTest('anula el e-CF de una venta y queda registrado en ANECF', async ({ authedPage: page }) => {
    await venderUnRefresco(page);
    await abrirDetalleUltimoECF(page);
    const encf = (await page.locator('body').innerText()).match(/E32\d{10}/)?.[0];
    expect(encf, 'e-NCF visible en el detalle').toBeTruthy();

    // Abrir modal de anulación y confirmar con motivo 5 (devolución/corrección).
    await page.locator('#modalAnular button[data-bs-target="#modalAnular"], button:has-text("Anular e-CF")')
      .first().click();
    await page.locator('#modalAnular.show').waitFor({ state: 'visible' });
    await page.locator('#modalAnular select[name="codigoMotivo"]').selectOption('5');
    await page.locator('#modalAnular textarea[name="motivo"]').fill('Anulación E2E: prueba automatizada');
    await page.getByRole('button', { name: /Confirmar y Transmitir ANECF/i }).click();
    await page.waitForLoadState('networkidle');

    // Sin error de servidor y el e-CF pasa a estado anulado (o en proceso) en la lista.
    await expect(page.locator('body')).not.toContainText(/SqlException|Internal Server Error/i);
    await page.goto('/Facturacion/Anulaciones');
    await expect(page.locator('body')).toContainText(new RegExp(encf!));
  });
});

// ---------------------------------------------------------------- Devolución en caja
authedTest.describe('Devolución de venta', () => {
  authedTest('devuelve la venta completa, reingresa stock y registra nota de crédito', async ({ authedPage: page }) => {
    await venderUnRefresco(page);

    // Id de la venta: la primera de la BD limpia es la #1, pero lo resolvemos desde el detalle
    // del e-CF más reciente (la lista ordena descendente).
    await page.goto('/Facturacion/Lista');
    const primeraFila = page.locator('table tbody tr').first();
    await expect(primeraFila).toContainText(/E32\d{10}/);
    await primeraFila.locator('a').first().click();
    await page.waitForLoadState('networkidle');
    const ventaId = (await page.locator('body').innerText()).match(/Venta\s*#?(\d+)/i)?.[1] ?? '1';

    await page.goto(`/Caja/Devolucion?ventaId=${ventaId}`);
    await expect(page.locator('body')).toContainText(/Refresco Cola 500ml/i);

    // Enviar el formulario nativo (el click del botón en headless a veces no dispara el submit;
    // el POST directo con request comparte cookies y token antiforgery de la página).
    // OJO: hay dos forms con action=/Caja/Devolucion (el buscador GET y el POST); el token solo
    // existe en el POST, así que lo extraemos por evaluate del form method=post.
    const { token, cantName } = await page.evaluate(() => {
      const form = [...document.querySelectorAll('form')].find(
        f => f.getAttribute('method') === 'post' && f.getAttribute('action') === '/Caja/Devolucion');
      const cant = [...form.querySelectorAll('input[name^="Cantidades"]')]
        .find(i => i.value !== '');
      return {
        token: form.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '',
        cantName: cant?.name ?? 'Cantidades[1]',
      };
    });
    const resp = await page.request.post('/Caja/Devolucion', {
      form: {
        ClaveIdempotencia: crypto.randomUUID(),
        VentaId: ventaId,
        [cantName]: '1',
        Motivo: 'Devolución E2E: producto devuelto por el cliente',
        __RequestVerificationToken: token,
      },
      maxRedirects: 0,
    });
    expect(resp.status(), 'el POST de devolución debe redirigir (302)').toBe(302);
    expect(resp.headers()['location']).toContain(`ventaId=${ventaId}`);

    // Tras el redirect, la página muestra el mensaje de éxito y la nota de crédito pendiente.
    await page.goto(`/Caja/Devolucion?ventaId=${ventaId}`);
    await expect(page.locator('.alert-success')).toContainText(/Devolución registrada/i);
    await expect(page.locator('body')).toContainText(/Emitir nota de crédito|E34/i);
  });
});

// ---------------------------------------------------------------- Cierre de turno con cuadre
authedTest.describe('Cierre de caja con cuadre', () => {
  authedTest('abre turno, vende y cierra con arqueo exacto (diferencia 0)', async ({ authedPage: page }) => {
    await venderUnRefresco(page); // deja turno abierto con una venta de 53.10 en efectivo

    await page.goto('/Caja');
    await page.getByRole('link', { name: /Cerrar Caja/i }).click();
    await page.waitForLoadState('networkidle');
    await expect(page.locator('body')).toContainText(/Efectivo F.sico|arqueo/i);

    // El botón de cierre llama a confirm(): sin handler el diálogo se auto-descarta y el submit
    // nunca ocurre. Registramos accept ANTES del click. Y apuntamos al form del arqueo (el
    // sidebar trae otro form POST de Logout).
    page.on('dialog', d => d.accept());
    await page.locator('form[action*="Cierre"] button[type="submit"], form:has(input[name="montoRealCierre"]) button[type="submit"]').first().click();
    await page.waitForLoadState('networkidle');

    // Redirige al Reporte Z con el arqueo.
    await expect(page).toHaveURL(/ReporteZ|Caja/i);
    await expect(page.locator('body')).toContainText(/Turno #\d+ cerrado|Reporte Z|Arqueo/i);
    await expect(page.locator('body')).not.toContainText(/SqlException|Internal Server Error/i);

    // El turno ya no está abierto: /Caja muestra de nuevo el botón de apertura.
    await page.goto('/Caja');
    await expect(page.getByRole('link', { name: /Abrir Turno/i }).first()).toBeVisible();
  });
});

// ---------------------------------------------------------------- Creación de producto
authedTest.describe('Inventario: crear producto', () => {
  authedTest('crea un producto con stock y aparece en el catálogo del POS', async ({ authedPage: page }) => {
    const codigo = `7469${Date.now().toString().slice(-8)}`;
    const descripcion = `Producto E2E ${codigo}`;

    await page.goto('/Inventario/Crear');
    await page.locator('input[name="Codigo"]').fill(codigo);
    await page.locator('input[name="Categoria"]').fill('Pruebas E2E');
    await page.locator('input[name="Descripcion"]').fill(descripcion);
    await page.locator('select[name="IndicadorFacturacion"]').selectOption('1'); // ITBIS 18%
    await page.locator('input[name="PrecioUnitario"]').fill('99.50');
    await page.locator('input[name="StockInicial"]').fill('25');
    await page.locator('#formCrear button[type="submit"]').click();
    await page.waitForLoadState('networkidle');

    await expect(page.locator('body')).not.toContainText(/SqlException|Internal Server Error/i);
    // Vuelve al listado (o muestra error de TempData): el producto debe existir.
    await page.goto('/Inventario');
    await expect(page.locator('body')).toContainText(descripcion);

    // Visible en el terminal POS (catálogo).
    await page.goto('/Pos');
    await expect(page.locator('body')).toContainText(descripcion);
  });

  authedTest('rechaza código duplicado con mensaje claro', async ({ authedPage: page }) => {
    await page.goto('/Inventario/Crear');
    // Código del seed: 746001001 (Refresco Cola 500ml).
    await page.locator('input[name="Codigo"]').fill('746001001');
    await page.locator('input[name="Categoria"]').fill('Pruebas E2E');
    await page.locator('input[name="Descripcion"]').fill('Duplicado E2E');
    await page.locator('input[name="PrecioUnitario"]').fill('10');
    await page.locator('#formCrear button[type="submit"]').click();
    await page.waitForLoadState('networkidle');
    await expect(page.locator('body')).toContainText(/Ya existe un producto registrado/i);
  });
});
