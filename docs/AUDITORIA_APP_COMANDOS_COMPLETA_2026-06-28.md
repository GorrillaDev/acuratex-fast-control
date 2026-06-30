# AUDITORIA APP COMANDOS COMPLETA

Fecha de auditoria: 2026-06-29
Repositorio: `C:\Proyectos\AcuratexFastControl`
Rama esperada y encontrada: `desarrollo-rapido`
Modo de trabajo: solo lectura, sin cambios de codigo fuente ni firmware

## 1. Estado inicial del repositorio

Comandos ejecutados:

```powershell
cd "C:\Proyectos\AcuratexFastControl"
git branch --show-current
git status --short
git diff --stat
git diff --name-only
git log -5 --oneline
```

Resultado resumido:

- Rama actual: `desarrollo-rapido`
- Cambios locales preexistentes:
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
  - `docs/AUDITORIA_FIRMWARE_PROGRAMAS_2026-06-28.md`
  - `docs/MAPA_PROGRAMAS_MODULAR.md`
  - `tmp_gen/`
- Cambios previos relacionados con Dashboard: si, en `CabezalDashboardTarjetas.razor`, `CabezalDashboardTarjetasModels.cs`, `FastDashboardCommandService.cs` y CSS del dashboard.

## 2. Aplicacion activa

### APLICACION ACTIVA:

Aplicacion Windows WinForms + Blazor WebView montada desde `Form1`.

### DASHBOARD ACTIVO:

`CabezalDashboardTarjetas.razor` dentro de `CardSystemShell.razor` del sistema modular.

### RUTA DE ARRANQUE:

`Program.Main` -> `Application.Run(new Form1())` -> `Form1.ConfigureBlazor()` -> `AppShell.razor` -> `MainControlPanel.razor` -> `TerminalView.razor` -> `Form1.LaunchGuiAsync()` -> `SystemSelectorForm` -> `SystemSelectorView.razor` -> `SystemSelectorForm.SelectCardSystemAsync()` -> `new CardSystemForm(...)` -> `CardSystemForm.ConfigureBlazor()` -> `CardSystemShell.razor` -> `CabezalDashboardTarjetas.razor`

### COMPONENTES ANTIGUOS O DESCONECTADOS:

- `CabezalDashboardTarjetasCommandService.cs`: servicio legacy no activo en la shell modular actual.
- `UnifiedSystemForm` y `CabezalDashboardUnificado.razor`: otra rama de interfaz, no la usada por el dashboard modular auditado.
- `command_head_program_runner.cpp`: ruta de firmware legacy asociada a `HEAD_STATUS`, `HEAD_ACTION`, `HEAD_PROGRAM_SELECT`; no corresponde al flujo directo usado por el dashboard modular activo.
- HTML host de otras shells:
  - `wwwroot/index-main.html`
  - `wwwroot/index-system-selector.html`
  - `wwwroot/index-card-system-shell.html`
  - `wwwroot/index-unified-system-shell.html`
  Solo `index-card-system-shell.html` participa en el dashboard modular activo.

### EVIDENCIAS:

- `app_windows/AcuratexControlApp/Program.cs:43-66`
  - `Application.Run(new Form1());`
- `app_windows/AcuratexControlApp/Form1.cs:914-940`
  - `blazorWebView.HostPage = "wwwroot\\index-main.html";`
  - `blazorWebView.RootComponents.Add<AppShell>("#app");`
- `app_windows/AcuratexControlApp/Components/AppShell.razor`
  - renderiza `MainControlPanel`
- `app_windows/AcuratexControlApp/Components/TerminalView.razor:105-109`
  - boton `Abrir interfaz grafica de pruebas`
- `app_windows/AcuratexControlApp/Form1.cs:699-706`
  - `LaunchGuiAsync()`
- `app_windows/AcuratexControlApp/Form1.cs:864-883`
  - `OpenSystemInterfaceFlowAsync()` muestra `SystemSelectorForm`
- `app_windows/AcuratexControlApp/Components/SystemSelectorView.razor:37-59`
  - selector entre `Sistema Unificado` y `Sistema Modular`
- `app_windows/AcuratexControlApp/SystemSelectorForm.cs:237-243`
  - `SelectCardSystemAsync()` devuelve `CardSystemForm`
- `app_windows/AcuratexControlApp/CardSystemForm.cs:222-247`
  - registra servicios y monta `CardSystemShell`
- `app_windows/AcuratexControlApp/Components/CardSystemShell.razor:87-94`
  - contiene `<CabezalDashboardTarjetas />`
- `app_windows/AcuratexControlApp/CardSystemForm.cs:241-242`
  - `ICabezalDashboardTarjetasCommandService -> FastDashboardCommandService`

## 3. Archivos de Programa 1 y Programa 2 ya revisados

Archivos auditados completamente:

- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasModels.cs`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`

Hallazgos:

- `CabezalDashboardTarjetasProgram1Commands.cs:3-20`
  - Define `Profile` de Programa 1.
  - Usa fabricas compartidas: `CreateDenMotors`, `CreateSicMotors`, `CreateJGroups`, `CreateYarnBlocks`, `CreateStitchBlocks`.
  - `Feet` queda vacio con `Array.Empty<CabezalMotorTarjetas>()`.
  - Secuencias:
    - `DenRunSequence`
    - `DenRun1Sequence`
    - `SicRunSequence`
    - `FeetRunSequence = []`
- `CabezalDashboardTarjetasProgram2Commands.cs:3-20`
  - Define `Profile` de Programa 2.
  - Reutiliza las mismas fabricas compartidas.
  - `Feet` usa `CreateFeetMotors()`.
  - `FeetRunSequence = { 1, 2 }`
- `CabezalDashboardTarjetasProgramProfiles.cs:9-42`
  - No contiene comandos enviados por USB.
  - Contiene metadatos de programa, factoria de modulos y secuencias locales de animacion.
- `CabezalDashboardTarjetasModels.cs:831-972`
  - Contiene metadatos visuales y plantillas CAN formateadas para trazas:
    - `FormatPositionLine(...)` -> `320 1C ...`
    - `FormatJRegisterLine(...)` -> `320 1D ...`
    - `FormatBlockPinLine(...)` -> `320 1E ...`
  - Define direcciones/pines de Yarn/Stitch:
    - Yarn1: `0x18..0x1F`
    - Yarn2: `0x24..0x27, 0x20..0x23`
    - Stitch1..4: direcciones `0x00..0x17` en grupos
  - Define secuencias locales:
    - `DenRunSequence = {1,3,5,2,4}`
    - `DenRun1Sequence = {1,3,5}`
    - `SicRunSequence = {1,2,3}`

Conclusion de estos archivos:

- Si contienen configuracion real de Programa 1 y Programa 2, pero no almacenan todos los comandos USB finales.
- Los comandos USB directos activos salen sobre todo de `FastDashboardCommandService.cs` y de literales dentro de `CabezalDashboardTarjetas.razor`.
- Los perfiles de P1 y P2 no son completamente independientes:
  - Son objetos separados.
  - Comparten fabricas, modelos, secuencias base y logica de ejecucion.
  - Programa 2 no copia de forma textual los comandos de Programa 1, pero reutiliza la misma implementacion interna.

## 4. Servicio real de envio

Servicio activo:

- `app_windows/AcuratexControlApp/CardSystemForm.cs:241-242`
  - `ICabezalDashboardTarjetasCommandService` se resuelve como `FastDashboardCommandService`

### Flujo real

`Boton o rutina` -> `metodo de CabezalDashboardTarjetas.razor` -> `literal o perfil actual` -> `FastDashboardCommandService` -> `ConnectionController.SendLineAsync` -> `IControllerTransport.SendLineAsync` -> `WinUsbControllerTransport` o `SerialControllerTransport` o `TcpControllerTransport` -> firmware

### Evidencia

- `app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs:15-109`
- `app_windows/AcuratexControlApp/ConnectionController.cs:283-293`
- `app_windows/AcuratexControlApp.Transport/WinUsbControllerTransport.cs:229-256`
- `app_windows/AcuratexControlApp.Transport/SerialControllerTransport.cs:188-199`
- `app_windows/AcuratexControlApp.Transport/TcpControllerTransport.cs:220-224`
- `app_windows/AcuratexControlApp.Transport/TcpControllerTransport.cs:367-382`

### Respuestas a las preguntas obligatorias

- El servicio reinterpreta comandos:
  - `FastDashboardCommandService`: no, salvo formatear algunos nombres (`den_select_...`, `j_set_...`, `yarn_pin_...`).
  - `CabezalDashboardTarjetasCommandService`: si, pero es legacy y no activo en la shell modular actual.
- Agrega salto de linea:
  - WinUSB: si, `line + "\n"` en `WinUsbControllerTransport.cs:237-239`
  - Serial: si, via `SerialPort.WriteLine()` con `NewLine = "\n"`
  - TCP: si, via `StreamWriter.WriteLineAsync()` con `NewLine = "\n"`
- Agrega prefijos o sufijos:
  - No agrega prefijos semanticos.
  - Solo agrega framing por nueva linea en transporte.
- Transforma mayusculas/minusculas:
  - No.
- Envia un comando por vez:
  - Cada llamada envia una linea.
  - TCP serializa con `SemaphoreSlim`.
  - WinUSB y Serial no implementan cola explicita.
- Tiene cola:
  - No en el servicio activo.
  - No en `ConnectionController`.
  - TCP solo serializa, no modela cola semantica.
