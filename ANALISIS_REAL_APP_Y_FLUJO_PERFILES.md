# ANALISIS_REAL_APP_Y_FLUJO_PERFILES

Informe generado en modo lectura. No se modifico codigo, no se compilo, no se hizo commit ni push. Este documento usa la evidencia ya reunida y no agrega nuevas busquedas extensas.

## Alcance

Se revisaron como piezas centrales la shell modular, la shell unificada, el flujo de transferencia de archivos, el parser de perfiles y el panel principal de edicion/subida.

Referencias base:
- `[Form1.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Form1.cs#L928)`
- `[CardSystemForm.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/CardSystemForm.cs#L242)`
- `[UnifiedSystemForm.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/UnifiedSystemForm.cs#L353)`
- `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L122)`
- `[CabezalDashboardUnificado.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor#L14)`
- `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L134)`
- `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L319)`
- `[AppScriptExecutionService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs#L84)`

## A. Hechos confirmados en la app

- La app tiene al menos tres hosts con composicion DI separada: `Form1`, `CardSystemForm` y `UnifiedSystemForm`. Cada uno construye su propio contenedor y registra servicios distintos. Eso hace que el flujo de UI y el flujo de comandos dependan del host activo, no de un estado global unico. Ver `[Form1.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Form1.cs#L935)`, `[CardSystemForm.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/CardSystemForm.cs#L244)` y `[UnifiedSystemForm.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/UnifiedSystemForm.cs#L355)`.
- La shell modular `CabezalDashboardTarjetas.razor` usa perfiles compilados en C# y no lee LittleFS para ejecutar la maquina. El selector real visible tiene solo Program 1 y Program 2. Ver `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L122)`, `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L128)` y `[CabezalDashboardTarjetasProgramProfiles.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs#L3)`.
- La shell unificada `CabezalDashboardUnificado.razor` si usa `HeadProfileService` para listar y aplicar programas desde archivos TXT, y mantiene un perfil activo en memoria. Ver `[CabezalDashboardUnificado.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor#L269)`, `[CabezalDashboardUnificado.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor#L306)` y `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L429)`.
- El protocolo de transferencia de archivos de la app implementa `FILE_BEGIN`, `FILE_DATA`, `FILE_END`, `FILE_SELECT`, `FILE_INFO`, `FILE_LIST` y `FILE_GET`. La subida usa chunks base64 de 32 bytes crudos por chunk. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1105)`, `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1117)`, `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1129)` y `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1167)`.
- La app guarda una copia local temporal de archivos de firmware en `%TEMP%\AccuratexControlApp\firmware-files`, no en una particion LittleFS local del PC. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1234)` y `[LocalTempFileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/LocalTempFileService.cs#L75)`.
- El servicio de ejecucion de scripts tiene modo por defecto `FirmwareHeadAction`, asi que la ruta activa en la app no es la de "descargar TXT y re-ejecutarlo", sino la de mandar `HEAD_ACTION|...` directamente. Ver `[AppScriptExecutionService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs#L53)` y `[AppScriptExecutionService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs#L84)`.

## B. Flujo de seleccion de Programa 1/2/3

### Programa 1 y Programa 2

1. El usuario pulsa el boton de Programa 1 o Programa 2 en la shell modular. Ver `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L122)` y `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L128)`.
2. `SelectProgramAsync` cambia `_selectedProgram`, intenta sincronizar el firmware y luego aplica el perfil compilado en la UI. Ver `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L3185)`.
3. `SyncFirmwareProgramSelectionAsync` emite `program_select_1` o `program_select_2` segun el enum actual. Ver `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L3217)` y `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L3227)`.
4. El perfil activo de UI apunta a `CabezalDashboardTarjetasProgram1Commands.Profile` o `CabezalDashboardTarjetasProgram2Commands.Profile`, no a un archivo TXT. Ver `[CabezalDashboardTarjetasProgramProfiles.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs#L35)` y `[CabezalDashboardTarjetasProgram1Commands.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs#L5)`.

### Programa 3

5. No existe boton visible de Programa 3 en la shell modular.
6. No existe entrada `Program3` en el catalogo compilado. El enum solo contempla `Program1` y `Program2`. Ver `[CabezalDashboardTarjetasProgramProfiles.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs#L3)`.
7. No existe `CabezalDashboardTarjetasProgram3Commands.cs` en la app.
8. Conclusion: `program_select_3` no tiene camino real en la app Windows revisada.

