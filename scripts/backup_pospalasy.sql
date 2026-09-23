-- =====================================================================
-- PosPalasy — Backup diario de la base fiscal (doc 16 §4)
-- Base: PosPalasy_DGII (LocalDB o SQL Server)
-- Uso manual:
--   sqlcmd -S "(localdb)\mssqllocaldb" -i backup_pospalasy.sql
-- El script toma la fecha del día y deja el .bak en el directorio de backups.
-- Verificación mensual obligatoria (doc 16): restaurar el último .bak en una
-- base de prueba y comprobar que ElectronicInvoices y Ventas tienen datos.
-- =====================================================================

DECLARE @Fecha varchar(8) = CONVERT(varchar(8), GETDATE(), 112);
DECLARE @Ruta varchar(500) = 'C:\Backups\PosPalasy\PosPalasy_DGII_' + @Fecha + '.bak';

-- El directorio debe existir: créelo una vez (o el paso 2b lo crea por usted).
BACKUP DATABASE [PosPalasy_DGII]
TO DISK = @Ruta
WITH INIT, CHECKSUM, COMPRESSION, NAME = 'PosPalasy fiscal backup';

-- Retención: eliminar los .bak de más de 30 días (gestionado por la tarea
-- programada con forfiles; el SQL no borra archivos del disco).

-- Comprobación de integridad del backup recién creado:
RESTORE VERIFYONLY
FROM DISK = @Ruta;
