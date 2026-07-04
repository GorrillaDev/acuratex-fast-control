# PLAN_CORRECCION_CONCURRENCIA_UNIFICADO

Documento generado a partir de la auditoria de concurrencia real del sistema unificado basado en TXT y comandos `HEAD_*`.

Alcance respetado:
- App: `Components/CabezalDashboardUnificado.razor`, `Services/HeadProfileService.cs`, `Services/AppScriptExecutionService.cs`, `Services/CommandFileTransferService.cs`, `Services/CabezalDashboardUnificadoCommandService.cs`, `UnifiedSystemForm.cs`.
- Firmware: `UsbSmokeIdf/main/file_transfer.cpp`, `UsbSmokeIdf/main/command_head_program_runner.cpp`, `UsbSmokeIdf/main/usb_smoketest_main.cpp`, `UsbSmokeIdf/main/command_processor.cpp`.

No se analizan ni se proponen cambios sobre `CabezalDashboardTarjetas.razor` ni perfiles modulares.

## A. Protecciones que ya existen

1. `HEAD_ACTION` en la app queda serializado por `AppScriptExecutionService._executionGate`.
   - Archivo: `app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs`
   - Funcion: `ExecuteHeadActionAsync`
   - Lineas: 12, 170-175
   - Clasificacion: ALTO mitigado

2. El runner firmware impide dos `HEAD_ACTION` TXT simultaneos con `s_state_mutex` y `app_head_is_busy_state()`.
   - Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
   - Funciones: `app_head_state_lock`, `app_head_state_unlock`, `app_head_is_busy_state`, `app_head_run_action`
   - Lineas: 117, 456-500, 728-730, 3462-3468
   - Clasificacion: ALTO mitigado

3. `HEAD_PROGRAM_SELECT` en el runner firmware rechaza seleccion si el runner esta `RUNNING` o `STOPPING`.
   - Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
   - Funcion: `app_head_select_program`
   - Lineas: 2929-2935
   - Clasificacion: MEDIO mitigado en firmware

4. `CommandFileTransferService` serializa operaciones de archivo y `HEAD_PROGRAM_SELECT` dentro de una misma instancia.
   - Archivo: `app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs`
   - Funciones: `UploadTextFileAsync`, `ListFilesAsync`, `DownloadFileAsync`, `SelectFileAsync`, `SelectHeadProgramAsync`, `DeleteFileAsync`
   - Lineas: 31-33, 149-159, 197-203, 301-302, 481-491, 531-541, 625-635
   - Clasificacion: ALTO mitigado dentro de la instancia app

5. `HeadProfileService` protege el diccionario static de perfiles activos.
   - Archivo: `app_windows/AcuratexControlApp/Services/HeadProfileService.cs`
   - Funciones: `ApplyProgramAsync`, `GetActiveProfile`, `HasActiveProfile`, `ClearActiveProfile`
   - Lineas: 23-24, 491-492, 542-543, 581-582, 653-654
   - Clasificacion: MEDIO mitigado para memoria de app

6. La UI bloquea cambios de programa solo mientras hay carga/listado de programa.
   - Archivo: `app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor`
   - Funciones: `CanChangeHeadProgram`, `RefreshHeadProgramsAsync`, `OnHeadProgramChangedAsync`
   - Lineas: 242, 263-267, 293-324, 328-372
   - Clasificacion: MEDIO parcialmente mitigado

7. El firmware protege la salida de respuestas por USB/UART con mutexes.
   - Archivo: `UsbSmokeIdf/main/usb_smoketest_main.cpp`
   - Funciones: `app_reply_mutexes_init`, `app_reply_usb`, `app_reply_uart`
   - Lineas: 221, 245, 1053-1125, 1421, 1553, 1833, 1937
   - Clasificacion: BAJO mitigado

## B. Carreras confirmadas

### 1. Que impide dos `HEAD_ACTION` simultaneos

La app serializa acciones con `_executionGate` y el firmware rechaza otra accion si `s_runner_state` esta ocupado.

- Archivo app: `app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs`
- Funcion: `ExecuteHeadActionAsync`
- Lineas: 12, 170-175
- Archivo firmware: `UsbSmokeIdf/main/command_head_program_runner.cpp`
- Funcion: `app_head_run_action`
- Lineas: 3462-3468
- Clasificacion: ALTO mitigado

