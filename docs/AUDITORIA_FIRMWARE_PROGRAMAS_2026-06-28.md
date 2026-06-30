# Auditoria tecnica del firmware ESP32-S3

Fecha: 2026-06-28

Alcance: solo inspeccion, trazabilidad y diagnostico. No se modificaron fuentes del firmware ni de la app durante esta auditoria.

## Clasificacion usada

- `DECLARADO`: el simbolo existe en codigo.
- `IMPLEMENTADO`: la logica ejecuta trabajo real.
- `COMPILADO`: el archivo entra al build.
- `CONECTADO`: existe ruta real desde USB/app hasta handler/profil/driver.
- `PROBADO FISICAMENTE`: no realizado en esta auditoria.

## A. RESUMEN EJECUTIVO

- El firmware activo es `UsbSmokeIdf` y compila en ESP-IDF 6.0.
- El selector real de programa es `program_select_1` / `program_select_2`; `program_status` responde `PROGRAM_STATE|ACTIVE=...`.
- El tipo real de programa es `app_head_program_id_t`; `HeadProgramId` no existe.
- `s_active_program` vive en RAM estatica y arranca en Program 1 por inicializacion estatica.
- `kProgram1Commands` y `kProgram2Commands` son objetos separados, no alias.
- Program 2 contiene Feet real; Program 1 deja Feet vacio.
- INIT y TESTEO son tareas propias y capturan el perfil una sola vez al arrancar.
- RUN usa el perfil activo en cada tick, no un snapshot previo.
- `program_select_*` se bloquea si hay motion activo o fast diag ocupado.
- TESTEO no mira motion activo; puede solaparse con RUN.
- INIT no bloquea por motion activo al entrar; la tarea detiene motion despues de arrancar.
- CAN trabaja por TWAI real con GPIO4/5/6 y STBY bajo.
- El bitrate real es 1 Mbps, no 500 kbps.
- `command_head_program_runner.cpp` sigue compilado, pero no es la ruta activa de `program_select_1/2`.
- No hubo prueba fisica, solo build de verificacion.

## B. REPOSITORIO Y RAMA

- Rama real: `desarrollo-rapido`.
- Hubo cambios locales preexistentes antes de la auditoria.
- Archivos modificados preexistentes:
  - `UsbSmokeIdf/main/CMakeLists.txt`
  - `UsbSmokeIdf/main/command_processor.cpp`
  - `UsbSmokeIdf/main/head_fast_diag.cpp`
  - `UsbSmokeIdf/main/head_state_manager.cpp`
  - `UsbSmokeIdf/main/head_state_manager.h`
  - `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`
  - `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasModels.cs`
  - `app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs`
  - `app_windows/AcuratexControlApp/wwwroot/css/cabezal-dashboard-tarjetas.css`
- Archivos nuevos preexistentes:
  - `UsbSmokeIdf/main/head_command_profile.h`
  - `UsbSmokeIdf/main/head_program_1_commands.cpp`
  - `UsbSmokeIdf/main/head_program_1_commands.h`
  - `UsbSmokeIdf/main/head_program_2_commands.cpp`
  - `UsbSmokeIdf/main/head_program_2_commands.h`
  - `UsbSmokeIdf/main/head_program_runtime.cpp`
  - `UsbSmokeIdf/main/head_program_runtime.h`
  - `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs`
  - `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs`
  - `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs`
  - `build_diag.txt`
  - `docs/MAPA_PROGRAMAS_MODULAR.md`
  - `tmp_gen/`
- No hubo archivos eliminados.
- No se cambio de rama.

FIRMWARE ACTIVO CONFIRMADO:

- `UsbSmokeIdf`

FIRMWARE ANTIGUO O DE REFERENCIA:

- `referencia_antigua/arduino_antiguo`
- `UsbSmokeIdf/main/command_head_program_runner.cpp` como legado compilado

EVIDENCIAS:

