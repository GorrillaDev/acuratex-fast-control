# ANALISIS_REAL_PERFILES_Y_LITTLEFS

## Alcance
Este informe se limita al codigo presente en este workspace. No se modifica nada mas. El objetivo es describir el flujo real actual de perfiles, LittleFS y seleccion de programa, no proponer una arquitectura nueva.

## A. Hechos confirmados por el codigo

### 1. Que hace exactamente `FILE_BEGIN`, `FILE_DATA` y `FILE_END`
- Subida de archivo: `UsbSmokeIdf/main/file_transfer.cpp:866-907` (`app_file_cmd_begin`) valida nombre y tamano, limpia el estado previo, borra `/fs/.upload.tmp`, abre ese temporal con `fopen("wb")` y deja listo el contexto de subida.
- Subida de archivo: `UsbSmokeIdf/main/file_transfer.cpp:941-1006` (`app_file_cmd_data`) valida el indice secuencial, decodifica base64 a un buffer local y escribe el bloque con `fwrite()` sobre el temporal abierto.
- Subida de archivo: `UsbSmokeIdf/main/file_transfer.cpp:1040-1075` (`app_file_cmd_end`) valida tamano final, cierra el temporal y renombra `/fs/.upload.tmp` a `/fs/<nombre>`.
- Descarga de archivo: `UsbSmokeIdf/main/file_transfer.cpp:1365-1395` (`app_file_cmd_get`) abre un archivo de LittleFS para lectura y responde `FILE_BEGIN|...`.
- Descarga de archivo: `UsbSmokeIdf/main/file_transfer.cpp:1429-1469` (`app_file_cmd_get_next`) lee chunks con `fread()`, los re-encodea y responde `FILE_DATA|...` hasta emitir `FILE_END|...`.
- Enrutamiento: `UsbSmokeIdf/main/file_transfer.cpp:1546-1561` (`app_file_transfer_is_command`) reconoce los tokens `FILE_BEGIN`, `FILE_DATA`, `FILE_END`, `FILE_LIST`, `FILE_SELECT`, `FILE_DELETE`, `FILE_INFO`, `FILE_GET` y `FILE_GET_NEXT`.
- El dispatcher que entra por el camino de comandos lo manda a este modulo en `UsbSmokeIdf/main/command_processor.cpp:1343-1355` (`app_command_process_line` clasifica `FILE_*` como `file_transfer`).

### 2. En que ruta y particion se guardan los archivos
- Ruta base LittleFS: `UsbSmokeIdf/main/file_transfer.cpp:14` define `APP_FS_BASE` como `/fs`.
- Particion LittleFS: `UsbSmokeIdf/main/file_transfer.cpp:16` define `APP_FS_PARTITION_LABEL` como `storage`.
- Montaje: `UsbSmokeIdf/main/file_transfer.cpp:775-831` (`app_fs_mount`) registra LittleFS con `base_path = "/fs"` y `partition_label = "storage"`.
- Particion fisica: `UsbSmokeIdf/partitions.csv:5` declara `storage, data, littlefs, ...`.
- Metadatos internos: `UsbSmokeIdf/main/file_transfer.cpp:18-20` define `/fs/.selected` y `/fs/.upload.tmp`.

### 3. Si los archivos guardados se vuelven a abrir para controlar la maquina
- En el camino activo de `program_select_*`, no.
- `UsbSmokeIdf/main/head_program_runtime.cpp:19-38` devuelve punteros a `kProgram1Commands`, `kProgram2Commands` y `kProgram3Commands`; no hay apertura de archivos.
- `UsbSmokeIdf/main/command_processor.cpp:1136-1178` (`app_handle_program_select_command`) solo cambia el selector activo.
- `UsbSmokeIdf/main/head_state_manager.cpp:686-735` (`app_head_state_manager_init`) y `UsbSmokeIdf/main/head_fast_diag.cpp:497-899` usan el perfil activo en RAM, no LittleFS.
- En el runner legacy, si se usa ese camino, si: `UsbSmokeIdf/main/command_head_program_runner.cpp:970-986` (`app_head_program_exists`), `1104-1205` (`app_head_load_module_counts_from_program`) y `1807-1947` (`app_head_find_and_execute_action`) abren y recorren el TXT con `fopen()`/`fgets()`.