Debilidad: acciones directas J `RUN/STOP` retornan por `app_head_handle_direct_j_action()` antes de entrar al estado `RUNNING`, por lo que no pasan por el mismo bloqueo largo de tarea TXT.

- Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
- Funcion: `app_head_run_action`
- Lineas: 3441-3459
- Clasificacion: ALTO posible

### 2. Si `HEAD_PROGRAM_SELECT` puede ejecutarse mientras `HEAD_ACTION` sigue activo

En firmware, no deberia ejecutarse: se rechaza con `ERR|HEAD_PROGRAM_SELECT|BUSY|...`.

- Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
- Funcion: `app_head_select_program`
- Lineas: 2929-2935
- Clasificacion: MEDIO mitigado

En UI, si puede intentarse porque `CanChangeHeadProgram` no mira acciones activas; solo conexion, permiso y `_isHeadProgramBusy`.

- Archivo: `app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor`
- Funcion: `CanChangeHeadProgram`
- Lineas: 263-267
- Clasificacion: MEDIO

### 3. Si `FILE_BEGIN/FILE_END` puede reemplazar el archivo usado por una accion activa

Si. `FILE_END` reemplaza el archivo final con `remove(final_path)` y `rename(APP_UPLOAD_TMP, final_path)` sin consultar si ese archivo es `s_active_program` ni si el runner esta activo. Al mismo tiempo, la tarea `HEAD_ACTION` lee el TXT desde LittleFS.

- Archivo: `UsbSmokeIdf/main/file_transfer.cpp`
- Funcion: `app_file_cmd_end`
- Lineas: 1059-1064
- Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
- Funcion: `app_head_find_and_execute_action`
- Lineas: 1836-1840, 1851, 1886-1905
- Clasificacion: CRITICO

### 4. Si dos subidas pueden compartir y corromper `/fs/.upload.tmp`

Si, si llegan desde mas de un origen o fuera de una sola instancia de `CommandFileTransferService`. El firmware tiene un unico temporal `APP_UPLOAD_TMP` y un unico estado global `s_upload`, sin mutex propio. Un `FILE_BEGIN` nuevo hace reset de la subida anterior, borra el temporal y abre el mismo path.

- Archivo: `UsbSmokeIdf/main/file_transfer.cpp`
- Funciones: `app_file_cmd_begin`, `app_file_cmd_data`, `app_file_transfer_process_line`
- Lineas: 20, 70, 854-855, 886-890, 950-1005, 1586-1588
- Clasificacion: CRITICO

La app reduce el riesgo dentro de una instancia con `_operationGate`.

- Archivo: `app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs`
- Funcion: `UploadTextFileAsync`
- Lineas: 31-33, 149-159
- Clasificacion: ALTO mitigado localmente

### 5. Si `FILE_DELETE` puede borrar el programa activo durante una accion

Si. `app_file_cmd_delete()` borra el archivo con `remove(path)` y no consulta `s_active_program`, `s_runner_state` ni `s_runner_task_handle`. Solo limpia `.selected` y una descarga activa si aplica.

- Archivo: `UsbSmokeIdf/main/file_transfer.cpp`
- Funcion: `app_file_cmd_delete`
- Lineas: 1250-1279
- Clasificacion: CRITICO

### 6. Si `HEAD_STOP` esta sincronizado con la tarea del runner

Parcialmente. `HEAD_STOP` toma el mutex, marca `s_stop_requested=true`, cambia estado a `STOPPING` y responde inmediatamente. La tarea coopera revisando `s_stop_requested` antes de ejecutar lineas y durante `WAIT/DELAY`. No hay join ni espera bloqueante hasta que la tarea termine.

- Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
- Funciones: `app_head_stop`, `app_head_wait_cancelable`, `app_head_execute_allowed_line`, `app_head_cleanup_task`
- Lineas: 3352-3367, 1458-1467, 1533-1536, 2061-2088
- Clasificacion: MEDIO

### 7. Variables globales compartidas en `command_head_program_runner.cpp`

Variables compartidas entre dispatcher, tarea `head_action_task`, `HEAD_STATUS` y `HEAD_STOP`:

- `s_active_program`
- `s_current_action`
- `s_current_line`
- `s_action_start_ms`
- `s_last_error`
- `s_last_stage`
- `s_stop_requested`
- `s_runner_state`
- `s_module_counts`
- `s_runner_task_handle`
- `s_state_mutex`

