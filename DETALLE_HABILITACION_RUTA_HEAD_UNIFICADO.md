# DETALLE_HABILITACION_RUTA_HEAD_UNIFICADO

Fecha: 2026-07-03
Rama verificada antes de modificar: correccion-concurrencia-unificado

## A. Ruta activa anterior

Ruta real antes del cambio:

```text
usb_smoketest_main.cpp
  -> app_core0_service_command() clasifica FILE_* y WiFi como servicio core0
  -> comandos no FILE_* se encolan con app_head_runtime_enqueue()
head_runtime.cpp
  -> app_head_control_task()
  -> app_command_process_line(...)
command_processor.cpp
  -> FILE_* / WiFi / ping / help
  -> program_select_1, program_select_2, program_select_3
  -> status / CAN / init / testeo / start / stop / emergency_stop
  -> J / Yarn / Stitch / DEN / SIC / Feet
  -> send / frame CAN
  -> app_process_text_command(...)
```

El fallback `app_process_text_command(...)` responde `ERR unknown command` y no llama al runner HEAD_*.

## B. Motivo por el que HEAD_* no llegaba al runner

`command_head_program_runner.cpp` ya tenia el dispatcher real de HEAD_*:

- `app_head_program_is_command(...)`
- `app_head_program_process_line(...)`

Pero `command_processor.cpp` no incluia `command_head_program_runner.h` y no llamaba a esas funciones antes del fallback generico. Por eso `HEAD_PROGRAM_SELECT`, `HEAD_PROGRAM_INFO`, `HEAD_ACTION`, `HEAD_STATUS` y `HEAD_STOP` podian entrar por la cola activa y terminar como comando desconocido.

No se encontro una llamada indirecta real desde `app_process_text_command(...)` ni desde otro punto activo del dispatcher hacia `app_head_program_process_line(...)`.

## C. Archivo y funcion donde se agrego la derivacion

Archivo:

- `UsbSmokeIdf/main/command_processor.cpp`

Funcion:

- `app_command_process_line(...)`

Ubicacion:

- despues de las validaciones generales y de los handlers existentes, incluido FILE_*, program_select_1/2/3, INIT, TESTEO, CAN, J, Yarn, Stitch, DEN, SIC y Feet;
- justo antes del fallback `app_process_text_command(...)`.

Derivacion agregada:

```cpp
if (app_head_program_is_command(line)) {
    ESP_LOGI(TAG, "CMD CLASS [%s]: head_program_runner", app_transport_name(env));
    return app_head_program_process_line(line, reply, ctx, env);
}
```

## D. Firma real utilizada

Firmas confirmadas en `UsbSmokeIdf/main/command_head_program_runner.h`:

```cpp
bool app_head_program_is_command(const char *line);

esp_err_t app_head_program_process_line(const char *line,
                                        app_reply_fn_t reply,
                                        void *ctx,
                                        const app_command_env_t *env);
```

El callback de respuesta es `app_reply_fn_t reply`; el contexto es `void *ctx`; el entorno necesario es `const app_command_env_t *env`.

## E. Inicializacion del runner

`app_head_program_runner_init()` existia pero no tenia caller activo encontrado.

Cambio aplicado:

- `UsbSmokeIdf/main/usb_smoketest_main.cpp`
- `app_main()` ahora llama una sola vez a `app_head_program_runner_init()` antes de aceptar comandos.

Esta llamada reemplaza la llamada directa previa a `app_head_state_manager_init()`. El runner init ya inicializa el estado rapido del cabezal mediante `app_head_state_manager_init()`, ademas de preparar el mutex/estado interno del runner HEAD_*.

No se inicializa desde cada comando. No se agregaron tareas duplicadas. No se cambiaron prioridades ni afinidad de tareas.

## F. Confirmacion de que existe una unica ruta

Busqueda ejecutada:

```text
rg -n "app_head_program_process_line|app_head_program_is_command" UsbSmokeIdf/main
```

Resultado relevante:

```text
UsbSmokeIdf/main\command_head_program_runner.cpp:3710:bool app_head_program_is_command(const char *line)
UsbSmokeIdf/main\command_head_program_runner.cpp:3754:esp_err_t app_head_program_process_line(const char *incoming_line,
UsbSmokeIdf/main\command_head_program_runner.h:137:bool app_head_program_is_command(const char *line);
UsbSmokeIdf/main\command_head_program_runner.h:174:esp_err_t app_head_program_process_line(const char *line,
UsbSmokeIdf/main\command_processor.cpp:1649:    if (app_head_program_is_command(line)) {
UsbSmokeIdf/main\command_processor.cpp:1651:        return app_head_program_process_line(line, reply, ctx, env);
```

Hay una sola llamada activa desde el dispatcher: `command_processor.cpp`. El resto son declaraciones, definiciones o comentarios existentes.

`command_head_program_runner.cpp` ya estaba incluido en `UsbSmokeIdf/main/CMakeLists.txt`.

`HEAD_STOP` ya tenia prioridad en `head_runtime.cpp`: `app_head_runtime_is_priority_stop()` reconoce `HEAD_STOP` y `app_head_runtime_enqueue()` usa `xQueueSendToFront(...)` para esos comandos. No se duplico esa prioridad en `command_processor.cpp`.

