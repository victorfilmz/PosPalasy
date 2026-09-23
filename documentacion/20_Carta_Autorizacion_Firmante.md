# 20 — Carta de autorización del firmante de comprobantes electrónicos

> **Propósito:** autorizar formalmente a una persona física distinta del representante legal
> como **titular del certificado digital** y firmante de los Comprobantes Fiscales
> Electrónicos (e-CF) emitidos bajo el RNC de la empresa. Es el documento No. 5 del checklist
> del doc 19 y se exige en la validación de identidad de ambas certificadoras.
>
> **Cómo usarla:** completar todos los campos entre corchetes (deben coincidir letra por letra
> con la cédula, el Registro Mercantil y los registros de la DGII), imprimirla en papel membretado
> de la empresa, firmarla por el representante legal y **legalizarla ante notario** (algunas
> certificadoras la piden autenticada de forma obligatoria; ambas la aceptan legalizada). Adjuntar
> copia a color de la cédula de ambas personas.

---

**LUGAR Y FECHA:** [Santo Domingo / ciudad], [dd] de [mes] de 2026

**LA EMPRESA:** [RAZÓN SOCIAL SEGÚN REGISTRO MERCANTIL], sociedad comercial constituida de
conformidad con las leyes de la República Dominicana, portadora del Registro Nacional del
Contribuyente (RNC) No. **[RNC]**, con Registro Mercantil No. **[número de registro]** de
[fecha], de su domicilio fiscal en [dirección completa], representada en este acto por su
**[presidente del directorio / administrador único / gerente general]**, **[NOMBRE COMPLETO
DEL REPRESENTANTE LEGAL]**, dominicano, mayor de edad, portador de la cédula de identidad y
electoral No. **[cédula del representante legal]**, en adelante "LA EMPRESA";

**POR CUANTO:** LA EMPRESA se encuentra en proceso de incorporación al régimen de Comprobantes
Fiscales Electrónicos (e-CF) de la Dirección General de Impuestos Internos (DGII), conforme a
la Norma General vigente sobre Comprobantes Fiscales Electrónicos y la Ley 32-23, y requiere
obtener un certificado digital de persona física con fines de procesos tributarios para la
firma de dichos comprobantes;

**POR CUANTO:** el titular designado ha manifestado su aceptación expresa del encargo;

**POR TANTO:** LA EMPRESA, por medio del suscrito representante legal, en uso de sus facultades,

**AUTORIZA:**

**PRIMERO:** A **[NOMBRE COMPLETO DEL TITULAR/APODERADO]**, dominicano(a), mayor de edad,
portador(a) de la cédula de identidad y electoral No. **[cédula del titular]**, para que, en
nombre y representación de LA EMPRESA y bajo su RNC:

1. Solicite, en calidad de suscriptor, un **certificado digital de persona física para
   procesos tributarios** ante la entidad de certificación autorizada por INDOTEL que la
   empresa seleccione ([Viafirma/Avansi] o la Cámara de Comercio y Producción de Santo Domingo
   — Digifirma), incluyendo la realización de la validación de identidad y prueba de vida que
   corresponda;

2. Actúe como **firmante de los Comprobantes Fiscales Electrónicos (e-CF)** —incluidos
   e-CF 31, 32, 33, 34, RFCE y ANECF— que LA EMPRESA emita mediante su sistema de facturación
   electrónica **PosPalasy**, así como para la autenticación ante los servicios web de la DGII
   (solicitud de semilla y validación) en los ambientes de pre-certificación (TestECF) y
   producción;

3. Realice ante la DGII y la entidad de certificación los trámites necesarios para la
   obtención, instalación, renovación y eventual revocación del referido certificado digital.

**SEGUNDO:** Esta autorización se extiende exclusivamente a los actos descritos en el artículo
anterior y no constituye delegación de la representación legal general de LA EMPRESA.

**TERCERO:** El titular se compromete a custodiar la clave privada del certificado digital con
la debida diligencia, no divulgarla ni permitir su uso por terceros, e informar de inmediato a
LA EMPRESA cualquier pérdida, compromiso o necesidad de revocación, conforme a la Ley 126-02
sobre Comercio Electrónico, Documentos y Firmas Digitales.

**CUARTO:** La presente autorización tiene vigencia hasta su revocación expresa por escrito de
LA EMPRESA o hasta la terminación de la relación del autorizado con la misma, lo que ocurra
primero. LA EMPRESA notificará la revocación a la entidad de certificación y a la DGII.

Atentamente,

```text
_________________________________          _________________________________
[NOMBRE DEL REPRESENTANTE LEGAL]            [NOMBRE DEL TITULAR/APODERADO]
Representante legal                         Autorizado — aceptación de encargo
Cédula: [___________]                       Cédula: [___________]
RNC empresa: [___________]                  Fecha: [____/____/2026]

SELLO DE LA EMPRESA
```

---

## Lista de verificación antes de entregar

```text
☐ Todos los corchetes completados; nombres idénticos a cédula/Registro Mercantil/DGII
☐ Impresa en papel membretado de la empresa
☐ Firmas: representante legal (autoriza) + titular (acepta el encargo)
☐ Sello de la empresa
☐ Legalizada ante notario público
☐ Copia a color de la cédula del representante legal (adjunta)
☐ Copia a color de la cédula del titular (adjunta)
☐ Copia del Registro Mercantil vigente (adjunta, si la certificadora la pide con la carta)
```

## Vínculo con el resto del paquete

| Documento | Papel en el trámite |
|---|---|
| Doc 19 (paquete y formulario) | Solicitud del certificado; esta carta es su adjunto No. 5 |
| Doc 14 (guía de trámite) | Pasos posteriores: carga del .pfx, user-secrets, verificación en PosPalasy |
| Doc 18 (carta a la DGII) | Habilitación del RNC como emisor + TestECF; aquí también puede citarse esta carta como prueba de designación del firmante |

> **Importante (doc 19 §2):** si el apoderado no consta aún ante la DGII como firmante bajo el
> RNC, registrar el poder en la Oficina Virtual antes de emitir comprobantes — la DGII cruza
> el firmante de cada e-CF contra sus registros y rechaza los que no coincidan.
