import { spawn, execSync, ChildProcess } from 'child_process';
import { setTimeout as wait } from 'timers/promises';
import path from 'path';

// BD aislada de pruebas: los tests E2E nunca mutan PosPalasy_DGII (producción local).
export const E2E_DB = 'PosPalasy_DGII_E2E';
export const E2E_APP_URL = 'http://localhost:5199';
export const E2E_CONN =
  `Server=(localdb)\\mssqllocaldb;Database=${E2E_DB};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True`;

const REPO = path.resolve(__dirname, '..', '..');
const PROYECTO = path.join(REPO, 'src', 'POS.UI');

let app: ChildProcess | null = null;

export async function setup() {
  // 1. Recrear la BD E2E desde cero (limpia ventas/secuencias de corridas anteriores).
  try {
    execSync(
      `sqlcmd -S "(localdb)\\mssqllocaldb" -E -Q "IF DB_ID('${E2E_DB}') IS NOT NULL ALTER DATABASE [${E2E_DB}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; IF DB_ID('${E2E_DB}') IS NOT NULL DROP DATABASE [${E2E_DB}];"`,
      { stdio: 'pipe', shell: 'cmd' });
    console.log(`[E2E] BD ${E2E_DB} previa eliminada.`);
  } catch {
    console.log(`[E2E] BD ${E2E_DB} no existía (o sqlcmd no disponible); se creará al arrancar la app.`);
  }

  // 2. Levantar la app en puerto aparte, con SALIDA DE BUILD separada (la instancia de
  // producción bloquea las DLL de bin/Debug) y cadena de conexión/credenciales propias.
  app = spawn('dotnet', [
    'run', '--no-launch-profile', '--urls', E2E_APP_URL,
    '--property', 'OutputPath=' + path.join(REPO, 'tests', 'E2E', '.app-build'),
  ], {
    cwd: PROYECTO,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ConnectionStrings__DefaultConnection: E2E_CONN,
      // El seed crea el admin con esta contraseña SIN cambio obligatorio (cumple la política).
      Seguridad__AdminInicial__Usuario: 'admin',
      Seguridad__AdminInicial__Password: 'PosPalasy#2026$Prod',
      Certificado__Password: process.env.Certificado__Password ?? 'ensayo-homologacion',
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  app.stdout!.on('data', (d: Buffer) => process.stdout.write(`[app] ${d}`));
  app.stderr!.on('data', (d: Buffer) => process.stderr.write(`[app:err] ${d}`));

  // 3. Esperar a que /health responda (incluye el seed inicial y el build en frío de dotnet,
  //    que en una máquina lenta puede tardar ~2 minutos).
  const inicio = Date.now();
  while (Date.now() - inicio < 300_000) {
    try {
      const r = await fetch(`${E2E_APP_URL}/health`);
      if (r.ok) {
        console.log(`[E2E] App lista en ${E2E_APP_URL} (BD ${E2E_DB}) tras ${((Date.now() - inicio) / 1000).toFixed(0)}s`);
        return;
      }
    } catch { /* aún no arranca */ }
    await wait(2000);
  }
  throw new Error('[E2E] La app no respondió /health en 300s');
}

export async function teardown() {
  app?.kill('SIGTERM');
  await wait(2000);
  app?.kill('SIGKILL');
}
