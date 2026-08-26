# SchId Consulta

Aplicación de escritorio para consultar las identificaciones capturadas: buscar
por nombre o CURP, ver el listado y sacar la ficha de una persona en PDF con sus
datos y las dos caras de la identificación.

## Por qué .NET y no Flutter ni Delphi

La decisión la forzó el requisito de entregar el ejecutable compilado. Flutter
para escritorio Windows solo se compila **desde** Windows con Visual Studio, y
Delphi es un compilador comercial exclusivo de Windows: ninguno de los dos se
puede construir en el entorno donde se desarrolló esto.

.NET sí cruza-compila a un `.exe` de Windows, y además comparte solución con la
API: los mismos modelos, el mismo token y las mismas convenciones. La conversión
del `TDateTime` de Delphi —que tiene la trampa de los dos días de desfase— la
hace el servidor y llega ya resuelta.

## Va por la API, no contra SQL Server

La primera versión se conectaba directo a la base de datos. Estaba mal, y por
una razón concreta: **las imágenes nuevas viven como archivo en el disco del
servidor**. Una aplicación en otra PC no puede leer `C:\SchId\ImagenesINE` sin
un recurso compartido de Windows — y montar un compartido con imágenes de INE
para que lo lea cada terminal es peor que servirlas por HTTP.

Con el servidor de por medio, además: la cadena de conexión deja de andar suelta
en cada terminal, el acceso pasa por el mismo token con roles que ya usa el
kiosko, y viaja cifrado por el TLS que ya está montado.

Usa tres endpoints, todos con el rol `Consulta`:

| Endpoint | Para qué |
|---|---|
| `GET /api/personas/buscar?q=` | El listado, por nombre o CURP |
| `GET /api/personas/{id}` | Los datos de la ficha |
| `GET /api/personas/{id}/imagen/{frente\|reverso}` | Cada cara de la identificación |
| `GET /api/personas/{id}/estancias` | Las estancias de esa persona |

## Las imágenes están en dos sitios

Es el punto delicado, y lo resuelve **el servidor**. Conviven dos formas de
guardar las identificaciones:

- **La app móvil** deja un archivo en `ImageStorage:BasePath`, con el nombre que
  calcula `ImagePathHelper` (`{id}_frente.jpg`, `{id}_reverso.jpg`).
- **El sistema anterior** guarda el binario en las columnas `IDFoto1` e
  `IDFoto2` de `dbo.Personas`.

La API busca **primero el archivo y luego la columna** (ver
`ResolutorImagenesIne`). El orden importa: la herramienta de migración escribe
el archivo y solo entonces vacía la columna, así que cuando existen los dos, el
archivo es el que ya se dio por bueno. Preferir la columna mostraría la copia
vieja de alguien ya migrado.

Un archivo de cero bytes no cuenta como imagen y se cae a la columna: si algo
salió mal al escribirlo, es mejor la copia vieja que una ficha sin
identificación.

La respuesta trae la cabecera `X-Origen-Imagen`, y la ficha dice de dónde salió
cada imagen. Durante la transición eso permite ver qué personas ya están
migradas sin entrar a la base a mano.

## El panel de estancias

A la derecha de la lista, con el divisor movible. Muestra las estancias de quien
esté seleccionado, de la más reciente a la más antigua, hasta 100 — un huésped
habitual de años puede tener cientos y el panel solo enseña las últimas.

`dbo.Estancias` es del PMS: la API **solo la lee**, nunca escribe ahí.

Dos detalles que no son evidentes:

- **Una salida en cero no es una fecha.** En estas columnas `numeric`, un 0 se
  convierte en la época de Delphi (1899-12-30), y eso no significa que alguien
  saliera en 1899: significa que el campo nunca se llenó. Se muestra como `—`.
  Es la misma regla que ya aplicaba el job de retención, ahora compartida en
  `DelphiDateTime.ToDateTimeSiEsReal`.
- **Por eso una salida vacía es ambigua**: puede ser una estancia todavía
  abierta o un campo que nadie llenó. En la columna se ven igual y desde aquí no
  se distinguen, así que el panel no afirma cuál de las dos es.

La bandera `Hospedado` se muestra tal como viene del PMS (`1` → Sí, `0` → No),
sin interpretarla más allá de eso.

Moverse por la lista con las flechas dispara una petición por renglón, así que
cada carga cancela la anterior y se vuelve a comprobar la selección antes de
pintar. Sin eso, una respuesta lenta podía acabar mostrando las estancias de una
persona con otra ya seleccionada.

### En el PDF

La ficha también lleva la tabla de estancias, en el mismo orden. Va al final a
propósito: lo primero de una ficha de identificación es quién es y cómo se ve su
identificación, así que quien imprima solo la primera hoja se lleva eso.

Si la tabla no cabe entera se parte, y el encabezado se repite en la página
siguiente — una tabla continuada sin sus títulos son cuatro columnas de fechas
sin saber cuál es la entrada y cuál la salida. Y no se empieza la tabla si solo
caben uno o dos renglones al final de una hoja: se pasa entera a la siguiente.

El pie con la fecha de generación y el aviso de datos personales sale en
**todas** las páginas: una hoja suelta sin esa advertencia es justo lo que no
conviene que circule.

## Configuración

`appsettings.json`, junto al ejecutable:

```json
{
  "Servidor": {
    "Direccion": "192.168.1.226:7443",
    "Token": "el-token-con-rol-Consulta"
  }
}
```

**La dirección es solo la IP y el puerto.** Igual que en la app Android, nunca se
conecta por IP: se pide siempre `https://schid-servidor:puerto` y ese nombre se
resuelve a la IP configurada al abrir el socket. Por eso el mismo certificado y
la misma compilación sirven para las 70 ubicaciones, y un cambio de IP se
arregla aquí sin tocar certificados.

El token tiene que llevar el rol **`Consulta`**, no el del kiosko. En el
`appsettings.json` del servidor:

```json
{
  "Nombre": "consulta-recepcion",
  "Token": "...",
  "Roles": ["Consulta"]
}
```

### Confiar en el certificado, una vez por terminal

La validación del certificado se deja en manos de Windows, así que la CA tiene
que estar en el almacén de raíces de cada PC donde corra la consulta:

```powershell
Import-Certificate -FilePath schid_ca.crt -CertStoreLocation Cert:\LocalMachine\Root
```

No se escribió una validación propia a propósito: es la forma más común de
acabar aceptando cualquier certificado sin enterarse, y aquí no hace falta.

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
- La aplicación no escribe nada: solo hace peticiones GET, y el token de
  consulta no tiene permiso para registrar nada aunque se quisiera.
- El listado no trae domicilio ni teléfono: una búsqueda por apellido puede
  devolver doscientas personas, y no hay razón para poner el domicilio de
  doscientos desconocidos en la pantalla de quien busca a uno.
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
