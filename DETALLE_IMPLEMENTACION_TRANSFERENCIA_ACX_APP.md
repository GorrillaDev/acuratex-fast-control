# Detalle de Implementacion de Transferencia ACX en la App Windows

## Objetivo de esta fase

La app Windows ya puede exportar un perfil ACX como binario opaco y transferirlo al firmware con el protocolo existente `FILE_BEGIN -> FILE_DATA -> FILE_END`.

Esta fase solo almacena el archivo `.acx` en LittleFS. No interpreta secciones, no construye `HeadRuntimeProfile`, no ejecuta `HEAD_ACTION`, no envia `FILE_SELECT` para ACX y no activa el ACX como perfil.

## Limites que se conservan

- Límite del formato/compilador ACX: `1 MiB` (`AcxFormatConstants.MaxFileSizeBytes = 1_048_576`).
- Límite temporal del transporte FILE_* actual: `64 KiB` (`65536 bytes`).
- Tamaño de bloque del transporte: `32 bytes` crudos por `FILE_DATA`.

## Flujo implementado

1. La UI selecciona un perfil publicado y, cuando corresponde, una versión publicada.
2. `AcxProfileTransferService` llama a `AcxProfileExportService` para generar el `.acx`.
3. El archivo generado se valida con `AcxProfilePackageReader`.
4. Se comprueba que el archivo no esté vacío.
5. Se comprueba que no supere `65536 bytes` antes de enviar `FILE_BEGIN`.
6. Se reutiliza `CommandFileTransferService.UploadBinaryFileAsync(...)` para enviar el binario exacto.
7. El archivo se transmite como bytes opacos, sin conversión a texto.
8. Al finalizar, no se envía `FILE_SELECT`.
9. La confirmación mínima válida es el ACK correcto de `FILE_END`.

## Clases creadas o modificadas

### Nuevas

- `app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxProfileTransferService.cs`
- `app_windows/AcuratexControlApp/Components/Profiles/AcxProfileTransferCard.razor`
- `app_windows/AcuratexControlApp.Tests/AcxProfileTransferWorkflowTests.cs`

### Modificadas

- `app_windows/AcuratexControlApp/Services/ICommandFileTransferService.cs`
- `app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxModels.cs`
- `app_windows/AcuratexControlApp/Services/Profiles/ProfileServiceCollectionExtensions.cs`
- `app_windows/AcuratexControlApp/Components/MainControlPanel.razor`

## Protocolo reutilizado

Se reutiliza el mismo protocolo que ya usa la app para archivos:

- `FILE_BEGIN|nombre|tamano`
- `FILE_DATA|indice|base64`
- `FILE_END|nombre`

Para ACX no se usa `FILE_SELECT` ni `FILE_INFO`.

La ruta binaria reutilizada vive en `CommandFileTransferService.UploadBinaryFileAsync(...)` y conserva el comportamiento existente de `UploadTextFileAsync(...)` para los archivos `.txt`.

## Progreso

El modelo de progreso expone como mínimo:

- etapa actual
- bytes enviados
- bytes totales
- porcentaje
- índice del bloque actual
- total de bloques

En la ruta binaria, la UI actualiza la barra de progreso y el estado textual mientras se exporta y se envía el archivo.

## Cancelación

La cancelación es cooperativa:

- Antes de `FILE_BEGIN`: no se envía nada.
- Entre bloques: se detienen los nuevos envíos.
- Después de una cancelación o error intermedio: no se envía `FILE_END`.

No existe un comando remoto de aborto en el protocolo actual.

Si la cancelación ocurre después de `FILE_BEGIN`, el archivo temporal puede quedar como `/fs/.upload.tmp`. El firmware lo gestiona según su comportamiento actual.

## Manejo de errores

Se corrigió el reconocimiento en la app de los errores que ya emite el firmware:

- `ERR FILE_BUSY`
- `ERR FILE_PROTECTED`

Estos errores se devuelven de forma inmediata como errores de transferencia, no como un timeout falso de 5 segundos.

La ruta binaria también reporta errores de `FILE_BEGIN`, `FILE_DATA` y `FILE_END` a través del servicio existente, sin inventar comandos nuevos.

## Ubicacion en LittleFS

El firmware actual monta LittleFS en `/fs` y usa `/fs/.upload.tmp` como temporal durante la subida.

Durante `FILE_END`, el firmware renombra ese temporal al archivo final dentro de `/fs`. Para el ACX, eso deja el binario almacenado en LittleFS con su nombre exportado, pero todavía sin activar como perfil.

## Lo que no hace esta fase

- No interpreta el contenido interno del `.acx`.
- No construye `HeadRuntimeProfile`.
- No ejecuta `HEAD_ACTION`.
- No activa el ACX como perfil.
- No envía `FILE_SELECT` para validar o activar el ACX.
- No modifica firmware.

## Pruebas realizadas

Se ejecutaron estas verificaciones:

- `dotnet restore app_windows/AcuratexControlApp/AcuratexControlApp.sln`
- `dotnet build app_windows/AcuratexControlApp/AcuratexControlApp.sln`
- `dotnet test app_windows/AcuratexControlApp/AcuratexControlApp.sln`
- `dotnet test app_windows/AcuratexControlApp.Tests/AcuratexControlApp.Tests.csproj --no-restore -v minimal`

Cobertura validada en pruebas:

- secuencia `FILE_BEGIN -> FILE_DATA -> FILE_END`
- bloques de `32 bytes`
- ultimo bloque parcial
- indices secuenciales
- reconstruccion binaria identica al ACX original
- archivo vacio rechazado antes de `FILE_BEGIN`
- archivo mayor de `65536 bytes` rechazado antes de `FILE_BEGIN`
- ACX invalido rechazado antes de `FILE_BEGIN`
- no se envia `FILE_SELECT`
- `ERR FILE_BUSY` y `ERR FILE_PROTECTED` como error inmediato
- cancelacion antes de `FILE_BEGIN`
- cancelacion entre bloques sin `FILE_END`
- fallo intermedio sin `FILE_END`
- comportamiento `.txt` conservado

## Trabajo pendiente para el lector ACX del firmware

Queda para una fase futura:

- leer el `.acx` almacenado en LittleFS
- validar o inspeccionar su contenido en firmware
- mapear su contenido a una estructura ejecutable si alguna vez se requiere activacion
- definir la transicion segura entre almacenamiento binario y uso operativo

Esta fase no debe cambiar la transferencia ya implementada ni el flujo actual de archivos `.txt`.