Evidencia:

- Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
- Funcion/ambito: variables globales del modulo
- Lineas: 99-117
- Clasificacion: ALTO

Debilidad confirmada: `s_current_line` se escribe desde la tarea sin tomar `s_state_mutex`.

- Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
- Funcion: `app_head_find_and_execute_action`
- Linea: 1886
- Clasificacion: MEDIO

### 8. Mutex, `SemaphoreSlim`, locks, flags busy y secciones criticas existentes

App:

- `_executionGate`
  - Archivo: `app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs`
  - Lineas: 12, 170-175, 229-280
- `_operationGate`
  - Archivo: `app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs`
  - Lineas: 31-33
- `ActiveProfilesGate`
  - Archivo: `app_windows/AcuratexControlApp/Services/HeadProfileService.cs`
  - Lineas: 23-24
- `_isHeadProgramBusy`
  - Archivo: `app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor`
  - Lineas: 242, 263-267, 293-324, 328-372

Firmware:

- `s_state_mutex`
  - Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
  - Lineas: 117, 456-500, 3657-3660
- `s_stop_requested`
  - Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
  - Lineas: 111, 3352-3360, 1458-1467, 1533-1536
- `s_usb_reply_mutex`, `s_uart_reply_mutex`
  - Archivo: `UsbSmokeIdf/main/usb_smoketest_main.cpp`
  - Lineas: 221, 245, 1421, 1553, 1833, 1937

Clasificacion general: ALTO para gates de ejecucion; BAJO para mutexes de salida.

### 9. Si `CommandFileTransferService` serializa `Upload`, `Download`, `List`, `Select`, `Delete` y `SaveEditedText`

Si, para operaciones remotas dentro de la misma instancia. `SaveEditedTextAsync` no toma el gate directamente, pero delega a `UploadTextFileAsync`, que si lo toma. La escritura de cache local posterior queda fuera del gate.

- Archivo: `app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs`
- Funciones: `UploadTextFileAsync`, `ListFilesAsync`, `DownloadFileAsync`, `SaveEditedTextAsync`, `SelectFileAsync`, `DeleteFileAsync`
- Lineas: 149-159, 197-203, 301-302, 418-425, 481-491, 625-635
- Clasificacion: BAJO/MEDIO

### 10. Si varios handlers `LineReceived` pueden aceptar la misma respuesta

Si. Las esperas se implementan con handlers temporales sobre el mismo evento `LineReceived`; no existe consumo exclusivo de respuesta. Si dos predicados coinciden, ambos pueden completar su `TaskCompletionSource`.

- Archivo: `app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs`
- Funcion: `SendFirmwareHeadActionAsync`
- Lineas: 288-315, 323-335
- Archivo: `app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs`
- Funciones: `SendFileCommandAsync`, `SendHeadProgramCommandAsync`
- Lineas: 924-949, 960-968, 1012-1031, 1039-1048
- Clasificacion: ALTO

### 11. Si las solicitudes tienen ID de correlacion o solo prefijos

No hay ID de correlacion. Las respuestas se distinguen por texto exacto o prefijos: `OK|HEAD_ACTION|accion`, `ERR|HEAD_ACTION`, `ACK FILE_DATA index`, `OK|HEAD_PROGRAM_SELECT|archivo`.

- Archivo: `app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs`
- Funcion: `SendFirmwareHeadActionAsync`
- Lineas: 297-306
- Archivo: `app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs`
- Funciones: `SendFileCommandAsync`, `SendHeadProgramCommandAsync`, `UploadTextFileWithChunkSizeAsync`
- Lineas: 934-944, 1021-1027, 1117-1178
- Clasificacion: ALTO

### 12. Si `HeadProfileService` protege su diccionario static y el perfil activo

Protege el diccionario static con `lock`, pero no coordina el perfil activo con una accion en ejecucion ni con cambios FILE en firmware.

- Archivo: `app_windows/AcuratexControlApp/Services/HeadProfileService.cs`
- Funciones: `ApplyProgramAsync`, `GetActiveProfile`, `HasActiveProfile`, `ClearActiveProfile`
- Lineas: 491-492, 542-543, 581-582, 653-654
- Clasificacion: MEDIO

