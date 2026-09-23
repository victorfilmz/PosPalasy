# Verificador de homologación TestECF

Ejecutable de consola que corre los **8 casos de verificación del doc 12 §4** contra la DGII real
(`testecf`) en orden, con aserciones y bitácora de contratos. Reutiliza los **componentes reales**
de PosPalasy (serializer XSD, firmador XML-DSig, autenticador semilla→token, cliente DGII), no
dobles: lo que aprueba este script es exactamente lo que la aplicación firma y transmite.

## Cuándo usarlo

Cuando la operación complete el checklist del doc 12 §2 (certificado A1 instalado, RNC habilitado
en testecf, rangos e-NCF asignados). Antes de eso el script no tiene contra qué operar.

## Preparación (una vez)

1. Copiar la plantilla y completar los valores reales:

   ```bash
   cp tools/HomologacionTestECF/appsettings.Homologacion.ejemplo.json \
      tools/HomologacionTestECF/appsettings.Homologacion.json
   ```

   `appsettings.Homologacion.json` contiene la contraseña del certificado: **no versionarlo**
   (está en `.gitignore`). Alternativa: variables de entorno `POSPALASY_Certificado__Password`, etc.

2. Instalar el `.pfx` del emisor en `%LOCALAPPDATA%\PosPalasy\certificados\` (doc 14 §2) y
   confirmar que la pantalla *Configuración → Certificado* dice "El sistema puede firmar
   comprobantes" — o dejar que el script lo verifique (paso de pre-requisitos).

## Ejecución

```bash
dotnet run --project tools/HomologacionTestECF            # los 8 casos en orden
dotnet run --project tools/HomologacionTestECF -- --list  # solo listar los casos
```

Códigos de salida: `0` = todo lo ejecutable PASS · `1` = hay FALLOs · `2` = pre-requisitos
incumplidos (no se contactó a la DGII).

## Qué ejecuta

| # | Caso | Qué verifica |
|---|---|---|
| 1 | Autenticación real | Semilla e-CF y RFCE firmadas con el certificado → token Bearer de ambos hosts |
| 2 | e-CF 32 (consumo) | XSD → firma → transmisión → TrackId → consulta → **Aceptado** (paso obligatorio) |
| 3 | e-CF 31 (crédito fiscal) | Igual que el 2, con comprador registrado y `FechaVencimientoSecuencia` |
| 4 | Resultado | `secuenciaUtilizada=true` + consulta RFCE del comprobante del caso 2 |
| 5 | Firma alterada (negativo) | Documento válido firmado, alterado después de firmar → **la DGII debe rechazarlo** |
| 6 | Envío duplicado | Re-transmisión idéntica → respuesta coherente; contrato documentado en la bitácora |
| 7 | TrackIds por e-NCF | Recuperación ante pérdida del TrackId local; forma de respuesta registrada sin inventar contrato |
| 8 | Corte prolongado (local) | La cola reintenta y desagua en orden, sin pérdidas ni duplicados |

Si el caso 1 falla, los casos 2–7 se marcan como dependientes de autenticación (el mensaje indica
qué revisar: certificado vs RNC habilitado). El caso 8 es local y corre siempre.

## Después de la ejecución

En `tools/HomologacionTestECF/artifacts/homologacion-testecf/` quedan:

- `informe-*.json` — veredictos por caso, criterio doc 12 §5 y datos de la sesión.
- `bitacora-*.json` — **cada respuesta cruda de la DGII** (HTTP + cuerpo).

La bitácora es el insumo para conciliar los contratos asumidos de la KB: comparar contra los
fixtures de `GrabadorTransmisionesDGIITests`; si algún contrato difiere, ajustar el parser del
método correspondiente y convertir la respuesta real en fixture nuevo (doc 12 §4, nota final).

> El script **consume secuencias e-NCF reales** del rango de testecf (2 del rango 32 + 1 del 31 +
> 1 del 32 para el negativo): configure `ENCFDesde32/31` en las primeras secuencias libres y
> avance los valores entre ejecuciones según lo consumido.
