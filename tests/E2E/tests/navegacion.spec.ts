import { expect } from '@playwright/test';
import { test, test as authedTest } from '../helpers';

const RUTAS = [
  '/',
  '/Caja',
  '/Facturacion/Lista',
  '/Facturacion/Anulaciones',
  '/Reportes/Ventas607',
  '/Configuracion/Certificado',
];

test.describe('Navegación global sin errores', () => {
  for (const ruta of RUTAS) {
    authedTest(`página ${ruta} carga sin error`, async ({ authedPage: page }) => {
      const resp = await page.goto(ruta);
      expect(resp?.status(), `HTTP status de ${ruta}`).toBeLessThan(400);
      await expect(page.locator('body')).not.toContainText(/Internal Server Error|SqlException/i);
    });
  }

  authedTest('Dashboard muestra contenido', async ({ authedPage: page }) => {
    await page.goto('/');
    await expect(page.locator('body')).not.toBeEmpty();
  });

  authedTest('/health responde', async ({ request }) => {
    const resp = await request.get('/health');
    expect(resp.status()).toBe(200);
    expect(await resp.text()).toMatch(/Healthy|Degraded/);
  });
});
