# MAPA VIGENTE DE PROGRAMAS MODULARES

## Estado final de migracion

Rama auditada: `desarrollo-rapido`.

Los perfiles son estructuras compiladas. No usan TXT, CSV, LittleFS, `/fs`,
`FILE_LIST`, `FILE_SELECT` ni WiFi.

### MIGRADO AL PERFIL

- INIT: secuencias, pausas entre pasos y pausa entre fases.
- TESTEO: trama ping, IDs de respuesta/reset, codigos esperados, reintentos y tiempos.
- DEN: ID CAN, opcode, base de motor, posiciones, RUN, RUN1 y periodos.
- SIC: ID CAN, opcode, base de motor, posiciones, RUN y periodo.
- Feet: ID CAN, opcode, base de motor, posiciones POS1/POS2, RUN y periodo.
- J: ID CAN, opcode, base, cantidad de canales, registros inicial/ON ALL/OFF ALL y periodo RUN.
- Yarn: ID CAN, opcode, direcciones, valores ON/OFF y periodo RUN.
- Stitch: ID CAN, opcode, direcciones, valores ON/OFF y periodo RUN.
- STOP: cada perfil contiene su configuracion. Actualmente `sends_can_frame=false`, que conserva el comportamiento existente de cancelar estado sin transmitir una trama nueva.
- Comandos manuales: POS, J, Yarn y Stitch llegan como intenciones semanticas y el firmware arma la trama desde el perfil activo.

### PENDIENTE DE MIGRAR

- Ningun comando fisico de INIT, TESTEO, DEN, SIC, Feet, J, Yarn, Stitch, RUN o STOP queda pendiente.
- La consola CAN manual sigue aceptando tramas arbitrarias por diseno; no representa un comando de modulo ni se reescribe por perfil.
- El CHECK del subsistema de alertas queda fuera de este perfil porque pertenece al alcance de alertas, no al Dashboard Modular.

## Estructura comun

Archivo: `UsbSmokeIdf/main/head_command_profile.h`

Simbolos:

- `HeadCanCommand`: ID, DATA y DLC de una trama fija.
- `HeadInitCommandSequence`: `phase1_steps`, `phase2_steps` y sus tiempos.
- `HeadTesteoCommandProfile`: ping, respuestas y tiempos de TESTEO.
- `HeadMotionCommandProfile`: DEN, SIC y Feet.
- `HeadJCommandProfile`: J1-J8.
- `HeadCascadeCommandProfile`: Yarn y Stitch.
- `HeadStopCommandProfile`: comportamiento STOP por perfil.
- `HeadCommandProfile`: agrega `init_sequence`, `testeo`, `den`, `sic`, `feet`, `j`, `yarn`, `stitch` y `stop`.

Lineas de referencia actuales: `14-103`.

## CÓMO MODIFICAR LOS COMANDOS DE PROGRAMA 2

Archivo principal: `UsbSmokeIdf/main/head_program_2_commands.cpp`.

Simbolo principal: `kProgram2Commands`.

### INIT

- Buscar `kInitPhase1` y `kInitPhase2`.
- Cambiar ahi las lineas CAN o `WAIT` de Programa 2.
- Buscar `kProgram2Commands.init_sequence` para cambiar `phase1_step_delay_ms`, `phase_gap_ms` o `phase2_step_delay_ms`.
- No editar `UsbSmokeIdf/main/head_fast_diag.cpp`: ese archivo solo recorre el perfil activo.
- Referencias actuales: `kInitPhase1` linea 3, `kInitPhase2` linea 18, `.init_sequence` linea 60.

### TESTEO

- Buscar `kProgram2Commands.testeo`.
- Cambiar `ping.can_id`, `ping.data`, `ping.dlc`, `response_can_id`, `reset_can_id`, `reset_data`, codigos o tiempos.
- No editar la maquina compartida `app_head_fast_diag_testeo_task(...)`.
- Referencia actual: `.testeo` linea 69.

### DEN, SIC y Feet

- DEN: `kDenRunSequence`, `kDenRun1Sequence`, `kDenPositions` y `kProgram2Commands.den`.
- SIC: `kSicRunSequence`, `kSicPositions` y `kProgram2Commands.sic`.
- Feet: `kFeetRunSequence`, `kFeetPositions` y `kProgram2Commands.feet`.
- En cada estructura se pueden cambiar `can_id`, `opcode`, `motor_index_base`, posiciones y periodos sin tocar Programa 1.
- Referencias actuales: tablas `39-45`; estructuras `.den` 85, `.sic` 99 y `.feet` 113.

### J

- Buscar `kProgram2Commands.j`.
- Cambiar `can_id`, `opcode`, `instance_index_base`, `channel_count`, `initial_register`, `on_all_register`, `off_all_register` o `run_period_ms`.
- Referencia actual: linea 127.

### Yarn y Stitch

- Yarn: buscar `kYarnAddresses` y `kProgram2Commands.yarn`.
- Stitch: buscar `kStitchAddresses` y `kProgram2Commands.stitch`.
- Cambiar direcciones, `can_id`, `opcode`, valores ON/OFF o periodo RUN.
- Referencias actuales: `kYarnAddresses` linea 46, `kStitchAddresses` linea 50, `.yarn` linea 138, `.stitch` linea 148.

