import { test as base, expect, Page } from '@playwright/test';

export const ADMIN_USER = 'admin';
export const ADMIN_PASS = 'PosPalasy#2026$Prod';

export const test = base.extend<{ authedPage: Page }>({
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
    await page.getByLabel(/cajero/i).fill('Cajero E2E');
    await page.getByLabel(/monto/i).fill('1000');
    await page.getByRole('button', { name: /abrir|guardar/i }).click();
    await page.waitForLoadState('networkidle');
  }
}
