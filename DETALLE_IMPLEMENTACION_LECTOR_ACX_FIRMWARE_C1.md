DETALLE IMPLEMENTACION LECTOR ACX FIRMWARE C1

Alcance

Esta fase deja el lector ACX aislado, capaz de abrir un archivo ACX, validar su estructura y materializar un HeadRuntimeProfile candidato proporcionado por el caller, sin activar el perfil activo, sin hacer swap y sin tocar PROFILE_SELECT.

Archivos creados

- UsbSmokeIdf/main/acx_profile_layout.h
- UsbSmokeIdf/main/acx_profile_loader.h
- UsbSmokeIdf/main/acx_profile_loader.cpp
- DETALLE_IMPLEMENTACION_LECTOR_ACX_FIRMWARE_C1.md

Archivo de build actualizado

- UsbSmokeIdf/main/CMakeLists.txt ahora incluye acx_profile_loader.cpp en el target principal.

API publica

- app_acx_profile_load_candidate(const char *file_path, HeadRuntimeProfile *candidate, AcxProfileLoadResult *result)
- app_acx_profile_load_candidate_from_bytes(const uint8_t *bytes, size_t size, const char *logical_name, HeadRuntimeProfile *candidate, AcxProfileLoadResult *result)

Layout leido

- Magic ACX1
- Formato v1, version 1
- Header fijo de 64 bytes
- Directorio de 8 entradas de 24 bytes
- Ocho secciones soportadas: Metadata, Init, Testeo, Motion, J, Cascade, Stop y Actions
- CRC32 IEEE incremental sobre payload y CRC32 por seccion
- CAN ID maximo del formato ACX v1: 0x1FFFFFFF
- La validacion del formato acepta CAN ID extendido segun ACX v1; la ruta fisica CAN se mantiene fuera de C1.

Estrategia de lectura

- No se carga el archivo ACX completo a RAM
- Se usan lecturas acotadas y validacion incremental
- El CRC32 del payload se calcula mientras se leen directorio y secciones
- El lector usa un buffer de lectura acotado de 256 bytes para strings y lecturas pequenas
- Adicionalmente usa una reserva temporal acotada para rastrear nombres de acciones y detectar duplicados: `APP_PROFILE_MAX_ACTIONS * (APP_PROFILE_MAX_ACTION_NAME_LEN + 1)`
- Esa reserva no depende del tamano del archivo ACX
- `AcxProfileLoadResult.temp_buffer_max` reporta el peak temporal real de la carga, incluyendo el buffer de lectura y la reserva de Actions, no solo 256 bytes
- Queda pendiente evaluar en C2 si esa reserva se reemplaza por un arreglo estatico o por otra estrategia sin heap
- La carga falla antes de materializar si la estructura previa no valida

Validaciones estructurales

- Archivo existente
- Tamano minimo y maximo 64 KiB
- Magic correcto
- Version soportada
- Endianess little-endian
- Header exacto de 64 bytes
- `header.file_size` coincide obligatoriamente con el tamano real del archivo o del buffer fuente
- Rechaza tanto archivos truncados como archivos con bytes extra al final
- payload_offset + payload_size = file_size
- Directorio dentro del archivo
- section_count = 8
- section_directory_size = 8 * 24
- Offsets y tamanos sin overflow
- Secciones dentro del archivo
- Secciones contiguas sin huecos
- Secciones sin solapamiento
- Secciones obligatorias presentes exactamente una vez
- Secciones duplicadas rechazadas
- Reserved en cero
- CRC32 global del payload
- CRC32 individual de cada seccion

Materializacion runtime

El parser llena el candidato recibido por el caller y reconstruye la vista interna de punteros al final de la carga, sin conservar punteros al archivo ni a buffers temporales.
Nota de origen: mientras no exista un origen ACX especifico en profile_store, el candidato queda file-backed con origin APP_PROFILE_ORIGIN_FILE y system = "ACX". El origen ACX especifico queda pendiente para C2/C3.
Nota de programa: ACX v1 almacena program_number como campo del paquete, pero C1 solo materializa 1, 2 o 3 porque el runtime actual solo mapea APP_HEAD_PROGRAM_1, APP_HEAD_PROGRAM_2 y APP_HEAD_PROGRAM_3. Los valores desconocidos se rechazan antes de materializar; no existe fallback silencioso a Programa 1.