### STOP

- Buscar `kProgram2Commands.stop`.
- Actualmente `sends_can_frame=false`; por eso STOP no agrega una accion fisica.
- Si el segundo cabezal define una trama STOP futura, completar `frame.can_id`, `frame.data`, `frame.dlc` y activar `sends_can_frame` solo en Programa 2.
- Referencia actual: linea 158.

## Independencia entre perfiles

- Programa 1: `UsbSmokeIdf/main/head_program_1_commands.cpp`, simbolo `kProgram1Commands`.
- Programa 2: `UsbSmokeIdf/main/head_program_2_commands.cpp`, simbolo `kProgram2Commands`.
- Cada `.cpp` contiene sus propios arrays INIT, posiciones, secuencias y direcciones.
- No hay alias, puntero ni array fisico compartido entre `kProgram1Commands` y `kProgram2Commands`.
- Programa 1 no expone Feet; Programa 2 agrega Feet1/Feet2 con POS1/POS2.

## Consumidores del perfil activo

- Selector en RAM: `head_program_runtime.cpp:app_head_program_select(...)` y `app_head_program_get_active_profile()`.
- INIT/TESTEO: `head_fast_diag.cpp:app_head_fast_diag_init_task(...)` y `app_head_fast_diag_testeo_task(...)`.
- RUN de modulos: `head_state_manager.cpp:app_head_state_manager_tick(...)`.
- Posiciones manuales: `command_processor.cpp:app_handle_profile_position_selection(...)` y `app_handle_short_position_command(...)`.
- J manual: `command_processor.cpp:app_send_profile_j_register(...)`.
- Yarn/Stitch manual: `command_processor.cpp:app_handle_cascade_pin_command(...)`.
- STOP: `command_processor.cpp:app_send_profile_stop_command(...)`.

## Comandos de aplicacion hacia firmware

- Selector: `program_select_1`, `program_select_2`, `program_status`.
- INIT/TESTEO: `init`, `testeo`.
- POS de tabla: `den_select_N|P`, `sic_select_N|P`, `feet_select_N|P`.
- Slider libre: `den_pos_N|VALUE`, `sic_pos_N|VALUE`, `feet_pos_N|VALUE`.
- J manual: `j_set_N|VALUE` o `j_ch_N_C`.
- Yarn manual: `yarn_pin_N|PIN|STATE`.
- Stitch manual: `stitch_pin_N|PIN|STATE`.
- RUN/STOP existentes: `j_*`, `y*`, `s_*`, `den_*`, `sic_*`, `feet_*`, `stop`.

La aplicacion emite estos comandos desde
`app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs`.
TESTEO usa `testeo` desde `CabezalDashboardTarjetas.razor:SendTesteoAsync()`.

## Flujo de ejemplo: Programa 2 Feet1 POS2

```text
CabezalDashboardTarjetas.razor:SetFeetPositionAsync(...)
-> feet_select_1|2
-> command_processor.cpp:app_handle_profile_position_selection(...)
-> app_head_program_get_active_profile()
-> kProgram2Commands.feet.positions[1]
-> kProgram2Commands.feet.can_id/opcode/motor_index_base
-> app_process_frame_command(...)
-> driver TWAI/CAN
```

## Integracion CMake

`UsbSmokeIdf/main/CMakeLists.txt` registra:

- `head_program_runtime.cpp`
- `head_program_1_commands.cpp`
- `head_program_2_commands.cpp`

---

# AUDITORIA HISTORICA OBSOLETA

El contenido siguiente se conserva solo como historial. No usar sus pendientes ni sus instrucciones para modificar Programa 2.

## Verificacion real

- Rama real: `desarrollo-rapido`.
- El arbol ya tenia cambios previos en app y firmware.
- `git grep` no ve untracked, por eso los archivos nuevos de Programa 2 se revisaron directamente.

## Aplicacion

### Selector y estado

- Archivo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor)
- Responsabilidad: selector `Programa 1` / `Programa 2`, sincronizacion con firmware, limpieza de sesion temporal y envio de `INIT`, `TESTEO`, `J`, `DEN`, `SIC`, `Feet`, `Yarn` y `Stitch`.
- Simbolos: `_selectedProgram`, `_syncedFirmwareProgram`, `CurrentProgramProfile`, `SelectProgramAsync(...)`, `SyncFirmwareProgramSelectionAsync()`, `ResetTransientSessionState()`, `HasActiveProgramRoutine()`, `SendInitAsync()`, `SendTesteoAsync()`, `RunAllYarnAsync()`, `RunAllStitchAsync()`, `SetFeetPositionAsync(...)`, `StartFeetRunAsync(...)`, `StopFeetRunAsync(...)`, `SendJRegisterAsync(...)`, `SetJAllAsync(...)`, `SetBlockAllAsync(...)`, `SetBlockAllCoreAsync(...)`.
- Lineas actuales: `122-130`, `2697-2710`, `3031-3233`, `3518-3552`, `3570-3803`, `5919-6042`, `6222-7484`.
- Que puedo modificar: textos visibles del selector, reglas de bloqueo al cambiar de programa, mensajes de estado.
- Que no debo modificar: comandos CAN/DO ya existentes, secuencias RUN/STOP, ni la logica compartida de Yarn/Stitch/J/DEN/SIC/Feet.

