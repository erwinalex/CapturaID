# SchId Consulta

Aplicación de escritorio para consultar las identificaciones capturadas: buscar
por nombre o CURP, ver el listado y sacar la ficha de una persona en PDF con sus
datos y las dos caras de la identificación.

## Por qué .NET y no Flutter ni Delphi

La decisión la forzó el requisito de entregar el ejecutable compilado. Flutter
para escritorio Windows solo se compila **desde** Windows con Visual Studio, y
Delphi es un compilador comercial exclusivo de Windows: ninguno de los dos se
puede construir en el entorno donde se desarrolló esto.

.NET sí cruza-compila a un `.exe` de Windows, y además deja reutilizar
`SchId.Shared`: la conversión del `TDateTime` de Delphi —que tiene la trampa de
los dos días de desfase— y el cálculo del nombre de archivo de las imágenes. En
Flutter o Delphi habría que reimplementar las dos cosas, y que se desincronicen
con la API es cuestión de tiempo.

## Las imágenes están en dos sitios

Es el punto delicado. Conviven dos formas de guardar las identificaciones:

- **La app móvil** deja un archivo en `ImageStorage:BasePath`, con el nombre que
  calcula `ImagePathHelper` (`{id}_frente.jpg`, `{id}_reverso.jpg`).
- **El sistema anterior** guarda el binario en las columnas `IDFoto1` e
  `IDFoto2` de `dbo.Personas`.

La aplicación busca **primero el archivo y luego la columna**. El orden importa:
la herramienta de migración escribe el archivo y solo entonces vacía la columna,
así que cuando existen los dos, el archivo es el que ya se dio por bueno.
Preferir la columna mostraría la copia vieja de alguien ya migrado.

Un archivo de cero bytes no cuenta como imagen y se cae a la columna: si algo
salió mal al escribirlo, es mejor la copia vieja que una ficha sin
identificación.

La ficha dice de dónde salió cada imagen. Durante la transición eso permite ver
qué personas ya están migradas sin entrar a la base a mano.

## Configuración

`appsettings.json`, junto al ejecutable:

```json
{
  "ConnectionStrings": {
    "SchIdDatabase": "Server=localhost\\SQLEXPRESS;Database=SCHIDData;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "ImageStorage": {
    "BasePath": "C:\\SchId\\ImagenesINE"
  }
}
```

Si la carpeta de imágenes no está configurada o no existe, la aplicación **lo
dice en la barra de estado al abrir** y sigue funcionando con lo que haya en la
base. Sin ese aviso, el síntoma sería que las fichas de la app móvil salen sin
imagen y las del sistema anterior salen bien, lo cual apunta a la app móvil, que
no tiene nada que ver.

La cuenta con la que corre puede tener permisos de **solo lectura**: la
aplicación no ejecuta ni una sentencia de escritura, y conviene que la base lo
imponga en vez de confiar en ello.

## Las dos formas de distribuirla

| | Tamaño | Requisito |
|---|---|---|
| **Autónoma** | ~73 MB | Ninguno |
| **Ligera** | ~12 MB | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) instalado |

Con 70 ubicaciones actualizadas por AnyDesk, la ligera se copia mucho más
rápido; la autónoma sirve para una máquina donde no se pueda instalar nada.

Las dos llevan `appsettings.json` al lado: hay que editarlo en cada ubicación.

## Compilar

```bash
dotnet publish src/SchId.Consulta -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publicacion
```

Con `--self-contained false` sale la versión ligera. Se puede compilar desde
Linux gracias a `EnableWindowsTargeting`.

## Privacidad

- El PDF lleva datos personales. Se escribe en una carpeta temporal propia que
  se borra al cerrar la aplicación, no en Escritorio ni en Descargas. Quien
  quiera conservarlo lo guarda desde el visor, a propósito.
- La aplicación no escribe nada en la base de datos.
- No hay registro de consultas: si más adelante hace falta auditoría de quién
  consultó qué, es una decisión a tomar aparte, con su aviso de privacidad.

## La fuente del PDF va incrustada

PdfSharp usa las fuentes del sistema y falla si no encuentra la que se le pide.
Depender de lo que tenga instalado cada máquina significaría que la misma ficha
se ve distinta en cada ubicación, y que un equipo recién formateado deje de
generar fichas por una razón que nadie relacionaría con el PDF.

Por eso viaja **Liberation Sans** dentro del ensamblado. Es SIL OFL 1.1 —permite
incrustarla y redistribuirla, y su licencia va en `Fuentes/`— y es métricamente
compatible con Arial.