### Respuestas directas a las preguntas 1, 6 y 7

- `program_select_1` y `program_select_2` cambian `_selectedProgram`, resetean estado transitorio, sincronizan el firmware y cargan el perfil compilado correspondiente en la shell modular. Ver `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L3185)` y `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L3057)`.
- El perfil activo de esa shell apunta a objetos compilados de C#, no a LittleFS. El catalogo usa `Program1Commands.Profile` o `Program2Commands.Profile`. Ver `[CabezalDashboardTarjetasProgramProfiles.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs#L39)`.
- `program_select_3` no esta implementado en la app actual.

## C. Flujo de subida y seleccion de archivos

### Subida de archivos a firmware

1. El usuario elige un TXT desde el navegador o desde la cola local. La seleccion del browser se valida como `.txt`, con tamano positivo y dentro del maximo permitido. Ver `[MainControlPanel.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/MainControlPanel.razor#L2062)`, `[MainControlPanel.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/MainControlPanel.razor#L2078)` y `[MainControlPanel.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/MainControlPanel.razor#L4126)`.
2. `ReadBrowserFileAsync` lee el archivo del browser una sola vez en memoria y `UploadSelectedCommandFileAsync` lo pasa a `CommandFileTransferService.UploadTextFileAsync`. Ver `[MainControlPanel.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/MainControlPanel.razor#L2087)` y `[MainControlPanel.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/MainControlPanel.razor#L2146)`.
3. `UploadTextFileWithChunkSizeAsync` hace la secuencia `FILE_BEGIN`, varios `FILE_DATA`, `FILE_END`, `FILE_SELECT` y luego `FILE_INFO`. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1117)`, `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1129)`, `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1160)`, `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1167)` y `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1174)`.
4. El chunk crudo es de 32 bytes, se codifica a Base64 y se valida que la linea no supere el limite del firmware. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L13)`, `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1129)` y `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1133)`.
5. `FILE_SELECT` despues de `FILE_END` forma parte del cierre de la subida. No cambia el perfil de maquina. Solo marca el archivo como seleccionado en el gestor de archivos del tester. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L473)` y `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1105)`.

### Lectura y edicion de archivos remotos

6. `DownloadFileAsync` usa `FILE_GET` y recibe `FILE_BEGIN` / `FILE_DATA` / `FILE_END` para reconstruir una copia local de cache. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L288)`.
7. La cache local de esos archivos vive en `%TEMP%\AccuratexControlApp\firmware-files`. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L1234)`.
8. `SaveEditedTextAsync` vuelve a subir el texto editado por el mismo protocolo de archivo. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L402)`.

### Flujo de programas TXT de cabezal

9. `HeadProfileService.ListProgramsAsync` enumera archivos del tester, filtra nombres validos y vuelve a abrir cada candidato para inspeccionarlo. Ver `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L319)` y `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L880)`.
10. `HeadProfileService.ApplyProgramAsync` vuelve a descargar el TXT, lo parsea y guarda un `HeadProfile` activo en RAM antes de mandar `HEAD_PROGRAM_SELECT`. Ver `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L429)`, `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L471)` y `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L491)`.
11. El parser real reconoce `PROFILE_NAME=`, `SYSTEM=`, `PROGRAM=`, `INIT_SCRIPT=`, `MODULE|...`, `BUTTON|...` y `BEGIN|...`. Ver `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L925)`, `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L949)`, `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L979)`, `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L985)`, `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L1079)`, `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L1148)` y `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L1227)`.

### Respuestas directas a las preguntas 2, 3, 4, 5, 10, 11, 13 y 14