### 4. Si existe un parser que convierta esos archivos en estructuras de perfil en RAM
- En el camino activo, no existe parser que convierta los uploads de LittleFS en `HeadCommandProfile`.
- `UsbSmokeIdf/main/head_command_profile.h:95-104` define una estructura estatica compilada, no un profile dinamico cargado desde archivo.
- El runner legacy si parsea TXT, pero solo a estado propio: `UsbSmokeIdf/main/command_head_program_runner.cpp:1104-1205` parsea `MODULE|...|COUNT|...` hacia `s_module_counts`, y `1807-1947` ejecuta lineas del archivo sobre CAN.
- Eso no crea una estructura equivalente a `HeadCommandProfile` para el camino activo.

### 5. Si existe una estructura dinamica tipo `HeadRuntimeProfile`
- No aparece ningun simbolo `HeadRuntimeProfile` ni `RuntimeProfile` en el arbol analizado.
- El camino activo solo maneja `const HeadCommandProfile *` y estado mutable separado.
- El runner legacy usa sus propias estructuras de estado (`s_active_program`, `s_module_counts`, estado de tarea y snapshot de status), no un tipo con ese nombre.

### 6. Que cambia exactamente cuando llega `program_select_1`, `program_select_2` o `program_select_3`
- `UsbSmokeIdf/main/command_processor.cpp:1136-1178` (`app_handle_program_select_command`) mapea la cadena a `APP_HEAD_PROGRAM_1`, `APP_HEAD_PROGRAM_2` o `APP_HEAD_PROGRAM_3`.
- Antes de cambiar, valida que no haya movimiento activo ni `fast_diag` ocupado: `UsbSmokeIdf/main/command_processor.cpp:1154-1158`.
- La seleccion efectiva la hace `UsbSmokeIdf/main/head_program_runtime.cpp:41-55` (`app_head_program_select`), que solo actualiza `s_active_program`.
- La respuesta es `OK program_select_1/2/3`: `UsbSmokeIdf/main/command_processor.cpp:1171-1175`.
- No se recarga nada desde LittleFS, no se reparte memoria nueva y no se reinicia el estado de la maquina.

### 7. Si el perfil activo apunta a `kProgram1Commands`, `kProgram2Commands` o `kProgram3Commands` compilados
- Si. `UsbSmokeIdf/main/head_program_runtime.cpp:19-38` (`app_head_program_get_profile` y `app_head_program_get_active_profile`) devuelve exactamente uno de esos tres objetos estaticos.
- El selector arranca en Program1 por inicializacion estatica: `UsbSmokeIdf/main/head_program_runtime.cpp:7`.
- `UsbSmokeIdf/main/head_program_runtime.cpp:9-12` (`app_head_program_runtime_init`) tambien fija Program1, pero no encontre caller dentro del camino activo del arbol.
- Los tres perfiles viven en compilado: `UsbSmokeIdf/main/head_program_1_commands.cpp:55-147`, `head_program_2_commands.cpp:57-162`, `head_program_3_commands.cpp:80-211`.

### 8. Durante INIT, TESTEO, DEN, SIC, J, Yarn y Stitch
- `INIT`: si lee strings compilados. `UsbSmokeIdf/main/command_processor.cpp:1432-1448` dispara `app_head_state_manager_init()` y `app_head_fast_diag_start_init()`. `UsbSmokeIdf/main/head_fast_diag.cpp:711-899` usa `profile->init_sequence` y ejecuta `phase1_steps` y `phase2_steps`.
- `TESTEO`: usa estructura numerica compilada. `UsbSmokeIdf/main/command_processor.cpp:1450-1465` llama `app_head_fast_diag_start_testeo()`. `UsbSmokeIdf/main/head_fast_diag.cpp:497-709` usa `profile->testeo`.
- `J`: usa estructura numerica compilada. `UsbSmokeIdf/main/command_processor.cpp:629-714` construye lecturas y escrituras de J con `app_head_program_get_active_profile()`. `UsbSmokeIdf/main/head_state_manager.cpp:686-735`, `935-1008` y `1793-2107` consumen `profile->j`.
- `Yarn`: usa estructura numerica compilada. `UsbSmokeIdf/main/command_processor.cpp:1500-1508` y `UsbSmokeIdf/main/head_state_manager.cpp:1201-1226`, `1926-1945` consumen `profile->yarn`.
- `Stitch`: usa estructura numerica compilada. `UsbSmokeIdf/main/command_processor.cpp:1513-1521` y `UsbSmokeIdf/main/head_state_manager.cpp:1263-1288`, `1940-1945` consumen `profile->stitch`.
- `DEN`: usa estructura numerica compilada. `UsbSmokeIdf/main/command_processor.cpp:1572-1581` y `UsbSmokeIdf/main/head_state_manager.cpp:1321-1548`, `1950-1980` consumen `profile->den`.
- `SIC`: usa estructura numerica compilada. `UsbSmokeIdf/main/command_processor.cpp:1583-1601` y `UsbSmokeIdf/main/head_state_manager.cpp:1581-1609`, `1981-2006` consumen `profile->sic`.
- `Feet`: usa estructura numerica compilada. `UsbSmokeIdf/main/command_processor.cpp:1594-1638` y `UsbSmokeIdf/main/head_state_manager.cpp:1642-2024` consumen `profile->feet`.
- Ninguno de esos caminos consulta LittleFS.

