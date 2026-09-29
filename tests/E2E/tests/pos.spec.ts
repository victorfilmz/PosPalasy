import { expect } from '@playwright/test';
import { test as authedTest, asegurarTurnoAbierto } from '../helpers';

authedTest.describe('Terminal POS', () => {
  authedTest('añadir producto actualiza carrito con ITBIS 18%', async ({ authedPage: page }) => {
    await page.goto('/Pos');
    await expect(page.locator('body')).toContainText(/Refresco Cola 500ml/i);
    await page.locator('button[aria-label="Añadir"]').nth(6).click({ force: true }); // Refresco Cola 500ml
    await expect(page.locator('body')).toContainText(/Refresco Cola 500ml/i);
    await expect(page.locator('body')).toContainText(/Total a Cobrar/i);
    await expect(page.locator('body')).toContainText(/ITBIS 18%: RD\$ \d/i);
  });

  authedTest('cobro completo genera e-CF y aparece en Facturación', async ({ authedPage: page }) => {
    await asegurarTurnoAbierto(page);
    await page.goto('/Pos');
    await page.locator('button[aria-label="Añadir"]').nth(6).click({ force: true });
    await expect(page.locator('body')).toContainText(/Total a Cobrar/i);

    await page.locator('#btnCobrar').click();
    await page.locator('#btnConfirmarVenta').click();

    await page.waitForLoadState('networkidle');
    await expect(page.locator('body')).not.toContainText(/Cannot insert|SqlException/i);

    await page.goto('/Facturacion/Lista');
    await expect(page.locator('body')).toContainText(/E3200000000/i);
  });
});