### Perfil y catalogo

- Archivo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs)
- Responsabilidad: enum y catalogo del programa activo.
- Simbolos: `CabezalDashboardTarjetasProgramId`, `CabezalDashboardTarjetasProgramProfile`, `CabezalDashboardTarjetasProgramCatalog.Get(...)`, `CabezalDashboardTarjetasProgramCommon.CreateJGroups()`.
- Lineas actuales: `3-41`.
- Que puedo modificar: agregar otro perfil o separar mas el catalogo.
- Que no debo modificar: la resolucion de `Program2` sin revisar `SelectProgramAsync(...)`.

### Programas

- Archivo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs)
- Responsabilidad: perfil base de Programa 1.
- Simbolo: `CabezalDashboardTarjetasProgram1Commands.Profile`.
- Lineas actuales: `3-19`.

- Archivo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs)
- Responsabilidad: perfil base de Programa 2.
- Simbolos: `CabezalDashboardTarjetasProgram2Commands.Profile`, `CreateFeetMotors`, `new[] { 1, 2 }`.
- Lineas actuales: `3-19`.

### Modelos y helpers

- Archivo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasModels.cs`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasModels.cs)
- Responsabilidad: tablas de motores, bloques y formateo textual.
- Simbolos: `SicMotorIndexes`, `FeetPositions`, `CreateDenMotors()`, `CreateSicMotors()`, `CreateFeetMotors()`, `CreateYarnBlocks()`, `CreateStitchBlocks()`, `FormatPositionLine(...)`, `FormatJRegisterLine(...)`, `FormatBlockPinLine(...)`, `GetBlockAddresses(...)`.
- Lineas actuales: `839`, `860-905`, `909-948`, `960-971`.
- Que puedo modificar: nombres visibles `Feet1` / `Feet2`, tablas de posiciones, helpers de formato si cambia el texto que la app manda.
- Que no debo modificar: `SicMotorIndexes` o las tablas compartidas si quieres conservar la igualdad inicial.

### Servicio de envio

- Archivo: [`app_windows/AcuratexControlApp/Services/ICabezalDashboardTarjetasCommandService.cs`](../app_windows/AcuratexControlApp/Services/ICabezalDashboardTarjetasCommandService.cs)
- Responsabilidad: contrato de envio de la vista modular.
- Simbolos: `SendCanLineAsync(...)`, `SendDoCommandAsync(...)`, `SendDenPositionAsync(...)`, `SendSicPositionAsync(...)`, `SendJRegisterAsync(...)`, `SendJAllAsync(...)`, `SendJChannelAsync(...)`, `SendBlockPinAsync(...)`.

- Archivo: [`app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs`](../app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs)
- Responsabilidad: implementacion activa del contrato.
- Simbolos: `SendDoCommandAsync(...)`, `SendDenPositionAsync(...)`, `SendSicPositionAsync(...)`, `SendJRegisterAsync(...)`, `SendJAllAsync(...)`, `SendJChannelAsync(...)`, `SendBlockPinAsync(...)`, `SendLineAsync(...)`.
- Lineas actuales: `6-67`.

- Archivo: [`app_windows/AcuratexControlApp/CardSystemForm.cs`](../app_windows/AcuratexControlApp/CardSystemForm.cs)
- Responsabilidad: DI que conecta la vista con `FastDashboardCommandService`.
- Simbolo: `services.AddScoped<ICabezalDashboardTarjetasCommandService, FastDashboardCommandService>();`
- Lineas actuales: `240-243`.

## Firmware

### Perfil compartido y runtime

- Archivo: [`UsbSmokeIdf/main/head_command_profile.h`](../UsbSmokeIdf/main/head_command_profile.h)
- Responsabilidad: enum de programas y estructura comun `HeadCommandProfile`.
- Simbolos: `app_head_program_id_t`, `APP_HEAD_PROGRAM_1`, `APP_HEAD_PROGRAM_2`, `HeadCommandProfile`.
- Lineas actuales: `6-32`.

- Archivo: [`UsbSmokeIdf/main/head_program_runtime.h`](../UsbSmokeIdf/main/head_program_runtime.h)
- Responsabilidad: declarar el runtime de programa.
- Simbolos: `app_head_program_runtime_init()`, `app_head_program_get_active_id()`, `app_head_program_get_profile(...)`, `app_head_program_get_active_profile()`, `app_head_program_select(...)`.
- Lineas actuales: `7-12`.

- Archivo: [`UsbSmokeIdf/main/head_program_runtime.cpp`](../UsbSmokeIdf/main/head_program_runtime.cpp)
- Responsabilidad: guardar el programa activo en RAM y devolver el perfil correcto.
- Simbolos: `s_active_program`, `app_head_program_runtime_init()`, `app_head_program_get_profile(...)`, `app_head_program_select(...)`.
- Funcion que cambia el perfil: `app_head_program_select(...)`.
- Lineas actuales: `6-46`.
- Que puedo modificar: la logica de seleccion o el valor inicial del programa.
- Que no debo modificar: `kProgram1Commands` / `kProgram2Commands` desde aqui; este archivo solo elige punteros.

### Perfiles compilados

- Archivo: [`UsbSmokeIdf/main/head_program_1_commands.h`](../UsbSmokeIdf/main/head_program_1_commands.h)
- Archivo: [`UsbSmokeIdf/main/head_program_1_commands.cpp`](../UsbSmokeIdf/main/head_program_1_commands.cpp)
- Responsabilidad: tabla compilada del perfil 1.
- Simbolos: `kProgram1Commands`, `kDenRunSequence`, `kDenRun1Sequence`, `kSicRunSequence`, `kDenPositions`, `kSicPositions`.
- Lineas actuales: `.h 3-5`, `.cpp 9-29`.

- Archivo: [`UsbSmokeIdf/main/head_program_2_commands.h`](../UsbSmokeIdf/main/head_program_2_commands.h)
- Archivo: [`UsbSmokeIdf/main/head_program_2_commands.cpp`](../UsbSmokeIdf/main/head_program_2_commands.cpp)
- Responsabilidad: tabla compilada del perfil 2.
- Simbolos: `kProgram2Commands`, `kDenRunSequence`, `kDenRun1Sequence`, `kSicRunSequence`, `kFeetRunSequence`, `kDenPositions`, `kSicPositions`, `kFeetPositions`.
- Lineas actuales: `.h 3-5`, `.cpp 11-31`.

### Parser y runtime

- Archivo: [`UsbSmokeIdf/main/command_processor.cpp`](../UsbSmokeIdf/main/command_processor.cpp)
- Responsabilidad: parsear `program_select_2`, `program_status`, `init`, `testeo`, `j_*`, `y_*`, `s_*`, `den_*`, `sic_*`, `feet_*` y posiciones directas.
- Simbolos: `app_handle_program_select_command(...)`, `app_handle_short_position_command(...)`, `app_handle_j_short_command(...)`, `app_handle_yarn_short_command(...)`, `app_handle_stitch_short_command(...)`, `app_handle_den_short_command(...)`, `app_handle_sic_short_command(...)`, `app_handle_feet_short_command(...)`.
- Literales clave: `den_pos_#` usa `8, 0`; `sic_pos_#` usa `2, 0x08`; `feet_pos_#` usa `2, 0x08`.
- Respuestas exactas: `program_select_2` devuelve `OK program_select_2`, `program_status` devuelve `PROGRAM_STATE|ACTIVE=2` y el bloqueo devuelve `ERR PROGRAM_BUSY`.
- Lineas actuales: `565-926`, `936-964`, `1161-1174`, `1298-1310`.

