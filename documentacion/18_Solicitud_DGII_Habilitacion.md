# Solicitud a la DGII — Habilitación como Emisor Electrónico de Comprobantes Fiscales (e-CF) y acceso al ambiente de pre-certificación TestECF

**Para:** Dirección General de Impuestos Internos (DGII) — Departamento de Facturación Electrónica / Oficina de homologación
**De:** [Razón social de la empresa], RNC [RNC del contribuyente]
**Fecha:** [dd/mm/2026]
**Asunto:** Solicitud de inscripción en el Registro de Emisores Electrónicos y acceso al ambiente de pre-certificación TestECF

---

Estimados señores:

Por medio de la presente, **[Razón social de la empresa]**, representada legalmente por **[Nombre del representante legal]**, portador de la cédula de identidad y electoral No. **[cédula]**, en su condición de **[presidente/administrador/representante legal]**, con RNC No. **[RNC]**, se dirige a ustedes con el propósito de solicitar formalmente:

1. **La inscripción de nuestro contribuyente en el Registro de Emisores Electrónicos de Comprobantes Fiscales (e-CF)**, conforme a la normativa de facturación electrónica vigente y al estándar técnico e-CF publicado por la Dirección General de Impuestos Internos.

2. **El acceso al ambiente de pre-certificación TestECF**, con el fin de ejecutar el proceso de homologación de nuestro sistema de facturación electrónica antes de la operación en el ambiente de producción.

## I. Datos del contribuyente

| Campo | Información |
|---|---|
| Razón social | [Razón social completa según Registro Mercantil] |
| Nombre comercial | [Nombre comercial] |
| RNC | [RNC de 9 u 11 dígitos] |
| Registro Mercantil | [Número y fecha de registro, vigente] |
| Actividad económica | [Descripción según registro DGII] |
| Dirección fiscal | [Dirección completa] |
| Provincia / Municipio | [Códigos DGII 01 / 010100, etc.] |
| Teléfono | [Teléfono] |
| Correo electrónico de contacto | [correo del responsable fiscal] |

## II. Representante técnico del sistema

| Campo | Información |
|---|---|
| Nombre del responsable técnico | [Nombre] |
| Cargo | [Encargado de TI / administrador del sistema] |
| Teléfono | [Teléfono] |
| Correo electrónico | [correo] |

Este representante será el punto de contacto para las incidencias técnicas durante la homologación (contratos de servicios, TrackIds, mensajes de rechazo y validación de XML).

## III. Sistema de facturación electrónica que se somete a homologación

| Campo | Información |
|---|---|
| Nombre del sistema | PosPalasy — Sistema de Punto de Venta con Facturación Electrónica |
| Tipo de solución | Software propio de emisión directa (POS + módulo fiscal e-CF integrado) |
| Tipos de comprobante a emitir | e-CF 31 (Crédito Fiscal), e-CF 32 (Consumo), e-CF 33 (Nota de Débito), e-CF 34 (Nota de Crédito), RFCE 32 (Consumo < RD$250,000.00), ANECF (Anulación de rangos) |
| Servicios DGII que utilizará | Autenticación (semilla/validarsemilla), Recepción e-CF, Recepción RFCE, Consulta de resultado por TrackId, Consulta RFCE, Aprobación Comercial, Anulación de rangos |
| Cumplimiento técnico | Generación y validación de XML contra los XSD oficiales v1.0 publicados por la DGII; firma XML-DSig (RSA-SHA256) con certificado digital tipo A1; código de seguridad de 6 caracteres por comprobante; transmisión multipart con nomenclatura oficial `RNC + eNCF + ".xml"`; regla de recepción por monto (RFCE < RD$250,000.00 / e-CF ≥ RD$250,000.00); consulta de resultado por TrackId con consolidación del veredicto fiscal; manejo de envío incierto, reintentos y contingencia con ventana de 30 días |

El sistema se encuentra completo y probado en ambiente local/simulador; se acompaña de la documentación técnica del diseño, los esquemas XSD y la trazabilidad de pruebas (313 pruebas automatizadas, validación XSD por tipo de comprobante, firma verificada criptográficamente antes de cada transmisión).

## IV. Documentos que se adjuntan

- [ ] Formulario de Solicitud para ser Emisor Electrónico (FI-GDF-016 o el vigente a la fecha)
- [ ] Copia del Registro Mercantil vigente
- [ ] Copia de la cédula de identidad del representante legal
- [ ] Certificado digital tipo A1 vigente a nombre del contribuyente (o constancia de solicitud en trámite ante la entidad certificadora autorizada)
- [ ] Carta de designación del responsable técnico (opcional)
- [ ] Ficha técnica del sistema (la sección III de esta carta puede servir como ficha)

*Marcar los adjuntos incluidos en la entrega.*

## V. Motivación y alcance de la solicitud

Nuestra empresa procesa un volumen creciente de ventas en punto de venta y considera estratégico cumplir con la normativa de comprobantes fiscales electrónicos, garantizando trazabilidad fiscal completa, reducción de carga administrativa y seguridad jurídica en las operaciones. PosPalasy ha sido desarrollado siguiendo estrictamente la documentación técnica de servicios DGII (Descripción Técnica de Servicios Web, v1.7) y los esquemas XSD oficiales, por lo que solicitamos se nos habilite en el ambiente de pre-certificación **TestECF** con:

- **RNC habilitado como emisor electrónico en TestECF**;
- **Secuencias e-NCF de prueba asignadas** (rangos E31/E32 para facturas de consumo y crédito fiscal);
- **Acceso al portal de consulta de resultados de homologación** para verificar el estado de nuestros envíos;
- **Indicaciones sobre el certificado digital requerido** para la autenticación y firma en el ambiente de homologación (si difiere del de producción).

## VI. Compromisos

Nos comprometemos a:

1. Ejecutar el plan de verificación de homologación que la DGII disponga, en el orden y plazos indicados, incluyendo los casos de prueba de firma válida, firma alterada, envío duplicado, corte de red y reintentos.
2. Registrar y reportar toda respuesta real de los servicios web que difiera de la documentación técnica publicada, a los efectos de la mejora continua del estándar.
3. No transmitir comprobantes con datos reales de clientes en el ambiente de homologación, salvo indicación expresa en contrario de la DGII.
4. Mantener vigente el certificado digital y custodiar su clave privada conforme a las buenas prácticas de seguridad.
5. Designar un responsable técnico disponible durante la fase de homologación.

Agradecemos de antemano su atención y quedamos a disposición para proporcionar cualquier información adicional o acudir a sus oficinas para completar el proceso.

Atentamente,

**[Nombre del representante legal]**
[Cargo]
[Razón social de la empresa] — RNC [RNC]
[Teléfono] · [Correo electrónico]

---

*Anexos: los indicados en la sección IV.*