- Proyecto activo: `UsbSmokeIdf/CMakeLists.txt:3-6`
- Fuentes compiladas: `UsbSmokeIdf/main/CMakeLists.txt:1-40`
- Target ESP32-S3 y USB: `UsbSmokeIdf/sdkconfig.defaults:1-24`
- Arranque real: `UsbSmokeIdf/main/usb_smoketest_main.cpp:6493-6773`
- INIT/INIT2 antiguo: `referencia_antigua/arduino_antiguo/firmware_arduino_antiguo.ino:137-176`

## C. BUILD ACTUAL

- Comando ejecutado:

```powershell
cd "C:\Proyectos\AcuratexFastControl\UsbSmokeIdf"
C:\Espressif\v6.0\esp-idf\export.ps1
$env:CMAKE_BUILD_PARALLEL_LEVEL='1'
idf.py -B build_auditoria_firmware build
```

- Resultado:
  - ESP-IDF 6.0 cargado.
  - Build completado con exito.
  - Se genero `UsbSmokeIdf.bin`.
  - No hubo errores de compilacion.
  - No se observaron warnings de compilador relevantes en el log visible.
- Notas:
  - El build se hizo en un directorio separado.
  - No se toco el firmware fuente.
  - No hubo prueba fisica.

## D. FLUJO REAL DE COMANDOS

```text
Aplicacion
-> FastDashboardCommandService / CabezalDashboardTarjetas.razor
-> USB RX task
-> app_command_ingress_queue
-> command_dispatch task
-> head_runtime queue
-> app_command_process_line
-> handler especifico
-> perfil activo / estado
-> app_can_send_standard
-> CAN TX queue
-> twai_transmit
```

Puntos de entrada y salida:

- La app modular envia `program_select_1` / `program_select_2` desde `CabezalDashboardTarjetas.razor:3185-3229`.
- `FastDashboardCommandService` reenvia comandos sin reinterpretarlos.
- `app_usb_rx_task` solo lee USB, limpia la linea y la encola.
- `app_command_dispatch_task` decide entre servicio Core 0 y cola del cabezal.
- `app_head_runtime_enqueue` prioriza `stop` / `emergency_stop`.
- `app_command_process_line` es el parser real.
- `app_can_send_standard` encola CAN y `twai_transmit` ejecuta el envio.

## E. SELECTOR DE PROGRAMA

| REQUISITO | EXISTE | FUNCIONA | EVIDENCIA |
|---|---:|---:|---|
| `HeadProgramId` | NO | NO | `UsbSmokeIdf/main/head_command_profile.h:9-12` |
| `active_program` | SI, como `s_active_program` | SI | `UsbSmokeIdf/main/head_program_runtime.cpp:6-16` |
| `program_select_1` | SI | SI | `UsbSmokeIdf/main/command_processor.cpp:1136-1167` |
| `program_select_2` | SI | SI | `UsbSmokeIdf/main/command_processor.cpp:1136-1167` |
| `program_status` | SI | SI | `UsbSmokeIdf/main/command_processor.cpp:1381-1384` |
| `PROGRAM_STATE` | SI | SI | `UsbSmokeIdf/main/command_processor.cpp:1381-1384` |
| bloqueo durante RUN | SI | SI | `UsbSmokeIdf/main/command_processor.cpp:1152-1154` |

Conclusiones:

- El selector real vive en `app_head_program_select()`.
- `program_status` responde `PROGRAM_STATE|ACTIVE=1/2`.
- `program_select_*` usa `app_head_state_manager_has_active_motion()` y `app_head_fast_diag_is_busy()` para negar cambios durante actividad.

## F. PERFILES C++

| ELEMENTO | PROGRAMA 1 | PROGRAMA 2 | INDEPENDIENTES | CONECTADOS |
|---|---|---|---:|---:|
| `kProgram1Commands` | SI | NO | SI | SI |
| `kProgram2Commands` | NO | SI | SI | SI |
| `init_sequence` | Igual | Igual | SI | SI |
| `testeo` | Igual | Igual | SI | SI |
| `den` | Igual | Igual | SI | SI |
| `sic` | Igual | Igual | SI | SI |
| `feet` | Vacio | 2 motores | SI | SI |
| `j` | Igual | Igual | SI | SI |
| `yarn` | Igual | Igual | SI | SI |
| `stitch` | Igual | Igual | SI | SI |
| `stop` | `sends_can_frame=false` | `sends_can_frame=false` | SI | SI |

