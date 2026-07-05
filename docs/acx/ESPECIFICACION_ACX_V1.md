# Especificacion ACX v1

## Objetivo

ACX v1 es un paquete binario portatil para exportar una version completa de perfil desde SQLite a un archivo `.acx`.
El objetivo es que el paquete sea determinista, validable y util para una futura carga en ESP32 sin depender de punteros, alineacion del compilador ni consultas SQLite durante la serializacion.

## Principios

- Extension: `.acx`
- Magic ASCII: `ACX1`
- Endianess: little-endian en todos los enteros de mas de 1 byte
- Sin punteros
- Sin `size_t`
- Sin serializar estructuras C/C++ directas con `fwrite`
- Offsets relativos al inicio del archivo
- Validacion completa de longitudes, offsets y CRC
- Payload con CRC32
- Formato determinista: la misma version del mismo perfil produce los mismos bytes

## Layout general

```text
0x00  +------------------------------+
      | Header ACX v1 (64 bytes)     |
0x40  +------------------------------+
      | Directorio de secciones      |
      | N x 24 bytes                 |
      +------------------------------+
      | Seccion 1                    |
      +------------------------------+
      | Seccion 2                    |
      +------------------------------+
      | ...                          |
      +------------------------------+
      | Seccion N                    |
      +------------------------------+
```

El campo `payload_offset` del header vale `0x40` en v1.
El payload empieza inmediatamente despues del header y contiene primero el directorio y luego todas las secciones.

## Header ACX v1

Tamano fijo: 64 bytes.

| Offset | Size | Campo | Tipo | Descripcion |
| --- | --- | --- | --- | --- |
| 0x00 | 4 | magic | char[4] | Siempre `ACX1` |
| 0x04 | 2 | format_version | uint16 | Version del formato, actualmente 1 |
| 0x06 | 2 | header_size | uint16 | Siempre 64 |
| 0x08 | 4 | file_size | uint32 | Tamano total del archivo |
| 0x0C | 4 | section_directory_offset | uint32 | Offset del directorio, siempre 64 en v1 |
| 0x10 | 4 | section_directory_size | uint32 | Tamano del directorio |
| 0x14 | 4 | payload_offset | uint32 | Inicio del payload, siempre 64 en v1 |
| 0x18 | 4 | payload_size | uint32 | Tamano del payload completo |
| 0x1C | 4 | payload_crc32 | uint32 | CRC32 del payload completo |
| 0x20 | 4 | profile_id | uint32 | Id del perfil en SQLite |
| 0x24 | 4 | profile_version_id | uint32 | Id de la version en SQLite |
| 0x28 | 4 | program_number | int32 | Numero de programa |
| 0x2C | 4 | version_number | int32 | Numero de version |
| 0x30 | 4 | schema_version | uint32 | Version del esquema de perfil |
| 0x34 | 2 | section_count | uint16 | Cantidad de entradas del directorio |
| 0x36 | 2 | reserved | uint16 | Debe ser 0 |
| 0x38 | 4 | reserved2 | uint32 | Debe ser 0 |
| 0x3C | 4 | reserved3 | uint32 | Debe ser 0 |

## Directorio de secciones

Cada entrada ocupa 24 bytes.

| Offset relativo | Size | Campo | Tipo | Descripcion |
| --- | --- | --- | --- | --- |
| 0x00 | 2 | section_id | uint16 | Id logico de la seccion |
| 0x02 | 2 | section_version | uint16 | Version interna de la seccion |
| 0x04 | 4 | offset | uint32 | Offset de la seccion dentro del archivo |
| 0x08 | 4 | size | uint32 | Tamano de la seccion en bytes |
| 0x0C | 4 | record_count | uint32 | Conteo logico de registros |
| 0x10 | 4 | crc32 | uint32 | CRC32 de la seccion |
| 0x14 | 4 | reserved | uint32 | Debe ser 0 |

Reglas:

- El directorio se ordena por el orden de escritura del compilador.
- `offset` y `size` se validan contra el tamano total del archivo.
- No se permiten solapamientos entre secciones.
- `crc32` se calcula sobre el cuerpo de la seccion, no sobre el header.

## IDs de seccion

| Id | Nombre | Record count esperado |
| --- | --- | --- |
| 1 | Metadata | 1 |
| 2 | Init | total de pasos INIT |
| 3 | Testeo | 1 |
| 4 | Motion | 3 |
| 5 | J | 1 |
| 6 | Cascade | 2 |
| 7 | Stop | 1 |
| 8 | Actions | cantidad de acciones |