- Archivo: [`UsbSmokeIdf/main/head_runtime.cpp`](../UsbSmokeIdf/main/head_runtime.cpp)
- Responsabilidad: cola y tarea que llevan la linea al parser del firmware.
- Simbolos: `app_head_runtime_init(...)`, `app_head_runtime_start()`, `app_head_runtime_enqueue(...)`, `app_head_control_task(...)`, `app_head_runtime_is_priority_stop(...)`.

### Estado y ejecucion

- Archivo: [`UsbSmokeIdf/main/head_state_manager.h`](../UsbSmokeIdf/main/head_state_manager.h)
- Responsabilidad: declarar capacidades maximas y RUN/STOP del cabezal.
- Simbolos: `APP_HEAD_STATE_MAX_J`, `APP_HEAD_STATE_MAX_YARN`, `APP_HEAD_STATE_MAX_SIC`, `APP_HEAD_STATE_MAX_FEET`, `APP_HEAD_STATE_MAX_STITCH`, `app_head_state_manager_start_feet_run(...)`, `app_head_state_manager_stop_feet_run(...)`, `app_head_state_manager_start_sic_run(...)`, `app_head_state_manager_stop_sic_run(...)`, `app_head_state_manager_start_den_run(...)`, `app_head_state_manager_start_den_run1(...)`, `app_head_state_manager_stop_all_motion()`, `app_head_state_manager_tick(...)`.
- Lineas actuales: `11-14`, `267-374`, `408-442`.

- Archivo: [`UsbSmokeIdf/main/head_state_manager.cpp`](../UsbSmokeIdf/main/head_state_manager.cpp)
- Responsabilidad: mantener el estado RUN y emitir las tramas CAN usando el perfil activo.
- Simbolos: `YARN1_ADDR`, `YARN2_ADDR`, `STITCH1_ADDR`, `STITCH2_ADDR`, `STITCH3_ADDR`, `STITCH4_ADDR`, `DEN_RUN_SEQUENCE`, `DEN_RUN1_SEQUENCE`, `SIC_RUN_SEQUENCE`, `DEN_POSITIONS`, `SIC_POSITIONS`, `app_head_state_active_profile()`, `app_head_state_select_den_sequence(...)`, `app_head_state_tick_motion(...)`, `app_head_state_tick_cascade(...)`, `app_head_state_manager_start_feet_run(...)`, `app_head_state_manager_stop_feet_run(...)`, `app_head_state_manager_tick(...)`, `app_head_state_manager_apply_successful_action(...)`.
- Lineas actuales: `30-56`, `681-721`, `1060-1066`, `1717-1961`, `2002-2037`.
- Nota: `Feet` usa `0x08 + i` y hoy sigue compartiendo base con `SIC`.

### INIT y TESTEO

