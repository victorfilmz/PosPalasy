/**
 * Genera la copia local del QR de verificación DGII usado en Pos/Ticket.cshtml y
 * Configuracion/FacturaFisica.cshtml (sustituye a api.qrserver.com para operar offline).
 *
 * Se ejecuta automáticamente en el global setup de la suite E2E
 * (y a mano: node generar-qr.js).
 */
const QRCode = require('qrcode');
const fs = require('fs');
const path = require('path');

const DESTINO = path.resolve(__dirname, '..', '..', 'src', 'POS.UI', 'wwwroot', 'img', 'qr-dgii.png');
const URL_TIMBRE = 'https://ecf.dgii.gov.do/testecf/consultatimbre?RncEmisor=131234567';

async function generarQrLocal() {
  if (fs.existsSync(DESTINO)) return;
  fs.mkdirSync(path.dirname(DESTINO), { recursive: true });
  await QRCode.toFile(DESTINO, URL_TIMBRE, { width: 280, margin: 2 });
  console.log(`[E2E] QR DGII local generado: ${DESTINO}`);
}

module.exports = { generarQrLocal };

if (require.main === module) {
  generarQrLocal().then(() => process.exit(0));
}