## Estructura de cada seccion

### 1. Metadata

Contiene la metadata completa del perfil.

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| profile_id | uint32 | Id de perfil |
| profile_version_id | uint32 | Id de version |
| enabled | uint8 | 1 = habilitado, 0 = deshabilitado |
| is_published | uint8 | 1 = publicado, 0 = borrador |
| source_kind | uint8 | 1=SQLITE, 2=CPP_IMPORT, 3=MANUAL |
| has_source_crc32 | uint8 | 1 si source_crc32 esta presente |
| program_number | int32 | Numero de programa |
| version_number | int32 | Numero de version |
| schema_version | uint32 | Version del esquema |
| profile_key_length | uint16 | Longitud UTF-8 de profile_key |
| display_name_length | uint16 | Longitud UTF-8 de display_name |
| description_length | uint16 | Longitud UTF-8 de description |
| notes_length | uint16 | Longitud UTF-8 de notes |
| source_crc32 | uint32 | 0 si `has_source_crc32 = 0` |
| profile_key | bytes | UTF-8 sin terminador |
| display_name | bytes | UTF-8 sin terminador |
| description | bytes | UTF-8 sin terminador |
| notes | bytes | UTF-8 sin terminador |

### 2. Init

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| phase1_step_delay_ms | uint32 | Retardo entre pasos de fase 1 |
| phase_gap_ms | uint32 | Retardo entre fase 1 y fase 2 |
| phase2_step_delay_ms | uint32 | Retardo entre pasos de fase 2 |
| phase1_step_count | uint16 | Conteo de pasos fase 1 |
| phase2_step_count | uint16 | Conteo de pasos fase 2 |
| steps | records | Pasos fase 1 primero, luego fase 2 |

#### Formato de INIT step

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| step_order | uint16 | Orden secuencial desde 0 |
| phase | uint8 | 1 o 2 |
| step_kind | uint8 | 1=CAN, 2=WAIT, 3=STATUS |
| bus | uint8 | 1 o 2 para CAN, 0 en WAIT/STATUS |
| dlc | uint8 | DLC del frame CAN, 0 en WAIT/STATUS |
| raw_text_length | uint16 | Longitud UTF-8 de raw_text |
| can_id | uint32 | CAN id exacto, 0 en WAIT/STATUS |
| wait_ms | uint32 | Delay exacto, 0 en CAN/STATUS |
| raw_text | bytes | Texto original importado desde SQLite |
| data | bytes | Longitud exacta igual a `dlc` en CAN, 0 en otros pasos |

Normalizacion de INIT:

- CAN: `bus`, `can_id`, `dlc` y `data` se conservan.
- WAIT: `can_id`, `dlc` y `data` se escriben como 0 o vacio.
- STATUS: todos los campos de CAN y espera se escriben como 0 o vacio.

### 3. Testeo

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| ping_can_id | uint32 | CAN id del ping |
| ping_dlc | uint8 | DLC del ping |
| response_can_id | uint32 | CAN id esperado de respuesta |
| reset_can_id | uint32 | CAN id del reset |
| reset_dlc | uint8 | DLC del reset |
| success_code | uint8 | Codigo de exito |
| missing_expansion_code | uint8 | Codigo de expansion ausente |
| missing_force_code | uint8 | Codigo de fuerza ausente |
| force_board_1_code | uint8 | Codigo board 1 |
| force_board_2_code | uint8 | Codigo board 2 |
| max_tries | uint16 | Maximo de intentos |
| response_timeout_ms | uint32 | Timeout de respuesta |
| retry_delay_ms | uint32 | Retardo entre reintentos |
| reset_debounce_ms | uint32 | Debounce del reset |
| ping_data | bytes | Longitud `ping_dlc` |
| reset_data | bytes | Longitud `reset_dlc` |

### 4. Motion

La seccion almacena tres modulos: DEN, SIC y FEET.

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| module_count | uint16 | Siempre 3 |
| modules | records | DEN, SIC y FEET |

#### Formato de modulo Motion

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| module_kind | uint8 | 1=DEN, 2=SIC, 3=FEET |
| opcode | uint8 | Opcode exacto |
| motor_index_base | uint8 | Base de motores |
| reserved | uint8 | Debe ser 0 |
| instance_count | uint16 | Cantidad de instancias |
| position_count | uint16 | Cantidad de posiciones |
| run_sequence_count | uint16 | Cantidad de pasos RUN |
| alternate_run_sequence_count | uint16 | Cantidad de pasos ALTERNATE |
| can_id | uint32 | CAN id exacto |
| run_period_ms | uint32 | Periodo RUN |
| alternate_run_period_ms | uint32 | Periodo ALTERNATE |
| positions | uint16[] | `position_count` elementos |
| run_sequence | uint8[] | `run_sequence_count` elementos |
| alternate_run_sequence | uint8[] | `alternate_run_sequence_count` elementos |