- Archivo: [`UsbSmokeIdf/main/head_fast_diag.cpp`](../UsbSmokeIdf/main/head_fast_diag.cpp)
- Responsabilidad: diagnosticos `INIT` y `TESTEO`.
- Simbolos: `app_head_fast_diag_testeo_task(...)`, `app_head_fast_diag_init_task(...)`, `app_head_fast_diag_start_testeo(...)`, `app_head_fast_diag_start_init(...)`, `INIT1_SEQ`, `INIT2_SEQ`, `PROFILE_COMMAND|PROGRAM=%u|MODULE=TESTEO|STEP=1`, `PROFILE_COMMAND|PROGRAM=%u|MODULE=INIT|STEP=1`.
- Lineas actuales: `480-678`, `681-923`, `1060-1083`.

### CMake

- Archivo: [`UsbSmokeIdf/main/CMakeLists.txt`](../UsbSmokeIdf/main/CMakeLists.txt)
- Responsabilidad: registrar los `.cpp` de perfil y runtime.
- Simbolos: `head_program_runtime.cpp`, `head_program_1_commands.cpp`, `head_program_2_commands.cpp`.
- Lineas actuales: `1-31`.

## Tabla de comandos de Programa 2

| Modulo | Archivo/simbolo | ID CAN | DLC | DATA | Secuencia/tiempo | Estado |
| --- | --- | --- | --- | --- | --- | --- |
| Selector | `SelectProgramAsync(...)`, `program_select_2`, `app_head_program_select(...)` | N/A | N/A | `program_select_2` | Cambia solo el perfil activo | Activo |
| INIT | Referencia historica, ver mapa vigente al inicio | `320`/variado | variado | secuencia mixta `320 ...` | `80U`, `5000U`, `200U` | `MIGRADO AL PERFIL 2` |
| TESTEO | Referencia historica, ver mapa vigente al inicio | `0x320` / `0x700` / `0x702` | variado | ping de perfil | perfil TESTEO | `MIGRADO AL PERFIL 2` |
| Posiciones individuales DEN/SIC/Feet | `SetFeetPositionAsync(...)`, `SendDenPositionAsync(...)`, `SendSicPositionAsync(...)`, `app_handle_short_position_command(...)`, `FormatPositionLine(...)` | `0x320` | `4` | `0x1C, motorIndex, LSB, MSB` | sin espera; cambia de inmediato la posicion visual | `DEN` y `SIC` ya estan en perfil; `Feet` sigue compartido |
| DEN RUN/STOP | `kProgram2Commands.den_run_sequence`, `den_run1_sequence`, `app_head_state_manager_start_den_run(...)` | `0x320` | `4` | `0x1C, motorIndex, value` | `DEN = 1,3,5,2,4`; `DEN1 = 1,3,5`; `80U` | Perfil 2 listo, con fallback compartido |
| SIC RUN/STOP | `kProgram2Commands.sic_run_sequence`, `app_head_state_manager_start_sic_run(...)` | `0x320` | `4` | `0x1C, 0x08 + i, value` | `1,2,3`; `300U` | Perfil 2 listo, base `0x08` compartida |
| Feet1 / Feet2 RUN/STOP | `kProgram2Commands.feet_run_sequence`, `kProgram2Commands.feet_positions`, `app_head_state_manager_start_feet_run(...)` | `0x320` | `4` | `0x1C, 0x08 + i, value` | `FeetRunSequence = { 1, 2 }`; `FeetRunPeriodMs = 300U` | Perfil 2 listo |
| J1-J8 | `SendJRegisterAsync(...)`, `SetJAllAsync(...)`, `app_head_state_manager_apply_successful_action(...)` | `0x320` | `3` | `0x1D, jIndex-1, register` | bit a bit; `ON_ALL/OFF_ALL` usan `0x00/0xFF` | Compartido |
| Yarn1/Yarn2 | `RunAllYarnAsync()`, `SetBlockAllAsync(...)`, `app_head_state_manager_start_yarn_run(...)` | `0x320` | `3` | `0x1E, addr, 01/00` | `YARN1_ADDR`, `YARN2_ADDR` | Compartido |
| Stitch1-4 | `RunAllStitchAsync()`, `SetBlockAllAsync(...)`, `app_head_state_manager_start_stitch_run(...)` | `0x320` | `3` | `0x1E, addr, 01/00` | `STITCH1_ADDR`..`STITCH4_ADDR` | Compartido |
| ON ALL / OFF ALL | `OnAllAsync()`, `OffAllAsync()`, `SetBlockAllCoreAsync(...)` | N/A | N/A | se expande pin por pin | loops de UI sobre todos los bloques | Compartido |
| STOP global | `stop`, `emergency_stop`, `app_head_state_manager_stop_all_motion()` | N/A | N/A | N/A | para J/Yarn/Stitch/DEN/SIC/Feet | Compartido |

## Ruta completa de ejecucion

### Feet1 POS2

```text
click en Feet1
-> CabezalDashboardTarjetas.razor:SetFeetPositionAsync(...)
-> SendDoCommandAsync("feet_pos_1|2")
-> FastDashboardCommandService.SendDoCommandAsync(...)
-> FastDashboardCommandService.SendLineAsync(...)
-> IConnectionController.SendLineAsync(...)
-> head_runtime.cpp:app_head_runtime_enqueue(...)
-> head_runtime.cpp:app_head_control_task(...)
-> command_processor.cpp:app_handle_short_position_command(..., "feet_pos_", 2, 0x08, ...)
-> command_processor.cpp:app_process_frame_command("320 1C 08 ...")
-> TWAI / CAN
-> head_state_manager.cpp:app_head_state_manager_tick(...) usa el perfil activo
```

