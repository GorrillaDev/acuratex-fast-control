# DETALLE IMPLEMENTACION PERFILES DINAMICOS RAM

## Resumen

Se implemento un `profile_store` para cargar perfiles TXT de LittleFS una sola vez, parsearlos a un `HeadRuntimeProfile` propietario y aplicar el perfil completo en RAM con doble buffer. Despues de `PROFILE_SELECT`, `HEAD_ACTION` busca la accion en el indice RAM y ejecuta comandos ya parseados, sin abrir ni recorrer el TXT.

No se encontraron dos perfiles TXT reales como archivos sueltos en el repositorio. El `main.zip` existente no contiene perfiles TXT. El formato usado se tomo del parser actual de la app y del runner firmware existente.

## Formato TXT usado

Campos aceptados en nivel superior:

- `PROFILE_NAME=<texto>`: nombre visible del perfil.
- `SYSTEM=UNI|UNIFIED|MOD|MODULAR`: debe coincidir con `cbz.uni...` o `cbz.mod...`.
- `PROGRAM=1|2|3`: debe coincidir con `progX` del nombre.
- `VERSION=<uint32>` o `PROFILE_VERSION=<uint32>`: opcional.
- `CRC32=<uint32>`, `CRC=<uint32>` o `SIGNATURE=<uint32>`: opcional, decimal o hex `0x...`.
- `INIT_SCRIPT=<archivo.txt>`: validado como nombre seguro.
- `MODULE|DEN|COUNT|n`, `MODULE|SIC|COUNT|n`, `MODULE|J|COUNT|n`, `MODULE|YARN|COUNT|n`, `MODULE|STITCH|COUNT|n`, `MODULE|FEET|COUNT|n`.
- `BUTTON|instancia|accion|script.txt`: valida estructura y nombre de script.
- `BEGIN|accion` o `BEGIN|accion|VALUE=n` ... `END`.

Comandos aceptados dentro de `BEGIN/END`:

- `WAIT n` o `DELAY n`, con `n <= 10000` ms.
- `can1` / `can2`.
- `status`.
- `send <id>,<bytes>`.
- `CAN|<id>,<bytes>`.
- linea CAN directa compatible con `app_parse_frame_line`.
- placeholder dinamico `XX` en `send/CAN` para acciones J dinamicas.

## Campos faltantes del TXT actual

El formato TXT actual no trae tablas numericas completas para DEN, SIC, J, Yarn, Stitch y Feet, ni secuencias/periodos/direcciones fisicas equivalentes a las tablas compiladas. Por eso el candidato se inicializa con el perfil compilado correspondiente a P1/P2/P3 y luego superpone metadatos, conteos y acciones TXT parseadas. Los perfiles compilados siguen siendo fallback seguro.

## HeadRuntimeProfile

`HeadRuntimeProfile` vive en `UsbSmokeIdf/main/profile_store.h` y es propietario de todos sus datos:

- `filename`, `profile_name`, `system`, `init_script`.
- `program_number`, version, CRC declarado/calculado y flags.
- conteos DEN/SIC/J/Yarn/Stitch/Feet.
- vista `HeadCommandProfile` con punteros reconstruidos hacia almacenamiento interno.
- buffers internos para scripts INIT, secuencias, posiciones y direcciones de cascada.
- `actions[]`, `commands[]` y `command_text[]` para ejecucion RAM.

No guarda punteros a lineas temporales ni a buffers del parser.

## Limites maximos

- Archivo de perfil: `APP_FILE_MAX_SIZE = 65536` bytes en transferencia.
- Nombre archivo: 48.
- Nombre perfil: 64.
- Nombre accion: 48.
- Linea TXT: 224.
- Acciones: 128.
- Comandos parseados: 768.
- Texto de comandos: 32768 bytes.
- WAIT/DELAY: 10000 ms.
- INIT steps: 192.
- INIT text: 12288 bytes.
- Secuencias movimiento: 16.
- Posiciones: 16.
- Yarn addresses: 16.
- Stitch addresses: 32.

No hay truncamiento silencioso: si una linea, nombre, cantidad o buffer excede el limite, el perfil se rechaza.

## Parser y validaciones

`app_profile_load_from_file()` abre `/fs/<archivo>` una sola vez, parsea todo el TXT y devuelve un candidato validado. Valida:

- nombre seguro y patron `cbz.(uni|mod).prog[1-3].txt`;
- `SYSTEM` y `PROGRAM` contra el nombre;
- version numerica si existe;
- CRC si existe;
- lineas desconocidas de nivel superior;
- `MODULE` soportados y rangos;
- `BUTTON` con script seguro;
- `BEGIN/END` balanceados;
- acciones duplicadas;
- cantidad maxima de acciones/comandos;
- comandos soportados dentro de accion;
- frames CAN y DLC;
- `WAIT/DELAY` en rango.

## CRC y version

El CRC32 calculado excluye las lineas declarativas de CRC (`CRC32=`, `CRC=`, `SIGNATURE=`). Si el TXT declara CRC, `declared_crc32` debe coincidir con `computed_crc32`; si no, el perfil se rechaza y el perfil activo RAM no cambia.

La version es opcional. Si no existe, `PROFILE_INFO` y `PROFILE_ACTIVE` reportan `VERSION=NONE`.

## Doble buffer y aplicacion atomica

`profile_store.cpp` mantiene dos `HeadRuntimeProfile` estaticos:

1. perfil activo;
2. perfil candidato/inactivo.

Flujo de `PROFILE_SELECT`:

1. valida nombre;
2. marca carga en curso;
3. carga y parsea candidato desde LittleFS;
4. valida formato, CRC, cantidades y punteros internos;
5. aplica candidato en el buffer inactivo;
6. toma el mutex corto del store;
7. incrementa generacion;
8. intercambia indice activo;
9. libera mutex;
10. guarda `/fs/.selected`.

Si guardar `.selected` falla despues de aplicar, se reporta error con `RAM_ACTIVE=1`. La politica implementada prefiere conservar el perfil RAM valido y no revertir a un estado parcial.

## Locks y concurrencia

Orden de locks documentado en el codigo:

- `s_state_mutex` del runner se usa solo para estado corto, flags de seleccion y snapshots.
- mutex de `profile_store` se usa solo para intercambio activo, generacion y flags de carga.
- no se mantiene `s_state_mutex` durante LittleFS, parsing, CAN ni WAIT.
- `PROFILE_SELECT` marca `s_profile_switch_in_progress` antes de cargar; `HEAD_ACTION` se rechaza mientras tanto.
- `PROFILE_INFO`/`PROFILE_LIST` usan el scratch inactivo y se serializan contra `PROFILE_SELECT` desde el runner.
- `FILE_BEGIN` rechaza una segunda subida activa.
- `FILE_END` rechaza reemplazar el perfil activo si esta cargandose, si hay accion activa, diagnostico rapido o movimiento activo.
- `FILE_DELETE` rechaza borrar el perfil activo o en carga.

## Comandos PROFILE

Se agregaron al firmware:

- `PROFILE_LIST`: inspecciona TXT compatibles y responde una linea `PROFILE_LIST|FILE=...` por perfil valido, mas `PROFILE_LIST|COUNT=n`.
- `PROFILE_SELECT|archivo.txt`: carga candidato, valida, aplica RAM, guarda `.selected` y responde OK solo al terminar.
- `PROFILE_INFO|archivo.txt`: inspecciona sin activar y responde version, CRC, acciones, comandos, modulos y validez.
- `PROFILE_ACTIVE`: responde archivo activo, perfil, version, CRC, generacion, origen y conteos.

`HEAD_PROGRAM_SELECT` queda como compatibilidad y llama al mismo selector interno, pero la app unificada usa `PROFILE_SELECT`.

## Arranque y fallback

En `usb_smoketest_main.cpp`, el orden queda:

1. montar LittleFS con `app_file_transfer_init()`;
2. `app_profile_store_init()`;
3. `app_profile_load_selected()`;
4. inicializar runner.

Si `/fs/.selected` no existe o el TXT es invalido, se aplica Programa 1 compilado como `COMPILED_FALLBACK`. No se sobreescribe `.selected` invalido y no hay boot loop.

## Ejecucion rapida desde RAM

`HEAD_ACTION` ya no llama `app_head_find_and_execute_action()` ni abre el TXT. El flujo nuevo:

1. valida accion;
2. marca runner `RUNNING` para bloquear `PROFILE_SELECT`;
3. toma snapshot del perfil activo RAM;
4. busca accion con `app_profile_find_action()`;
5. crea tarea con punteros al perfil activo y accion;
6. ejecuta `HeadRuntimeCommand` ya parseados.

