# Diseño SQLite Acuratex v1

## Objetivo

Usar SQLite dentro de la app Windows como fuente de verdad de perfiles completos. La base no participa en el camino crítico de movimiento.

Flujo:

```text
Editor visual
→ SQLite
→ compilador de perfil
→ paquete .acx
→ transferencia al ESP32
→ validación
→ HeadRuntimeProfile en RAM
→ CAN
```

Durante una acción:

```text
botón
→ búsqueda en RAM
→ CAN
```

No se consulta SQLite, TXT ni LittleFS en cada botón.

## Punto de partida real

El firmware actual define un `HeadCommandProfile` con estas familias:

- `HeadInitCommandSequence`
- `HeadTesteoCommandProfile`
- `HeadMotionCommandProfile` para DEN, SIC y FEET
- `HeadJCommandProfile`
- `HeadCascadeCommandProfile` para YARN y STITCH
- `HeadStopCommandProfile`

El esquema SQL adjunto representa cada uno de esos campos y añade:

- perfiles y versiones;
- acciones dinámicas;
- pasos CAN/WAIT/STATUS;
- registro de paquetes exportados.

## Qué guarda SQLite

### Identidad y versiones

- `profiles`
- `profile_versions`

Un perfil mantiene una identidad estable y puede tener varias versiones. Las versiones publicadas deben tratarse como inmutables.

### INIT

- `init_config`
- `init_steps`

Conserva las dos fases, delays por paso y pausa entre fases.

### TESTEO

- `testeo_profiles`

Conserva ping, IDs de respuesta/reset, códigos, reintentos y timeouts.

### DEN, SIC y FEET

- `motion_modules`
- `motion_positions`
- `motion_sequences`

Conserva CAN ID, opcode, base de índice, cantidad de instancias, posiciones, secuencia normal, secuencia alternativa y periodos.

### J

- `j_modules`

Conserva cantidad de instancias/canales, registros inicial/ON/OFF y periodo.

### YARN y STITCH

- `cascade_modules`
- `cascade_addresses`

Conserva direcciones ordenadas, tamaño por instancia, valores ON/OFF y periodo.

### STOP

- `stop_profiles`

Puede representar tanto STOP local sin CAN como STOP con trama.

### Acciones

- `actions`
- `action_steps`

Representa acciones adicionales con pasos CAN, WAIT o STATUS. Todo esto se compila y carga una sola vez en RAM.

## Lo que hará la app

La app no debe mostrar SQL al usuario. Debe ofrecer:

- crear perfil;
- duplicar perfil;
- editar módulos;
- editar posiciones y secuencias;
- editar INIT/TESTEO;
- editar acciones;
- validar;
- publicar versión;
- compilar `.acx`;
- enviar al ESP32.

Capas sugeridas en C#:

```text
ProfileEditor UI
ProfileManager
ProfileValidator
ProfileRepository (SQLite)
AcxProfileCompiler
ProfileTransferService
```

## Primera importación

Los CPP actuales siguen siendo la fuente de verdad inicial.

Proceso:

1. Leer Programa 1 compilado.
2. Insertarlo en SQLite como `CPP_IMPORT`.
3. Leer Programa 2 compilado.
4. Insertarlo en SQLite como `CPP_IMPORT`.
5. Volver a generar un modelo desde SQLite.
6. Comparar campo por campo con los CPP.
7. Comparar las tramas CAN y los tiempos.
8. Recién después publicar las versiones SQLite.

Programa 3 modular no forma parte de los dos perfiles unificados iniciales, pero el esquema puede representar cualquier cantidad de perfiles.

## Paquete para el ESP32

SQLite vive en la PC. El ESP32 recibe un paquete de un solo perfil.

Nombre sugerido:

```text
perfil_<profile_key>_v<version>.acx
```

El paquete v1 debe contener:

- magic `ACX1`;
- versión del formato;
- longitud total;
- CRC32;
- identidad/nombre/versión;
- INIT;
- TESTEO;
- DEN/SIC/FEET;
- J;
- YARN/STITCH;
- STOP;
- acciones y pasos.

El formato físico exacto del `.acx` debe cerrarse después de medir:

- tamaño máximo de P1/P2;
- RAM disponible;
- alineación del ESP32-S3;
- necesidad de actualización parcial.

La recomendación es un formato binario versionado y con offsets, no una base SQLite dentro del ESP32.

## Carga en firmware

```text
PROFILE_SELECT|perfil.acx
→ abrir una sola vez
→ validar magic, versión, longitudes y CRC
→ cargar candidato
→ validar límites
→ construir punteros internos
→ intercambio atómico
→ cerrar archivo
```

Después, ninguna acción debe abrir el paquete otra vez.

## Validaciones obligatorias

- CAN ID dentro de rango;
- DLC entre 0 y 8;
- `length(data) == dlc`;
- bus 1 o 2;
- contadores dentro de máximos del firmware;
- posiciones `uint16`;
- direcciones/opcodes/registros `uint8`;
- secuencias referencian posiciones existentes;
- acciones sin nombres duplicados;
- orden de pasos continuo;
- WAIT dentro del máximo permitido;
- CRC correcto;
- versión de esquema soportada.

## Qué puede hacerse antes de volver a usar Codex

Ya queda definido:

- modelo lógico completo;
- esquema SQLite v1;
- correspondencia con el firmware;
- límites de tipos;
- separación entre gestión y ejecución;
- estrategia de importación P1/P2;
- dirección del paquete `.acx`.

## Siguiente tarea de Codex

La siguiente tarea no debe tocar firmware todavía. Debe:

1. agregar `Microsoft.Data.Sqlite` a la app;
2. ejecutar el esquema SQL;
3. crear modelos C#;
4. crear `ProfileRepository`;
5. crear migración/creación automática de la base;
6. crear importador P1/P2 desde los perfiles C# o una representación explícita;
7. generar pruebas de equivalencia;
8. no modificar el sistema modular;
9. no implementar aún el `.acx`.

Así se valida primero la base de datos antes de cambiar nuevamente el ESP32.