- Puede enviar comandos simultaneos:
  - A nivel de UI, si pueden invocarse multiples tareas.
  - A nivel TCP, se serializan.
  - A nivel WinUSB/Serial, no hay arbitraje extra.
- Ignora comandos cuando no hay conexion:
  - `FastDashboardCommandService` no ignora: lanza excepcion si `_connection.IsConnected` es falso.
- Registra historial:
  - La UI si registra trazas en `SendTrackedAsync`.
  - El transporte no mantiene historial funcional.
- Devuelve ACK:
  - El servicio no fabrica ACK.
  - Solo transporta la respuesta textual del firmware.
- La UI espera respuesta:
  - Parcialmente.
  - `INIT` y `TESTEO` si dependen de respuestas para actualizar estado.
  - Muchos botones cambian visualmente antes del ACK.
- Los botones cambian visualmente antes del envio:
  - Si, en varios casos:
    - J pin, J all, bloques Yarn/Stitch, sliders, posiciones y varias rutinas locales.
- Hay comandos enviados en segundo plano:
  - Si:
    - `program_select_n` se sincroniza automaticamente desde `SendTrackedAsync` antes de casi cualquier envio.
    - `CHECK` envia `320 07`.
    - TCP envia `ping` periodico en `HeartbeatLoop()`.
- Los comandos enviados son independientes o el servicio introduce dependencias:
  - El servicio activo introduce una dependencia oculta importante:
    - casi cualquier comando puede provocar antes un `program_select_n` automatico por `SendTrackedAsync`.

## 5. Flujo exacto de cambio de programa

Metodo principal:

- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor:3185-3236`

Orden real:

1. Evento visual
   - click en boton `Programa 1` o `Programa 2`
2. Metodo ejecutado
   - `SelectProgramAsync(program)`
3. Validacion
   - si hay rutina activa (`HasActiveProgramRoutine()`), no cambia y solo informa estado
4. Sincronizacion con firmware
   - `SyncFirmwareProgramSelectionAsync()`
   - envia `program_select_1` o `program_select_2`
5. Limpieza de estado local
   - `ResetTransientSessionState()`
6. Aplicacion de perfil
   - `ApplyProgramProfile(program)`
7. Estado local sincronizado
   - `_syncedFirmwareProgram = program`

Confirmaciones:

- No se envia automaticamente `STOP`
- No se envia automaticamente `OFF ALL`
- No se envia automaticamente `RESET`
- No se envia automaticamente `INIT`
- Si se envia automaticamente:
  - `program_select_1` o `program_select_2`
- No hay evidencia de `program_status` automatico al cambiar programa
- No hay evidencia de `status` automatico al cambiar programa
- No hay evidencia de CAN crudo automatico al cambiar programa

Evidencia:

- `CabezalDashboardTarjetas.razor:3185-3215`
- `CabezalDashboardTarjetas.razor:3217-3236`
- `CabezalDashboardTarjetas.razor:3057-3154`

## 6. Recepcion de respuestas del firmware

Punto de entrada:

- `app_windows/AcuratexControlApp/ConnectionController.cs:328-332`
  - `LineReceived?.Invoke(line);`
- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor:8703-8951`
  - recibe `_lastRx`, hace `AddTrace("RX", _lastRx)`, parsea y actualiza UI

### Tabla de respuestas

| RESPUESTA | PARSER APP | CAMBIA UI | CAMBIA ESTADO LOCAL | SOLO LOG | ARCHIVO:LINEA |
|---|---|---:|---:|---:|---|
| `CAN_RX|...` | `TryParseCanRxLine` | Si | Si | No | `CabezalDashboardTarjetas.razor:8719-8779`, `11210-12138` |
| `427|...|420` | `HeadStateEventParser.TryParse` | Parcial | Parcial | No | `HeadStateEventParser.cs:38-76`, `CabezalDashboardTarjetas.razor:8793-9071` |
| `INIT|...` | comparacion directa `StartsWith("INIT|")` | Si | Si | No | `CabezalDashboardTarjetas.razor:8809-8847` |
| `TESTEO|...` | comparacion directa `StartsWith("TESTEO|")` | Si | Si | No | `CabezalDashboardTarjetas.razor:8809-8847` |
| `OK` / `OK ...` | comparacion directa | Si | Si | No | `CabezalDashboardTarjetas.razor:8871-8903` |
| `ERR|HEAD_ACTION...` | comparacion directa | Si | Si | No | `CabezalDashboardTarjetas.razor:8911-8919` |
| `OK|HEAD_ACTION|...` | comparacion directa | Si | Si | No | `CabezalDashboardTarjetas.razor:8927-8935` |
| `PROGRAM_STATE|ACTIVE=1/2` | No se encontro parser activo en dashboard modular | No confirmado | No confirmado | Si/no usado | no encontrado en `CabezalDashboardTarjetas.razor` |
| `STATUS ...` | se muestra como texto general si llega por RX | Minimo | Minimo | Mayormente si | `CabezalDashboardTarjetas.razor:8871-8903` |
| `TX_OK ...` | no tiene parser dedicado | No especifico | No especifico | Si, via traza RX | no handler dedicado |
| `ERR unknown command` | no parser dedicado | No especifico | No especifico | Si, via traza RX | no handler dedicado |

### Observaciones criticas

- La app no usa activamente `program_status` para confirmar programa activo.
- El firmware reconoce `program_status`, pero el dashboard modular auditado no muestra un flujo que lo consuma.
- El estado visual de DEN y SIC depende de estado local, no del `427|...|420`:
  - `CabezalDashboardTarjetas.razor:9175`
  - `CabezalDashboardTarjetas.razor:9232`
  - `CabezalDashboardTarjetas.razor:9585`
  - `CabezalDashboardTarjetas.razor:9642`
- J si actualiza registro local desde `427|Jx|CH|...|420`:
  - `CabezalDashboardTarjetas.razor:9401-9433`
- Hay riesgo real de desincronizacion UI/firmware en DEN y SIC porque la UI privilegia estado local.

## 7. Parser CAN_RX, alarmas y testeo

### Parser de CAN_RX

- `CabezalDashboardTarjetas.razor:11210-12138`

Formato esperado:

`CAN_RX|BUS=CAN1|ID=0x700|DLC=1|DATA=CB`

Validaciones:

- prefijo `CAN_RX|`
- `BUS` debe ser `CAN1` o `CAN2`
- `ID` hexadecimal estandar `0x000..0x7FF`
- `DLC` entre `0` y `8`
- `DATA` debe tener exactamente `DLC` bytes hex separados por espacios

### Alarmas CAN

- `CanAlarmDetector` y `ProcessCanAlarm(frame)`
- Evidencia:
  - `CanAlarmMonitoring.cs:26-145`
  - `CabezalDashboardTarjetas.razor:10747-11029`

Comportamiento:

- Detecta alarma y la pone en modal activo o cola de pendientes.
- Activa `_alarmHaltActive`.
- Registra historial y actividad.
- `CHECK` envia `320 07` para diagnostico:
  - `CabezalDashboardTarjetas.razor:11041-11049`

### TESTEO

- `HeadTestResultDetector`:
  - `CanAlarmMonitoring.cs:251-632`
- consumo en UI:
  - `CabezalDashboardTarjetas.razor:11052-11098`

Comportamiento:

- Observa respuestas CAN `0x700` y `0x702`.
- Reconoce:
  - `0xCB` -> completo
  - `0xBC` -> falta expansion
  - `0xBF` -> faltan placas de fuerza segun patron A1/A2
- La UI guarda detalle, estado y mensaje final.

## 8. Inventario total de acciones del dashboard

### Sistema

| MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN DEL COMANDO | COMANDO EXACTO |
|---|---|---|---|---|---|
| Sistema | Programa 1 | `@onclick` | `SelectProgramAsync(Program1)` | literal en `.razor` | `program_select_1` |
| Sistema | Programa 2 | `@onclick` | `SelectProgramAsync(Program2)` | literal en `.razor` | `program_select_2` |
| Sistema | INIT | `@onclick` | `SendInitAsync` | literal en `.razor` | `init` |
| Sistema | TESTEO | `@onclick` | `SendTesteoAsync` | literal en `.razor` | `testeo` |
| Sistema | STATUS | `@onclick` | `SendStatusAsync` | literal en `.razor` | `status` |
| Sistema | CHECK | `@onclick` | `SendCanAlarmCheckAsync` | literal en `.razor` | `320 07` |
| Sistema | OFF ALL | `@onclick` | `OffAllAsync` | secuencia generada | multiples `j_set_n|255` + `yarn_pin_n|p|0` + `stitch_pin_n|p|0` |
| Sistema | ON ALL | `@onclick` | `OnAllAsync` | secuencia generada | multiples `j_set_n|0` + `yarn_pin_n|p|1` + `stitch_pin_n|p|1` |
| Sistema | Emergencia | rutina interna | `HandleEmergencyStopAsync` | literales + secuencia | `j_stop_all`, `y_stop_all`, `s_stop_all` + OFF ALL local |
| Sistema | Limpiar consola | boton UI | JS/UI | no envia | no envia comando |
| Sistema | Abrir historial CAN RX | boton UI | `OpenCanRxHistoryAsync` | no envia | no envia comando |
| Sistema | Abrir historial alarmas | boton UI | `OpenCanAlarmHistoryAsync` | no envia | no envia comando |
| Sistema | Detalle testeo | boton UI | `OpenHeadTestDetailAsync` | no envia | no envia comando |
| Sistema | SEND CAN | `@onclick` | `SendManualCanAsync` | comando manual | linea exacta escrita por usuario |
| Sistema | SEND DO | `@onclick` | `SendManualDoAsync` | comando manual | comando exacto escrito por usuario |

