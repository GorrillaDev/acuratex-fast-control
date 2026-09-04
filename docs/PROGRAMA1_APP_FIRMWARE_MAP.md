# Programa 1: mapa App -> firmware -> CAN

Fuentes: `app_windows/AcuratexControlApp/Components/CabezalDashboardUnificadoProgram1Commands.cs` y `UsbSmokeIdf/main/head_unified_program_1_commands.cpp`.

Handlers: todos entran por `UsbSmokeIdf/main/command_processor.cpp`; RUN temporizado llama a `head_state_manager.cpp` y las tramas se producen en `head_command_frame_builder.cpp`.

## Formatos CAN

| Acción | ID | DLC | DATA |
|---|---:|---:|---|
| Posición DEN | mapping DEN | 4 | `1C selector pos_low pos_high` |
| Registro/canal J | mapping J | 3 | `1D selector registro_activo_bajo` |
| Yarn/Stitch ON | mapping instancia | 3 | `1E dirección 01` |
| Yarn/Stitch OFF | mapping instancia | 3 | `1E dirección 00` |
| STOP lógico | — | 0 | Cancela estado; Programa 1 no transmite trama STOP |
| RESET testeo | 0x702 | 2 | `3F 00` |

## DEN1..DEN8

Para `N=1..8`, selector `N-1`, CAN `0x320`:

| Control app | Token | Handler firmware | CAN resultante |
|---|---|---|---|
| Posición predefinida | `den_select_N|P` | parser posición -> `app_head_build_motion_frame` | DLC 4, `1C selector low high`; P usa `0000,00A2,0145,01E7,028A` |
| Posición libre | `den_pos_N|V` | parser posición -> `app_head_build_motion_frame` | DLC 4, `1C selector V_low V_high` |
| RUN | `den_run_N` | `app_handle_den_short_command` -> `start_den_run` | secuencia `1,3,5,2,4`, 300 ms |
| RUN1 | `den_run1_N` | mismo handler -> `start_den_run1` | secuencia `1,3,5`, 300 ms |
| STOP/STOP1 | `den_stop_N` / `den_stop1_N` | mismo handler -> `stop_den_run` | sin trama STOP |

## J1..J8

Para `N=1..8`, selector `N-1`, CAN `0x320`, canales válidos `1..6`:

| Control app | Token | Handler firmware | CAN resultante |
|---|---|---|---|
| Canal C | `j_ch_N_C` | `app_handle_j_output_command` | DLC 3, `1D selector registro`; solo bits 0..5 |
| Todos ON/OFF | `j_set_N|0` / `j_set_N|255` | mismo handler | DLC 3, `1D selector 00/FF` |
| RUN | `j_run_N` | `app_handle_j_short_command` -> `start_j_run` | motor J genérico, DLC 3 cada 80 ms |
| STOP | `j_stop_N` | mismo handler -> `stop_j_run` | sin trama STOP |
| RUN ALL | `j_run_all` | mismo handler; itera `profile->j.instance_count` y llama `start_j_run` | mismas tramas individuales |
| STOP ALL | `j_stop_all` | mismo handler -> `stop_all_j_runs` | sin trama STOP |

## Yarn1..Yarn2

| Instancia | Token RUN/STOP | Direcciones | Handler y trama |
|---|---|---|---|
| Yarn1 | `y1_run` / `y1_stop` | `18 19 1A 1B 1C 1D 1E 1F` | parser genérico Yarn -> motor cascada; `320`, DLC 3, `1E dirección 01/00`, 80 ms |
| Yarn2 | `y2_run` / `y2_stop` | `24 25 26 27 20 21 22 23` | igual |
| Todos | `y_run_all` / `y_stop_all` | ambas tablas | itera `profile->yarn.instance_count`; reutiliza start/stop Yarn |

Cambio manual de pin: `yarn_pin_N|C|0/1` -> `app_handle_cascade_pin_command` -> `320`, DLC 3, `1E dirección 00/01`.

## Stitch1..Stitch4

| Instancia | Token RUN/STOP | Direcciones | Handler y trama |
|---|---|---|---|
| Stitch1 | `s_run_1` / `s_stop_1` | `00 01 02 05` | handler Stitch -> motor cascada; `320`, DLC 3, `1E dirección 01/00`, 120 ms |
| Stitch2 | `s_run_2` / `s_stop_2` | `06 07 08 0B` | igual |
| Stitch3 | `s_run_3` / `s_stop_3` | `0C 0D 0E 11` | igual |
| Stitch4 | `s_run_4` / `s_stop_4` | `12 13 14 17` | igual |
| Todos | `s_run_all` / `s_stop_all` | las cuatro tablas | itera `profile->stitch.instance_count`; reutiliza start/stop Stitch |

Las posiciones visuales Stitch son `1,2,3,4,5`. Las posiciones 1..4 corresponden a sus cuatro salidas físicas actuales; la quinta es una posición visual sin dirección CAN adicional. Cambio manual: `stitch_pin_N|C|0/1` -> handler cascada -> DLC 3, `1E dirección 00/01`.

## Comandos globales y RESET

| Control app | Token(es) | Handler | CAN |
|---|---|---|---|
| RUN ALL | `j_run_all`, `y_run_all`, `s_run_all` | compone los tres ALL; cada ALL inicia instancias con el mismo motor individual | según tabla de cada módulo |
| STOP ALL | `j_stop_all`, `y_stop_all`, `s_stop_all` | compone los tres ALL; cancela estados individuales | sin trama STOP |
| RESET Stitch visual | pines OFF del bloque (`stitch_pin_N|C|0`) | handler cascada | `320`, DLC 3, `1E dirección 00` por salida |
| RESET testeo firmware | comando de testeo/reset | perfil `testeo.reset_*` | `702`, DLC 2, `3F 00` |
| Emergencia | `emergency_stop` y los tres STOP ALL | procesador/emergencia + state manager | cancela ejecuciones; STOP de Programa 1 no tiene trama propia |

## Regla de sincronización

Toda modificación de constantes/tablas en el archivo de app debe tener el mismo cambio en el perfil firmware. No copie mappings dentro de handlers: los handlers deben seguir leyendo `HeadCommandProfile` y construyendo mediante `app_head_build_motion_frame`, `app_head_build_j_frame` o `app_head_build_cascade_frame`.