### 13. Si `CabezalDashboardUnificadoCommandService` impide comandos simultaneos

No tiene gate propio. Las acciones que pasan por `_scripts.ExecuteActionAsync()` quedan serializadas en `AppScriptExecutionService`, pero `HEAD_STATUS`, `HEAD_STOP` y comandos directos salen por `_connection.SendLineAsync()` sin gate de este servicio.

- Archivo: `app_windows/AcuratexControlApp/Services/CabezalDashboardUnificadoCommandService.cs`
- Funciones: `SendDoCommandAsync`, `ExecuteProfileActionAsync`, `SendLineAsync`
- Lineas: 120-152, 430-438, 566-573
- Clasificacion: ALTO

### 14. Si la UI puede cambiar de perfil mientras `ExecuteActionAsync` sigue esperando

Si puede intentarlo. `SendTrackedAsync` no marca una accion como ocupada y `CanChangeHeadProgram` no consulta `_executionGate` ni estado de accion. El firmware deberia rechazar el cambio si llega al runner mientras esta ocupado.

- Archivo: `app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor`
- Funciones: `CanChangeHeadProgram`, `SendTrackedAsync`
- Lineas: 263-267, 786-798
- Archivo firmware: `UsbSmokeIdf/main/command_head_program_runner.cpp`
- Funcion: `app_head_select_program`
- Lineas: 2929-2935
- Clasificacion: MEDIO

### 15. Si el perfil en RAM de Windows puede quedar diferente al perfil del firmware

Si. La UI puede limpiar el perfil RAM sin mandar comando al firmware cuando el selector queda vacio. Tambien puede haber cambios externos por `FILE_DELETE`, `FILE_END` o comandos desde otro transporte.

- Archivo: `app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor`
- Funcion: `OnHeadProgramChangedAsync`
- Lineas: 335-340
- Archivo: `app_windows/AcuratexControlApp/Services/HeadProfileService.cs`
- Funcion: `ApplyProgramAsync`
- Lineas: 470-492
- Archivo: `UsbSmokeIdf/main/file_transfer.cpp`
- Funciones: `app_file_cmd_end`, `app_file_cmd_delete`
- Lineas: 1059-1064, 1250-1279
- Clasificacion: MEDIO/ALTO

## C. Riesgos posibles pero no demostrados

1. Semantica exacta de LittleFS al borrar o renombrar un archivo ya abierto por `fopen("rb")`.
   - Condicion confirmada: el codigo permite que ocurra.
   - Resultado exacto: depende de LittleFS/stdio.
   - Clasificacion: CRITICO posible

2. Multiples clientes o transportes.
   - `file_transfer.cpp` no tiene mutex propio y `command_processor.cpp` declara que puede ejecutarse desde mas de una tarea/transporte.
   - Archivo: `UsbSmokeIdf/main/command_processor.cpp`
   - Funcion: `app_command_process_line`
   - Lineas: 1304-1306
   - Clasificacion: ALTO posible

3. Enrutamiento `HEAD_*` desde `command_processor.cpp`.
   - En el archivo revisado no aparece llamada visible a `app_head_program_process_line()`.
   - El fallback responde `ERR unknown command`.
   - Archivo: `UsbSmokeIdf/main/command_processor.cpp`
   - Funciones: `app_process_text_command`, `app_command_process_line`
   - Lineas: 486-489, 1648-1649
   - Clasificacion: ALTO posible si esa es la ruta compilada activa

4. `HEAD_PROGRAM_INFO` lee `s_active_program` y `s_module_counts` sin tomar mutex.
   - Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
   - Funcion: `app_head_program_info`
   - Lineas: 3154-3181
   - Clasificacion: MEDIO posible

5. `s_last_stage` se escribe mediante `app_head_set_stage()` sin lock obligatorio.
   - Archivo: `UsbSmokeIdf/main/command_head_program_runner.cpp`
   - Funcion: `app_head_set_stage`
   - Lineas: 535-542
   - Clasificacion: BAJO/MEDIO

## D. Correccion minima antes de una presentacion

1. App: introducir un gate unico de operacion de cabezal unificado que cubra:
   - `HEAD_ACTION`
   - `HEAD_PROGRAM_SELECT`
   - `UploadTextFileAsync`
   - `SaveEditedTextAsync`
   - `DeleteFileAsync`
   - `SelectFileAsync`