### DEN

| MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN | COMANDO EXACTO |
|---|---|---|---|---|---|
| DEN | POS1/POS2/POS3/POS4/POS5 | click | `SetDenPositionAsync` | `FastDashboardCommandService` | `den_select_{motor}|{pos}` |
| DEN | Slider | change | `SetDenSliderAsync` | `FastDashboardCommandService` | `den_pos_{motor}|{value}` |
| DEN | RUN | click | `StartDenRunAsync` | literal en `.razor` | `den_run_{motor}` |
| DEN | RUN1 | click | `StartDenRun1Async` | literal en `.razor` | `den_run1_{motor}` |
| DEN | STOP | click | `StopDenRunAsync` | literal en `.razor` | `den_stop_{motor}` o `den_stop1_{motor}` |

### SIC

| MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN | COMANDO EXACTO |
|---|---|---|---|---|---|
| SIC | POS1/POS2/POS3 | click | `SetSicPositionAsync` | `FastDashboardCommandService` | `sic_select_{sic}|{pos}` |
| SIC | Slider | change | `SetSicSliderAsync` | `FastDashboardCommandService` | `sic_pos_{sic}|{value}` |
| SIC | RUN | click | `StartSicRunAsync` | literal en `.razor` | `sic_run_{sic}` |
| SIC | STOP | click | `StopSicRunAsync` | literal en `.razor` | `sic_stop_{sic}` |

### Feet

| MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN | COMANDO EXACTO |
|---|---|---|---|---|---|
| Feet | POS1/POS2 | click | `SetFeetPositionAsync` | literal en `.razor` | `feet_select_{feet}|{pos}` |
| Feet | Slider | change | `SetFeetSliderAsync` | literal en `.razor` | `feet_pos_{feet}|{value}` |
| Feet | RUN | click | `StartFeetRunAsync` | literal en `.razor` | `feet_run_{feet}` |
| Feet | STOP | click | `StopFeetRunAsync` | literal en `.razor` | `feet_stop_{feet}` |

Nota:

- `Feet` solo existe visualmente en Programa 2.
- No hay evidencia de `POS3` en `Feet`.

### J

| MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN | COMANDO EXACTO |
|---|---|---|---|---|---|
| J | J1..J8 pin individual | click | `ToggleJPinAsync` | `FastDashboardCommandService` | `j_set_{grupo}|{valorRegistro}` |
| J | ON ALL por grupo | click | `SetJAllAsync(group, true)` | `FastDashboardCommandService` | `j_set_{grupo}|0` |
| J | OFF ALL por grupo | click | `SetJAllAsync(group, false)` | `FastDashboardCommandService` | `j_set_{grupo}|255` |
| J | RUN por grupo | click | `SendDoCommandAsync(...)` | literal en `.razor` | `j_run_{grupo}` |
| J | STOP por grupo | click | `SendDoCommandAsync(...)` | literal en `.razor` | `j_stop_{grupo}` |
| J | RUN ALL (J) | click | `RunAllJAsync` | literal en `.razor` | `j_run_all` |
| J | STOP ALL (J) | click | `StopAllJAsync` | literal en `.razor` | `j_stop_all` |

### Yarn

| MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN | COMANDO EXACTO |
|---|---|---|---|---|---|
| Yarn | pin individual | click | `ToggleBlockPinAsync` | `FastDashboardCommandService` | `yarn_pin_{instancia}|{pin}|{0|1}` |
| Yarn | ON ALL por bloque | click | `SetBlockAllAsync(..., true)` | `FastDashboardCommandService` | `yarn_pin_{instancia}|{pin}|1` por cada pin |
| Yarn | OFF ALL por bloque | click | `SetBlockAllAsync(..., false)` | `FastDashboardCommandService` | `yarn_pin_{instancia}|{pin}|0` por cada pin |
| Yarn | RUN bloque | click | `SendDoCommandAsync` | metadata en Models + literal | `y1_run` o `y2_run` |
| Yarn | STOP bloque | click | `SendDoCommandAsync` | metadata en Models + literal | `y1_stop` o `y2_stop` |
| Yarn | RUN ALL | click | `RunAllYarnAsync` | literal en `.razor` | `y_run_all` |
| Yarn | STOP ALL | click | `StopAllYarnAsync` | literal en `.razor` | `y_stop_all` |

### Stitch

| MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN | COMANDO EXACTO |
|---|---|---|---|---|---|
| Stitch | pin individual | click | `ToggleBlockPinAsync` | `FastDashboardCommandService` | `stitch_pin_{instancia}|{pin}|{0|1}` |
| Stitch | ON ALL por bloque | click | `SetBlockAllAsync(..., true)` | `FastDashboardCommandService` | `stitch_pin_{instancia}|{pin}|1` por cada pin |
| Stitch | OFF ALL por bloque | click | `SetBlockAllAsync(..., false)` | `FastDashboardCommandService` | `stitch_pin_{instancia}|{pin}|0` por cada pin |
| Stitch | RUN bloque | click | `SendDoCommandAsync` | metadata en Models + literal | `s_run_{instancia}` |
| Stitch | STOP bloque | click | `SendDoCommandAsync` | metadata en Models + literal | `s_stop_{instancia}` |
| Stitch | RUN ALL | click | `RunAllStitchAsync` | literal en `.razor` | `s_run_all` |
| Stitch | STOP ALL | click | `StopAllStitchAsync` | literal en `.razor` | `s_stop_all` |

### CAN manual

| MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN | COMANDO EXACTO |
|---|---|---|---|---|---|
| CAN manual | SEND CAN | click | `SendManualCanAsync` | entrada manual | linea cruda exacta del textbox |
| CAN manual | validacion RX | recepcion | `TryParseCanRxLine` | parser UI | `CAN_RX|BUS=...|ID=...|DLC=...|DATA=...` |

## 9. Tabla obligatoria boton por boton

Formato: `PROGRAMA | MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN DEL COMANDO | COMANDO EXACTO | PARAMETROS | SERVICIO | ENVIA USB | ARCHIVO:LINEA`