### Programa 2 -> INIT

```text
click en INIT
-> CabezalDashboardTarjetas.razor:SendInitAsync()
-> CommandService.SendDoCommandAsync("init")
-> head_runtime.cpp -> command_processor.cpp
-> command_processor.cpp: init
-> head_fast_diag.cpp:app_head_fast_diag_start_init(...)
-> INIT1_SEQ / INIT2_SEQ
```

### Programa 2 -> TESTEO

```text
click en TESTEO
-> CabezalDashboardTarjetas.razor:SendTesteoAsync()
-> CommandService.SendCanLineAsync("320 07")
-> head_runtime.cpp -> command_processor.cpp
-> command_processor.cpp: app_process_frame_command(...)
-> la app escucha la respuesta con el detector de TESTEO
```

### Programa 2 -> J / Yarn / Stitch

```text
J:
-> SendJRegisterAsync(...)
-> FormatJRegisterLine(...)
-> linea 320 1D ...

Yarn:
-> RunAllYarnAsync() o SetBlockAllAsync(...)
-> FormatBlockPinLine(...)
-> linea 320 1E ...

Stitch:
-> RunAllStitchAsync() o SetBlockAllAsync(...)
-> FormatBlockPinLine(...)
-> linea 320 1E ...
```

## CÓMO MODIFICAR LOS COMANDOS DE PROGRAMA 2

### Cambiar `Feet1 POS2`

1. Abrir `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs`.
2. Revisar `CreateFeetMotors`, `FeetRunSequence` y `FeetRunPeriodMs`.
3. Abrir `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasModels.cs`.
4. Buscar `FeetPositions`, `CreateFeetMotors()` y `Title = $"Feet{index + 1}"`.
5. Abrir `UsbSmokeIdf/main/head_program_2_commands.cpp`.
6. Cambiar `kFeetRunSequence` o `kFeetPositions`.
7. Si el parser textual cambia, abrir `UsbSmokeIdf/main/command_processor.cpp` y revisar `feet_pos_` con `2, 0x08`.
8. No tocar `kProgram1Commands`.

### Cambiar INIT de Programa 2

1. Abrir `UsbSmokeIdf/main/head_fast_diag.cpp`.
2. Buscar `INIT1_SEQ`, `INIT2_SEQ` y `app_head_fast_diag_init_task(...)`.
3. Cambiar la secuencia y revisar `INIT1_DELAY_MS`, `INIT_GAP_MS` e `INIT2_DELAY_MS`.
4. Hoy INIT sigue centralizado; si lo quieres distinto por programa, hace falta separar tabla.

### Cambiar J, Yarn y Stitch de Programa 2

1. Abrir `UsbSmokeIdf/main/head_state_manager.cpp`.
2. Buscar `YARN1_ADDR`, `YARN2_ADDR`, `STITCH1_ADDR`, `STITCH2_ADDR`, `STITCH3_ADDR`, `STITCH4_ADDR`, `DEN_RUN_SEQUENCE`, `DEN_RUN1_SEQUENCE`, `SIC_RUN_SEQUENCE`, `DEN_POSITIONS`, `SIC_POSITIONS`.
3. Buscar `app_head_state_tick_motion(...)` y `app_head_state_tick_cascade(...)`.
4. Para J, revisar `app_head_state_manager_start_j_run(...)`, `app_head_state_manager_stop_j_run(...)` y `app_head_state_manager_apply_successful_action(...)`.
5. Para Yarn, revisar `app_head_state_manager_start_yarn_run(...)` y `app_head_state_manager_stop_yarn_run(...)`.
6. Para Stitch, revisar `app_head_state_manager_start_stitch_run(...)` y `app_head_state_manager_stop_stitch_run(...)`.
7. En la app, si cambian nombres o cantidad de bloques, editar `CabezalDashboardTarjetasModels.cs` y `CabezalDashboardTarjetas.razor`.

## Comparacion Programa 1 vs Programa 2

- Firmware:
  - `kProgram1Commands` y `kProgram2Commands` son estructuras distintas.
  - `app_head_program_select(...)` solo cambia `s_active_program`.
  - Riesgo compartido: `head_state_manager.cpp` tiene fallback tables y literales comunes.
- App:
  - `CabezalDashboardTarjetasProgram1Commands.Profile` y `CabezalDashboardTarjetasProgram2Commands.Profile` son records separados.
  - Riesgo compartido: `CabezalDashboardTarjetasProtocol` concentra tablas y `SicMotorIndexes` se comparte con `Feet`.
  - `Feet` sigue reutilizando la base `0x08 + i` en firmware.

### Confirmacion practica

- Modificar `kProgram2Commands` no modifica `kProgram1Commands`.
- Modificar `CabezalDashboardTarjetasProgram2Commands.Profile` no modifica `CabezalDashboardTarjetasProgram1Commands.Profile`.
- Si editas `CabezalDashboardTarjetasProtocol` o `head_state_manager.cpp`, ambos programas pueden cambiar a la vez.

## Pendientes fuera del perfil (historico resuelto)