Evidencia:

- `kProgram1Commands`: `UsbSmokeIdf/main/head_program_1_commands.cpp:55-146`
- `kProgram2Commands`: `UsbSmokeIdf/main/head_program_2_commands.cpp:57-160`
- Runtime selector: `UsbSmokeIdf/main/head_program_runtime.cpp:18-47`

## G. ESTADO POR MODULO

| MODULO | HANDLER REAL | USA PERFIL ACTIVO | P1/P2 SEPARADOS | ESTADO |
|---|---|---|---|---|
| INIT | `app_head_fast_diag_start_init()` | SI, snapshot al inicio | NO | COMPLETO |
| TESTEO | `app_head_fast_diag_start_testeo()` | SI, snapshot al inicio | NO | COMPLETO |
| DEN | `app_head_state_manager_start_den_run*()` | SI | NO | COMPLETO |
| SIC | `app_head_state_manager_start_sic_run()` | SI | NO | COMPLETO |
| Feet | `app_head_state_manager_start_feet_run()` | SI | SI, solo P2 tiene datos | PARCIAL |
| J | `app_head_state_manager_start_j_run()` y `tick()` | SI | NO | COMPLETO |
| Yarn | `app_head_state_manager_start_yarn_run()` | SI | NO | COMPLETO |
| Stitch | `app_head_state_manager_start_stitch_run()` | SI | NO | COMPLETO |
| RUN | `app_head_state_manager_tick()` | SI, en cada tick | NO | PARCIAL |
| STOP | `app_head_state_manager_stop_all_motion()` / `app_head_fast_diag_request_stop()` | SI | NO | COMPLETO |

## H. INIT EN DETALLE

1. Entrada por `init` en `app_command_process_line()`.
2. Handler: `app_head_fast_diag_start_init()`.
3. Tarea creada: `app_head_fast_diag_init_task()` en core 1.
4. Secuencia usada: `profile->init_sequence`.
5. Perfil capturado una vez al crear la tarea.
6. Se usa `HeadCommandProfile`.
7. Program 1 y Program 2 usan la misma rutina INIT hoy.
8. Ambos terminan en la misma tarea global.
9. `INIT1_SEQ` / `INIT2_SEQ` pertenecen al legado antiguo; en el firmware actual son `phase1` y `phase2` de `init_sequence`.
10. Hay logs con programa y fase.

Flujo real:

```text
App/USB
-> parser
-> handler INIT
-> tarea
-> secuencia
-> app_head_fast_diag_run_script_line
-> app_can_send_standard
-> twai_transmit
```

Evidencias:

- `UsbSmokeIdf/main/command_processor.cpp:1408-1424`
- `UsbSmokeIdf/main/head_fast_diag.cpp:711-925`
- `UsbSmokeIdf/main/head_fast_diag.cpp:995-1000`
- `UsbSmokeIdf/main/head_fast_diag.cpp:778-780`

## I. TESTEO EN DETALLE

1. Entrada por `testeo` en `app_command_process_line()`.
2. Handler: `app_head_fast_diag_start_testeo()`.
3. Tarea creada: `app_head_fast_diag_testeo_task()` en core 1.
4. Secuencia usada: `profile->testeo`.
5. El perfil se captura una sola vez al arrancar.
6. Se usa `HeadCommandProfile`.
7. Program 1 y Program 2 comparten el mismo TESTEO hoy.
8. No depende de `TX_OK`; decide por RX real y codigos de respuesta.
9. Puede ejecutarse concurrentemente con RUN porque no consulta motion activo.

Flujo real:

```text
App/USB
-> parser
-> handler TESTEO
-> tarea
-> ping CAN
-> RX queue
-> decision
-> respuesta
```

Evidencias:

