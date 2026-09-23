# 19 — Paquete de solicitud del certificado digital (Avansi / Cámara de Comercio)

> **Propósito:** ejecutar el paso 1 del doc 14 (§2) — obtener el certificado digital que
> PosPalasy necesita para firmar XML-DSig (`FirmadorComprobanteECF`) y autenticar la semilla
> (`DgiiAuthenticator`). Este documento es el paquete listo para presentar: comparativa de
> proveedores, checklist de documentos, formulario precargado y plan de entrega.
>
> **Fecha:** 23-09-2026 · Información verificada en los portales oficiales de Viafirma/Avansi
> (viafirma.do) y Cámara de Comercio y Producción de Santo Domingo (digi.camarasantodomingo.do).

---

## 0. Dato crítico antes de pagar: el certificado NO va a nombre de la empresa

El certificado que la DGII acepta para firmar e-CF es un **certificado de persona física,
perfil "Procesos Tributarios" (PT)**, emitido a nombre de la **persona que firma** las
facturas — el representante legal o quien la empresa autorice — y **debe ser la misma persona
que consta en la DGII como autorizada a firmar bajo el RNC emisor**.

- ❌ No pidas un "certificado de persona jurídica" ni "certificado SSL": la DGII no los acepta.
- ✅ Pide literalmente: **"Certificado Digital de Persona Física para Procesos/Tareas Tributarios"**
  (Avansi/Viafirma) o **"Certificado de Facturación Electrónica"** (Digifirma, Cámara de Santo Domingo).
- La DGII cruza el RNC + datos de la persona física firmante contra sus registros: si la persona
  no coincide con la autorizada, el comprobante se rechaza aunque el certificado sea válido.

**Validación previa obligatoria (10 min, gratis):** entrar a la Oficina Virtual DGII con el RNC
y confirmar quién figura como firmante autorizado. Si el registro mercantil muestra un
representante distinto al que firmará, resolver ese cambio ANTES de solicitar el certificado —
un expediente inconsistente se devuelve con días perdidos.

---

## 1. Comparativa de proveedores (verificada 23-09-2026)

| Criterio | Avansi (Viafirma) | Cámara de Comercio Sto. Dgo. (Digifirma) |
|---|---|---|
| Producto exacto | Certificado de Persona Física para Procesos Tributarios (PT) | Certificado de Facturación Electrónica |
| Nombre que pedir | "Certificado PT de persona física" | "Certificado de Facturación Electrónica" |
| Precio | **RD$2,360** impuestos incluidos (solicitud o renovación) | **USD$29.95** (1 año) / **USD$45.00** (2 años) |
| Vigencia | 1 año | 1 o 2 años |
| Formato de entrega | Archivo software (descarga) — compatible con A1/.pfx | Archivo software — compatible con A1/.pfx |
| Proceso | Íntegramente en línea | En línea + prueba de vida por correo |
| Entidad de validación | Viafirma actúa como Entidad de Certificación y de Registro (RA) | Cámara Sto. Dgo. bajo Ley 126-02, acreditada INDOTEL |
| Validación de identidad | En línea (RA) | Cédula/pasaporte a color + prueba de vida |
| Portal | viafirma.do → Certificados → Procesos Tributarios | digi.camarasantodomingo.do → Solicitar |

**Recomendación:** ambos son válidos (ambas entidades están acreditadas por INDOTEL, requisito
de la Ley 126-02). Criterios de decisión:

1. **Costo total:** Digifirma 2 años (USD$45 ≈ RD$2,700) vs. 2 renovaciones Avansi
   (RD$4,720). Si se contrata 2 años con la Cámara, se evita una renovación.
2. **Riesgo de vencimiento:** la DGII rechaza e-CF firmados con certificado vencido y **no hay
   período de gracia** — ese día se detiene la facturación electrónica. Vigencia de 2 años
   reduce el riesgo. PosPalasy avisa 30 días antes (pantalla Configuración → Certificado).
3. **Velocidad:** ambas emiten en 1–5 días laborables con expediente limpio.

Decisión sugerida: **Digifirma 2 años** por costo y vigencia; **Avansi** si se prefiere el
actor histórico del programa e-CF o si el firmante ya tiene expediente con ellos.

> **Lista oficial:** la lista vigente de Entidades de Certificación autorizadas vive en el
> portal de INDOTEL — revisarla al momento del trámite porque se actualiza.

---

## 2. Checklist de documentos (persona jurídica que factura)

Imprimir/adjuntar y marcar. Los requisitos varían levemente por proveedor; este paquete cubre
a ambos.

| # | Documento | Avansi | Digifirma | Estado |
|---|---|:---:|:---:|---|
| 1 | Cédula de identidad vigente **a color** del firmante (titular) | ✅ | ✅ | ☐ |
| 2 | Cédula/pasaporte a color del **representante legal** (si firma un delegado) | — | ✅ | ☐ |
| 3 | **RNC de la empresa** activo (constancia de la Oficina Virtual) | ✅ | ✅* | ☐ |
| 4 | Documento de representación: **Registro Mercantil** vigente / acta de asamblea | ✅ | ✅* | ☐ |
| 5 | **Carta de autorización firmada por la empresa** (si el firmante no es el representante legal) | — | ✅ | ☐ |
| 6 | **Prueba de vida** (verificación por correo/video según proveedor) | ✅ | ✅ | ☐ |
| 7 | Correo electrónico y teléfono del solicitante | ✅ | ✅ | ☐ |
| 8 | Formulario de solicitud de la entidad (en línea) | ✅ | ✅ | ☐ |
| 9 | Método de pago | ✅ | ✅ | ☐ |

\* Digifirma los pide vía el formulario y la carta de autorización; confirmar el expediente
exacto al iniciar la solicitud en línea.