### 5. J

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| can_id | uint32 | CAN id |
| opcode | uint8 | Opcode |
| instance_index_base | uint8 | Base de indice |
| instance_count | uint16 | Cantidad de instancias |
| channel_count | uint8 | Cantidad de canales |
| initial_register | uint8 | Registro inicial |
| on_all_register | uint8 | Registro ON |
| off_all_register | uint8 | Registro OFF |
| run_period_ms | uint32 | Periodo |

### 6. Cascade

La seccion almacena dos modulos: YARN y STITCH.

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| module_count | uint16 | Siempre 2 |
| modules | records | YARN y STITCH |

#### Formato de modulo Cascade

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| module_kind | uint8 | 1=YARN, 2=STITCH |
| opcode | uint8 | Opcode |
| addresses_per_instance | uint16 | Direcciones por instancia |
| instance_count | uint16 | Cantidad de instancias |
| address_count | uint16 | Cantidad total de direcciones |
| can_id | uint32 | CAN id |
| run_period_ms | uint32 | Periodo |
| on_value | uint8 | Valor ON |
| off_value | uint8 | Valor OFF |
| reserved | uint16 | Debe ser 0 |
| addresses | uint8[] | `address_count` elementos |

### 7. Stop

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| sends_can_frame | uint8 | 1 si el STOP envia frame |
| frame_present | uint8 | 1 si existe frame |
| reserved | uint16 | Debe ser 0 |
| can_id | uint32 | CAN id del frame, 0 si no existe |
| dlc | uint8 | DLC del frame, 0 si no existe |
| reserved2 | uint8 | Debe ser 0 |
| data | bytes | Longitud `dlc` |

### 8. Actions

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| action_count | uint16 | Cantidad de acciones |
| actions | records | Acciones dinamicas |

#### Formato de accion

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| action_id | uint32 | Id de accion en SQLite |
| step_count | uint16 | Cantidad de pasos |
| enabled | uint8 | 1 habilitada, 0 deshabilitada |
| reserved | uint8 | Debe ser 0 |
| action_name_length | uint16 | Longitud UTF-8 de action_name |
| category_length | uint16 | Longitud UTF-8 de category |
| reserved2 | uint32 | Debe ser 0 |
| action_name | bytes | UTF-8 sin terminador |
| category | bytes | UTF-8 sin terminador |
| steps | records | Pasos de la accion |

#### Formato de action step

| Campo | Tipo | Descripcion |
| --- | --- | --- |
| step_order | uint16 | Orden secuencial desde 0 |
| step_kind | uint8 | 1=CAN, 2=WAIT, 3=STATUS |
| bus | uint8 | 1 o 2 para CAN, 0 en WAIT/STATUS |
| dlc | uint8 | DLC exacto, 0 en WAIT/STATUS |
| reserved | uint8 | Debe ser 0 |
| can_id | uint32 | CAN id exacto, 0 en WAIT/STATUS |
| wait_ms | uint32 | Delay exacto, 0 en CAN/STATUS |
| reserved2 | uint16 | Debe ser 0 |
| data | bytes | Longitud exacta igual a `dlc` |

## Strings

Reglas:

- Todas las cadenas se serializan como UTF-8 sin BOM.
- No hay terminador NUL.
- Cada cadena lleva su longitud en bytes antes del contenido.
- La longitud se valida antes de escribir.
- Para `profile_key`, `display_name`, `action_name` y `raw_text` se aplican limites de firmware.
- Para `description`, `notes` y `category` el limite ACX es `uint16.MaxValue` bytes, porque el formato usa longitudes `uint16`.

## Alineacion y padding

- No hay alineacion implicita.
- No hay padding entre campos salvo bytes `reserved` escritos de forma explicita.
- Todas las secciones se escriben empaquetadas y en orden estable.
- Los arrays se escriben como bytes contiguos sin relleno.

## CRC32

- Algoritmo: CRC32 IEEE standard
- Polinomio: `0xEDB88320`
- Inicial: `0xFFFFFFFF`
- Final XOR: `0xFFFFFFFF`
- Cobertura: todo el payload, es decir, directorio de secciones + cuerpos de seccion
- Cada entrada del directorio tambien guarda un CRC32 de la seccion individual

