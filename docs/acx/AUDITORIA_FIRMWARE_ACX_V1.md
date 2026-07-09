# AuditorÃ­a firmware ACX v1

Fecha de auditorÃ­a: 2026-07-05
Rama analizada: `lector-acx-firmware`

## Alcance

Esta nota recoge el estado real del firmware y del flujo de app/transferencia para preparar:

- la activaciÃ³n segura de archivos ACX v1 en el lector;
- la reconstrucciÃ³n de `HeadRuntimeProfile` en RAM;
- la preservaciÃ³n del sistema modular P1/P2/P3;
- la migraciÃ³n sin romper la ruta TXT existente.

No es una propuesta de implementaciÃ³n. Es un inventario tÃ©cnico con evidencia.

## Resumen ejecutivo

- La app Windows ya compila, exporta y valida ACX v1.
- La app ya transfiere ACX por `FILE_BEGIN` / `FILE_DATA` / `FILE_END` usando bloques de 32 bytes y lÃ­mite de 65536 bytes.
- El firmware guarda archivos en LittleFS bajo `/fs`.
- El firmware todavÃ­a no interpreta ACX como perfil runtime.
- El firmware sÃ­ tiene un `profile_store` completo en RAM con doble buffer y swap atÃ³mico.
- La ruta TXT sigue viva y hoy es la que materializa `HeadRuntimeProfile`.
- El sistema modular P1/P2/P3 sigue separado y no depende de ACX ni SQLite.

## 1. Almacenamiento y transferencia

### LittleFS y rutas internas

- Punto de montaje: `/fs`
- ParticiÃ³n: `storage`
- Metadatos de control:
  - `/fs/.selected`
  - `/fs/.upload.tmp`

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L17)
- [UsbSmokeIdf/partitions.csv](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/partitions.csv#L5)

La particiÃ³n LittleFS `storage` mide `0xF0000` bytes, es decir 960 KiB.

### FILE_BEGIN

`FILE_BEGIN`:

- valida nombre;
- valida tamaÃ±o decimal;
- rechaza tamaÃ±os mayores de 65536 bytes;
- rechaza si hay una subida activa;
- borra un temporal previo;
- abre `/fs/.upload.tmp`;
- deja la subida preparada en RAM.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L888)

### FILE_DATA

`FILE_DATA`:

- exige que exista una subida activa;
- exige Ã­ndice secuencial;
- decodifica Base64;
- rechaza si el total superarÃ­a el tamaÃ±o declarado o 65536 bytes;
- escribe en `/fs/.upload.tmp`.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L967)

### FILE_END

`FILE_END`:

- exige el nombre exacto de la subida;
- exige que `received_size == expected_size`;
- bloquea si el archivo objetivo estÃ¡ activo o siendo cargado;
- cierra el temporal;
- elimina el archivo final anterior si existÃ­a;
- renombra `/fs/.upload.tmp` al nombre final.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L1066)

### Comportamiento ante subida incompleta

- Si la subida queda a medias, el estado en RAM se limpia con `app_upload_reset()`.
- El archivo temporal puede quedar en LittleFS hasta que se inicie otra subida.
- No hay limpieza automÃ¡tica del temporal en el arranque.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L439)

### Comportamiento ante reinicio

- La RAM de transferencia se reinicia.
- `/fs/.selected` se vuelve a leer.
- Si `.selected` no existe o es invÃ¡lido, el firmware cae a perfil compilado P1.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L677)
- [UsbSmokeIdf/main/profile_store.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.cpp#L1451)

### FILE_BUSY

Casos observados:

- comenzar una subida si ya hay otra activa;
- terminar una subida si el destino estÃ¡ activo/cargÃ¡ndose o el sistema estÃ¡ ocupado;
- borrar un archivo si ese archivo estÃ¡ en carga.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L79)
- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L900)
- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L1082)
- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L1297)

### FILE_PROTECTED

`FILE_PROTECTED` se devuelve cuando se intenta borrar el archivo actualmente activo.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L1291)

### FILE_LIST

`FILE_LIST`:

- enumera `/fs`;
- oculta `.selected` y `.upload.tmp`;
- marca con `*` el archivo seleccionado.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L1140)

### FILE_INFO

`FILE_INFO`:

