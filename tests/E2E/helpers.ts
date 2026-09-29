import { test as base, expect, Page } from '@playwright/test';

export const ADMIN_USER = 'admin';
export const ADMIN_PASS = 'PosPalasy#2026$Prod';

/**
 * Garantía offline: aborta toda petición saliente que no sea al propio servidor E2E
 * (localhost/127.0.0.1). Si la app intenta cargar un CDN u otro recurso externo,
 * la petición se registra y el test en curso falla al terminar.
 */
export const test = base.extend<{ authedPage: Page; bloquearExternos: void }>({
  bloquearExternos: [
    async ({ page, baseURL }, use) => {
      const origenesExternos = new Set<string>();
      await page.route(/^https?:\/\//i, (route) => {
        const url = route.request().url();
        const base = baseURL ?? 'http://localhost:5199';
        if (url.startsWith(base) || url.startsWith('http://localhost:') || url.startsWith('http://127.0.0.1:')) {
          return route.continue();
        }
        origenesExternos.add(url);
        return route.abort();
      });
      await use();
      if (origenesExternos.size > 0) {
        throw new Error(
          `La app hizo ${origenesExternos.size} petición(es) externa(s) — no es 100% offline:\n  ` +
            [...origenesExternos].slice(0, 10).join('\n  '),
        );
      }
    },
    { auto: true },
  ],
  authedPage: async ({ page }, use) => {
    await login(page);
    await use(page);
  },
});

export async function login(page: Page, user = ADMIN_USER, pass = ADMIN_PASS) {
  await page.goto('/Cuenta/Login');
  await page.getByLabel(/Usuario/i).fill(user);
  await page.getByLabel(/Contrase/i).fill(pass);
  await page.getByRole('button', { name: /entrar|iniciar|acceder|login/i }).click();
  await page.waitForLoadState('networkidle');
}

/** Abre turno de caja si no hay uno activo (necesario para cobrar en POS). */
export async function asegurarTurnoAbierto(page: Page) {
  await page.goto('/Caja');
  const linkApertura = page.getByRole('link', { name: /apertura|abrir/i }).first();
  if (await linkApertura.isVisible().catch(() => false)) {
    await linkApertura.click();
    // El textbox del cajero no tiene label asociado; viene precargado con "Cajero Principal".
    await page.locator('form input[type="text"]').first().fill('Cajero E2E');
    await page.getByRole('button', { name: /Confirmar y Abrir/i }).click();
    await page.waitForLoadState('networkidle');
  }
  // Verificación dura: si al volver a /Caja sigue el link de apertura, el turno NO quedó abierto.
  await page.goto('/Caja');
  if (await page.getByRole('link', { name: /apertura|abrir/i }).first().isVisible().catch(() => false)) {
    throw new Error('No fue posible abrir el turno de caja: la venta no podrá cobrarse.');
  }
}
