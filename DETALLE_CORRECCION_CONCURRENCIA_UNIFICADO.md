# DETALLE_CORRECCION_CONCURRENCIA_UNIFICADO

Fecha: 2026-07-03

Estado: BLOQUEADO ANTES DE IMPLEMENTAR

Motivo: la verificacion obligatoria de enrutamiento real de `HEAD_*` encontro que
`app_head_program_process_line(...)` existe y se compila, pero no esta conectado
al dispatcher activo. Por instruccion de la tarea, no se implemento el resto para
evitar agregar una segunda ruta de despacho sin confirmar la ruta activa.

## A. Archivos modificados

- `DETALLE_CORRECCION_CONCURRENCIA_UNIFICADO.md`

No se modifico firmware, app ni sistema modular.

## B. Carrera exacta corregida

No se corrigio ninguna carrera en codigo porque la tarea quedo bloqueada en la
Parte 1.

Carreras pendientes confirmadas por los documentos base:

- `FILE_END` puede reemplazar el TXT activo mientras `HEAD_ACTION` lo usa.
- `FILE_DELETE` puede borrar el TXT activo durante `HEAD_ACTION`.
- Dos subidas pueden compartir `/fs/.upload.tmp` y pisar `s_upload`.
- La app usa handlers temporales `LineReceived` sin ID de correlacion.
- `HEAD_ACTION`, `HEAD_PROGRAM_SELECT`, `FILE_*` y comandos directos no comparten
  una coordinacion unica.

## C. Arquitectura del coordinador app

No implementada.

DiseÃ±o pendiente previsto para la siguiente etapa:

- `UnifiedHeadOperationCoordinator` registrado una sola vez en
  `UnifiedSystemForm`.
- `SemaphoreSlim` para operaciones exclusivas.
- Estado observable de operacion actual para la UI.
- Exclusiones para `HEAD_ACTION`, `HEAD_PROGRAM_SELECT`, `UploadTextFileAsync`,
  `SaveEditedTextAsync`, `DeleteFileAsync`, `SelectFileAsync` y cambios de perfil
  TXT activo.
- `HEAD_STOP` y `HEAD_STATUS` fuera del gate exclusivo para que puedan ejecutarse
  durante una accion.

## D. Arquitectura del guard firmware

No implementada.

DiseÃ±o pendiente previsto:

- API pequeÃ±a y thread-safe en el runner o en un guard compartido.
- `HEAD_ACTION` protege el programa TXT activo mientras pueda abrirlo, leerlo o
  ejecutar sus lineas.
- `FILE_BEGIN`, `FILE_END` y `FILE_DELETE` consultan de forma atomica si la
  mutacion de LittleFS esta permitida.
- `FILE_*` responde `BUSY` si el archivo activo esta protegido.
- Sin acceso `extern` desde `file_transfer.cpp` a variables privadas del runner.

## E. Orden de locks

No se agregaron locks.

Orden pendiente recomendado:

1. Guard compartido de programa/archivo activo.
2. Mutex corto de estado del runner `s_state_mutex`, solo para snapshots o
   actualizaciones pequeÃ±as.
3. Mutex propio de upload/archivo, si se mantiene separado.

Regla pendiente: no mantener ningun mutex durante respuestas USB/UART, logging
extenso, `vTaskDelay`, espera CAN, `Task.Delay` ni espera de respuesta de
firmware.

## F. Razon por la que no existe deadlock

No se introdujeron locks nuevos, por lo tanto este cambio no puede introducir
deadlock.

La implementacion pendiente debe evitar:

- Mantener el guard firmware mientras se responde por transporte.
- Mantener `s_state_mutex` durante ejecucion de archivo, CAN o WAIT.
- Doble adquisicion del coordinador app cuando `HeadProfileService` llama a
  `CommandFileTransferService`.

## G. Comportamiento de HEAD_STOP

No modificado.

Comportamiento actual documentado:

- `HEAD_STOP` esta reconocido como prioridad en
  `UsbSmokeIdf/main/head_runtime.cpp:93`.
- La ruta de cola lo manda al frente con `xQueueSendToFront`.
- En el runner, `app_head_stop()` responde `OK|HEAD_STOP|REQUESTED` antes de que
  la tarea haya terminado realmente.

Pendiente: la app debe enviar `HEAD_STOP`, aceptar `REQUESTED`, consultar
`HEAD_STATUS` periodicamente y liberar la UI solo cuando el estado ya no sea
`RUNNING` ni `STOPPING`.

## H. Consistencia RAM app versus firmware

No modificada.

Pendiente: `HeadProfileService.ApplyProgramAsync` debe descargar, parsear y
validar el TXT como candidato; enviar `HEAD_PROGRAM_SELECT`; y solo despues de
un OK del firmware actualizar el perfil activo en RAM y la seleccion visible.

Si el firmware responde `BUSY`, `ERR`, timeout o desconexion, la app debe
conservar el perfil y selector anteriores.

## I. Errores BUSY agregados

Ninguno. No se implemento firmware.

Errores pendientes esperados:

- Segundo `FILE_BEGIN` durante upload activo: `ERR FILE_BUSY` o formato
  equivalente existente.
- `FILE_END` sobre archivo activo protegido: `BUSY`.
- `FILE_DELETE` sobre archivo activo protegido: `BUSY`.
- `HEAD_PROGRAM_SELECT` durante `RUNNING` o `STOPPING`: conservar rechazo actual.

## J. Resultado completo de ambos builds

No se ejecutaron builds porque la tarea quedo bloqueada antes de implementar.

Comandos pendientes:

```text
cd "C:\Proyectos\AcuratexFastControl\app_windows\AcuratexControlApp"
dotnet build AcuratexControlApp.sln
```

```text
cd "C:\Proyectos\AcuratexFastControl\UsbSmokeIdf"
C:\Espressif\v6.0\esp-idf\export.ps1
$env:CCACHE_DISABLE='1'
$env:CMAKE_BUILD_PARALLEL_LEVEL='1'
idf.py -B build_unified_concurrency build
```

## K. Pruebas manuales pendientes con hardware

- Ejecutar accion de Perfil 1.
- Intentar seleccionar Perfil 2 durante accion.
- Intentar reemplazar Perfil 1 durante accion.
- Intentar borrar Perfil 1 durante accion.
- Intentar dos `FILE_BEGIN` consecutivos.
- Enviar `HEAD_STOP` durante WAIT largo.
- Confirmar que la UI permanece bloqueada hasta `IDLE`.
- Cambiar a Perfil 2 despues de `IDLE`.
- Volver a Perfil 1.
- Provocar timeout de `HEAD_PROGRAM_SELECT`.
- Desconectar durante cambio de perfil.

## L. Limitaciones restantes hasta implementar REQUEST_ID

- Los handlers temporales `LineReceived` siguen sin correlacion fuerte.
- Dos predicados compatibles podrian completar operaciones distintas si esperan
  simultaneamente.
- La mitigacion minima debe impedir esperas simultaneas de operaciones
  exclusivas, pero no reemplaza un dispatcher con consumo exclusivo.
- `HEAD_STATUS` y `HEAD_STOP` deben usar predicados especificos y desuscripcion
  en `finally`.
- Una solucion definitiva requiere `REQUEST_ID` en comandos y respuestas.

## M. Confirmacion de que no se toco el sistema modular

Confirmado. No se modificaron:

- `CabezalDashboardTarjetas.razor`
- `CabezalDashboardTarjetasProgramProfiles.cs`
- `CabezalDashboardTarjetasProgram1Commands.cs`
- `CabezalDashboardTarjetasProgram2Commands.cs`
- `CabezalDashboardTarjetasProgram3Commands.cs`
- `head_program_1_commands.*`
- `head_program_2_commands.*`
- `head_program_3_commands.*`
- `head_program_runtime.*`
- `program_select_1`
- `program_select_2`
- `program_select_3`

## Hallazgo bloqueante de la Parte 1

Ruta real encontrada:

- `UsbSmokeIdf/main/usb_smoketest_main.cpp:2277`:
  `app_core0_service_command()` clasifica solo `FILE_*` y WiFi como servicio
  core0.
- `UsbSmokeIdf/main/usb_smoketest_main.cpp:5369`:
  el resto se encola con `app_head_runtime_enqueue(...)`.
- `UsbSmokeIdf/main/head_runtime.cpp:161`:
  `app_head_control_task()`.
- `UsbSmokeIdf/main/head_runtime.cpp:203`:
  llama `app_command_process_line(...)`.
- `UsbSmokeIdf/main/command_processor.cpp:1319`:
  `app_command_process_line(...)`.
- `UsbSmokeIdf/main/command_processor.cpp:1343`:
  maneja `FILE_*`.
- `UsbSmokeIdf/main/command_processor.cpp:1388`:
  maneja `program_select_1`, `program_select_2` y `program_select_3`.
- `UsbSmokeIdf/main/command_processor.cpp:1648`:
  cae a `text_passthrough`.
- `UsbSmokeIdf/main/command_processor.cpp:1649`:
  llama `app_process_text_command(...)`.

Runner `HEAD_*` existente pero sin caller activo encontrado:

- `UsbSmokeIdf/main/command_head_program_runner.h:137`:
  `app_head_program_is_command(...)`.
- `UsbSmokeIdf/main/command_head_program_runner.h:174`:
  `app_head_program_process_line(...)`.
- `UsbSmokeIdf/main/command_head_program_runner.cpp:3710`:
  definicion de `app_head_program_is_command(...)`.
- `UsbSmokeIdf/main/command_head_program_runner.cpp:3754`:
  definicion de `app_head_program_process_line(...)`.

Busqueda realizada:

```text
rg -n "command_head_program_runner|app_head_program_is_command|app_head_program_process_line|HEAD_PROGRAM_INFO" UsbSmokeIdf/main
```

Resultado relevante: solo aparecen definiciones, prototipos, includes y
comentarios; no aparece una llamada real a `app_head_program_process_line(...)`.
