# Detalle de implementacion de Programa 3

Fecha de trabajo: 2026-07-03

## Contexto

- Rama actual: `programa-3`
- Alcance: `app_windows/AcuratexControlApp`
- Objetivo: integrar Programa 3 en la shell modular sin tocar el sistema TXT, LittleFS, firmware ni otros modulos ajenos al selector de programas.

## Cambios realizados

### 1. Catalogo de programas

Archivo: [CabezalDashboardTarjetasProgramProfiles.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs)

- Se agrego `Program3 = 3` al enum `CabezalDashboardTarjetasProgramId`.
- Se reemplazo la logica binaria de `Get()` por un `switch` explicito.
- El catalogo ahora resuelve asi:
  - `Program1` -> `CabezalDashboardTarjetasProgram1Commands.Profile`
  - `Program2` -> `CabezalDashboardTarjetasProgram2Commands.Profile`
  - `Program3` -> `CabezalDashboardTarjetasProgram3Commands.Profile`
  - fallback defensivo -> `Program1`

### 2. Perfil independiente de Programa 3

Archivo: [CabezalDashboardTarjetasProgram3Commands.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram3Commands.cs)

- Se creo un perfil nuevo e independiente para Programa 3.
- La configuracion inicial replica la base funcional de Programa 1:
  - DEN igual a Programa 1
  - SIC igual a Programa 1
  - J igual a Programa 1
  - Yarn igual a Programa 1
  - Stitch igual a Programa 1
  - Feet vacio
  - mismas secuencias iniciales
  - mismos periodos iniciales
- El perfil usa `CabezalDashboardTarjetasProgramId.Program3` y el nombre `"Programa 3"`.
- No se uso ningun alias del perfil de Programa 1.
- No se compartieron referencias mutables con `CabezalDashboardTarjetasProgram1Commands.Profile`.

### 3. Boton visual de Programa 3

Archivo: [CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor)

- Se agrego el boton de Programa 3 junto a Programa 1 y Programa 2.
- El boton usa la misma clase visual que los otros seleccionadores.
- El `onclick` llama a `SelectProgramAsync(CabezalDashboardTarjetasProgramId.Program3)`.
- No se cambio `SyncFirmwareProgramSelectionAsync()`.
- Por eso, la sincronizacion sigue enviando `program_select_{(int)_selectedProgram}` y `Program3` produce `program_select_3` por el valor del enum.

## Verificacion realizada

- Se ejecuto:

```powershell
cd "C:\Proyectos\AcuratexFastControl\app_windows\AcuratexControlApp"
dotnet build AcuratexControlApp.sln
```

- Resultado: compilacion correcta.
- Errores: 0
- Advertencias: 5 existentes en `CabezalDashboardTarjetas.razor`
  - CS0162 en 9190, 9247 y 9600
  - CS0169 en 2748
  - CS0649 en 2732

## Confirmaciones tecnicas

- El flujo de seleccion de programa sigue usando `SelectProgramAsync(...)`.
- La app no introdujo nuevos comandos de archivo o perfil en esta integracion.
- No se toco:
  - `CabezalDashboardUnificado.razor`
  - `HeadProfileService.cs`
  - `AppScriptExecutionService.cs`
  - `CommandFileTransferService.cs`
  - `FastDashboardCommandService.cs`
  - `CardSystemForm.cs`
  - `CabezalDashboardTarjetasModels.cs`
  - `AcuratexControlApp.csproj`
  - firmware
  - LittleFS
  - sistema TXT

## Estado final

- Archivos modificados:
  - [CabezalDashboardTarjetasProgramProfiles.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgramProfiles.cs)
  - [CabezalDashboardTarjetasProgram3Commands.cs](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetasProgram3Commands.cs)
  - [CabezalDashboardTarjetas.razor](/C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor)

- No se hizo commit.
- No se hizo push.