### 9. Si hay llamadas como `fopen`, `fread`, `fgets`, `read`, `open`, `getline` o funciones LittleFS dentro del camino de ejecucion de esos comandos
- En el camino activo de `program_select_*`, `init`, `testeo`, `j`, `yarn`, `stitch`, `den`, `sic` y `feet`, no encontre `fopen`, `fread`, `fgets`, `open`, `read`, `getline` ni llamadas LittleFS.
- Donde si aparecen es en `UsbSmokeIdf/main/file_transfer.cpp` para almacenamiento/transferencia, y en `UsbSmokeIdf/main/command_head_program_runner.cpp` para ejecutar TXT desde LittleFS.

### 10. Si el archivo se carga una sola vez en RAM o se lee repetidamente
- Camino activo compilado: no se carga desde archivo; vive en flash/rodata y se consulta por puntero cada vez.
- Camino legacy file-backed: se reabre y se recorre cada vez que se selecciona o ejecuta una accion. `UsbSmokeIdf/main/command_head_program_runner.cpp:1104-1205` y `1807-1947` usan `fopen()`/`fgets()` sobre el TXT activo.

### 11. Si los archivos subidos estan actualmente desconectados del runtime real
- Si, del runtime activo que usa `program_select_*`.
- `UsbSmokeIdf/main/file_transfer.cpp:1503-1513` monta y limpia LittleFS para transferencia de archivos.
- `UsbSmokeIdf/main/command_processor.cpp:1343-1355` clasifica `FILE_*` y los manda a `file_transfer`.
- Ningun punto del camino activo conecta esos archivos con `head_program_runtime`, `head_fast_diag` o `head_state_manager`.
- Hay un runner legacy que si podria usarlos, pero no esta enganchado al dispatcher activo.

### 12. Flujo real actual desde la app hasta CAN
```text
Origen externo de comandos
  -> UsbSmokeIdf/main/usb_smoketest_main.cpp::app_core0_service_command() para FILE_* y wifi config
  -> UsbSmokeIdf/main/usb_smoketest_main.cpp::app_head_runtime_enqueue() para el resto
  -> UsbSmokeIdf/main/head_runtime.cpp::app_head_control_task()
  -> UsbSmokeIdf/main/command_processor.cpp::app_command_process_line()
  -> UsbSmokeIdf/main/command_processor.cpp::program_select_1/2/3, init, testeo, j, yarn, stitch, den, sic, feet
  -> UsbSmokeIdf/main/head_program_runtime.cpp::app_head_program_get_active_profile()
  -> UsbSmokeIdf/main/head_fast_diag.cpp / UsbSmokeIdf/main/head_state_manager.cpp
  -> CAN
```

### 13. Flujo separado de `FILE_BEGIN` / `FILE_DATA` / `FILE_END`
```text
Subida de archivo
  -> FILE_BEGIN
  -> UsbSmokeIdf/main/file_transfer.cpp::app_file_cmd_begin()
  -> crea /fs/.upload.tmp
  -> FILE_DATA
  -> UsbSmokeIdf/main/file_transfer.cpp::app_file_cmd_data()
  -> decodifica base64 y escribe el temporal
  -> FILE_END
  -> UsbSmokeIdf/main/file_transfer.cpp::app_file_cmd_end()
  -> cierra y renombra a /fs/<nombre>

Descarga de archivo
  -> FILE_GET
  -> UsbSmokeIdf/main/file_transfer.cpp::app_file_cmd_get()
  -> fopen() del archivo
  -> FILE_BEGIN
  -> FILE_GET_NEXT
  -> UsbSmokeIdf/main/file_transfer.cpp::app_file_cmd_get_next()
  -> fread() por chunks
  -> FILE_DATA ... FILE_END
```

