-- Corre esto ANTES de la migración para ver cuánto espacio están ocupando
-- las imágenes hoy, y DESPUÉS (y después del DBCC SHRINKFILE) para confirmar
-- que se liberó espacio.

USE [SCHIDData];
GO

-- Tamaño actual del archivo de datos y espacio libre dentro de él.
EXEC sp_spaceused;
GO

-- Cuántas personas siguen teniendo imágenes como binario en la tabla
-- (debería ser 0 después de correr SchId.ImageMigration sin --dry-run).
SELECT
    COUNT(*) AS PersonasConImagenEnBD
FROM dbo.Personas
WHERE IDFoto1 IS NOT NULL OR IDFoto2 IS NOT NULL;
GO

-- Personas cuya última salida ya pasó del periodo de retención configurado
-- (365 días por defecto) - útil para revisar a mano antes de habilitar el
-- borrado automático en el job de retención.
SELECT
    p.ID,
    RTRIM(p.Nombre) AS Nombre,
    RTRIM(p.CURP) AS CURP,
    MAX(DATEADD(DAY, e.FSalida, '18991230')) AS UltimaSalida
FROM dbo.Personas p
JOIN dbo.Estancias e ON e.IdPersona = p.ID
WHERE e.FSalida IS NOT NULL
GROUP BY p.ID, p.Nombre, p.CURP
HAVING MAX(DATEADD(DAY, e.FSalida, '18991230')) < DATEADD(DAY, -365, GETDATE())
ORDER BY UltimaSalida;
GO