**Reglas del expediente (causas típicas de devolución):**

- Los datos del formulario deben coincidir **letra por letra** con los que constan en la DGII
  (cédula o pasaporte, incluidas tildes y guiones).
- El nombre del firmante debe coincidir con el Registro Mercantil y con lo que la DGII tiene
  registrado para el RNC.
- Cédula vigente — Avansi aceptó cédulas vencidas durante 2024 en condiciones especiales, pero
  no se debe contar con ello.

---

## 3. Formulario precargado (completar corchetes)

Datos técnicos para la solicitud — precargar de aquí y verificar contra la DGII:

```text
=== SOLICITANTE (titular del certificado — persona física firmante) ===
Nombres y apellidos        : [SEGÚN CÉDULA — idéntico a DGII]
Tipo y número de documento : Cédula [000-0000000-0]
Nacionalidad               : Dominicana
Correo electrónico         : [correo del firmante — revisado a diario]
Teléfono                   : [809/829/849-000-0000]
Cargo en la empresa        : [Representante Legal / Apoderado según carta]

=== EMPRESA (contribuyente emisor) ===
Razón social               : [RAZÓN SOCIAL SEGÚN REGISTRO MERCANTIL]
RNC                        : [RNC DE 9 U 11 DÍGITOS — debe coincidir con Enterprise:RNC de PosPalasy]
Actividad económica        : [DESCRIPCIÓN SEGÚN REGISTRO MERCANTIL]
Dirección fiscal           : [DIRECCIÓN SEGÚN RNC]
Registro Mercantil         : [NÚMERO Y FECHA DE REGISTRO — vigente]

=== SOLICITUD ===
Tipo de certificado        : Persona Física — Procesos Tributarios (Avansi)
                             / Certificado de Facturación Electrónica (Digifirma)
Vigencia                   : [1 año | 2 años — ver recomendación §1]
Uso declarado              : Firma de Comprobantes Fiscales Electrónicos (e-CF)
                             ante la DGII (Ley 32-23) y trámites en Oficina Virtual
Entidad certificadora      : [Viafirma/Avansi | Cámara de Comercio y Producción de Santo Domingo]
```

> **La contraseña del certificado** se define en el formulario (se introduce dos veces).
> Elegir una contraseña fuerte y **registrarla de inmediato en el gestor de secretos de la
> empresa**: sin ella el `.pfx` no sirve y NO hay recuperación — toca emitir un certificado nuevo.

---

## 4. Plan de ejecución (checklist operativo)

```text
Día 0  ☐ Verificar en Oficina Virtual DGII quién es el firmante autorizado del RNC
       ☐ Decir proveedor (§1) y completar formulario (§3)
Día 0  ☐ Reunir documentos §2 (cédula a color, RNC, registro mercantil, carta si aplica)
Día 1  ☐ Someter solicitud en línea + subir documentos + pagar
Día 1-3 ☐ Completar prueba de vida (atender el correo el mismo día: expira)
Día 3-5 ☐ Recibir/emitir el certificado: descargar el archivo en UNA computadora
```

**Al recibir el certificado (seguir doc 14 §2 "Al recibirlo"):**

```text
☐ Copiar el .p12/.pfx a %LOCALAPPDATA%\PosPalasy\certificados\emisor.pfx (nunca versionarlo,
   nunca circularlo por correo/WhatsApp: firma con valor legal a nombre de una persona)
☐ dotnet user-secrets set "Certificado:Password" "..." (nunca en appsettings.json)
☐ Configuración → Certificado debe mostrar "El sistema puede firmar comprobantes"
☐ Respaldo del .pfx + contraseña en DOS lugares seguros (p. ej. gestor de secretos + USB cifrada)
☐ Registro en calendario: renovación 30 días antes del vencimiento (PosPalasy avisa;
   aun así la renovación es un trámite de días — no dejarla para la semana del vencimiento)
```

**Verificación técnica de PosPalasy (doc 12/14):** el CN/datos del certificado deben
corresponder al RNC emisor de `Enterprise:RNC`; la DGII rechaza la semilla firmada con un
certificado de otro RNC.

---

## 5. Conexión con el resto del trámite

| Este paquete | Siguiente paso | Documento |
|---|---|---|
| Certificado obtenido y cargado | Habilitación del RNC como emisor electrónico + acceso testecf — **la carta formal ya está redactada** | Doc 18 (enviar junto con este paquete) |
| Certificado cargado | Checklist de homologación y plan de verificación de los 8 casos | Docs 12 §4 y 14 §4–5 |

El certificado y la habilitación del RNC (doc 14 §3) son **paralelizables**: iniciar ambos hoy.
La carta del doc 18 §IV admite adjuntar la **constancia de certificado en trámite** si el A1
aún no llegó — no esperar el certificado para enviar la solicitud a la DGII.

---

## 6. Errores frecuentes que este paquete evita

1. **Comprar el perfil equivocado** (persona jurídica / SSL / firma genérica): la DGII solo
   valida el perfil PT de persona física para e-CF.
2. **Firmante no autorizado en DGII**: el certificado se emite pero la DGII rechaza cada e-CF.
   Verificar en Oficina Virtual ANTES de pagar.
3. **Expediente inconsistente**: nombre con/sin tilde inconsistente entre documentos,
   representante desactualizado en el registro mercantil → devolución con días perdidos.
4. **Perder el `.p12` o su contraseña**: no hay recuperación; certificado nuevo = nuevo trámite.
5. **Dejarlo vencer**: la DGII empieza a rechazar e-CF el mismo día del vencimiento, sin
   período de gracia. Regla: iniciar renovación con 30 días de anticipación.