| PROGRAMA | MODULO | CONTROL/BOTON | EVENTO C# | METODO | ORIGEN DEL COMANDO | COMANDO EXACTO | PARAMETROS | SERVICIO | ENVIA USB | ARCHIVO:LINEA |
|---|---|---|---|---|---|---|---|---|---|---|
| Global | Sistema | Programa 1 | `@onclick` | `SelectProgramAsync` | literal en `.razor` | `program_select_1` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:122-131,3217-3236` |
| Global | Sistema | Programa 2 | `@onclick` | `SelectProgramAsync` | literal en `.razor` | `program_select_2` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:122-131,3217-3236` |
| Global | Sistema | INIT | `@onclick` | `SendInitAsync` | literal en `.razor` | `init` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:325,3238-3350` |
| Global | Sistema | TESTEO | `@onclick` | `SendTesteoAsync` | literal en `.razor` | `testeo` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:296,3518-3553` |
| Global | Sistema | STATUS | `@onclick` | `SendStatusAsync` | literal en `.razor` | `status` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:349,3470-3502` |
| Global | Sistema | CHECK | `@onclick` | `SendCanAlarmCheckAsync` | literal en `.razor` | `320 07` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:270,11041-11049` |
| Global | Sistema | OFF ALL | `@onclick` | `OffAllAsync` | generado dinamicamente | `j_set_{j}|255` + `yarn_pin_{n}|{p}|0` + `stitch_pin_{n}|{p}|0` | J/grupo/pin | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:333,6693-6851` |
| Global | Sistema | ON ALL | `@onclick` | `OnAllAsync` | generado dinamicamente | `j_set_{j}|0` + `yarn_pin_{n}|{p}|1` + `stitch_pin_{n}|{p}|1` | J/grupo/pin | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:341,6867-6995` |
| Global | Sistema | RUN ALL (J) | `@onclick` | `RunAllJAsync` | literal en `.razor` | `j_run_all` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:365,3570-3595` |
| Global | Sistema | STOP ALL (J) | `@onclick` | `StopAllJAsync` | literal en `.razor` | `j_stop_all` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:373,3611-3643` |
| Global | Sistema | RUN ALL Yarn | `@onclick` | `RunAllYarnAsync` | literal en `.razor` | `y_run_all` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:389,3659-3691` |
| Global | Sistema | STOP ALL Yarn | `@onclick` | `StopAllYarnAsync` | literal en `.razor` | `y_stop_all` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:397,3707-3739` |
| Global | Sistema | RUN ALL Stitch | `@onclick` | `RunAllStitchAsync` | literal en `.razor` | `s_run_all` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:413,3755-3787` |
| Global | Sistema | STOP ALL Stitch | `@onclick` | `StopAllStitchAsync` | literal en `.razor` | `s_stop_all` | ninguno | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:421,3803-3835` |
| Global | Sistema | SEND CAN | `@onclick` | `SendManualCanAsync` | comando CAN manual | linea exacta del usuario | libre | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:893,3999-4035` |
| Global | Sistema | SEND DO | `@onclick` | `SendManualDoAsync` | comando textual manual | texto exacto del usuario | libre | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:957,4047-4143` |
| P1/P2 | DEN | POSx | click | `SetDenPositionAsync` | `FastDashboardCommandService` | `den_select_{motor}|{pos}` | `motor`, `pos` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:5242-5263`, `FastDashboardCommandService.cs:31-34` |
| P1/P2 | DEN | Slider | change | `SetDenSliderAsync` | `FastDashboardCommandService` | `den_pos_{motor}|{value}` | `motor`, `value` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:5287-5345`, `FastDashboardCommandService.cs:31-34` |
| P1/P2 | DEN | RUN | click | `StartDenRunAsync` | literal en `.razor` | `den_run_{motor}` | `motor` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:5369-5425` |
| P1/P2 | DEN | RUN1 | click | `StartDenRun1Async` | literal en `.razor` | `den_run1_{motor}` | `motor` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:5449-5505` |
| P1/P2 | DEN | STOP | click | `StopDenRunAsync` | literal en `.razor` | `den_stop_{motor}` o `den_stop1_{motor}` | `motor` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:5529-5593` |
| P1/P2 | SIC | POSx | click | `SetSicPositionAsync` | `FastDashboardCommandService` | `sic_select_{sic}|{pos}` | `sic`, `pos` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:5661-5686`, `FastDashboardCommandService.cs:43-46` |
| P1/P2 | SIC | Slider | change | `SetSicSliderAsync` | `FastDashboardCommandService` | `sic_pos_{sic}|{value}` | `sic`, `value` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:5710-5768`, `FastDashboardCommandService.cs:43-46` |
| P1/P2 | SIC | RUN | click | `StartSicRunAsync` | literal en `.razor` | `sic_run_{sic}` | `sic` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:5792-5843` |
| P1/P2 | SIC | STOP | click | `StopSicRunAsync` | literal en `.razor` | `sic_stop_{sic}` | `sic` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:5867-5907` |
| P2 | Feet | POSx | click | `SetFeetPositionAsync` | literal en `.razor` | `feet_select_{feet}|{pos}` | `feet`, `pos` | `FastDashboardCommandService` via texto directo | Si | `CabezalDashboardTarjetas.razor:5919-5999` |
| P2 | Feet | Slider | change | `SetFeetSliderAsync` | literal en `.razor` | `feet_pos_{feet}|{value}` | `feet`, `value` | `FastDashboardCommandService` via texto directo | Si | `CabezalDashboardTarjetas.razor:5919-5999` |
| P2 | Feet | RUN | click | `StartFeetRunAsync` | literal en `.razor` | `feet_run_{feet}` | `feet` | `FastDashboardCommandService` via texto directo | Si | `CabezalDashboardTarjetas.razor:5919-5999` |
| P2 | Feet | STOP | click | `StopFeetRunAsync` | literal en `.razor` | `feet_stop_{feet}` | `feet` | `FastDashboardCommandService` via texto directo | Si | `CabezalDashboardTarjetas.razor:5919-5999` |
| P1/P2 | J | Pin individual | click | `ToggleJPinAsync` | generado dinamicamente | `j_set {j} {valor}` conceptual, envio real `j_set_{j}|{valor}` | `j`, `valorRegistro` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:1486,6062-6206`, `FastDashboardCommandService.cs:49-52` |
| P1/P2 | J | ON ALL grupo | click | `SetJAllAsync` | generado dinamicamente | `j_set_{j}|0` | `j` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:1374,6222-6414` |
| P1/P2 | J | OFF ALL grupo | click | `SetJAllAsync` | generado dinamicamente | `j_set_{j}|255` | `j` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:1366,6222-6414` |
| P1/P2 | J | RUN grupo | click | `SendDoCommandAsync` | literal en `.razor` | `j_run_{j}` | `j` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:1398-1406` |
| P1/P2 | J | STOP grupo | click | `SendDoCommandAsync` | literal en `.razor` | `j_stop_{j}` | `j` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:1398-1406` |
| P1/P2 | Yarn | pin individual | click | `ToggleBlockPinAsync` | generado dinamicamente | `yarn_pin_{instancia}|{pin}|{0|1}` | `instancia`, `pin`, `estado` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:6430-6537`, `FastDashboardCommandService.cs:66-93` |
| P1/P2 | Yarn | ON ALL bloque | click | `SetBlockAllAsync` | generado dinamicamente | `yarn_pin_{instancia}|{pin}|1` | `instancia`, `pin` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:6553-6670` |
| P1/P2 | Yarn | OFF ALL bloque | click | `SetBlockAllAsync` | generado dinamicamente | `yarn_pin_{instancia}|{pin}|0` | `instancia`, `pin` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:6553-6670` |
| P1/P2 | Yarn | RUN bloque | click | `SendDoCommandAsync` | metadata en Models | `y1_run` o `y2_run` | `instancia` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetasModels.cs:904-917`, `CabezalDashboardTarjetas.razor:1606-1646` |
| P1/P2 | Yarn | STOP bloque | click | `SendDoCommandAsync` | metadata en Models | `y1_stop` o `y2_stop` | `instancia` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetasModels.cs:904-917`, `CabezalDashboardTarjetas.razor:1606-1646` |
| P1/P2 | Stitch | pin individual | click | `ToggleBlockPinAsync` | generado dinamicamente | `stitch_pin_{instancia}|{pin}|{0|1}` | `instancia`, `pin`, `estado` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:6430-6537`, `FastDashboardCommandService.cs:66-93` |
| P1/P2 | Stitch | ON ALL bloque | click | `SetBlockAllAsync` | generado dinamicamente | `stitch_pin_{instancia}|{pin}|1` | `instancia`, `pin` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:6553-6670` |
| P1/P2 | Stitch | OFF ALL bloque | click | `SetBlockAllAsync` | generado dinamicamente | `stitch_pin_{instancia}|{pin}|0` | `instancia`, `pin` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetas.razor:6553-6670` |
| P1/P2 | Stitch | RUN bloque | click | `SendDoCommandAsync` | metadata en Models | `s_run_{instancia}` | `instancia` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetasModels.cs:919-940`, `CabezalDashboardTarjetas.razor:1726-1766` |
| P1/P2 | Stitch | STOP bloque | click | `SendDoCommandAsync` | metadata en Models | `s_stop_{instancia}` | `instancia` | `FastDashboardCommandService` | Si | `CabezalDashboardTarjetasModels.cs:919-940`, `CabezalDashboardTarjetas.razor:1726-1766` |
| Global | Sistema | Historial/Modales | click | varios | no envia comando | no envia | ninguno | ninguno | No | `CabezalDashboardTarjetas.razor:270-314,11100-11127` |

## 10. Matriz Programa 1 vs Programa 2

| MODULO | ACCION | COMANDO APP P1 | COMANDO APP P2 | IGUALES | DEFINICION SEPARADA | UBICACION |
|---|---|---|---|---|---|---|
| Sistema | INIT | `init` | `init` | Si | No | `.razor` |
| Sistema | TESTEO | `testeo` | `testeo` | Si | No | `.razor` |
| Sistema | STATUS | `status` | `status` | Si | No | `.razor` |
| Sistema | Program select | `program_select_1` | `program_select_2` | No | No, mismo metodo | `.razor` |
| DEN | POSx | `den_select_{m}|{p}` | `den_select_{m}|{p}` | Si | No | `FastDashboardCommandService.cs` |
| DEN | Slider | `den_pos_{m}|{v}` | `den_pos_{m}|{v}` | Si | No | `FastDashboardCommandService.cs` |
| DEN | RUN | `den_run_{m}` | `den_run_{m}` | Si | No | `.razor` |
| DEN | RUN1 | `den_run1_{m}` | `den_run1_{m}` | Si | No | `.razor` |
| DEN | STOP | `den_stop_{m}` / `den_stop1_{m}` | igual | Si | No | `.razor` |
| SIC | POSx | `sic_select_{m}|{p}` | `sic_select_{m}|{p}` | Si | No | `FastDashboardCommandService.cs` |
| SIC | Slider | `sic_pos_{m}|{v}` | `sic_pos_{m}|{v}` | Si | No | `FastDashboardCommandService.cs` |
| SIC | RUN | `sic_run_{m}` | `sic_run_{m}` | Si | No | `.razor` |
| SIC | STOP | `sic_stop_{m}` | `sic_stop_{m}` | Si | No | `.razor` |
| Feet | POSx | no existe visualmente | `feet_select_{m}|{p}` | No | Si, solo P2 | `.razor` |
| Feet | Slider | no existe | `feet_pos_{m}|{v}` | No | Si, solo P2 | `.razor` |
| Feet | RUN | no existe | `feet_run_{m}` | No | Si, solo P2 | `.razor` |
| Feet | STOP | no existe | `feet_stop_{m}` | No | Si, solo P2 | `.razor` |
| J | Pin individual | `j_set_{j}|{v}` | `j_set_{j}|{v}` | Si | No | `FastDashboardCommandService.cs` |
| J | RUN/STOP | `j_run_*`, `j_stop_*` | iguales | Si | No | `.razor` |
| Yarn | Pin individual | `yarn_pin_{n}|{p}|{e}` | igual | Si | No | `FastDashboardCommandService.cs` |
| Yarn | RUN/STOP | `y1_run`, `y1_stop`, `y2_run`, `y2_stop` | iguales | Si | No | `Models.cs` + `.razor` |
| Stitch | Pin individual | `stitch_pin_{n}|{p}|{e}` | igual | Si | No | `FastDashboardCommandService.cs` |
| Stitch | RUN/STOP | `s_run_n`, `s_stop_n` | iguales | Si | No | `Models.cs` + `.razor` |
| ON ALL | Global | misma secuencia | misma secuencia | Si | No | `.razor` |
| OFF ALL | Global | misma secuencia | misma secuencia | Si | No | `.razor` |
| CAN manual | Manual | igual | igual | Si | No | `.razor` |

Clasificacion solicitada:

- A. Ambos programas envian el mismo comando textual, pero el firmware utiliza perfiles diferentes:
  - Si para casi todo DEN, SIC, J, Yarn, Stitch.
- B. La app envia comandos textuales diferentes segun el programa:
  - Solo en `program_select_1` vs `program_select_2`.
- C. La app contiene tramas CAN diferentes segun el programa:
  - No se encontraron tramas CAN directas distintas por programa en la UI activa.
- D. La app no distingue el programa para esa accion:
  - Si para INIT, TESTEO, STATUS, DEN, SIC, J, Yarn, Stitch.
- E. La accion solo existe en Programa 2:
  - Feet.

ConclusiÃ³n de independencia:

- Programa 1 y Programa 2 no son independientes a nivel de implementacion.
- Son perfiles separados, pero comparten:
  - catalogo de perfiles
  - modelos
  - fabricas de modulos
  - secuencias base
  - metodos de la vista
  - servicio de envio
  - parser de respuestas
- Diferencia funcional principal:
  - Programa 2 habilita `Feet`.

## 11. Comandos propiedad de la app

### 11.1 Comandos textuales semanticos

| COMANDO | ARCHIVO | LINEA | CONSUMIDOR | ACTIVO | PROGRAMA | INDEPENDENCIA |
|---|---|---:|---|---|---|---|
| `program_select_1` | `CabezalDashboardTarjetas.razor` | 3217 | seleccion de programa | Si | Global | comparte metodo con P2 |
| `program_select_2` | `CabezalDashboardTarjetas.razor` | 3217 | seleccion de programa | Si | Global | comparte metodo con P1 |
| `init` | `CabezalDashboardTarjetas.razor` | 3238 | boton INIT | Si | Global | global |
| `testeo` | `CabezalDashboardTarjetas.razor` | 3518 | boton TESTEO | Si | Global | global |
| `status` | `CabezalDashboardTarjetas.razor` | 3470 | boton STATUS | Si | Global | global |
| `j_run_all` | `CabezalDashboardTarjetas.razor` | 3570 | RUN ALL J | Si | P1/P2 | compartido |
| `j_stop_all` | `CabezalDashboardTarjetas.razor` | 3611 | STOP ALL J | Si | P1/P2 | compartido |
| `y_run_all` | `CabezalDashboardTarjetas.razor` | 3659 | RUN ALL Yarn | Si | P1/P2 | compartido |
| `y_stop_all` | `CabezalDashboardTarjetas.razor` | 3707 | STOP ALL Yarn | Si | P1/P2 | compartido |
| `s_run_all` | `CabezalDashboardTarjetas.razor` | 3755 | RUN ALL Stitch | Si | P1/P2 | compartido |
| `s_stop_all` | `CabezalDashboardTarjetas.razor` | 3803 | STOP ALL Stitch | Si | P1/P2 | compartido |
| `den_run_n` | `CabezalDashboardTarjetas.razor` | 5369 | RUN DEN | Si | P1/P2 | compartido |
| `den_run1_n` | `CabezalDashboardTarjetas.razor` | 5449 | RUN1 DEN | Si | P1/P2 | compartido |
| `den_stop_n` | `CabezalDashboardTarjetas.razor` | 5529 | STOP DEN | Si | P1/P2 | compartido |
| `den_stop1_n` | `CabezalDashboardTarjetas.razor` | 5529 | STOP DEN RUN1 | Si | P1/P2 | compartido |
| `sic_run_n` | `CabezalDashboardTarjetas.razor` | 5792 | RUN SIC | Si | P1/P2 | compartido |
| `sic_stop_n` | `CabezalDashboardTarjetas.razor` | 5867 | STOP SIC | Si | P1/P2 | compartido |
| `feet_run_n` | `CabezalDashboardTarjetas.razor` | 5919 | RUN Feet | Si | P2 | comparte loop con SIC |
| `feet_stop_n` | `CabezalDashboardTarjetas.razor` | 5919 | STOP Feet | Si | P2 | comparte loop con SIC |
| `y1_run`, `y1_stop`, `y2_run`, `y2_stop` | `CabezalDashboardTarjetasModels.cs` | 904 | bloques Yarn | Si | P1/P2 | compartido |
| `s_run_1..4`, `s_stop_1..4` | `CabezalDashboardTarjetasModels.cs` | 919 | bloques Stitch | Si | P1/P2 | compartido |

### 11.2 Comandos construidos dinamicamente

| COMANDO | ARCHIVO | LINEA | CONSUMIDOR | ACTIVO | PROGRAMA | INDEPENDENCIA |
|---|---|---:|---|---|---|---|
| `den_select_{motor}|{pos}` | `FastDashboardCommandService.cs` | 31 | DEN POS | Si | P1/P2 | compartido |
| `den_pos_{motor}|{value}` | `FastDashboardCommandService.cs` | 31 | DEN slider | Si | P1/P2 | compartido |
| `sic_select_{sic}|{pos}` | `FastDashboardCommandService.cs` | 43 | SIC POS | Si | P1/P2 | compartido |
| `sic_pos_{sic}|{value}` | `FastDashboardCommandService.cs` | 43 | SIC slider | Si | P1/P2 | compartido |
| `j_set_{j}|{value}` | `FastDashboardCommandService.cs` | 51 | J pin / all | Si | P1/P2 | compartido |
| `j_ch_{j}_{channel}` | `FastDashboardCommandService.cs` | 62 | canal J directo | Si, pero no boton directo visible principal | P1/P2 | compartido |
| `yarn_pin_{instancia}|{pin}|{estado}` | `FastDashboardCommandService.cs` | 92 | Yarn pin | Si | P1/P2 | compartido |
| `stitch_pin_{instancia}|{pin}|{estado}` | `FastDashboardCommandService.cs` | 92 | Stitch pin | Si | P1/P2 | compartido |
| `feet_select_{n}|{p}` | `CabezalDashboardTarjetas.razor` | 5919 | Feet POS | Si | P2 | comparte implementacion general |
| `feet_pos_{n}|{v}` | `CabezalDashboardTarjetas.razor` | 5919 | Feet slider | Si | P2 | comparte implementacion general |

### 11.3 Tramas CAN crudas almacenadas en C#

| COMANDO | ARCHIVO | LINEA | CONSUMIDOR | ACTIVO | PROGRAMA | INDEPENDENCIA |
|---|---|---:|---|---|---|---|
| `320 07` | `CabezalDashboardTarjetas.razor` | 11041 | CHECK / alarmas | Si | Global | global |
| `320 1C {motorIndex} {low} {high}` | `CabezalDashboardTarjetasModels.cs` | 944 | traza local de posicion | Si como traza, no envio directo principal | P1/P2 | compartido |
| `320 1D {jIndex-1} {value}` | `CabezalDashboardTarjetasModels.cs` | 951 | traza local de J | Si como traza, no envio USB directo principal | P1/P2 | compartido |
| `320 1E {address} {01|00}` | `CabezalDashboardTarjetasModels.cs` | 958 | traza local de bloques | Si como traza, no envio USB directo principal | P1/P2 | compartido |

### 11.4 Secuencias temporizadas en la app

| COMANDO/SECUENCIA | ARCHIVO | LINEA | CONSUMIDOR | ACTIVO | PROGRAMA | INDEPENDENCIA |
|---|---|---:|---|---|---|---|
| secuencia DEN RUN `{1,3,5,2,4}` | `Models.cs` / `.razor` | 831 / 8084 | animacion DEN | Si | P1/P2 | compartida |
| secuencia DEN RUN1 `{1,3,5}` | `Models.cs` / `.razor` | 831 / 8084 | animacion DEN1 | Si | P1/P2 | compartida |
| secuencia SIC RUN `{1,2,3}` | `Models.cs` / `.razor` | 831 / 8276 | animacion SIC | Si | P1/P2 | compartida |
| secuencia Feet RUN `{1,2}` | `Program2Commands.cs` / `.razor` | 3 / 6006 | animacion Feet | Si | P2 | depende de loop SIC |
| animacion J 1..8 con delay `120ms` | `CabezalDashboardTarjetas.razor` | 12714 | animacion J | Si | P1/P2 | compartida |

### 11.5 Comandos heredados o antiguos

| COMANDO | ARCHIVO | LINEA | CONSUMIDOR | ACTIVO | PROGRAMA | INDEPENDENCIA |
|---|---|---:|---|---|---|---|
| `HEAD_STATUS` | `CabezalDashboardTarjetasCommandService.cs` | 143-145 | servicio legacy | No en shell modular activa | legacy | ruta separada |
| `HEAD_STOP` | `CabezalDashboardTarjetasCommandService.cs` | 148-150 | servicio legacy | No en shell modular activa | legacy | ruta separada |
| `HEAD_ACTION/INIT` | `CabezalDashboardTarjetasCommandService.cs` | 138-140,447-459 | servicio legacy + firmware runner | No en shell modular activa | legacy | depende de scripts |
| `OK|HEAD_ACTION|...` | `command_head_program_runner.cpp` | 2094-2118 | firmware runner legacy | no ruta principal modular | legacy | ruta separada |
| `OK|HEAD_PROGRAM_SELECT|...` | `command_head_program_runner.cpp` | 2885-2952 | firmware runner legacy | no ruta principal modular | legacy | ruta separada |

## 12. Rutinas RUN y animaciones locales

| RUTINA | PROGRAMA | SE EJECUTA EN APP/FIRMWARE | COMANDOS ENVIADOS | DELAYS | CANCELACION | RIESGO |
|---|---|---|---|---|---|---|
| `RunDenLoopAsync` | P1/P2 | App | ninguno dentro del loop; el arranque envia `den_run_n` | `80ms` o `300ms` segun modo | `CancellationTokenSource` por motor | UI puede seguir animando localmente sin confirmacion de firmware |
| `RunSicLoopAsync` | P1/P2 | App | ninguno dentro del loop; el arranque envia `sic_run_n` | `300ms` | `CancellationTokenSource` por motor | estado visual local privilegiado |
| `StartFeetLoopAsync -> RunSicLoopAsync` | P2 | App | ninguno dentro del loop; el arranque envia `feet_run_n` | `FeetRunPeriodMs` | `CancellationTokenSource` por motor | dependencia oculta: Feet reutiliza loop SIC |
| `RunJAnimationLoopAsync` | P1/P2 | App | ninguno dentro del loop; `j_run_n` sale al inicio | `120ms` | `CancellationTokenSource` por J | animacion visual independiente del feedback firmware |
| `TESTEO` | Global | Firmware + recepcion app | `testeo` | timeout app `3000ms` aprox detector | `_headTestTimeoutCts` | depende de recepcion CAN valida |
| `INIT` | Global | Firmware + recepcion app | `init` | secuencia interna firmware | `_headInitBusy` y stop emergencia | UI depende de mensajes `INIT|...` |
| `OffAllCoreAsync` | P1/P2 | App + firmware | multiples `j_set`, `yarn_pin`, `stitch_pin` | sin delay explicito | sin CTS global | secuencia parcial si falla un envio intermedio |
| `OnAllCoreAsync` | P1/P2 | App + firmware | multiples `j_set`, `yarn_pin`, `stitch_pin` | sin delay explicito | sin CTS global | secuencia parcial si falla un envio intermedio |

Observaciones:

- Que RUN se ejecuta fisicamente en firmware:
  - `den_run_*`, `den_run1_*`, `sic_run_*`, `feet_run_*`, `j_run_*`, `y_run_*`, `s_run_*`
- Que RUN es secuencia generada por la app:
  - animaciones visuales DEN, SIC, Feet, J
- Botones que solo cambian color localmente durante animacion:
  - varios J, DEN, SIC, Feet segun `RunMode`, `Register` y `States`
- Si al cambiar de programa se cancelan:
  - si, `ResetTransientSessionState()` cancela rutinas locales
- Si una rutina captura el programa al comenzar:
  - usa `CurrentProgramProfile`; el riesgo principal es mas de desincronizacion visual que de reenvio de comandos continuos, porque los loops locales no envian nuevos comandos en cada tick.

## 13. Comparacion con el parser real del firmware

Archivo principal inspeccionado:

- `UsbSmokeIdf/main/command_processor.cpp`

Comparacion:

| COMANDO APP | RECONOCIDO POR FIRMWARE | HANDLER | PARAMETROS VALIDOS | RESPUESTA | EVIDENCIA |
|---|---|---|---|---|---|
| `program_select_1` | Si | `app_handle_program_select_command` | fijo | `OK program_select_1` o `ERR PROGRAM_BUSY` | `command_processor.cpp:1136-1167,1373-1626` |
| `program_select_2` | Si | `app_handle_program_select_command` | fijo | `OK program_select_2` o `ERR PROGRAM_BUSY` | idem |
| `program_status` | Si | inline `app_command_process_line` | ninguno | `PROGRAM_STATE|ACTIVE=1/2` | `command_processor.cpp` linea de `program_status` |
| `status` | Si | `app_send_status` | ninguno | `STATUS usb=... wifi=... CAN=...` | `command_processor.cpp:318-333,1307-1626` |
| `init` | Si | `app_head_fast_diag_start_init` | ninguno | `OK init`, `INIT|...`, `ERR|INIT|BUSY` | `command_processor.cpp`, `head_fast_diag.cpp:757-919` |
| `testeo` | Si | `app_head_fast_diag_start_testeo` | ninguno | `OK testeo`, `TESTEO|...`, `ERR|TESTEO|...` | `command_processor.cpp`, `head_fast_diag.cpp:549-703` |
| `j_run_all` | Si | `app_handle_j_short_command` | ninguno | `OK j_run_all` | `command_processor.cpp:797-853` |
| `j_stop_all` | Si | `app_handle_j_short_command` | ninguno | `OK j_stop_all` | idem |
| `j_run_n` | Si | `app_handle_j_short_command` | `1..APP_HEAD_STATE_MAX_J` | `OK j_run_n` | idem |
| `j_stop_n` | Si | `app_handle_j_short_command` | `1..APP_HEAD_STATE_MAX_J` | `OK j_stop_n` | idem |
| `j_set_n|v` | Si | `app_handle_j_output_command` | indice y byte | `TX_OK` o `ERR|CAN_TX...` | `command_processor.cpp:664-714` |
| `j_ch_n_m` | Si | `app_handle_j_output_command` | indices validos | `TX_OK` o error | idem |
| `y_run_all` | Si | `app_handle_yarn_short_command` | ninguno | `OK y_run_all` | `command_processor.cpp:861-918` |
| `y_stop_all` | Si | `app_handle_yarn_short_command` | ninguno | `OK y_stop_all` | idem |
| `y1_run`, `y1_stop`, `y2_run`, `y2_stop` | Si | `app_handle_yarn_short_command` | fijo | `OK ...` | idem |
| `s_run_all` | Si | `app_handle_stitch_short_command` | ninguno | `OK s_run_all` | `command_processor.cpp:921-977` |
| `s_stop_all` | Si | `app_handle_stitch_short_command` | ninguno | `OK s_stop_all` | idem |
| `s_run_n`, `s_stop_n` | Si | `app_handle_stitch_short_command` | indice valido | `OK ...` | idem |
| `den_run_n` | Si | `app_handle_den_short_command` | indice valido | `OK ...` | `command_processor.cpp:980-1049` |
| `den_run1_n` | Si | `app_handle_den_short_command` | indice valido | `OK ...` | idem |
| `den_stop_n`, `den_stop1_n` | Si | `app_handle_den_short_command` | indice valido | `OK ...` | idem |
| `sic_run_n`, `sic_stop_n` | Si | `app_handle_sic_short_command` | indice valido | `OK ...` | `command_processor.cpp:1052-1091` |
| `feet_run_n`, `feet_stop_n` | Si | `app_handle_feet_short_command` | indice valido | `OK ...` | `command_processor.cpp:1094-1128` |
| `den_select_n|p` | Si | `app_handle_profile_position_selection` | posicion valida de perfil | `TX_OK` o error | `command_processor.cpp:595-627` |
| `sic_select_n|p` | Si | `app_handle_profile_position_selection` | posicion valida de perfil | `TX_OK` o error | idem |
| `feet_select_n|p` | Si | `app_handle_profile_position_selection` | posicion valida de perfil | `TX_OK` o error | idem |
| `den_pos_n|v` | Si | `app_handle_short_position_command` | `value` valido | `TX_OK` o error | `command_processor.cpp:565-593` |
| `sic_pos_n|v` | Si | `app_handle_short_position_command` | `value` valido | `TX_OK` o error | idem |
| `feet_pos_n|v` | Si | `app_handle_short_position_command` | `value` valido | `TX_OK` o error | idem |
| `yarn_pin_n|p|e` | Si | `app_handle_cascade_pin_command` | instancia, pin, estado | `TX_OK` o error | `command_processor.cpp:717-766` |
| `stitch_pin_n|p|e` | Si | `app_handle_cascade_pin_command` | instancia, pin, estado | `TX_OK` o error | idem |
| `320 07` | Si, como frame | `app_process_frame_command` | linea CAN valida | `TX_OK ...` o `ERR|CAN_TX|...` | `command_processor.cpp:419-450` |
| linea manual `send <hex>` o frame | Si | `app_process_frame_command` | trama valida | `TX_OK ...` o `ERR...` | `command_processor.cpp:1307-1626` |

Clasificacion:

- Conectado y reconocido:
  - todos los comandos activos del dashboard modular inventariados arriba
- Enviado por app pero no reconocido:
  - no se encontro uno activo y visible en el dashboard modular directo
- Reconocido por firmware pero nunca enviado por la app modular:
  - `program_status`
  - `can1`
  - `can2`
  - `start`
  - `stop`
  - `emergency_stop`
- Comando solo visual:
  - animaciones locales DEN/SIC/Feet/J
  - apertura de modales/historiales
- Comando antiguo:
  - `HEAD_STATUS`, `HEAD_STOP`, `HEAD_ACTION`, `HEAD_PROGRAM_SELECT` de la ruta legacy

## 14. Lista maestra unica de comandos

| NOMBRE O PLANTILLA | ORIGEN | PROGRAMA | MODULO | DISPARADOR | FORMATO EXACTO | PARAMETROS | DESTINO | FIRMWARE LO RECONOCE | HANDLER | RESPUESTA ESPERADA | ESTADO | INDEPENDENCIA |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `program_select_1` | `.razor` | Global | Sistema | boton Programa 1 | `program_select_1` | ninguno | firmware | Si | `app_handle_program_select_command` | `OK program_select_1` | ACTIVO Y CONECTADO | comparte logica con P2 |
| `program_select_2` | `.razor` | Global | Sistema | boton Programa 2 | `program_select_2` | ninguno | firmware | Si | `app_handle_program_select_command` | `OK program_select_2` | ACTIVO Y CONECTADO | comparte logica con P1 |
| `init` | `.razor` | Global | Sistema | INIT | `init` | ninguno | firmware | Si | `app_head_fast_diag_start_init` | `OK init`, `INIT|...` | ACTIVO Y CONECTADO | global |
| `testeo` | `.razor` | Global | Sistema | TESTEO | `testeo` | ninguno | firmware | Si | `app_head_fast_diag_start_testeo` | `OK testeo`, `TESTEO|...` | ACTIVO Y CONECTADO | global |
| `status` | `.razor` | Global | Sistema | STATUS | `status` | ninguno | firmware | Si | `app_send_status` | `STATUS ...` | ACTIVO Y CONECTADO | global |
| `den_select_{m}|{p}` | `FastDashboardCommandService` | P1/P2 | DEN | POS | plantilla | motor, posicion | firmware | Si | `app_handle_profile_position_selection` | `TX_OK` | ACTIVO Y CONECTADO | compartido |
| `den_pos_{m}|{v}` | `FastDashboardCommandService` | P1/P2 | DEN | slider | plantilla | motor, valor | firmware | Si | `app_handle_short_position_command` | `TX_OK` | ACTIVO Y CONECTADO | compartido |
| `den_run_{m}` | `.razor` | P1/P2 | DEN | RUN | plantilla | motor | firmware | Si | `app_handle_den_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `den_run1_{m}` | `.razor` | P1/P2 | DEN | RUN1 | plantilla | motor | firmware | Si | `app_handle_den_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `den_stop_{m}` | `.razor` | P1/P2 | DEN | STOP | plantilla | motor | firmware | Si | `app_handle_den_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `den_stop1_{m}` | `.razor` | P1/P2 | DEN | STOP RUN1 | plantilla | motor | firmware | Si | `app_handle_den_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `sic_select_{m}|{p}` | `FastDashboardCommandService` | P1/P2 | SIC | POS | plantilla | modulo, posicion | firmware | Si | `app_handle_profile_position_selection` | `TX_OK` | ACTIVO Y CONECTADO | compartido |
| `sic_pos_{m}|{v}` | `FastDashboardCommandService` | P1/P2 | SIC | slider | plantilla | modulo, valor | firmware | Si | `app_handle_short_position_command` | `TX_OK` | ACTIVO Y CONECTADO | compartido |
| `sic_run_{m}` | `.razor` | P1/P2 | SIC | RUN | plantilla | modulo | firmware | Si | `app_handle_sic_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `sic_stop_{m}` | `.razor` | P1/P2 | SIC | STOP | plantilla | modulo | firmware | Si | `app_handle_sic_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `feet_select_{m}|{p}` | `.razor` | P2 | Feet | POS | plantilla | modulo, posicion | firmware | Si | `app_handle_profile_position_selection` | `TX_OK` | ACTIVO Y CONECTADO | depende de P2 |
| `feet_pos_{m}|{v}` | `.razor` | P2 | Feet | slider | plantilla | modulo, valor | firmware | Si | `app_handle_short_position_command` | `TX_OK` | ACTIVO Y CONECTADO | depende de P2 |
| `feet_run_{m}` | `.razor` | P2 | Feet | RUN | plantilla | modulo | firmware | Si | `app_handle_feet_short_command` | `OK ...` | ACTIVO Y CONECTADO | comparte loop SIC |
| `feet_stop_{m}` | `.razor` | P2 | Feet | STOP | plantilla | modulo | firmware | Si | `app_handle_feet_short_command` | `OK ...` | ACTIVO Y CONECTADO | comparte loop SIC |
| `j_set_{j}|{v}` | `FastDashboardCommandService` | P1/P2 | J | pin/all | plantilla | grupo, registro | firmware | Si | `app_handle_j_output_command` | `TX_OK` | ACTIVO Y CONECTADO | compartido |
| `j_ch_{j}_{ch}` | `FastDashboardCommandService` | P1/P2 | J | canal directo | plantilla | grupo, canal | firmware | Si | `app_handle_j_output_command` | `TX_OK` | ACTIVO Y CONECTADO | compartido |
| `j_run_all` | `.razor` | P1/P2 | J | RUN ALL | fijo | ninguno | firmware | Si | `app_handle_j_short_command` | `OK j_run_all` | ACTIVO Y CONECTADO | compartido |
| `j_stop_all` | `.razor` | P1/P2 | J | STOP ALL | fijo | ninguno | firmware | Si | `app_handle_j_short_command` | `OK j_stop_all` | ACTIVO Y CONECTADO | compartido |
| `j_run_{j}` | `.razor` | P1/P2 | J | RUN grupo | plantilla | grupo | firmware | Si | `app_handle_j_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `j_stop_{j}` | `.razor` | P1/P2 | J | STOP grupo | plantilla | grupo | firmware | Si | `app_handle_j_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `yarn_pin_{n}|{p}|{e}` | `FastDashboardCommandService` | P1/P2 | Yarn | pin/all | plantilla | bloque, pin, estado | firmware | Si | `app_handle_cascade_pin_command` | `TX_OK` | ACTIVO Y CONECTADO | compartido |
| `y1_run` | `Models.cs` | P1/P2 | Yarn | RUN bloque | fijo | ninguno | firmware | Si | `app_handle_yarn_short_command` | `OK y1_run` | ACTIVO Y CONECTADO | compartido |
| `y1_stop` | `Models.cs` | P1/P2 | Yarn | STOP bloque | fijo | ninguno | firmware | Si | `app_handle_yarn_short_command` | `OK y1_stop` | ACTIVO Y CONECTADO | compartido |
| `y2_run` | `Models.cs` | P1/P2 | Yarn | RUN bloque | fijo | ninguno | firmware | Si | `app_handle_yarn_short_command` | `OK y2_run` | ACTIVO Y CONECTADO | compartido |
| `y2_stop` | `Models.cs` | P1/P2 | Yarn | STOP bloque | fijo | ninguno | firmware | Si | `app_handle_yarn_short_command` | `OK y2_stop` | ACTIVO Y CONECTADO | compartido |
| `y_run_all` | `.razor` | P1/P2 | Yarn | RUN ALL | fijo | ninguno | firmware | Si | `app_handle_yarn_short_command` | `OK y_run_all` | ACTIVO Y CONECTADO | compartido |
| `y_stop_all` | `.razor` | P1/P2 | Yarn | STOP ALL | fijo | ninguno | firmware | Si | `app_handle_yarn_short_command` | `OK y_stop_all` | ACTIVO Y CONECTADO | compartido |
| `stitch_pin_{n}|{p}|{e}` | `FastDashboardCommandService` | P1/P2 | Stitch | pin/all | plantilla | bloque, pin, estado | firmware | Si | `app_handle_cascade_pin_command` | `TX_OK` | ACTIVO Y CONECTADO | compartido |
| `s_run_{n}` | `Models.cs` | P1/P2 | Stitch | RUN bloque | plantilla | bloque | firmware | Si | `app_handle_stitch_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `s_stop_{n}` | `Models.cs` | P1/P2 | Stitch | STOP bloque | plantilla | bloque | firmware | Si | `app_handle_stitch_short_command` | `OK ...` | ACTIVO Y CONECTADO | compartido |
| `s_run_all` | `.razor` | P1/P2 | Stitch | RUN ALL | fijo | ninguno | firmware | Si | `app_handle_stitch_short_command` | `OK s_run_all` | ACTIVO Y CONECTADO | compartido |
| `s_stop_all` | `.razor` | P1/P2 | Stitch | STOP ALL | fijo | ninguno | firmware | Si | `app_handle_stitch_short_command` | `OK s_stop_all` | ACTIVO Y CONECTADO | compartido |
| `320 07` | `.razor` | Global | Alarma | CHECK | fijo | ninguno | firmware CAN | Si | `app_process_frame_command` | `TX_OK ...` | ACTIVO Y CONECTADO | global |
| `send <hex>` / frame manual | input usuario | Global | CAN manual | SEND CAN | libre | libres | firmware CAN | Si | `app_process_frame_command` | `TX_OK` o `ERR` | ACTIVO Y CONECTADO | global |
| `HEAD_STATUS` | servicio legacy | legacy | legacy | no boton activo modular | fijo | ninguno | firmware runner legacy | Si | `app_head_status` | `HEAD_STATUS|...` | LEGADO | ruta separada |
| `HEAD_STOP` | servicio legacy | legacy | legacy | no boton activo modular | fijo | ninguno | firmware runner legacy | Si | runner legacy | `OK/ERR` | LEGADO | ruta separada |