2. UI: agregar flag visible de accion en curso.
   - Deshabilitar selector de programa, subida, guardado y borrado mientras una accion espera respuesta.
   - No depender solo de `_isHeadProgramBusy`.

3. Firmware: bloquear cambios destructivos sobre el programa activo.
   - `FILE_BEGIN|active_program|...`: rechazar si runner `RUNNING/STOPPING`.
   - `FILE_END|active_program`: rechazar o posponer si runner `RUNNING/STOPPING`.
   - `FILE_DELETE|active_program`: rechazar si runner `RUNNING/STOPPING`.

4. `HEAD_STOP`: despues de `OK|HEAD_STOP|REQUESTED`, la app debe esperar `HEAD_STATUS` hasta `IDLE`, `DONE` o `ERROR` antes de permitir perfil/archivo.

5. App: si `HEAD_PROGRAM_SELECT` devuelve `BUSY`, no cambiar `_selectedHeadProgramFile` ni refrescar como si hubiera cambiado el activo.

## E. Correccion definitiva posterior

1. Agregar `REQUEST_ID` a comandos y respuestas.
   - Ejemplo: `HEAD_ACTION|REQ=123|J1.CH1`.
   - Respuesta: `OK|HEAD_ACTION|REQ=123|J1.CH1`.

2. Usar temporales unicos por sesion o request.
   - Ejemplo: `/fs/.upload.<session>.<request>.tmp`.
   - Evita que dos subidas compartan `/fs/.upload.tmp`.

3. Agregar lock firmware comun para LittleFS y runner de programa.
   - O alternativa: contador/registro de archivo en uso.
   - `HEAD_ACTION` marca `s_active_program` como en uso hasta cleanup.
   - `FILE_DELETE`/`FILE_END` consultan ese estado.

4. Agregar readback formal del programa activo.
   - `HEAD_STATUS` o `HEAD_PROGRAM_INFO` debe exponer archivo activo, estado y hash/version del TXT.
   - La app no debe considerar valido su perfil RAM si no coincide con firmware.

5. Unificar cola de comandos en app.
   - Un solo despachador request/response para `HEAD_*` y `FILE_*`.
   - Respuestas consumidas de forma exclusiva, no por handlers temporales paralelos.

6. Firmware: revisar enrutamiento real de `HEAD_*`.
   - Si `command_processor.cpp` es el dispatcher activo, debe llamar explicitamente a `app_head_program_process_line()` para `HEAD_*`.
   - Si otro dispatcher lo hace, documentarlo y dejar una sola ruta.

## F. Archivos exactos que habria que modificar

App:

- `app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor`
  - Bloquear selector y acciones de archivo mientras `HEAD_ACTION` esta en curso.
  - Mostrar estado `BUSY/STOPPING`.

- `app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs`
  - Exponer estado busy o compartir gate comun.
  - Esperar final real si el protocolo lo soporta.

- `app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs`
  - Integrar gate comun con acciones de cabezal.
  - Agregar correlacion si cambia protocolo.

- `app_windows/AcuratexControlApp/Services/CabezalDashboardUnificadoCommandService.cs`
  - Serializar `HEAD_STOP`, `HEAD_STATUS` y comandos directos con politica clara.

- `app_windows/AcuratexControlApp/Services/HeadProfileService.cs`
  - No cambiar RAM si firmware esta busy.
  - Validar readback de programa activo.

- `app_windows/AcuratexControlApp/UnifiedSystemForm.cs`
  - Registrar el nuevo coordinador/gate compartido por DI.

Firmware:

- `UsbSmokeIdf/main/file_transfer.cpp`
  - Bloquear `FILE_BEGIN`, `FILE_END`, `FILE_DELETE` sobre programa activo en uso.
  - Agregar mutex o temporal unico.

- `UsbSmokeIdf/main/command_head_program_runner.cpp`
  - Exponer estado seguro de programa activo/en uso.
  - Proteger lecturas no bloqueadas como `HEAD_PROGRAM_INFO`.
  - Sincronizar mejor `HEAD_STOP` si se requiere confirmacion fuerte.

- `UsbSmokeIdf/main/usb_smoketest_main.cpp`
  - Revisar separacion `FILE_*` core0 versus runtime si se agrega lock comun.