- `UsbSmokeIdf/main/command_processor.cpp:1426-1440`
- `UsbSmokeIdf/main/head_fast_diag.cpp:497-709`
- `UsbSmokeIdf/main/head_fast_diag.cpp:390-495`
- `UsbSmokeIdf/main/head_fast_diag.cpp:1118-1130`

## J. RIESGOS TECNICOS

| Severidad | Riesgo | Evidencia |
|---|---|---|
| Critico | TESTEO puede correr mientras hay RUN activo | `UsbSmokeIdf/main/command_processor.cpp:1426-1432`, `UsbSmokeIdf/main/head_fast_diag.cpp:1118-1130` |
| Alto | INIT puede arrancar con motion activo y solo luego detiene movimiento | `UsbSmokeIdf/main/command_processor.cpp:1408-1424`, `UsbSmokeIdf/main/head_fast_diag.cpp:778-780` |
| Alto | `s_active_program` es RAM estatica sin mutex propio | `UsbSmokeIdf/main/head_program_runtime.cpp:6-16` |
| Alto | Runner legado compilado pero desconectado del flujo actual | `UsbSmokeIdf/main/command_head_program_runner.cpp:3754-3778`, `UsbSmokeIdf/main/head_runtime.cpp:203-206` |
| Medio | Bitrate CAN real es 1 Mbps, no 500 kbps | `UsbSmokeIdf/main/can_driver_twai.cpp:639-705` |
| Medio | Feet solo existe realmente en Program 2 | `UsbSmokeIdf/main/head_program_1_commands.cpp:111-146`, `UsbSmokeIdf/main/head_program_2_commands.cpp:113-160` |
| Bajo | `program_status` existe pero la UI usa `status` | `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor:3470-3494` |
| Bajo | `app_command_line_is_physical()` no tiene caller en el arbol actual | `UsbSmokeIdf/main/command_processor.cpp:1205-1265` |
| Bajo | `HALT` no existe como comando/estado de firmware | `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor:277`, `referencia_antigua/html_antiguo/index_antiguo.html:759-761` |

## K. YA IMPLEMENTADO REALMENTE

- `program_select_1/2` desde app hasta firmware.
- `program_status` con respuesta `PROGRAM_STATE|ACTIVE=...`.
- INIT y TESTEO como rutinas separadas.
- RUN por modulo con perfil activo.
- CAN TX y RX reales sobre TWAI.
- Program 2 con Feet real.
- Selector de programa integrado en la app modular.

Evidencia base:

- `UsbSmokeIdf/main/command_processor.cpp:1136-1384`
- `UsbSmokeIdf/main/head_fast_diag.cpp:497-925`
- `UsbSmokeIdf/main/head_state_manager.cpp:1793-2030`
- `UsbSmokeIdf/main/can_driver_twai.cpp:241-495`

## L. IMPLEMENTADO PARCIALMENTE

- `start` existe pero solo responde `OK start`.
- Feet esta implementado solo en Program 2.
- `app_head_program_runtime_init()` existe, pero no vi caller.
- `command_head_program_runner.cpp` esta compilado, pero no es la ruta activa para Program 1/2.
- `app_command_line_is_physical()` existe pero no participa del flujo real.
- INIT y TESTEO usan snapshot del perfil, no relectura por paso.
- La UI no usa `program_status`.

## M. NO IMPLEMENTADO

- `HeadProgramId` como simbolo.
- `app_head_program_get_active()`.
- Persistencia del programa activo entre reinicios.
- Un HALT de firmware.
- Separacion real por programa para INIT/TESTEO/DEN/SIC/J/Yarn/Stitch.
- `500 kbps` en CAN.

## N. ARCHIVOS CLAVE PARA MODIFICAR DESPUES

