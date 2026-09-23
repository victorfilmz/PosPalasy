# 14 — Guía de trámite: certificado digital A1 y habilitación como emisor electrónico

> **Propósito:** ejecutar el pendiente operativo de la FASE 5 (doc 13 §7) — conseguir las
> credenciales reales para la homologación en `testecf`, según la checklist del doc 12 §2.
> Este doc es guía de trámite: nada de código, solo el camino administrativo y su preparación
> técnica en PosPalasy.
>
> **Fecha:** 23-09-2026 · Estado del código: FASE 5 cerrada (gate 5.6 PASS, 302/302 pruebas).

---

## 1. Resumen del camino

```text
[1] Certificado digital A1  ──┐
[2] RNC habilitado e-CF     ──┼──►  [3] Acceso a testecf  ──►  [4] Homologación (doc 12 §4)
[5] eNCF activas            ──┘
```

Los pasos 1 y 2 son paralelizables; el 3 depende de ambos; el 4 es el plan ya escrito.

## 2. Paso 1 — Certificado digital tipo A1

**Qué es:** archivo `.pfx`/`.p12` con clave privada, emitido por una entidad certificadora
autorizada en RD, a nombre del contribuyente emisor. PosPalasy lo exige para firmar XML-DSig
(`FirmadorComprobanteECF`) y autenticar la semilla (`DgiiAuthenticator`).

**Entidades emisoras autorizadas (verificar vigencia al momento del trámite):**

| Entidad | Nota |
|---|---|
| Avansi (avansi.com.do) | Histórica del programa e-CF |
| Cámara de Comercio y Producción de Santo Domingo | Emisión presencial y en línea |
| PUCMM / Digicel (verificar) | Confirmar listado vigente en dgii.gov.do |

**Requisitos típicos (confirmar con la entidad):**

- RNC activo del contribuyente.
- Registro Mercantil vigente (persona jurídica).
- Cédula o pasaporte del representante legal **tal como consta en la DGII**.
- Formulario de solicitud de la entidad (en línea o presencial).
- Costo aproximado: RD$1,000–2,500 por 1–2 años de vigencia (varía por entidad).

**Especificaciones técnicas obligatorias:**

- Tipo **A1** (archivo software, no token USB exigido por la API; si la entidad entrega A3
  en token, verificar que permita exportar a `.pfx` — si no, consultar antes de comprar).
- Con **clave privada exportable** y contraseña conocida.
- El **CN/RNC del certificado debe corresponder al RNC emisor** configurado en
  `Enterprise:RNC` (la DGII rechaza la semilla firmada con certificado de otro RNC).
- Vigencia mínima recomendada: 1 año (la pantalla de Configuración avisa 30 días antes de expirar).

**Al recibirlo:**

1. Copiar el `.pfx` a `%LOCALAPPDATA%\PosPalasy\certificados\emisor.pfx` (fuera de la app,
   nunca versionado).
2. Guardar la contraseña en **user-secrets** (`dotnet user-secrets set "Certificado:Password" "..."`)
   o variable de entorno — nunca en `appsettings.json`.
3. Verificar en *Configuración → Certificado* que muestra **"El sistema puede firmar comprobantes"**.

## 3. Paso 2 — Habilitación del RNC como emisor electrónico

**Dónde:** portal DGII → Ciclo del Contribuyente → Facturación Electrónica → e-CF
(dgii.gov.do, sección "Ser emisor electrónico").

**Requisitos (según el reglamento vigente del e-CF):**

- RNC activo y al día (sin deudas ni cartón pendiente).
- Estar inscrito en el **Registro Nacional de Emisores Electrónicos** (solicitud en la Oficina
  Virtual o en la Ventanilla Única, con el formulario de solicitud para ser emisor electrónico).
- Tener al menos tres (3) contribuyentes certificados como emisores electrónicos de referencia
  (requisito reportado por contribuyentes en el proceso 2025–2026; confirmar con la DGII).
- Designar un administrador/electrónico con certificado digital de persona física vinculado a
  procesos tributarios (puede ser el representante legal).

**Qué se obtiene:** la habilitación del RNC en el ambiente de **pre-certificación `testecf`**
y acceso al portal de consultas de resultados de la homologación.

**Plazo realista:** semanas (3–8), dependiente de la carga de la DGII y de la completitud de
la documentación. **Por eso este trámite debe iniciarse hoy.**

## 4. Paso 3 — Acceso a testecf y eNCF

Con el RNC habilitado:

1. Solicitar/confirmar en el portal que el RNC aparece en `testecf`.
2. Verificar en el portal las **secuencias e-NCF asociadas** (rangos E31/E32 autorizados) y
   cargarlas en *Configuración → Secuencias ECF* (`SecuenciasECF`), respetando el rango activo.
3. Configurar el sistema para el primer contacto:
   - `DGII:ModoSimulador = false`
   - `DGII:Ambiente = TestECF`
   - `DGII:GrabarTransmisiones = true` (cofre de contratos: cada respuesta real se graba en
     `artifacts/cofre-dgii/` para verificar/actualizar los contratos asumidos de la KB)

## 5. Paso 4 — Ejecución del plan de homologación

Ejecutar los 8 casos del doc 12 §4 en orden. Tras el primer contacto real:

- Revisar el cofre (`artifacts/cofre-dgii/sesion-*.json`) contra los fixtures de
  `GrabadorTransmisionesDGIITests` — si algún contrato difiere de la KB, ajustar el parser
  puntualmente (los parsers son el punto único de ajuste por método del cliente).
- Registrar cualquier desviación como hallazgo en el doc 12.

## 6. Criterio de cierre

```text
Mínimo un e-CF 32 y un e-CF 31 de PosPalasy ACEPTADOS en testecf (TrackId + portal DGII),
con el cofre de contratos revisado y sin desviaciones de contrato pendientes.
```

Cerrado esto, FASE 5 queda 100% completa incluido su pendiente operativo, y FASE 6 puede
arrancar con evidencia real en lugar de supuestos.