## 15. Comandos dispersos fuera de Program1/Program2

### COMANDOS DISPERSOS FUERA DE PROGRAM1/PROGRAM2

| COMANDO | UBICACION | ACTIVO | OBSERVACION FUTURA | INDEPENDENCIA |
|---|---|---|---|---|
| `program_select_n` | `CabezalDashboardTarjetas.razor` | Si | deberia seguir global porque sincroniza programa | depende de `_selectedProgram` y `SendTrackedAsync` |
| `init`, `testeo`, `status` | `CabezalDashboardTarjetas.razor` | Si | hoy globales; podrian variar por programa solo si firmware cambia contrato | globales |
| `feet_*` | `CabezalDashboardTarjetas.razor` | Si | hoy fuera de perfiles C# del servicio activo | comparte logica general y loop SIC |
| `j_run_*`, `j_stop_*`, `y_*`, `s_*`, `den_*`, `sic_*` | `CabezalDashboardTarjetas.razor` | Si | siguen dispersos como literales semanticos | comparten servicio y `SendTrackedAsync` |
| `den_select_*`, `sic_select_*`, `den_pos_*`, `sic_pos_*`, `j_set_*`, `j_ch_*`, `yarn_pin_*`, `stitch_pin_*` | `FastDashboardCommandService.cs` | Si | punto central de formateo real activo | compartidos |
| `320 07` | `CabezalDashboardTarjetas.razor` | Si | comando global de diagnostico | global |
| `HEAD_STATUS`, `HEAD_STOP`, `HEAD_ACTION` | `CabezalDashboardTarjetasCommandService.cs` | No en modular activo | permanecer como legado hasta decidir retiro | independientes de ruta activa |
| `HEAD_STATUS`, `HEAD_PROGRAM_SELECT`, `OK|HEAD_ACTION|...` | `command_head_program_runner.cpp` | No para dashboard modular activo | firmware legacy | independientes de protocolo corto actual |