- `UsbSmokeIdf/main/head_program_runtime.cpp` y `.h`: selector, estado activo y API.
- `UsbSmokeIdf/main/head_program_1_commands.cpp` y `.h`: tabla de Program 1.
- `UsbSmokeIdf/main/head_program_2_commands.cpp` y `.h`: tabla de Program 2.
- `UsbSmokeIdf/main/command_processor.cpp`: parser, selector y handlers.
- `UsbSmokeIdf/main/head_fast_diag.cpp`: INIT/TESTEO.
- `UsbSmokeIdf/main/head_state_manager.cpp` y `.h`: RUN y estado fisico.
- `UsbSmokeIdf/main/head_runtime.cpp`: cola de cabezal y STOP prioritario.
- `UsbSmokeIdf/main/can_driver_twai.cpp`: TWAI, RX y TX.
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`: UI de programa y comandos.
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs`
- `app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs`
- `UsbSmokeIdf/main/CMakeLists.txt`

## O. PLAN RECOMENDADO POR FASES

| Fase | Objetivo unico | Archivos probables | Criterio de compilacion | Criterio de prueba |
|---|---|---|---|---|
| 1 | Base de perfiles y selector RAM | `head_command_profile.*`, `head_program_runtime.*` | `idf.py build` + `dotnet build` | `program_status` y default P1/P2 |
| 2 | Selector USB y sincronizacion UI-firmware | `command_processor.cpp`, `CabezalDashboardTarjetas.razor`, `FastDashboardCommandService.cs` | build firmware + app | `program_select_1/2` con ack y bloqueo busy |
| 3 | INIT | `head_fast_diag.cpp` | `idf.py build` | `init` recorre INIT1/INIT2 y loguea programa |
| 4 | TESTEO | `head_fast_diag.cpp` | `idf.py build` | `testeo` valida RX real y falla limpio |
| 5 | DEN | `command_processor.cpp`, `head_state_manager.cpp` | `idf.py build` | `den_run`, `den_run1`, `den_stop` |
| 6 | SIC | `command_processor.cpp`, `head_state_manager.cpp` | `idf.py build` | `sic_run`, `sic_stop`, posiciones correctas |
| 7 | Feet | `head_program_2_commands.cpp`, `head_state_manager.cpp` | `idf.py build` + `dotnet build` | Feet solo P2 y falla en P1 |
| 8 | J | `command_processor.cpp`, `head_state_manager.cpp` | `idf.py build` | `j_set`, `j_ch`, `j_run_all`, `j_stop_all` |
| 9 | Yarn | `command_processor.cpp`, `head_state_manager.cpp`, app Yarn | `idf.py build` + `dotnet build` | `y1/y2 run/stop`, `y_run_all`, `y_stop_all` |
| 10 | Stitch | `command_processor.cpp`, `head_state_manager.cpp`, app Stitch | `idf.py build` + `dotnet build` | `s_run_*`, `s_stop_*`, `s_run_all`, `s_stop_all` |
| 11 | RUN/STOP y concurrencia | `head_runtime.cpp`, `command_processor.cpp`, `head_state_manager.cpp` | `idf.py build` | STOP preemptivo y sin mezcla de perfiles |
| 12 | Integracion final y documentacion | `main/CMakeLists.txt`, `docs/*`, app docs | `idf.py build` + `dotnet build` | matriz completa y trazabilidad |

## P. SIGUIENTE TAREA RECOMENDADA

- Cerrar primero la concurrencia de `TESTEO` contra RUN en `UsbSmokeIdf/main/command_processor.cpp` y `UsbSmokeIdf/main/head_fast_diag.cpp`.

## Referencias rapidas

- `UsbSmokeIdf/main/command_processor.cpp:1136-1167`
- `UsbSmokeIdf/main/head_program_runtime.cpp:6-47`
- `UsbSmokeIdf/main/head_program_1_commands.cpp:55-146`
- `UsbSmokeIdf/main/head_program_2_commands.cpp:57-160`
- `UsbSmokeIdf/main/head_fast_diag.cpp:497-925`
- `UsbSmokeIdf/main/head_state_manager.cpp:686-2030`
- `UsbSmokeIdf/main/can_driver_twai.cpp:241-705`
- `UsbSmokeIdf/main/usb_smoketest_main.cpp:2277-6773`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor:3185-3494`