- Esta lista historica quedo resuelta por la migracion documentada al inicio del archivo.
- No usar las instrucciones de esta seccion obsoleta para editar Programa 2.

## Mapa corto para cambiar Programa 2

- App: `CabezalDashboardTarjetasProgram2Commands.cs`, `CabezalDashboardTarjetasModels.cs`, `CabezalDashboardTarjetas.razor`.
- Firmware: `head_program_2_commands.cpp/.h`, `head_program_runtime.cpp/.h`, `command_processor.cpp`, `head_state_manager.cpp`, `head_fast_diag.cpp`, `CMakeLists.txt`.

> El contenido historico que sigue abajo es la version anterior del mapa y no debe usarse como referencia principal para editar Programa 2.

# Mapa Programas Modular

## Por que se descarto TXT/LittleFS

La tarea real del Dashboard Modular es seleccionar dos perfiles de comandos directos en codigo, no cargar programas desde archivos externos.

No se toca:

- `file_transfer.cpp`
- LittleFS
- `/fs`
- `partitions.csv`
- `.txt`
- `.csv`
- `FILE_LIST`
- `FILE_SELECT`
- `wifi_manager.cpp`

## Aplicacion

### Selector visual

- Archivo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor)
- Selector en la barra superior: botones `Programa 1` y `Programa 2`
- Lineas actuales: `124-130`

### Programa activo

- Enum: `CabezalDashboardTarjetasProgramId`
- Campo activo: `_selectedProgram`
- Replica de sincronizacion firmware: `_syncedFirmwareProgram`
- Perfil activo: `CurrentProgramProfile`
- Lineas actuales: `2696-2702`, `3185-3238`

### Limpieza temporal

- Metodo principal: `ResetTransientSessionState()`
- Carga de perfil: `ApplyProgramProfile(...)`
- Al cambiar de programa primero se sincroniza el selector con firmware y luego se limpia la sesion local
- OnInitialized carga `Programa 1`
- Dispose tambien reutiliza `ResetTransientSessionState()`
- Lineas actuales: `3032`, `3102-3109`, `3185-3217`, `14191`

### Como se impide cambiar durante RUN

- Metodo: `HasActiveProgramRoutine()`
- Bloqueo visual: `SelectProgramAsync(...)`
- Si hay rutina activa, muestra:

```text
Detenga las rutinas antes de cambiar de programa.
```

- Lineas actuales: `3166-3217`

### Envio al firmware

- Metodo: `SyncFirmwareProgramSelectionAsync()`
- Comando enviado: `program_select_1` o `program_select_2`
- Lineas actuales: `3217-3233`

### Donde se arma y envia el comando

- Servicio directo: [`app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs`](../app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs)
- Metodo de envio: `SendDoCommandAsync(...)`
- Tambien:
  - `SendDenPositionAsync(...)`
  - `SendSicPositionAsync(...)`
  - `SendJRegisterAsync(...)`
  - `SendBlockPinAsync(...)`
- Lineas actuales: `6-64`

## Perfiles

### Tipos compartidos

- Archivo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs)
- Simbolos:
  - `CabezalDashboardTarjetasProgramId`
  - `CabezalDashboardTarjetasProgramProfile`
  - `CabezalDashboardTarjetasProgramCommon.CreateJGroups(...)`
  - `CabezalDashboardTarjetasProgramCatalog.Get(...)`
- Lineas actuales: `3-41`

### Programa 1

- Archivo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs)
- Simbolo: `CabezalDashboardTarjetasProgram1Commands.Profile`
- Perfil de tipo: `CabezalDashboardTarjetasProgramProfile`
- Lineas actuales: `3-19`

### Programa 2

- Archivo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs)
- Simbolo: `CabezalDashboardTarjetasProgram2Commands.Profile`
- Perfil de tipo: `CabezalDashboardTarjetasProgramProfile`
- Lineas actuales: `3-21`

### Igualdad inicial

Ambos perfiles usan los mismos comandos directos actuales para:

- `DEN`
- `SIC`
- `J`
- `Yarn`
- `Stitch`
- `RUN`
- `STOP`
- `TESTEO`

La diferencia inicial de `Programa 2` esta solo en la segunda seccion visual:

- Titulo: `Feet`
- Bloques: `Feet1`, `Feet2`
- Posiciones: solo `Pos1` y `Pos2`
- Secuencia RUN: `1, 2`

### Vista Feet