## 16. Dependencias compartidas encontradas

### Dependencias fuertes

1. `SendTrackedAsync()` introduce dependencia transversal.
   - Antes de casi cualquier comando, intenta sincronizar `program_select_n`.
   - Evidencia: `CabezalDashboardTarjetas.razor:8460-8635`

2. Programa 1 y Programa 2 comparten fabricas y modelos.
   - Evidencia:
     - `Program1Commands.cs:3-20`
     - `Program2Commands.cs:3-20`
     - `ProgramProfiles.cs:9-42`
     - `Models.cs:831-972`

3. Feet reutiliza la logica de SIC.
   - `StartFeetLoopAsync()` llama `RunSicLoopAsync(...)`
   - Evidencia: `CabezalDashboardTarjetas.razor:6006-6044`

4. J pin individual no envia un comando de pin.
   - Reconstruye un byte completo y envia `j_set_{j}|{valorRegistro}`.
   - Evidencia:
     - `CabezalDashboardTarjetas.razor:6062-6206`
     - `FastDashboardCommandService.cs:49-52`

5. Yarn y Stitch comparten el mismo metodo de pin.
   - `SendBlockPinAsync` cambia solo prefijo `yarn` / `stitch`
   - Evidencia: `FastDashboardCommandService.cs:66-93`

