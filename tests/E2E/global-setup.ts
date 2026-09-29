import { setup as _setup, teardown as _teardown, E2E_APP_URL } from './setup-app';
import { generarQrLocal } from './generar-qr';
// Playwright exige un solo export setup/teardown en globalSetup; reenvolvemos los del módulo real.
export default async function setup() {
  process.env.E2E_APP_URL = E2E_APP_URL;
  await generarQrLocal();
  await _setup();
}

export async function teardown() {
  await _teardown();
}