- devuelve tamaÃ±o;
- devuelve si el archivo estÃ¡ seleccionado.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L1356)

### FILE_GET

`FILE_GET`:

- inicia una descarga secuencial del archivo;
- responde con `FILE_BEGIN|<nombre>|<tamaÃ±o>`;
- `FILE_GET_NEXT` devuelve bloques de 32 bytes codificados en Base64;
- al final responde `FILE_END|<nombre>`.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L1407)
- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L1471)

### FILE_SELECT

`FILE_SELECT`:

- valida que el archivo exista;
- guarda `/fs/.selected`;
- actualiza `s_selected_name` en RAM.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L1232)

### /fs/.selected

`/fs/.selected`:

- se carga al montar LittleFS;
- si el nombre no es vÃ¡lido o el archivo no existe, se borra;
- persiste el archivo seleccionado entre reinicios.

Evidencia:

- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L677)
- [UsbSmokeIdf/main/file_transfer.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/file_transfer.cpp#L736)

### DÃ³nde queda un `.acx` sin `FILE_SELECT`

Queda almacenado como archivo opaco en:

```text
/fs/<nombre>.acx
```

Puede localizarse despuÃ©s con:

- `FILE_LIST`
- `FILE_INFO|<nombre>.acx`
- `FILE_GET|<nombre>.acx`

No queda activo como perfil runtime por sÃ­ solo.

## 2. `profile_store` y RAM

### Estructura de doble buffer

`profile_store` usa:

- `s_profiles[2]`
- `s_active_index`
- `s_profile_mutex`
- `s_loading`
- `s_loading_name`
- `s_generation`

Evidencia:

- [UsbSmokeIdf/main/profile_store.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.cpp#L23)

### `HeadRuntimeProfile`

`HeadRuntimeProfile` contiene:

- metadatos de origen y versiÃ³n;
- nombre de archivo;
- nombre de perfil;
- sistema;
- `init_script`;
- `command_profile` completo;
- buffers de texto para init y commands;
- arrays de motion, J, cascade;
- acciones;
- comandos runtime;
- contadores y CRC.

Evidencia:

- [UsbSmokeIdf/main/profile_store.h](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.h#L77)

### Swap atÃ³mico

El flujo es:

1. cargar candidato en el buffer inactivo;
2. validar;
3. tomar mutex;
4. incrementar generaciÃ³n;
5. cambiar `s_active_index`.

Evidencia:

- [UsbSmokeIdf/main/profile_store.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.cpp#L1224)

### Rollback si falla

SÃ­ existe:

- el buffer activo no se pisa hasta que el candidato ya pasÃ³ validaciÃ³n;
- si falla la carga o la validaciÃ³n, el perfil activo anterior sigue intacto.

Evidencia:

- [UsbSmokeIdf/main/profile_store.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.cpp#L1290)

### Bloqueos y concurrencia

Hay mutexes para:

- el profile store;
- el estado del runner;
- el estado del cabezal;
- el fast-diag.

El profile store ademÃ¡s marca `s_loading` para impedir reemplazos o borrados conflictivos.

### Vida Ãºtil y propiedad de punteros

Los punteros internos no son persistentes por sÃ­ solos.
Se reconstruyen con `app_profile_rebuild_view_pointers()` para apuntar al mismo `HeadRuntimeProfile` donde viven los buffers.

Evidencia:

- [UsbSmokeIdf/main/profile_store.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.cpp#L225)

### Â¿Existe una API pÃºblica equivalente a construir candidato, validar, cargar en inactivo, activar swap y conservar previo?

SÃ­, por piezas:

- construir candidato: `app_profile_load_from_file()`
- validar: `app_profile_validate()`
- cargar en buffer inactivo: `app_profile_select_internal()`
- activar con swap: `app_profile_apply()`
- conservar anterior si falla: por diseÃ±o del doble buffer

No existe todavÃ­a una Ãºnica API pÃºblica que encapsule todo ese contrato para ACX.

## 3. Ruta TXT actual

### Identidad del parser

El firmware sigue cargando perfiles TXT con este patrÃ³n de nombre:

- `cbz.uni.progN.txt`
- `cbz.mod.progN.txt`

Evidencia:

- [UsbSmokeIdf/main/profile_store.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.cpp#L113)

### QuÃ© hace el parser

`app_profile_load_from_file()`:

- copia defaults compilados del programa correspondiente;
- abre el archivo en `/fs`;
- lee lÃ­nea por lÃ­nea;
- reconoce `PROFILE_NAME`, `SYSTEM`, `PROGRAM`, `VERSION`, `CRC`, `INIT_SCRIPT`, `MODULE`, `BUTTON`, `BEGIN`, `END`;
- construye acciones y comandos en RAM;
- calcula CRC de cuerpo;
- rebasa punteros;
- valida al final.

Evidencia:

- [UsbSmokeIdf/main/profile_store.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.cpp#L891)

### QuÃ© es reutilizable

- validaciÃ³n de nombre;
- CRC de cuerpo;
- almacenamiento fijo en buffers;
- doble buffer;
- `HeadRuntimeCommand`;
- `HeadRuntimeAction`;
- `HeadRuntimeProfile`.

### QuÃ© es exclusivo de TXT

- `BEGIN` / `END`;
- comentarios y lÃ­neas libres;
- `WAIT`, `DELAY`, `status`, `can1`, `can2`, `send`;
- bÃºsqueda textual por bloque.

### Â¿Se lee el archivo en cada acciÃ³n o solo al cargar?

Depende de la ruta:

- `PROFILE_LIST` y `PROFILE_INFO` leen el archivo al consultar.
- `PROFILE_SELECT` carga el archivo y lo materializa en RAM.
- `HEAD_ACTION` activo hoy usa el perfil RAM cargado, no vuelve a depender del archivo para ejecutar la acciÃ³n.

Existe ademÃ¡s una funciÃ³n legacy que abre el TXT y busca `BEGIN|accion`, pero no se encontrÃ³ caller real fuera del propio archivo.

## 4. Sistema modular

### Rutas que siguen aisladas

- Programa 1: `head_program_1_commands.*`
- Programa 2: `head_program_2_commands.*`
- Programa 3: `head_program_3_commands.*`
- `program_select_1/2/3`
- `app_head_program_runtime.*`
- `app_head_state_manager.*`

Evidencia:

- [UsbSmokeIdf/main/head_program_runtime.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/head_program_runtime.cpp#L28)
- [UsbSmokeIdf/main/command_processor.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/command_processor.cpp#L1137)

### QuÃ© no debe tocarse para mantener modularidad

- `UsbSmokeIdf/main/head_program_1_commands.cpp`
- `UsbSmokeIdf/main/head_program_2_commands.cpp`
- `UsbSmokeIdf/main/head_program_3_commands.cpp`
- `UsbSmokeIdf/main/head_program_runtime.cpp`
- `UsbSmokeIdf/main/head_fast_diag.cpp`
- `UsbSmokeIdf/main/head_state_manager.cpp`

### SeparaciÃ³n actual

- `PROGRAM_SELECT_*` activa perfiles compilados.
- `PROFILE_SELECT` activa perfiles file-backed.
- `HEAD_ACTION` corre sobre el perfil activo en RAM.

## 5. Mapeo ACX v1 â†’ runtime

### Lo que sÃ­ tiene destino

- Metadata bÃ¡sica: `profile_name`, `program_number`, `version`.
- Init: secuencia y tiempos.
- Testeo: perfil CAN del test de arranque.
- Motion: DEN, SIC y FEET.
- J: `HeadCommandProfile.j`.
- Cascade: YARN y STITCH.
- Stop: `HeadCommandProfile.stop`.
- Actions: `HeadRuntimeAction` y `HeadRuntimeCommand`.

### Lo que no tiene destino directo

- `ProfileId`
- `ProfileVersionId`
- `Description`
- `Notes`
- `Enabled`
- `IsPublished`
- `SourceKind`
- `SourceCrc32`
- en actions: `Category`, `ActionId` si no se guarda aparte

### Lo que runtime tiene y ACX no trae como estado

- `generation`
- `origin`
- `source_size`
- `computed_crc32`
- `crc_ok`
- `line_number`
- revisiÃ³n de J runtime
- estados de movimiento y cascada

### Archivos fuente para el mapeo

- [docs/acx/ESPECIFICACION_ACX_V1.md](C:/Proyectos/AcuratexFastControl/docs/acx/ESPECIFICACION_ACX_V1.md)
- [app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxFormatConstants.cs](C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxFormatConstants.cs)
- [app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxProfileCompiler.cs](C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxProfileCompiler.cs)
- [app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxProfilePackageReader.cs](C:/Proyectos/AcuratexFastControl/app_windows/AcuratexControlApp/Services/Profiles/Acx/AcxProfilePackageReader.cs)
- [UsbSmokeIdf/main/profile_store.h](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.h)
- [UsbSmokeIdf/main/head_command_profile.h](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/head_command_profile.h)

## 6. Validaciones del lector ACX

### Validaciones por bloque

- Magic `ACX1`
- versiÃ³n de formato
- header de 64 bytes
- little-endian
- `fileSize` exacto
- `sectionDirectoryOffset`
- `sectionDirectorySize`
- `payloadOffset`
- `payloadSize`
- `payloadCRC32`
- `reserved` en cero
- `sectionCount`
- entry size de 24 bytes
- CRC por secciÃ³n
- secciÃ³n duplicada
- secciÃ³n fuera del archivo
- secciÃ³n solapada
- secciÃ³n vacÃ­a

### Validaciones semÃ¡nticas

- secciones obligatorias;
- `recordCount` correcto por secciÃ³n;
- `WaitMs <= 10000`;
- `Dlc <= 8`;
- `CanId` dentro del lÃ­mite admitido por el runtime;
- `ActionCount <= 128`;
- `CommandCount <= 768`;
- `Init steps per phase <= 192`;
- `Motion`, `Cascade`, `J`, `Stop` con conteos coherentes;
- cadenas UTF-8 vÃ¡lidas y dentro de longitudes mÃ¡ximas;
- orden de acciones y pasos.

### Validaciones que requieren memoria

- construir el candidato en `HeadRuntimeProfile`;
- almacenar textos de init y command text;
- copiar arrays de motion/cascade/J;
- materializar acciones y comandos runtime.

### Validaciones que pueden hacerse en streaming

- magic;
- versiÃ³n;
- header;
- offsets;
- tamaÃ±os;
- solapamientos;
- CRC de payload;
- CRC por secciÃ³n;
- truncamiento.

## 7. Memoria y particiones

### Particiones

`storage`:

- tamaÃ±o: `0xF0000`
- aproximado: 960 KiB

### PSRAM

PSRAM estÃ¡ soportada por el SoC, pero no habilitada en `sdkconfig`:

- `CONFIG_SOC_SPIRAM_SUPPORTED=y`
- `CONFIG_SPIRAM is not set`

### TamaÃ±o aproximado de `HeadRuntimeProfile`

Por la suma de sus miembros y el ABI de 32 bits, un buffer ocupa aproximadamente 83.7 KiB.
Con doble buffer, el consumo estÃ¡tico ronda 167 KiB.

### Riesgo de fragmentaciÃ³n

Muy bajo en el profile store actual, porque:

- no usa `new`/`malloc` para el contenido del perfil;
- usa arrays fijos y dos buffers estÃ¡ticos.

El riesgo aparece si el lector ACX se implementa con staging heap grande o colecciones dinÃ¡micas sin control.

### LÃ­mite seguro inicial

Para el lector firmware actual, el lÃ­mite prÃ¡ctico y seguro sigue siendo 64 KiB mientras exista el transporte FILE_* actual.

### Diferencia de lÃ­mites

- ACX del compilador C#: 1 MiB lÃ³gico.
- Transporte actual: 64 KiB.
- Lector ESP32 recomendable: 64 KiB inicial, parser streaming y candidato en buffer inactivo.

## 8. Arquitectura propuesta

### Flujo propuesto

```text
ACX almacenado
â†’ abrir archivo
â†’ validar header / directorio / CRC
â†’ calcular memoria
â†’ construir candidato en buffer inactivo
â†’ validar semÃ¡ntica
â†’ swap atÃ³mico
â†’ confirmar perfil activo
```

### Propiedades que debe conservar

- perfil anterior si falla;
- ninguna modificaciÃ³n parcial del buffer activo;
- ningÃºn acceso al archivo durante `HEAD_ACTION`;
- sistema modular independiente;
- ruta TXT intacta durante la migraciÃ³n;
- errores claros y recuperables.

### Archivos nuevos sugeridos

- `UsbSmokeIdf/main/acx_profile_loader.h`
- `UsbSmokeIdf/main/acx_profile_loader.cpp`
- `UsbSmokeIdf/main/acx_profile_layout.h` o equivalente, solo si hace falta centralizar offsets/IDs

### Archivos existentes que probablemente se modificarÃ­an

- [UsbSmokeIdf/main/profile_store.h](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.h)
- [UsbSmokeIdf/main/profile_store.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/profile_store.cpp)
- [UsbSmokeIdf/main/command_head_program_runner.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/command_head_program_runner.cpp)
- opcionalmente [UsbSmokeIdf/main/usb_smoketest_main.cpp](C:/Proyectos/AcuratexFastControl/UsbSmokeIdf/main/usb_smoketest_main.cpp)

### API pÃºblica nueva sugerida

- `app_profile_load_from_acx_file(...)`
- `app_profile_validate_acx_candidate(...)`
- `app_profile_select_source_aware(...)`
- `app_profile_is_acx_filename(...)`

### Estrategia de pruebas

- tests de lector ACX puro;
- tests de materializaciÃ³n a `HeadRuntimeProfile`;
- tests de rollback;
- tests de compatibilidad TXT;
- tests de coexistencia con P1/P2/P3.

## 9. Comandos futuros

### RelaciÃ³n recomendada

- `PROFILE_LIST`: lista perfiles runtime disponibles.
- `PROFILE_INFO`: informa metadatos de un perfil concreto.
- `PROFILE_SELECT`: carga y activa el perfil.
- `PROFILE_ACTIVE`: informa el perfil activo en RAM.
- `HEAD_ACTION`: ejecuta una acciÃ³n ya cargada.

### SeparaciÃ³n `FILE_SELECT` vs `PROFILE_SELECT`

Se deben mantener separados.

Motivo:

- `FILE_SELECT` es selecciÃ³n de archivo persistente.
- `PROFILE_SELECT` es activaciÃ³n de runtime.

Unificarlos sin una capa intermedia confunde almacenamiento con activaciÃ³n.

## 10. Pruebas propuestas

- ACX vÃ¡lido P1
- ACX vÃ¡lido P2
- perfil temporal con Actions
- CRC global incorrecto
- CRC por secciÃ³n incorrecto
- magic incorrecto
- versiÃ³n incorrecta
- offset fuera del archivo
- solapamiento
- secciÃ³n faltante
- secciÃ³n duplicada
- DLC invÃ¡lido
- cantidades excesivas
- archivo truncado
- falta de memoria
- cancelaciÃ³n de carga
- perfil activo conservado ante error
- swap atÃ³mico
- reinicio
- cambio repetido de perfil
- `HEAD_ACTION` sin leer archivos
- modular P1/P2/P3 intacto

## 11. ConfirmaciÃ³n final del estado auditado

- Rama actual: `lector-acx-firmware`
- ACX almacenado: `/fs/<nombre>.acx`
- `profile_store`: doble buffer estÃ¡tico con mutex y generaciÃ³n
- doble buffer: sÃ­, con swap por Ã­ndice activo
- ruta TXT: `cbz.uni.progN.txt` / `cbz.mod.progN.txt`
- separaciÃ³n modular/unificado: intacta
- mapeo ACX â†’ `HeadRuntimeProfile`: viable, pero no lossless
- lÃ­mite fÃ­sico de LittleFS: 960 KiB
- lÃ­mite de transporte: 64 KiB
- archivos propuestos para implementaciÃ³n: los del mÃ³dulo ACX nuevo + `profile_store`/runner
- sin cambios de cÃ³digo en esta auditorÃ­a
- sin commit
- sin push

## 12. VerificaciÃ³n git

Se ejecutÃ³ verificaciÃ³n final de solo lectura:

- `git branch --show-current`
- `git status --short --untracked-files=all`
- `git diff --stat`
- `git diff --check`
- `git diff --name-only -- "app_windows/"`
- `git diff --name-only -- "UsbSmokeIdf/"`

Resultado:

- no hay cambios tracked en el Ã¡rbol de trabajo;
- solo aparecen artefactos untracked ajenos a esta documentaciÃ³n.
