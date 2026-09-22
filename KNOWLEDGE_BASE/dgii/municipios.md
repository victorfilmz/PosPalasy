# KNOWLEDGE_BASE/dgii/municipios.md

**Fuente:** Extraído de e-CF 32 v.1.0.xsd
**Fecha:** 18/09/2026

---

## Provincias y Municipios de la República Dominicana

### Provincia: Distrito Nacional (01)

| Código | Municipio |
|--------|-----------|
| 010100 | Santo Domingo de Guzmán |
| 010101 | Santo Domingo de Guzmán (D.M.) |
| 010102 | Los Alcarrizos (D.M.) |
| 010103 | Pantoja (D.M.) |
| 010104 | Pedro Santana (D.M.) |
| 010105 | Bani (D.M.) |
| 010106 | Boca Chica (D.M.) |
| 010107 | La Caleta de los Caes (D.M.) |
| 010108 | Guayacan (D.M.) |
| 010109 | Haina (D.M.) |
| 010110 | Herrera (D.M.) |
| 010111 | Jagua Grande (D.M.) |
| 010112 | Jardines del Yuna (D.M.) |
| 010113 | Licey al Medio (D.M.) |
| 010114 | Manoguayabo (D.M.) |
| 010115 | Naco (D.M.) |
| 010116 | Sabana Perdida (D.M.) |
| 010117 | San Anton (D.M.) |
| 010118 | San Carlos (D.M.) |
| 010119 | San Cristobal (D.M.) |
| 010120 | San Martin (D.M.) |
| 010121 | Santiago Rodriquez (D.M.) |
| 010122 | Santiago Zepp (D.M.) |
| 010123 | Tamboril (D.M.) |
| 010200 | Gazcue |
| 010300 | Villa Mella |
| 010400 | Puerta Plata Norte |
| 010500 | Sabana de la Mar |
| 010600 | Guanabo |
| 010700 | Guayabal |
| 010800 | Guerra |
| 010900 | Sabana Grande de Boya |
| 011000 | Santo Domingo Este |
| 011100 | Santo Domingo Oeste |
| 011200 | Santa Clara (D.M.) | -->

### Provincia: Azua (02)

| Código | Municipio |
|--------|-----------|
| 020100 | Azua |
| 020101 | Azua (D.M.) |
| 020102 | Sabana Buey (D.M.) |
| 020103 | Rancho Arriba (D.M.) |
| 020200 | Baní |
| 020300 | Estebanía |
| 020400 | Las Otras |
| 020500 | Niguá |
| 020600 | Peñaflor |
| 020700 | Pescadero |
| 020800 | Sabana de la Mar |
| 020900 | Sabana Yegua |
| 021000 | Tamayo |
| 021100 | Villa Borbuena (Boca de Mao) |

### Provincia: Baoruco (03)

| Código | Municipio |
|--------|-----------|
| 030100 | Barahona |
| 030101 | Barahona (D.M.) |
| 030102 | La Ciénaga (D.M.) |
| 030103 | La Ciénaga Centro (D.M.) |
| 030104 | La Ciénaga Norte (D.M.) |
| 030105 | La Ciénaga Sur (D.M.) |
| 030106 | La Ciénaga Oeste (D.M.) |
| 030107 | Angelina (D.M.) |
| 030108 | La Esquina (D.M.) |
| 030109 | El Recodo (D.M.) |
| 030110 | Herrera (D.M.) |
| 030111 | Humber (D.M.) |
| 030112 | La Flor (D.M.) |
| 030113 | La Juanita (D.M.) |
| 030114 | Nuevo (D.M.) |
| 030115 | Padre Las Casas (D.M.) |
| 030200 | Enrique |
| 030300 | Galván |
| 030400 | Janico |
| 030500 | Neiba |
| 030600 |<|special_4013|>.0601 | Neiba (D.M.) |
...

### Provincias restantes

40=La Altagracia, 41=Independencia, 42=La Romana, 43=San Pedro de Macorís, 44=Santiago Rodríguez, 45=La Vega, 46=Montecristi, 47=María Trinidad Sánchez, 48=Monseñor Nouel, 49=Monte Plata, 50=Duarte, 51=El Seibo, 52=San José de Ocoa, 53=Santiago, 54=Santo Domingo, 55=Samaná, 56=San Juan, 57=Valverde, 58=Maria Montez, 59=Padre Varela (no existe), 60=Miches, 61=Nagua, 62=OBN, 63=Pakito, 64=Santiago Islita, 65=Tamboril (no existe), 66=Veragua, 67=Estero Hondo, 68=Yuma

**Total provincias:** 31 (más Distrito Nacional = 32 jurisdicciones)

---

## Nota sobre uso

Los códigos se usan en los campos:
- `Municipio` / `MunicipioComprador` (e-CF 32)
- `Provincia` / `ProvinciaComprador` (e-CF 32)
- `CodigoTelefono` (formato: provincia + municipio + teléfono)

**Formato del código:** `XXYYY00` donde XX=provincia (2 dígitos), YYY=municipio (3 dígitos), 00=código municipal (2 dígitos opcionales).

Ejemplo: Distrito Nacional = `010000`
