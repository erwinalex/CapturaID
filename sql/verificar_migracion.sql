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

-- ---------------------------------------------------------------------------
-- Personas que ya pasaron el periodo de retención (365 días por defecto).
-- Útil para revisar a mano antes de poner HabilitarBorradoAutomatico en true.
--
-- Esta consulta reproduce a propósito el MISMO criterio que RetentionPolicy.cs:
-- se toma la señal de actividad más reciente de la persona, venga de donde
-- venga (última salida, última entrada, última modificación o fecha de alta),
-- y no solo la última salida. Si aquí se filtrara únicamente por FSalida, la
-- lista no coincidiría con lo que el job reporta en el log, y quien la revise
-- creería que el job va a borrar menos de lo que en realidad borra.
--
-- Sobre la conversión de fechas: las columnas son TDateTime de Delphi, cuyo día
-- cero es 1899-12-30. NO se puede usar CAST(columna AS DATETIME), porque SQL
-- Server toma 1900-01-01 como día cero y el resultado queda 2 días desfasado.
-- DATEADD(DAY, <numeric>, '18991230') sí da la fecha correcta, aunque trunca la
-- hora — para comparar periodos de un año eso da igual.
--
-- Se descartan las fechas anteriores a 1990 porque un 0 en la columna numeric
-- se convierte en 1899-12-30: eso no es actividad real, es un campo que nunca
-- se llenó.
-- ---------------------------------------------------------------------------
WITH ActividadEstancias AS (
    SELECT
        e.IdPersona,
        MaxIngreso = MAX(DATEADD(DAY, e.FIngreso, '18991230')),
        MaxSalida  = MAX(DATEADD(DAY, e.FSalida,  '18991230'))
    FROM dbo.Estancias e
    WHERE e.IdPersona IS NOT NULL
    GROUP BY e.IdPersona
),
Actividad AS (
    SELECT
        p.ID,
        Nombre = RTRIM(p.Nombre),
        CURP   = RTRIM(p.CURP),
        UltimaActividad = (
            SELECT MAX(f.Fecha)
            FROM (VALUES
                (DATEADD(DAY, p.FechaAlta,          '18991230')),
                (DATEADD(DAY, p.UltimaModificacion, '18991230')),
                (a.MaxIngreso),
                (a.MaxSalida)
            ) AS f(Fecha)
            WHERE f.Fecha >= '19900101'
        )
    FROM dbo.Personas p
    LEFT JOIN ActividadEstancias a ON a.IdPersona = p.ID
)
SELECT
    ID,
    Nombre,
    CURP,
    UltimaActividad,
    DiasSinActividad = DATEDIFF(DAY, UltimaActividad, GETDATE())
FROM Actividad
WHERE UltimaActividad IS NOT NULL
  AND UltimaActividad < DATEADD(DAY, -365, GETDATE())
ORDER BY UltimaActividad;
GO

-- Personas que el kiosko capturó pero a las que el PMS nunca les asignó una
-- estancia. No es un error por sí solo (un walk-in que se arrepintió deja
-- exactamente este rastro), pero si la lista crece de forma sostenida conviene
-- revisar si el PMS está amarrando bien el Id que devuelve /api/personas/registro.
SELECT
    p.ID,
    RTRIM(p.CURP) AS CURP,
    DATEADD(DAY, p.FechaAlta, '18991230') AS FechaAlta
FROM dbo.Personas p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Estancias e WHERE e.IdPersona = p.ID)
  AND DATEADD(DAY, p.FechaAlta, '18991230') >= '19900101'
ORDER BY FechaAlta DESC;
GO
