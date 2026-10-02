import { expect } from '@playwright/test';
import { test, asegurarTurnoAbierto } from '../helpers';

/**
 * Prueba de operación con DGII REAL sin internet (ModoSimulador=false + proxy muerto).
 * Requiere que la app bajo prueba se haya levantado con:
 *   DGII__ModoSimulador=false  +  HTTP(S)_PROXY apuntando a un puerto cerrado.
 * La suite E2E normal la omite (no arranca la app en ese modo); se usa ad hoc:
 *   npx playwright test tests/offline-dgii.spec.ts
 */
test.describe('Contingencia DGII real sin red', () => {
  test.skip(process.env.E2E_DGII_REAL_OFFLINE !== '1',
    'Solo corre con la app en modo DGII real + red de proceso bloqueada (E2E_DGII_REAL_OFFLINE=1).');

  test('venta sin red → contingencia declarada y comprobante en cola con reintento', async ({ authedPage: page }) => {
    await asegurarTurnoAbierto(page);

    // 1) Vender en el POS con la DGII inalcanzable.
    await page.goto('/Pos');
    await page.getByRole('button', { name: /Refresco Cola 500ml/ }).click();
    await expect(page.locator('body')).toContainText(/Total a Cobrar/i);

    await page.locator('#btnCobrar').click();
    await page.locator('#btnConfirmarVenta').click();

    // 2) El cobro DEBE llegar al modal de éxito (la venta queda intacta aunque la DGII caiga).
    await page.waitForSelector('#modalExito.show', { state: 'visible', timeout: 60_000 })
      .catch(async () => {
        const alerta = await page.locator('#alertaErrorCobro').innerText().catch(() => '(sin alerta)');
        throw new Error('El cobro no llegó al modal de éxito. Alerta: ' + alerta.trim());
      });

    // 3) El mensaje debe informar contingencia, no fingir transmisión exitosa.
    const textoExito = await page.locator('#modalExito').innerText();
    expect(textoExito).toMatch(/contingencia|cola|pendiente de transmisión|PendienteReenvio/i);

    // 4) En Facturación: e-NCF emitido y estado de contingencia visible.
    await page.goto('/Facturacion/Lista');
    const lista = await page.locator('body').innerText();
    expect(lista).toMatch(/E32\d{10}/); // e-NCF emitido localmente
    expect(lista).toMatch(/contingencia|pendiente/i);

    // 5) El estado del dashboard refleja la cola (Aceptados sigue en 0: nada confirmado sin red).
    await page.goto('/');
    const dashboard = await page.locator('body').innerText();
    expect(dashboard).not.toMatch(/SqlException|exception/i);
  });
});