- Archivo modelo: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasModels.cs`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasModels.cs)
- Simbolos:
  - `SicMotorIndexes`
  - `FeetPositions`
  - `CreateFeetMotors()`
- Lineas actuales: `839`, `860`, `895-903`
- Archivo vista: [`app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`](../app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor)
- Seccion visual: `Feet`
- Lineas actuales: `1257`, `2497`, `3071`, `5919-6042`, `13582-13716`

## Donde editar Programa 2 despues

- Perfil completo: `CabezalDashboardTarjetasProgram2Commands.Profile`
- Segunda seccion visual: `CabezalDashboardTarjetasModels.CreateFeetMotors()`
- Posiciones Feet: `FeetPositions`
- Selector visual: `CabezalDashboardTarjetas.razor`
- Lineas actuales:
  - `3-19` en `CabezalDashboardTarjetasProgram1Commands.cs`
  - `3-21` en `CabezalDashboardTarjetasProgram2Commands.cs`
  - `3-41` en `CabezalDashboardTarjetasProgramProfiles.cs`
  - `839`, `860`, `895-903` en `CabezalDashboardTarjetasModels.cs`
  - `124-130`, `2696-2702`, `3030-3031`, `3101-3107`, `3164-3226`, `14165` en `CabezalDashboardTarjetas.razor`

## Firmware

### Runtime y perfiles

- Archivo runtime: [`UsbSmokeIdf/main/head_program_runtime.h`](../UsbSmokeIdf/main/head_program_runtime.h)
- Archivo runtime: [`UsbSmokeIdf/main/head_program_runtime.cpp`](../UsbSmokeIdf/main/head_program_runtime.cpp)
- Archivo de perfil compartido: [`UsbSmokeIdf/main/head_command_profile.h`](../UsbSmokeIdf/main/head_command_profile.h)
- Lineas actuales:
  - `head_program_runtime.h`: `7-11`
  - `head_program_runtime.cpp`: `6-46`
  - `head_command_profile.h`: `6-31`

### Perfiles Programa 1 y Programa 2

- Archivo: [`UsbSmokeIdf/main/head_program_1_commands.h`](../UsbSmokeIdf/main/head_program_1_commands.h)
- Archivo: [`UsbSmokeIdf/main/head_program_1_commands.cpp`](../UsbSmokeIdf/main/head_program_1_commands.cpp)
- Simbolo: `kProgram1Commands`
- Lineas actuales: `3-29`

- Archivo: [`UsbSmokeIdf/main/head_program_2_commands.h`](../UsbSmokeIdf/main/head_program_2_commands.h)
- Archivo: [`UsbSmokeIdf/main/head_program_2_commands.cpp`](../UsbSmokeIdf/main/head_program_2_commands.cpp)
- Simbolo: `kProgram2Commands`
- Lineas actuales: `3-31`

- `kProgram1Commands` mantiene `feet_run_sequence = NULL`
- `kProgram2Commands` agrega `feet_run_sequence = { 1, 2 }`

## Firmwares y simbolos exactos

- Archivo de comandos: [`UsbSmokeIdf/main/command_processor.cpp`](../UsbSmokeIdf/main/command_processor.cpp)
- Archivo de secuencias/runtime: [`UsbSmokeIdf/main/head_state_manager.cpp`](../UsbSmokeIdf/main/head_state_manager.cpp)

#### Simbolos a buscar en `command_processor.cpp`

- `status`
- `init`
- `testeo`
- `program_select_1`
- `program_select_2`
- `program_status`
- `j_run_all`
- `j_stop_all`
- `y_run_all`
- `y_stop_all`
- `s_run_all`
- `s_stop_all`
- `den_run_#`
- `den_run1_#`
- `den_stop_#`
- `sic_run_#`
- `sic_stop_#`
- `feet_run_#`
- `feet_stop_#`
- `den_pos_#|#`
- `sic_pos_#|#`
- `feet_pos_#|#`

Lineas actuales de referencia:

- `program_select_*`: `941-964`
- `program_status`: `1171-1174`
- `status`: `1176-1179`
- `init`: `1181-1194`
- `testeo`: `1196-1211`
- `j_*`: `1213-1220`
- `y_*`: `1222-1237`
- `s_*`: `1239-1246`
- `den_*`: `1248-1264`
- `sic_*`: `1266-1273`
- `feet_*`: `1292-1310`
- `den_pos_*`: `1302-1305`
- `sic_pos_*`: `1307-1309`
- `feet_pos_*`: `1308-1310`

#### Simbolos a buscar en `head_state_manager.cpp`

- `APP_HEAD_STATE_MAX_FEET`
- `app_head_state_manager_start_feet_run`
- `app_head_state_manager_stop_feet_run`
- `app_head_state_manager_stop_all_feet_runs`
- `app_head_state_manager_has_active_motion`
- `PROFILE_COMMAND`

Lineas actuales de referencia:

- `APP_HEAD_STATE_MAX_FEET`: `14`
- `feet` en estado: `55`
- `reset feet`: `719-720`
- `tick feet`: `1941-1958`
- `start_feet_run`: `1569-1590`
- `stop_feet_run`: `1595-1608`
- `stop_all_feet_runs`: `1613-1622`
- `has_active_motion`: `1636-1668`
- `PROFILE_COMMAND|PROGRAM=...|MODULE=...`: `1415`
- `DEN`: `1883-1908`
- `SIC`: `1914-1931`
- `YARN`: `1699-1710`
- `STITCH`: `1719-1732`
- `command_select` runtime: [`UsbSmokeIdf/main/head_program_runtime.cpp`](../UsbSmokeIdf/main/head_program_runtime.cpp)
- Lineas actuales runtime: `6-46`

#### `head_fast_diag.cpp`

- `PROFILE_COMMAND|PROGRAM=%u|MODULE=TESTEO|STEP=1`: `525-528`
- `PROFILE_COMMAND|PROGRAM=%u|MODULE=INIT|STEP=1`: `755-758`

## Observacion final

No hay cambios de LittleFS/TXT para este trabajo. La seleccion de programa y la separacion de perfiles quedan en codigo, con memoria temporal local por sesion de dashboard.
