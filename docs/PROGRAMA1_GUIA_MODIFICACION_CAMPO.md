# Programa 1: guia de modificacion en campo

Todas las rutas son relativas a la raiz `C:\Proyectos\AcuratexFastControl`.

## Fuentes unicas

- App: `app_windows/AcuratexControlApp/Components/CabezalDashboardUnificadoProgram1Commands.cs`.
- Firmware: `UsbSmokeIdf/main/head_unified_program_1_commands.cpp`.
- Motor generico firmware: `UsbSmokeIdf/main/head_state_manager.cpp`; no se modifica para cambiar mappings.
- Constructor generico CAN: `UsbSmokeIdf/main/head_command_frame_builder.cpp`.
- Entrada de tokens: `UsbSmokeIdf/main/command_processor.cpp`.

En la app, cambie cantidades, tiempos, IDs/selectores, posiciones DEN, posiciones Stitch y secuencias en `CabezalDashboardUnificadoProgram1Commands.cs`. En firmware cambie sus equivalentes `k*` en `head_unified_program_1_commands.cpp`.

## Cambios frecuentes

1. Cantidades: edite `DenCount`, `JGroupCount`, `YarnCount` o `StitchCount` y la tabla correspondiente en ambos archivos fuente unica.
2. Tiempos: edite `DenPeriodMs`, `JPeriodMs`, `YarnPeriodMs`, `StitchPeriodMs` en app y `kDenPeriodMs`, `kJPeriodMs`, `kYarnPeriodMs`, `kStitchPeriodMs` en firmware.
3. CAN/selectores: edite `DenMap`, `JMap`, `YarnCanIds`, `StitchMap`; replique en `kDenCanIds/kDenSelectors`, `kJCanIds/kJSelectors`, `kYarnCanIds/kYarnSelectors`, `kStitchCanIds/kStitchSelectors`.
4. Posiciones DEN: edite `DenPositions` y `DenSequence`/`DenRun1Sequence`; replique `kDenPositions`, `kDenRunSequence` y `kDenRun1Sequence`.
5. Posiciones Stitch: edite `StitchPositions` y `StitchSequence`; replique `kStitchPositions`. Las cuatro direcciones físicas de cada Stitch siguen en `StitchAddresses`/`kStitchAddresses`.
6. J con otra cantidad de canales: cambie `JChannelCount` y `kJChannelCount` entre 1 y 8. `JSequence` se genera con ese valor y el firmware impide comandos fuera del límite.
7. Agregar/retirar Yarn: cambie `YarnCount`, agregue/retire una fila de `YarnCanIds`, `YarnAddresses`, `kYarnCanIds`, `kYarnSelectors` y ocho direcciones en `kYarnAddresses`. Los tokens `yN_run/yN_stop` se analizan genéricamente.
8. Agregar posición Stitch: aumente `StitchPositionCount`, agregue el número a `StitchSequence`, `StitchPositions` y `kStitchPositions`. Si requiere salida física nueva, agregue además una dirección por Stitch y actualice `kStitchChannelCount`.

## Comprobación app/firmware

Compare cantidades, periodos, orden de secuencias, IDs, selectores, direcciones y posiciones entre las dos fuentes. Después revise `docs/PROGRAMA1_APP_FIRMWARE_MAP.md` y ejecute:

```powershell
git diff -- app_windows/AcuratexControlApp/Components/CabezalDashboardUnificadoProgram1Commands.cs UsbSmokeIdf/main/head_unified_program_1_commands.cpp
```

## Compilar sin internet

App, desde la raiz:

```powershell
dotnet build app_windows/AcuratexControlApp/AcuratexControlApp.csproj --no-restore
```

Firmware ESP-IDF 6.0 instalado en este equipo:

```powershell
. C:\Espressif\v6.0\esp-idf\export.ps1
idf.py -C UsbSmokeIdf -B build-production -D SDKCONFIG=sdkconfig.production build
```

Ver todos los cambios:

```powershell
git diff
git status --short
```

Guardar una versión recuperable sin destruir trabajo:

```powershell
git switch -c respaldo/programa1-campo-AAAA-MM-DD
git add app_windows/AcuratexControlApp/Components/CabezalDashboardUnificadoProgram1Commands.cs UsbSmokeIdf/main/head_unified_program_1_commands.cpp docs
git commit -m "Respaldo Programa 1 en campo"
```

Para volver posteriormente: guarde primero cualquier trabajo actual con otro commit o `git stash push -u -m "trabajo antes de volver"`; luego use `git switch respaldo/programa1-campo-AAAA-MM-DD`. No use `git reset --hard`.

## Mappings actuales

| Módulo | Instancias | CAN ID | Selectores/direcciones |
|---|---:|---:|---|
| DEN1..DEN8 | 8 | 0x320 | 00,01,02,03,04,05,06,07 |
| J1..J8 | 8 | 0x320 | 00,01,02,03,04,05,06,07; canales 1..6 |
| Yarn1 | 1 | 0x320 | 18,19,1A,1B,1C,1D,1E,1F |
| Yarn2 | 1 | 0x320 | 24,25,26,27,20,21,22,23 |
| Stitch1 | 1 | 0x320 | 00,01,02,05 |
| Stitch2 | 1 | 0x320 | 06,07,08,0B |
| Stitch3 | 1 | 0x320 | 0C,0D,0E,11 |
| Stitch4 | 1 | 0x320 | 12,13,14,17 |

## Pruebas mínimas

- Compilar app y firmware sin errores.
- Seleccionar Programa 1 y comprobar que aparecen 8 DEN, 8 J de 6 canales, 2 Yarn y 4 Stitch de 5 posiciones visuales.
- Probar posición mínima/máxima de cada DEN y confirmar bytes little-endian.
- Probar RUN/STOP individual y ALL de J, Yarn y Stitch.
- Confirmar periodos con traza CAN: DEN 300 ms, J 80 ms, Yarn 80 ms, Stitch 120 ms.
- Confirmar que RESET/STOP cancela estados running y que otro programa conserva su perfil.
- Probar selectores primero y último y verificar que DLC nunca supera 8.
