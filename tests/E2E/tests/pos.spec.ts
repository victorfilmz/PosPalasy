import { expect } from '@playwright/test';
import { test as authedTest, asegurarTurnoAbierto } from '../helpers';

function botonAnadir(page: import('@playwright/test').Page, producto: RegExp) {
  // Cada tarjeta de catálogo es en sí un <button> que CONTIENE otro botón "Añadir";
  // el click en la tarjeta también añade el producto, así que clicamos la tarjeta.
  return page.getByRole('button', { name: producto });
}

authedTest.describe('Terminal POS', () => {
  authedTest('añadir producto actualiza carrito con ITBIS 18%', async ({ authedPage: page }) => {
    await page.goto('/Pos');
    await botonAnadir(page, /Refresco Cola 500ml/).click();
    await expect(page.locator('body')).toContainText(/Total a Cobrar/i);
    await expect(page.locator('body')).toContainText(/ITBIS 18%: RD\$ \d/i);
  });

  authedTest('cobro completo genera e-CF y aparece en Facturación', async ({ authedPage: page }) => {
    await asegurarTurnoAbierto(page);
    await page.goto('/Pos');
    await botonAnadir(page, /Refresco Cola 500ml/).click();
    await expect(page.locator('body')).toContainText(/Total a Cobrar/i);

    await page.locator('#btnCobrar').click();
    await page.locator('#btnConfirmarVenta').click();

    // Esperar el desenlace real: modal de éxito visible O alerta de error con texto.
    await page.waitForSelector('#modalExito.show', { state: 'visible', timeout: 30_000 })
      .catch(async () => {
        const alerta = await page.locator('#alertaErrorCobro').innerText().catch(() => '(sin alerta)');
        throw new Error('El cobro no llegó al modal de éxito. Alerta: ' + alerta.trim());
      });

    // La transmisión a la DGII (simulador) es asíncrona: la cola puede tardar unos segundos.
    await expect
      .poll(
        async () => {
          await page.goto('/Facturacion/Lista');
          const texto = await page.locator('body').innerText();
          return /E32\d{10}/.test(texto);
        },
        { timeout: 60_000, intervals: [2_000, 5_000] },
      )
      .toBe(true);
  });
});