Mapa principal

- Metadata -> HeadRuntimeProfile y metadatos auxiliares
- Init -> HeadCommandProfile.init_sequence y texto de init
- Testeo -> HeadCommandProfile.testeo
- Motion -> HeadCommandProfile.den, sic y feet
- J -> HeadCommandProfile.j
- Cascade -> HeadCommandProfile.yarn y stitch
- Stop -> HeadCommandProfile.stop
- Actions -> HeadRuntimeAction y HeadRuntimeCommand

Campos ignorados en RAM

Se validan pero no se conservan en memoria si no tienen destino actual:

- ProfileId
- ProfileVersionId
- Description
- Notes
- IsPublished
- SourceKind
- SourceCrc32
- Category
- ActionId

Manejo de Enabled

- Una accion deshabilitada se lee y valida, pero no se materializa como accion ejecutable
- El parser consume todos los pasos de la accion deshabilitada y valida orden, tipo, bus, DLC, CAN ID, WAIT, STATUS, reserved y payload
- El conteo total puede validarse, pero la accion deshabilitada no entra al set ejecutable

Limites de runtime aplicados

- APP_PROFILE_MAX_ACTIONS = 128
- APP_PROFILE_MAX_COMMANDS = 768
- APP_PROFILE_MAX_INIT_STEPS = 192
- APP_PROFILE_MAX_MOTION_SEQUENCE = 16
- APP_PROFILE_MAX_POSITIONS = 16
- APP_PROFILE_MAX_YARN_ADDRESSES = 16
- APP_PROFILE_MAX_STITCH_ADDRESSES = 32
- APP_HEAD_STATE_MAX_J = 8
- APP_HEAD_STATE_MAX_YARN = 2
- APP_HEAD_STATE_MAX_SIC = 2
- APP_HEAD_STATE_MAX_FEET = 2

Errores principales

- FILE_NOT_FOUND
- FILE_TOO_SMALL
- FILE_TOO_LARGE
- INVALID_MAGIC
- UNSUPPORTED_VERSION
- INVALID_HEADER
- INVALID_DIRECTORY
- INVALID_OFFSET
- OVERLAPPING_SECTIONS
- MISSING_SECTION
- DUPLICATE_SECTION
- PAYLOAD_CRC_MISMATCH
- SECTION_CRC_MISMATCH
- INVALID_RECORD
- INVALID_COUNT
- INVALID_STRING
- RUNTIME_CAPACITY_EXCEEDED
- IO_ERROR

Build ejecutado

- Entorno local ESP-IDF 6.0 en C:/Espressif/v6.0/esp-idf
- Build del firmware exitoso con idf.py build
- Resultado: UsbSmokeIdf.bin generado correctamente
- Advertencias actuales del loader: funciones auxiliares sin uso dentro de acx_profile_loader.cpp

Pruebas

No existe un runner host adecuado dentro del repositorio para ejecutar el parser ACX en esta sesion. La carga se valido por compilacion del firmware y por revisiones directas del codigo.

Limitacion actual

- No hay compilador nativo disponible en la sesion para un runner host simple
- No se ejecuto hardware real en esta sesion
- La fase C1 deja solo un candidato HeadRuntimeProfile listo para integracion futura, pero no activa perfiles ni hace swap
- C1 no decide la ruta fisica CAN
- ACX v1 almacena program_number como campo del paquete
- C1 acepta solo 1, 2 o 3 porque el runtime actual solo mapea APP_HEAD_PROGRAM_1, APP_HEAD_PROGRAM_2 y APP_HEAD_PROGRAM_3
- Esta es una limitacion de materializacion runtime C1, no una expansion del formato ACX
- No existe mapeo silencioso de valores desconocidos a Programa 1; valores fuera de rango se rechazan antes de materializar
- Perfiles dinamicos futuros deberan usar un program_number compatible o ampliar el runtime en una fase posterior

Pendiente para C2

- Integracion con buffer inactivo
- Validacion final antes de activar
- Swap atomico
- PROFILE_SELECT y activacion de perfil ACX quedan pendientes para C2
- Hooks de prueba ejecutable cuando exista runner adecuado