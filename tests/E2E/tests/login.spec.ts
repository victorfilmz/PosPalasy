import { test, expect } from '@playwright/test';
import { login } from '../helpers';

test.describe('Login y sesión', () => {
  test('login con credenciales válidas lleva al Dashboard', async ({ page }) => {
    await login(page);
    await expect(page).not.toHaveURL(/Login/);
    await expect(page.locator('body')).not.toContainText(/exception|error interno/i);
  });

  test('login con contraseña inválida muestra error', async ({ page }) => {
    await login(page, 'admin', 'contraseña-mal');
    await expect(page.locator('body')).toContainText(/inválid|incorrect|error/i);
  });

  test('páginas protegidas redirigen a login sin sesión', async ({ page }) => {
    await page.goto('/Facturacion/Lista');
    await expect(page).toHaveURL(/Login/);
  });
});