## G. Resultado completo del build

Build limpio ejecutado:

```text
cd "C:\Proyectos\AcuratexFastControl\UsbSmokeIdf"
C:\Espressif\v6.0\esp-idf\export.ps1
$env:CCACHE_DISABLE='1'
$env:CMAKE_BUILD_PARALLEL_LEVEL='1'
idf.py -B build_head_unified_routing build
```

Resultado: exit code 0. Se genero `UsbSmokeIdf.bin` en `UsbSmokeIdf/build_head_unified_routing` y el comando termino con `Project build complete`.

Salida completa de la verificacion incremental inmediata sobre el mismo build:

```text
Done! You can now compile ESP-IDF projects.
Go to the project directory and run:

  idf.py build

Executing action: all (aliases: build)
Running ninja in directory C:\Proyectos\AcuratexFastControl\UsbSmokeIdf\build_head_unified_routing
Executing "ninja all"...
[1/4] C:\windows\system32\cmd.exe /C "cd /D C:\Proyectos\AcuratexFastControl\UsbSmokeIdf\build_head_unified_routing && C:\Users\Manuel22\.espressif\python_env\idf6.0_py3.14_env\Scripts\python.exe C:/Espressif/v6.0/esp-idf/components/partition_table/check_sizes.py --offset 0x8000 partition --type app C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/build_head_unified_routing/partition_table/partition-table.bin C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/build_head_unified_routing/UsbSmokeIdf.bin"
UsbSmokeIdf.bin binary size 0xe6c20 bytes. Smallest app partition is 0x100000 bytes. 0x193e0 bytes (10%) free.
[2/4] Performing build step for 'bootloader'
[1/1] C:\windows\system32\cmd.exe /C "cd /D C:\Proyectos\AcuratexFastControl\UsbSmokeIdf\build_head_unified_routing\bootloader && C:\Users\Manuel22\.espressif\python_env\idf6.0_py3.14_env\Scripts\python.exe C:/Espressif/v6.0/esp-idf/components/partition_table/check_sizes.py --offset 0x8000 bootloader 0x0 C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/build_head_unified_routing/bootloader/bootloader.bin"
Bootloader binary size 0x5240 bytes. 0x2dc0 bytes (36%) free.
[3/4] No install step for 'bootloader'
[4/4] Completed 'bootloader'

Project build complete. To flash, run:
 idf.py flash
or
 idf.py -p PORT flash
or
 python -m esptool --chip esp32s3 -b 460800 --before default-reset --after hard-reset write-flash --flash-mode dio --flash-size 2MB --flash-freq 80m 0x0 build_head_unified_routing\bootloader\bootloader.bin 0x8000 build_head_unified_routing\partition_table\partition-table.bin 0x10000 build_head_unified_routing\UsbSmokeIdf.bin
or from the "C:\Proyectos\AcuratexFastControl\UsbSmokeIdf\build_head_unified_routing" directory
 python -m esptool --chip esp32s3 -b 460800 --before default-reset --after hard-reset write-flash "@flash_args"
Activating ESP-IDF 6.0
Setting IDF_PATH to 'C:\Espressif\v6.0\esp-idf'.
* Checking python version ... 3.14.4
* Checking python dependencies ... OK
* Deactivating the current ESP-IDF environment (if any) ... OK
* Establishing a new ESP-IDF environment ... OK
* Identifying shell ... powershell.exe
* Detecting outdated tools in system ... OK - no outdated tools found
```

## H. Archivos modificados

- `UsbSmokeIdf/main/command_processor.cpp`
- `UsbSmokeIdf/main/usb_smoketest_main.cpp`
- `DETALLE_HABILITACION_RUTA_HEAD_UNIFICADO.md`

## I. Pruebas manuales pendientes

- `HEAD_STATUS`
- `HEAD_PROGRAM_INFO`
- `HEAD_PROGRAM_SELECT` con Perfil 1
- `HEAD_ACTION`
- `HEAD_STOP`
- seleccion de Perfil 2

## J. Confirmacion de que el modular quedo intacto

No se modifico ningun archivo modular.

Confirmacion por `git diff --name-only` antes de crear este informe:

```text
UsbSmokeIdf/main/command_processor.cpp
UsbSmokeIdf/main/usb_smoketest_main.cpp
```

No se tocaron:

- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs`
- archivos `head_program_1_commands.*`, `head_program_2_commands.*`, `head_program_3_commands.*`
- sistema modular de Programa 1, Programa 2 o Programa 3

## Verificaciones finales realizadas antes del informe

```text
git diff --check
```

Resultado: exit code 0. Git informo solo el aviso de conversion futura LF -> CRLF en `UsbSmokeIdf/main/usb_smoketest_main.cpp`.

```text
git diff --stat
```

Resultado antes de crear este informe:

```text
 UsbSmokeIdf/main/command_processor.cpp  | 6 ++++++
 UsbSmokeIdf/main/usb_smoketest_main.cpp | 2 +-
 2 files changed, 7 insertions(+), 1 deletion(-)
```
