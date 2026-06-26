# Estado de la fase rapida

Fecha: 2026-06-24

Repositorio: `acuratex-fast-control`

Rama activa: `desarrollo-rapido`

Commit local actual: `516abca feat: migrar DEN SIC y fast diag`

## Objetivo de esta fase

Dejar una version rapida de Acuratex con:

- UI modular sin rediseño visual
- comandos cortos
- memoria visual local
- RUN / STOP no bloqueante en la app
- ejecucion fisica en firmware ESP-IDF
- TESTEO e INIT asynchronos en Core 1

## Alcance respetado

- No se modifico `referencia_antigua`
- No se cambio el Dashboard Unificado
- No se altero markup ni CSS del Dashboard Modular salvo logica interna
- No se crearon carpetas paralelas de firmware o app
- No se hizo push

## Estado actual real

### App Windows

- Compila correctamente
- El Dashboard Modular conserva la estructura y el estilo existentes
- `J1..J8` usa memoria visual local
- `RUN` y `STOP` ya no esperan confirmacion fisica
- `DEN` y `SIC` usan comandos cortos
- `TESTEO|...` e `INIT|...` actualizan el estado visual desde la linea recibida

### Firmware ESP-IDF

- Compila correctamente
- `J1..J8` mantiene registros activos-bajos
- Se agrego cola logica de comandos para el flujo rapido
- `DEN` y `SIC` ya tienen comandos cortos y secuencias en firmware
- `TESTEO` y `INIT` corren en worker separado
- `stop` y `emergency_stop` cancelan el diagnostico rapido

## Cambios funcionales ya hechos

### Dashboard Modular

- `j_run_#`
- `j_stop_#`
- `j_run_all`
- `j_stop_all`
- `den_run_#`
- `den_run1_#`
- `den_stop_#`
- `den_stop1_#`
- `sic_run_#`
- `sic_stop_#`
- `init`
- `testeo`

### Comportamiento visual local

- J mantiene registro local byte a byte
- valor inicial por canal: `0xFF`
- al pulsar un canal la UI cambia de inmediato
- al pulsar RUN se guarda snapshot visual y arranca animacion local
- al pulsar STOP se detiene la animacion y se restaura snapshot
- la animacion local de RUN no genera CAN

### Firmware

- `TESTEO`:
  - worker propio
  - rearmado por `0x702 3F 00`
  - clasificacion de respuestas `0x700`
  - salida de estado con lineas `TESTEO|...`
- `INIT`:
  - worker propio
  - secuencia larga trasladada del comportamiento antiguo
  - salida de progreso `INIT|...`
- `DEN`:
  - secuencia `1,3,5,2,4`
  - secuencia `RUN1` `1,3,5`
  - periodos trasladados al firmware
- `SIC`:
  - secuencia `1,2,3`
  - periodo trasladado al firmware

## Archivos tocados

### App Windows

- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`

### Firmware

- `UsbSmokeIdf/main/CMakeLists.txt`
- `UsbSmokeIdf/main/command_processor.cpp`
- `UsbSmokeIdf/main/head_runtime.cpp`
- `UsbSmokeIdf/main/head_state_manager.cpp`
- `UsbSmokeIdf/main/head_state_manager.h`
- `UsbSmokeIdf/main/head_fast_diag.cpp`
- `UsbSmokeIdf/main/head_fast_diag.h`

## Comprobaciones hechas

### Build app

Comando usado:

```powershell
dotnet build "AcuratexControlApp.csproj" -c Debug
```

Resultado:

- OK
- warnings no bloqueantes:
  - `NU1900` por acceso a `nuget.org`
  - campos no usados en `CabezalDashboardTarjetas.razor`

### Build firmware

Comando usado:

```powershell
. "C:\Espressif\v6.0\esp-idf\export.ps1"
$env='1'
idf.py build
```

Resultado:

- OK
- se corrigio el warning de TWAI legacy en `head_fast_diag.cpp`

## Nota sobre el workspace

`UsbSmokeIdf/managed_components/` aparece como generado por ESP-IDF y queda fuera del trabajo funcional principal. No forma parte de la logica nueva del cabezal.

## Lo que ya no depende de la UI

- `RUN` repetitivo de `J`, `Yarn`, `Stitch`, `DEN` y `SIC`
- ejecucion fisica de `TESTEO`
- ejecucion fisica de `INIT`
- respuesta inmediata de la app sin esperar `DONE`

## Pendiente real

- pruebas fisicas en hardware
- medir latencia real de clic a CAN
- validar simultaneidad J1/J2 en banco
- validar `TESTEO` e `INIT` con placas reales
- decidir si `managed_components` se ignora o se limpia del workspace

## Regla de continuidad

Si vuelves a pedir continuar, el punto de entrada es:

1. revisar este archivo
2. validar el estado de build
3. seguir con pruebas fisicas o la siguiente fase funcional