- `UsbSmokeIdf/main/command_processor.cpp`
  - Enrutar `HEAD_*` de forma explicita si esta ruta es la activa.
  - Mantener coherencia entre `FILE_*` y runner.

## G. Pruebas controladas con dos perfiles TXT

### Preparacion

Crear dos archivos:

- `cbz.uni.prog1.txt`
  - Accion `BEGIN|J1.CH1` con `WAIT 5000` y una trama CAN identificable.
- `cbz.uni.prog2.txt`
  - Misma accion `BEGIN|J1.CH1`, pero con trama CAN distinta.

### Prueba 1: `HEAD_PROGRAM_SELECT` durante accion

1. Subir ambos TXT.
2. Seleccionar `cbz.uni.prog1.txt`.
3. Ejecutar `HEAD_ACTION|J1.CH1`.
4. Antes de terminar el `WAIT`, intentar `HEAD_PROGRAM_SELECT|cbz.uni.prog2.txt`.
5. Resultado esperado actual: firmware responde `ERR|HEAD_PROGRAM_SELECT|BUSY|...`.
6. Resultado requerido app: UI no debe cambiar RAM ni selector a prog2.

### Prueba 2: reemplazo del archivo activo durante accion

1. Seleccionar `cbz.uni.prog1.txt`.
2. Ejecutar accion larga.
3. Durante ejecucion, subir otra version con el mismo nombre `cbz.uni.prog1.txt`.
4. Resultado actual posible: `FILE_END` reemplaza path activo.
5. Resultado requerido: firmware debe rechazar o posponer reemplazo mientras runner esta `RUNNING/STOPPING`.

### Prueba 3: borrado del archivo activo durante accion

1. Seleccionar `cbz.uni.prog1.txt`.
2. Ejecutar accion larga.
3. Durante ejecucion, mandar `FILE_DELETE|cbz.uni.prog1.txt`.
4. Resultado actual posible: archivo borrado.
5. Resultado requerido: `ERR FILE_BUSY` o equivalente.

### Prueba 4: dos subidas simultaneas

1. Abrir dos clientes o dos rutas de transporte.
2. Iniciar `FILE_BEGIN` de archivo A.
3. Antes de `FILE_END`, iniciar `FILE_BEGIN` de archivo B.
4. Resultado actual posible: se resetea `s_upload` y ambos usan `/fs/.upload.tmp`.
5. Resultado requerido: segundo upload rechazado o temporal unico por request.

### Prueba 5: `HEAD_STOP` y reactivacion de UI

1. Ejecutar accion con `WAIT 10000`.
2. Enviar `HEAD_STOP`.
3. Confirmar que app recibe `OK|HEAD_STOP|REQUESTED`.
4. Consultar `HEAD_STATUS` hasta estado final no ocupado.
5. Resultado requerido: UI solo re-habilita selector/archivos cuando el runner ya no esta `RUNNING/STOPPING`.

### Prueba 6: desincronizacion RAM Windows versus firmware

1. Seleccionar `cbz.uni.prog1.txt` desde UI.
2. Limpiar seleccion en UI.
3. Consultar `HEAD_PROGRAM_INFO` o `HEAD_STATUS`.
4. Resultado actual posible: RAM app sin perfil, firmware aun con programa.
5. Resultado requerido: UI debe mandar comando explicito de deseleccion o mantener estado hasta confirmar readback.

## Resumen ejecutivo

Hallazgos criticos:

- `FILE_END` puede reemplazar el TXT activo mientras una accion lo usa.
- `FILE_DELETE` puede borrar el TXT activo durante una accion.
- Dos subidas externas pueden compartir `/fs/.upload.tmp`.

Hallazgos altos:

- Respuestas sin ID de correlacion.
- Handlers temporales `LineReceived` no consumen respuestas de forma exclusiva.
- `CabezalDashboardUnificadoCommandService` no tiene gate propio para comandos simultaneos.

Hallazgos medios:

- UI puede intentar cambio de perfil durante accion.
- `HEAD_STOP` es cooperativo y no espera fin real.
- RAM Windows puede diferir del estado firmware.

Prioridad minima antes de presentacion:

1. Bloquear UI y app con gate unico.
2. Rechazar en firmware `FILE_END`/`FILE_DELETE` sobre programa activo ocupado.
3. Esperar estado final tras `HEAD_STOP`.