- Ruta local confirmada en la app: `%TEMP%\AccuratexControlApp\firmware-files`. La particion LittleFS real del firmware no esta descrita en la app; la app solo habla por protocolo.
- Los archivos subidos no se reutilizan automaticamente para controlar la maquina. Solo se convierten en `HeadProfile` si pasan por `HeadProfileService`, y aun asi el modo activo por defecto en ejecucion es `HEAD_ACTION`, no un replay de TXT.
- Si existe un parser que convierte TXT en estructuras de perfil en RAM: si, `HeadProfileService.ParseProgramContent` construye `HeadProfile` y sus bindings. Ver `[IHeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/IHeadProfileService.cs#L169)` y `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L925)`.
- Si existe una estructura dinamica tipo `HeadRuntimeProfile`: no aparece con ese nombre. La equivalente real en la app es `HeadProfile`, almacenada en memoria por `HeadProfileService`.
- `FILE_SELECT` existe y se usa, pero solo como seleccion de archivo en el tester.
- `PROFILE_LIST`, `PROFILE_SELECT`, `PROFILE_INFO` y `PROFILE_ACTIVE` no tienen soporte visible en el codigo de la app.
- `HEAD_PROGRAM_SELECT` si existe y se usa en el flujo de admin de programas TXT. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L523)` y `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L471)`.

## D. Flujo de comandos de maquina

### Shell modular

1. La shell modular usa `FastDashboardCommandService` como transportador real en `CardSystemForm`. Ver `[CardSystemForm.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/CardSystemForm.cs#L242)`.
2. `SendDoCommandAsync` en el componente modular envia `init`, `testeo`, `status` y comandos similares a traves de ese servicio, pero sin pasar por LittleFS. Ver `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L4159)` y `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L4287)`.
3. `SendDenPositionAsync`, `SendSicPositionAsync`, `SendJRegisterAsync` y `SendBlockPinAsync` construyen comandos numericos desde estructuras compiladas de C#. Ver `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L7252)`, `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L7356)`, `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L7460)` y `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L6521)`.
4. Las tablas de perfil compilado de Program 1 y Program 2 son las que definen DEN, SIC, J, Yarn, Stitch y, en el caso de Program 2, tambien pies. Ver `[CabezalDashboardTarjetasProgram1Commands.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs#L5)` y `[CabezalDashboardTarjetasProgram2Commands.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs#L5)`.
5. Durante INIT, TESTEO, DEN, SIC, J, Yarn y Stitch en la shell modular no se consulta LittleFS. Se usan estructuras numericas y perfiles compilados.

### Shell unificada

6. La shell unificada si pasa por `HeadProfileService` y `AppScriptExecutionService`. Ver `[CabezalDashboardUnificado.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor#L306)` y `[CabezalDashboardUnificado.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor#L786)`.
7. El modo por defecto de ejecucion es mandar `HEAD_ACTION|...` al firmware. Ver `[AppScriptExecutionService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs#L108)` y `[AppScriptExecutionService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs#L155)`.
8. `HEAD_STATUS`, `HEAD_STOP` y `HEAD_ACTION` son soportados de forma explicita en el servicio unificado. Ver `[CabezalDashboardUnificadoCommandService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CabezalDashboardUnificadoCommandService.cs#L120)`, `[CabezalDashboardUnificadoCommandService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CabezalDashboardUnificadoCommandService.cs#L127)`, `[CabezalDashboardUnificadoCommandService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CabezalDashboardUnificadoCommandService.cs#L132)`, `[CabezalDashboardUnificadoCommandService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CabezalDashboardUnificadoCommandService.cs#L137)` y `[CabezalDashboardUnificadoCommandService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CabezalDashboardUnificadoCommandService.cs#L416)`.
9. El runner de script TXT sigue existiendo como ruta latente en `ExecuteBindingAsync`, pero no es el camino por defecto. Ver `[AppScriptExecutionService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs#L206)` y `[AppScriptExecutionService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs#L231)`.

### Respuestas directas a las preguntas 8, 9 y 12

- Durante INIT, TESTEO, DEN, SIC, J, Yarn y Stitch la shell modular no consulta LittleFS. La shell unificada consulta perfiles TXT solo cuando se esta resolviendo un perfil o una accion de programa, no como parte del archivo seleccionado por `FILE_SELECT`.
- No hay llamadas `fopen`, `fread`, `fgets`, `getline`, `open` o `read` de C en la app Windows para este flujo. Tampoco se ve una API LittleFS nativa en la app; todo lo de archivo es .NET y protocolo de texto.
- El flujo actual desde la app hasta CAN, segun el codigo visible, pasa por la capa USB/serial/TCP despues de la construccion de una linea de texto. La logica de transporte solo reenvia la linea.

### Diagrama corto del flujo real de comandos

```text
Shell modular
  -> SelectProgramAsync
  -> program_select_1 / program_select_2
  -> FastDashboardCommandService.SendLineAsync
  -> USB/Serial/TCP
  -> firmware

Shell unificada
  -> ApplyProgramAsync / ExecuteActionAsync
  -> HeadProfileService + AppScriptExecutionService
  -> HEAD_PROGRAM_SELECT / HEAD_ACTION / HEAD_STATUS / HEAD_STOP
  -> USB/Serial/TCP
  -> firmware
```

## E. Comparacion app versus firmware

- La conclusion del firmware de que `FILE_*` solo almacena archivos encaja con la app: `UploadTextFileAsync` y `SaveEditedTextAsync` implementan un protocolo de archivo, no un cambio de programa de maquina. Ver `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L134)` y `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs#L402)`.
- La conclusion del firmware de que `program_select_*` usa perfiles compilados encaja con la shell modular: el catalogo de Program 1 y Program 2 apunta a `Profile` compilados y `SelectProgramAsync` manda `program_select_1/2`.
- La conclusion del firmware de que `FILE_SELECT` no modifica el perfil activo de maquina tambien encaja con la app: `SelectFileAsync` solo selecciona archivo en el gestor, mientras que el cambio de perfil de maquina ocurre por `program_select_*` o por `HEAD_PROGRAM_SELECT`.
- El runner legacy de programa TXT existe como idea de ejecucion, pero en la app actual la ruta efectiva por defecto sigue siendo `HEAD_ACTION|...`. Eso significa que el flujo de archivos y el flujo de control de maquina siguen separados salvo por el puente de `HeadProfileService`.

### Respuesta directa a la pregunta 18

Los flujos no se unen en `FILE_SELECT`. La unica union real observable esta en `HeadProfileService.ApplyProgramAsync`, que descarga un TXT, lo parsea, guarda el perfil en RAM y luego usa `HEAD_PROGRAM_SELECT`. Ese puente no convierte `FILE_SELECT` en control de maquina.

## F. Inconsistencias encontradas

- La app no expone Programa 3 en la shell modular, pero el contexto de firmware confirmado por el usuario dice que `program_select_3` existe.
- La shell modular tiene un selector de programa cabezal oculto y deshabilitado. `CanChangeHeadProgram` es `false` y `RefreshHeadProgramsAsync` no enlaza con `HeadProfileService`. Ver `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L2950)`, `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L3366)` y `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor#L3414)`.
- Existen dos vocabularios de "programa" al mismo tiempo: uno compilado en C# para la shell modular y otro basado en TXT para la shell unificada. El codigo no los fusiona.
- `FILE_SELECT` puede parecer una seleccion de programa, pero en realidad solo marca archivos del tester.
- `CabezalDashboardTarjetasCommandService.cs` existe como servicio mas rico, pero la shell modular esta cableada a `FastDashboardCommandService` en `CardSystemForm`. Eso crea una divergencia entre lo que el codigo podria hacer y lo que hace realmente.
- `AppScriptExecutionService.ExecuteBindingAsync` existe, pero el modo por defecto no lo usa. Por eso el runner TXT es una capacidad latente, no el camino real.
- No se ve soporte para `PROFILE_LIST`, `PROFILE_SELECT`, `PROFILE_INFO`, `PROFILE_ACTIVE` ni para un `PROGRAM_STATE|ACTIVE=1/2/3`. Eso deja sin lectura directa el estado real del firmware.
- En la shell modular no hay verificacion fuerte de que el firmware haya cambiado realmente de programa; el estado local `_syncedFirmwareProgram` se basa en la respuesta esperada del comando, no en un readback de estado.

### Riesgos de lifetime, copias y estado

- No se ven punteros manuales ni memoria temporal de bajo nivel en esta parte de la app. El riesgo es de logica y sincronizacion, no de memoria nativa.
- `HeadProfileService` guarda el perfil activo en un diccionario `static`, asi que el estado activo de perfiles TXT es compartido dentro del proceso. Ver `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs#L21)`.
- `CommandFileTransferService` usa handlers temporales de `LineReceived` para cada operacion y los desuscribe en `finally`. Eso evita fugas obvias, pero sigue dependiendo de que la respuesta llegue dentro del timeout.
- En la subida secuencial de archivos locales hay una captura de indice en el callback de progreso. No es un problema de lifetime de memoria, pero si es un punto sensible de asincronia.

## G. Funcionalidad que todavia no existe

- No existe Programa 3 en el catalogo modular ni su archivo `CabezalDashboardTarjetasProgram3Commands.cs`.
- No existe una familia de comandos `PROFILE_LIST`, `PROFILE_SELECT`, `PROFILE_INFO` o `PROFILE_ACTIVE`.
- No existe un readback de estado de programa activo equivalente a `PROGRAM_STATE|ACTIVE=1/2/3`.
- No existe una union semantica entre `FILE_SELECT` y el perfil de maquina.
- No existe una estructura llamada `HeadRuntimeProfile`; la app usa `HeadProfile` como snapshot activo.
- No existe una sincronizacion fuerte entre lo que la app cree que esta activo y lo que el firmware realmente tiene activo, mas alla de los ACK/OK esperados.
- No existe un flujo unico de ejecucion TXT para la shell modular. Esa capacidad queda relegada al camino latente de `AppScriptExecutionService`.

## H. Archivos que seria necesario modificar, sin modificarlos

- `[CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor)` si se quiere mostrar Programa 3, activar el selector oculto o reconectar la shell modular al flujo TXT.
- `[CabezalDashboardTarjetasProgramProfiles.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs)` si se quiere agregar Program 3 al catalogo compilado.
- `[CabezalDashboardTarjetasProgram1Commands.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram1Commands.cs)` y `[CabezalDashboardTarjetasProgram2Commands.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram2Commands.cs)` si se necesita homogeneizar el formato de comandos compilados.
- `[CardSystemForm.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/CardSystemForm.cs)` y `[FastDashboardCommandService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs)` si se quiere cambiar el transportador real de la shell modular.
- `[CabezalDashboardUnificado.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardUnificado.razor)` si se quiere mostrar mejor el estado de perfiles TXT o sincronizar la experiencia con el resto de la app.
- `[CabezalDashboardUnificadoCommandService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CabezalDashboardUnificadoCommandService.cs)` si se quiere cambiar el dialecto de comando de la shell unificada.
- `[HeadProfileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/HeadProfileService.cs)` si se quiere cambiar el parser, el cache de perfiles o la forma de seleccionar el programa activo.
- `[AppScriptExecutionService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/AppScriptExecutionService.cs)` si se quiere activar el replay real de TXT o exponer el modo de ejecucion.
- `[CommandFileTransferService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/CommandFileTransferService.cs)` si se quiere ligar `FILE_SELECT` con una semantica de programa o ajustar el protocolo.
- `[MainControlPanel.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/MainControlPanel.razor)` si se quiere alinear edicion, importacion, exportacion y seleccion de programas con una UX unica.
- `[LocalTempFileService.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/LocalTempFileService.cs)` si se quiere cambiar la cache local de archivos.

## I. Recomendacion de arquitectura basada unicamente en el codigo existente

- Mantener separados los dos dominios que ya existen en el codigo: archivos y control de maquina. `FILE_*` debe seguir siendo almacenamiento/lectura de archivos; `program_select_*`, `HEAD_ACTION`, `HEAD_STATUS` y `HEAD_STOP` deben seguir siendo control.
- Tomar `HeadProfileService` como unica fuente de verdad para programas TXT de cabezal. Ya parsea, valida, lista y cachea el perfil activo en RAM.
- Si el firmware realmente soporta `program_select_3`, agregarlo en la app solo con una tercera entrada consistente en el catalogo compilado y una UI visible. Si no se va a soportar, no dejar rastros ocultos.
- Si se necesita que la maquina use archivos TXT como runtime real, el puente correcto no es `FILE_SELECT`; el puente tiene que pasar por `HeadProfileService.ApplyProgramAsync` y luego por un comando de seleccion explicito o por un readback de estado.
- Si la ejecucion por scripts TXT sigue siendo deseable, exponer el modo de `AppScriptExecutionService` en la UI o eliminar la ruta muerta para evitar falsa expectativa funcional.
- Para evitar estados divergentes, cada shell deberia tener una sola interpretacion de "programa activo". Hoy la shell modular usa perfiles compilados y la unificada usa perfiles TXT; eso es valido si se documenta, pero confuso si se mezcla.

## Resumen ejecutivo

- `FILE_BEGIN` abre la carga de archivo.
- `FILE_DATA` envia chunks base64.
- `FILE_END` cierra la carga.
- `FILE_SELECT` solo selecciona el archivo en el tester.
- Los programas 1 y 2 de la shell modular son perfiles compilados en C#.
- No hay Programa 3 en la app revisada.
- Los TXT de cabezal si se parsean y se guardan en RAM, pero el camino activo por defecto sigue siendo `HEAD_ACTION|...`.
- El flujo de archivos y el flujo de control de maquina siguen separados, con un unico puente parcial en `HeadProfileService`.
