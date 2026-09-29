import { expect } from '@playwright/test';
import { test as authedTest } from '../helpers';

authedTest.describe('Certificado digital', () => {
  authedTest('página de certificado muestra estado del certificado', async ({ authedPage: page }) => {
    await page.goto('/Configuracion/Certificado');
    await expect(page.locator('body')).toContainText(/V.lido y Activo|Ensayo Homologacion/i);
  });
});

authedTest.describe('Reportes 607', () => {
  authedTest('Reporte de Ventas 607 carga con datos', async ({ authedPage: page }) => {
    await page.goto('/Reportes/Ventas607');
    await expect(page.locator('body')).toContainText(/607|Venta/i);
    await expect(page.locator('body')).not.toContainText(/SqlException|Internal Server Error/i);
  });
});

authedTest.describe('Caja', () => {
  authedTest('página de caja carga y muestra estado del turno', async ({ authedPage: page }) => {
    await page.goto('/Caja');
    await expect(page.locator('body')).toContainText(/turno|caja/i);
  });
});