6. DEN y SIC privilegian estado visual local por encima del telemetrico `427|...|420`.
   - Evidencia:
     - `CabezalDashboardTarjetas.razor:9175`
     - `CabezalDashboardTarjetas.razor:9232`
     - `CabezalDashboardTarjetas.razor:9585`
     - `CabezalDashboardTarjetas.razor:9642`

### Dependencias medias

1. `OnAllCoreAsync` y `OffAllCoreAsync` dependen de estado interno de J, Yarn y Stitch.
2. `ApplyLocalDoCommandAsync` mezcla ack de envio con cambios visuales locales.
3. La existencia de `FastDashboardCommandService` y `CabezalDashboardTarjetasCommandService` crea dos semanticas de comando en el mismo repo.

## 17. Deuda tecnica sin corregir

### Alta

- `SendTrackedAsync()` puede inyectar `program_select_n` antes de otro comando y acopla toda la matriz de envios a un estado global.
- DEN y SIC no confirman visualmente con `HEAD_STATE`; dependen de estado local y pueden desincronizarse.
- J pin individual usa `j_set_{j}|registroCompleto`, no un comando por pin; un bug de reconstruccion del registro mandaria un estado equivocado.

### Media

- Los comandos activos estan dispersos entre `.razor`, `FastDashboardCommandService.cs`, `Models.cs` y perfiles.
- Coexisten un servicio activo directo y un servicio legacy reinterpretador.
- Feet solo existe en P2, pero comparte implementacion con SIC; esto oculta dependencia funcional.