### 14. Donde se unen ambos flujos, o si no se unen
- En el camino activo actual no se unen.
- `program_select_*` solo cambia un enum que apunta a un perfil compilado.
- `FILE_*` solo persiste archivos en `/fs`.
- El unico punto que podria unir ambos mundos es el runner legacy `UsbSmokeIdf/main/command_head_program_runner.cpp`, porque lee TXT desde LittleFS y ejecuta acciones; pero el dispatcher activo no llama a `app_head_program_process_line()`.

### 15. Punteros a memoria temporal, riesgo de lifetime o copia superficial
- En el camino activo de perfiles compilados, el puntero de perfil es estable: `UsbSmokeIdf/main/head_program_runtime.cpp:19-38` devuelve objetos estaticos `const`.
- `UsbSmokeIdf/main/head_fast_diag.cpp:995-1034` guarda `task_args->profile = app_head_program_get_active_profile()`, que apunta a datos estaticos, no a memoria temporal.
- `UsbSmokeIdf/main/head_state_manager.cpp:1062-1069` y `1799-2024` vuelven a obtener el mismo perfil estatico en cada tick.
- No hay copia superficial de un struct dinamico de perfil porque ese struct no existe.
- Riesgo real en el runner legacy: `UsbSmokeIdf/main/command_head_program_runner.cpp:378-385` devuelve un puntero a `static char stage_copy[48]`; la cadena es estable para el retorno inmediato, pero no es reentrante ni adecuada para uso concurrente prolongado.
- Riesgo secundario: `s_active_program` es estado global mutable en `head_program_runtime.cpp:7-55`; no es un problema de lifetime, pero si de concurrencia si otro hilo lo leyera sin el mismo orden de ejecucion.

## B. Inferencias
- El codigo conserva dos modelos: un runtime moderno de perfiles compilados y un runner legacy file-backed. Eso sugiere una migracion parcial o una coexistencia de caminos.
- `command_head_program_runner.cpp` parece ser una implementacion anterior o alternativa que hoy quedo fuera del camino activo, porque su entrada publica existe pero no encontre llamada real desde `command_processor.cpp` ni desde `head_runtime.cpp`.
- `program_name` en `HeadCommandProfile` parece metadata de UI o diagnostico. En el camino activo no vi consumo real de ese campo.
- `program_select_3` esta soportado por el firmware compilado, aunque el flujo file-backed legacy no esta integrado; eso sugiere que el selector de tres programas es la verdad actual y no el archivo TXT.

## C. Funcionalidad que todavia no existe
- No existe parser activo que transforme archivos subidos con `FILE_BEGIN/FILE_DATA/FILE_END` en una estructura de perfil consumida por `head_fast_diag` o `head_state_manager`.
- No existe `HeadRuntimeProfile` ni una estructura equivalente visible en el arbol.
- No existe `PROFILE_SELECT` como comando real en el firmware analizado.
- No existe enlace activo entre `FILE_SELECT` y el selector de programa que gobierna la maquina.
- No existe caller real a `app_head_program_process_line()` dentro del dispatcher activo.
- No existe persistencia del programa activo en LittleFS para el camino compilado de `program_select_*`.

## D. Flujo actual

### D.1 Camino activo de comandos de maquina
1. Una linea entra por el transporte.
2. `UsbSmokeIdf/main/usb_smoketest_main.cpp:2277-2329` (`app_core0_service_command`) decide si la linea es `FILE_*` o wifi config.
3. Si no es servicio core0, `UsbSmokeIdf/main/usb_smoketest_main.cpp:5301-5385` la encola con `app_head_runtime_enqueue()`.
4. `UsbSmokeIdf/main/head_runtime.cpp:161-206` (`app_head_control_task`) saca mensajes de la cola y llama `app_command_process_line()`.
5. `UsbSmokeIdf/main/command_processor.cpp:1136-1178` maneja `program_select_1/2/3`.
6. `UsbSmokeIdf/main/command_processor.cpp:1432-1465` maneja `init` y `testeo`.
7. `UsbSmokeIdf/main/command_processor.cpp:1494-1638` maneja J, Yarn, Stitch, Den, SIC y Feet.
8. `UsbSmokeIdf/main/head_fast_diag.cpp:711-899` y `UsbSmokeIdf/main/head_state_manager.cpp:686-2029` consumen el perfil activo compilado y terminan mandando CAN.