## Limites maximos

| Limite | Valor ACX v1 | Fuente / justificacion |
| --- | --- | --- |
| CAN DLC | 8 | Firmware `APP_HEAD_PROFILE_MAX_DLC` |
| Posiciones por modulo Motion | 16 | Firmware `APP_PROFILE_MAX_POSITIONS` |
| Secuencia RUN / ALTERNATE | 16 | Firmware `APP_PROFILE_MAX_MOTION_SEQUENCE` |
| Pasos INIT por fase | 192 | Firmware `APP_PROFILE_MAX_INIT_STEPS` |
| Pasos de acciones totales | 768 | Firmware `APP_PROFILE_MAX_COMMANDS` |
| Acciones dinamicas | 128 | Firmware `APP_PROFILE_MAX_ACTIONS` |
| Nombre de perfil | 64 bytes UTF-8 | Firmware `APP_PROFILE_MAX_PROFILE_NAME_LEN` |
| Profile key | 48 bytes UTF-8 | Firmware `APP_PROFILE_MAX_FILENAME_LEN` usado como clave exportable |
| Nombre de accion | 48 bytes UTF-8 | Firmware `APP_PROFILE_MAX_ACTION_NAME_LEN` |
| Linea / raw_text | 224 bytes UTF-8 | Firmware `APP_PROFILE_MAX_LINE_LEN` |
| DEN instances | 8 | Firmware/runtime actual |
| SIC instances | 2 | Firmware/runtime actual |
| FEET instances | 2 | Limite ACX v1 explicitado; el firmware no publica una constante separada |
| YARN instances | 2 | Firmware/runtime actual |
| STITCH instances | 4 | Firmware/runtime actual |
| CAN ids | 29 bits validos, almacenados en uint32 | Limite del perfil actual |
| Tamano maximo del archivo | 1,048,576 bytes | Limite provisional de implementacion ACX v1 (se revisara con el lector ESP32 y mediciones reales) |

- Este limite es provisional y se revisara cuando exista el lector ACX del ESP32 y se midan particiones, almacenamiento y RAM reales.

## Validacion

Un lector ACX v1 debe rechazar:

- magic distinto de `ACX1`
- version de formato no soportada
- tamano de header distinto de 64
- tamanos de archivo, payload o directorio incoherentes
- archivos mayores que 1 MiB (1,048,576 bytes) antes de leer el directorio de secciones
- offsets fuera del archivo
- secciones solapadas
- secciones con tamano 0
- CRC de payload incorrecto
- CRC de seccion incorrecto
- conteos de registros incoherentes
- nombres demasiado largos
- CAN ids fuera de rango
- DLC mayor que 8
- datos CAN con longitud distinta al DLC
- pasos fuera de orden
- perfiles incompletos
- versiones inexistentes en la fuente SQLite

## Ejemplos

### Programa 1

- Archivo: `programa-1_v1.acx`
- Tamano: `5471` bytes
- CRC32 payload: `0x5CAE00B7`
- FEET: vacio
- Actions: `0`

- Nota: no se encontro evidencia local reproducible de `0x5CAF00B7`; el valor real verificado para Programa 1 es `0x5CAE00B7`.

### Programa 2

- Archivo: `programa-2_v1.acx`
- Tamano: `5477` bytes
- CRC32 payload: `0x2CFB7499`
- FEET: configurado
- Actions: `0`

## Carga futura en ESP32

Flujo previsto:

1. Abrir el archivo `.acx`.
2. Verificar magic, version, tamano total y CRC32 del payload.
3. Leer el directorio de secciones.
4. Validar offsets, tamanos y CRC32 de cada seccion.
5. Reconstruir la metadata y los perfiles de comando en memoria.
6. Resolver secciones conocidas por `section_id`.
7. Saltar secciones desconocidas usando `offset` y `size`.
8. Cargar el resultado en la estructura runtime sin consultar SQLite.

## Compatibilidad futura

- ACX v1 reserva un directorio de secciones para permitir extensiones sin romper el layout basico.
- Nuevas secciones deben recibir un `section_id` nuevo y mantener estable el significado de los ids existentes.
- Un lector futuro puede saltar secciones desconocidas mientras el header y el directorio sigan siendo validos.
- Un cambio incompatible de layout debe ir a una nueva version mayor del formato.
- Los lectores v1 deben rechazar versiones de formato no soportadas en vez de asumir compatibilidad.