### Baja

- Hay codigo inalcanzable en `CabezalDashboardTarjetas.razor` cerca de `ApplyPositionStateEvent` y `ApplyRunStateEvent`.
- Campos no usados:
  - `_selectedHeadProgramFile`
  - `_isHeadProgramBusy`
- Permanecen rutas legacy de firmware y app no conectadas al dashboard modular activo.

### Partes que hoy funcionan y pueden mantenerse sin migracion inmediata

- `FastDashboardCommandService` como emisor directo del protocolo corto actual.
- `program_select_n` + protocolo corto (`den_*`, `sic_*`, `j_*`, `y_*`, `s_*`, `feet_*`).
- detector de `CAN_RX` para alarmas y testeo.
- perfiles P1/P2 actuales mientras no se intente independizarlos.

## 18. Build de la aplicacion

Solucion real encontrada:

- `app_windows/AcuratexControlApp/AcuratexControlApp.sln`

Proyecto principal:

- `app_windows/AcuratexControlApp/AcuratexControlApp.csproj`

Comandos ejecutados:

```powershell
cd "C:\Proyectos\AcuratexFastControl\app_windows"
dotnet build "C:\Proyectos\AcuratexFastControl\app_windows\AcuratexControlApp\AcuratexControlApp.sln" -p:BaseOutputPath=$env:TEMP\acuratexfastcontrol-build\out\ -p:BaseIntermediateOutputPath=$env:TEMP\acuratexfastcontrol-build\obj\ -p:UseAppHost=false
dotnet build "C:\Proyectos\AcuratexFastControl\app_windows\AcuratexControlApp\AcuratexControlApp.sln"
```

Resultado:

- Build con rutas temporales externas: fallo
  - `NETSDK1005` por `project.assets.json` sin destino `net8.0-windows`
  - `CS1069` por `System.IO.Ports` al compilar `AcuratexControlApp.Transport`
- Build directo de la solucion real: correcto

Proyectos compilados correctamente en build directo:

- `AcuratexControlApp.Core`
- `AcuratexControlApp.Transport`
- `AcuratexControlApp`

Warnings relevantes:

- `CabezalDashboardTarjetas.razor(9184,13)` `CS0162` codigo inaccesible
- `CabezalDashboardTarjetas.razor(9241,13)` `CS0162` codigo inaccesible
- `CabezalDashboardTarjetas.razor(9594,13)` `CS0162` codigo inaccesible
- `CabezalDashboardTarjetas.razor(2726,21)` `CS0649` `_selectedHeadProgramFile` nunca se asigna
- `CabezalDashboardTarjetas.razor(2742,18)` `CS0169` `_isHeadProgramBusy` nunca se usa

## 19. Conclusiones ejecutivas

1. El dashboard modular activo no usa el servicio legacy de scripts.
   - Usa `FastDashboardCommandService` y envia el protocolo corto actual directamente al firmware.

2. Los comandos no estan almacenados en un solo lugar.
   - Estan repartidos entre:
     - `CabezalDashboardTarjetas.razor`
     - `FastDashboardCommandService.cs`
     - `CabezalDashboardTarjetasModels.cs`
     - `CabezalDashboardTarjetasProgram1Commands.cs`
     - `CabezalDashboardTarjetasProgram2Commands.cs`
     - firmware `command_processor.cpp`

3. Programa 1 y Programa 2 no son independientes internamente.
   - Tienen perfiles separados, pero comparten implementacion, modelos, secuencias y servicio.
   - La diferencia material visible hoy es `Feet` en P2.

4. El firmware actual si reconoce el conjunto principal de comandos del dashboard modular activo.
   - La mayor parte del protocolo corto app/firmware esta conectada.

5. Existe una segunda ruta heredada en repo.
   - app legacy: `CabezalDashboardTarjetasCommandService.cs`
   - firmware legacy: `command_head_program_runner.cpp`
   - Esa ruta no es la activa en el dashboard modular auditado, pero explica nombres como `HEAD_STATUS` y `HEAD_ACTION`.

6. La dependencia oculta mas importante hoy es `SendTrackedAsync`.
   - Puede enviar `program_select_n` automaticamente antes de otro comando.

## 20. Estado final de Git

Comandos ejecutados:

```powershell
cd C:\Proyectos\AcuratexFastControl
git status --short
git diff --stat
git diff --name-only
```

Comparacion contra el estado inicial:

- Los modificados tracked siguen siendo exactamente los mismos 9 archivos preexistentes:
  - `UsbSmokeIdf/main/CMakeLists.txt`
  - `UsbSmokeIdf/main/command_processor.cpp`
  - `UsbSmokeIdf/main/head_fast_diag.cpp`
  - `UsbSmokeIdf/main/head_state_manager.cpp`
  - `UsbSmokeIdf/main/head_state_manager.h`
  - `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`
  - `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasModels.cs`
  - `app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs`
  - `app_windows/AcuratexControlApp/wwwroot/css/cabezal-dashboard-tarjetas.css`
- Los untracked preexistentes siguen presentes.
- Archivo nuevo agregado por esta auditoria:
  - `docs/AUDITORIA_APP_COMANDOS_COMPLETA_2026-06-28.md`
- No aparecieron `bin` ni `obj` en Git como parte del resultado final auditado.
- No se hicieron `reset`, `clean`, `restore`, `checkout --`, `stash`, `merge`, `rebase`, commit ni push.

Resultado final:

- El unico archivo nuevo creado por esta tarea fue el informe permitido.