### D.2 Camino de archivos LittleFS
1. `UsbSmokeIdf/main/file_transfer.cpp:1503-1513` monta LittleFS.
2. `UsbSmokeIdf/main/file_transfer.cpp:1599-1685` parsea y despacha `FILE_*`.
3. `UsbSmokeIdf/main/file_transfer.cpp:866-1075` recibe subidas.
4. `UsbSmokeIdf/main/file_transfer.cpp:1365-1469` sirve descargas.
5. `UsbSmokeIdf/main/file_transfer.cpp:1201-1216` selecciona archivo y persiste `/fs/.selected`.
6. Ese camino no alimenta el runtime activo de perfiles compilados.

### D.3 Camino legacy file-backed
1. `UsbSmokeIdf/main/command_head_program_runner.cpp:3754-3792` define `app_head_program_process_line()` para `HEAD_PROGRAM_SELECT`, `HEAD_ACTION`, `HEAD_STOP` y `HEAD_STATUS`.
2. `UsbSmokeIdf/main/command_head_program_runner.cpp:2905-2952` selecciona un TXT desde `/fs`.
3. `UsbSmokeIdf/main/command_head_program_runner.cpp:1104-1205` lee conteos de modulo.
4. `UsbSmokeIdf/main/command_head_program_runner.cpp:1807-1947` abre el archivo y ejecuta el bloque de accion linea por linea.
5. Ese camino si usa LittleFS para controlar la maquina, pero no esta conectado al flujo activo visible en `command_processor.cpp` y `head_runtime.cpp`.

## E. Flujo necesario para usar perfiles desde archivos
- El archivo seleccionado en LittleFS tendria que convertirse en la fuente de verdad para `program_select_*` o reemplazarlo.
- La seleccion tendria que producir un objeto de runtime consumible por `head_fast_diag` y `head_state_manager`, o esos dos modulos tendrian que aprender a leer del archivo de forma coordinada.
- La vida util de cualquier buffer cargado tendria que definirse explicitamente; hoy el camino activo depende de objetos estaticos compilados.
- Si se quiere reutilizar lo que ya existe, el runner legacy tiene la logica de lectura y ejecucion desde TXT, pero hoy requiere ser conectado al dispatcher activo.
- Si se quiere mantener `HeadCommandProfile`, haria falta un parser de archivo a esa estructura y una estrategia clara de ownership.

## F. Archivos que habria que modificar, sin modificarlos todavia
- `UsbSmokeIdf/main/command_processor.cpp`: para cambiar como `program_select_*` decide el programa activo o para enrutar `HEAD_*`.
- `UsbSmokeIdf/main/head_program_runtime.cpp`: para dejar de depender de un enum estatico y aceptar seleccion dinamica.
- `UsbSmokeIdf/main/head_fast_diag.cpp`: para consumir un profile cargado desde archivo si se decide ese modelo.
- `UsbSmokeIdf/main/head_state_manager.cpp`: para leer perfiles dinamicos en vez de `HeadCommandProfile` compilado.
- `UsbSmokeIdf/main/file_transfer.cpp`: para enlazar la seleccion de archivo con el runtime de maquina.
- `UsbSmokeIdf/main/head_command_profile.h`: si se introduce una estructura dinamica nueva o cambian los campos necesarios.
- `UsbSmokeIdf/main/command_head_program_runner.cpp`: si se decide reutilizar el camino legacy file-backed en lugar de reimplementarlo.
- `UsbSmokeIdf/main/usb_smoketest_main.cpp`: si cambia la clasificacion de comandos entre servicio core0 y cola de runtime.
- `UsbSmokeIdf/main/head_runtime.cpp`: si cambia el punto de entrada o la politica de encolado para el nuevo flujo.