Logs agregados:

- `PROFILE_TIMING|LOAD_MS=...|PARSE_MS=...`;
- `PROFILE_TIMING|VALIDATE_MS=...|APPLY_MS=...|TOTAL_MS=...`;
- `ACTION_TIMING|LOOKUP_US=...`;
- `ACTION_TIMING|FIRST_CAN_US=...`.

Despues de `PROFILE_SELECT`, la accion no realiza `fopen`, `fgets` ni recorrido `BEGIN/END`.

## Integracion firmware

Archivos principales:

- `UsbSmokeIdf/main/profile_store.h` nuevo.
- `UsbSmokeIdf/main/profile_store.cpp` nuevo.
- `UsbSmokeIdf/main/CMakeLists.txt` agrega el modulo.
- `UsbSmokeIdf/main/command_head_program_runner.cpp` agrega `PROFILE_*` y ejecucion RAM.
- `UsbSmokeIdf/main/command_head_program_runner.h` expone busy helper.
- `UsbSmokeIdf/main/file_transfer.cpp` agrega protecciones de concurrencia.
- `UsbSmokeIdf/main/head_program_runtime.cpp` usa profile_store como vista activa y fallback compilado.
- `UsbSmokeIdf/main/usb_smoketest_main.cpp` inicializa profile_store tras montar LittleFS.

## Integracion app Windows

Se modificaron solo servicios:

- `ICommandFileTransferService.cs`: modelos y firmas `PROFILE_*`.
- `CommandFileTransferService.cs`: `PROFILE_LIST`, `PROFILE_INFO`, `PROFILE_SELECT`, `PROFILE_ACTIVE`.
- `HeadProfileService.cs`: lista con `PROFILE_LIST`; aplica con `PROFILE_SELECT`; confirma con `PROFILE_ACTIVE`; actualiza activo local solo despues.

No se uso `HEAD_PROGRAM_SELECT` como seleccion final del runtime dinamico.

## Builds

App:

- Comando: `dotnet build AcuratexControlApp.sln`.
- Resultado: correcto, 0 errores.
- Advertencias: 5 existentes en `CabezalDashboardTarjetas.razor` sobre codigo inaccesible/campos no usados.
- Tiempo reportado: 00:00:09.27.

Firmware:

- Comando: `idf.py -B build_dynamic_profiles build` tras `export.ps1`, `CCACHE_DISABLE=1`, `CMAKE_BUILD_PARALLEL_LEVEL=1`.
- Resultado: correcto.
- Binario: `UsbSmokeIdf.bin` generado.
- Size check: `0xe9ca0` bytes, particion app `0x100000`, libres `0x16360` (9%).

## Tiempos medidos

Sin hardware conectado no se midieron tiempos reales de `PROFILE_SELECT` ni de `HEAD_ACTION` contra CAN. Quedo instrumentado en firmware para obtener:

- tiempo completo de carga/parse/validacion/aplicacion;
- `LOOKUP_US` desde recepcion de accion hasta lookup RAM;
- `FIRST_CAN_US` desde recepcion hasta primera trama CAN.

## Pruebas pendientes con hardware

Pendiente ejecutar en ESP32 real:

- `PROFILE_LIST` con dos perfiles.
- `PROFILE_INFO` de ambos.
- `PROFILE_SELECT` Perfil 1 y Perfil 2.
- `PROFILE_ACTIVE` despues de cada seleccion y tras reinicio.
- acciones repetidas confirmando cero accesos LittleFS posteriores.
- TXT corrupto conserva perfil anterior.
- CRC incorrecto conserva perfil anterior.
- borrar perfil activo rechazado.
- reemplazar perfil activo durante accion rechazado.
- segundo `FILE_BEGIN` rechazado.
- fallo de `PROFILE_SELECT` no altera RAM.
- fallback compilado sin `.selected` o con `.selected` invalido.

## Modular visual intacto

No se modificaron:

- `CabezalDashboardTarjetas.razor`;
- `CabezalDashboardTarjetasProgramProfiles.cs`;
- `CabezalDashboardTarjetasProgram1Commands.cs`;
- `CabezalDashboardTarjetasProgram2Commands.cs`;
- `CabezalDashboardTarjetasProgram3Commands.cs`;
- botones Program 1/2/3 modulares;
- tablas compiladas existentes P1/P2/P3.

Los perfiles compilados permanecen como fallback seguro.