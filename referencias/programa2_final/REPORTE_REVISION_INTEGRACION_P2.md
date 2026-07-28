# Revisión de integración — Programa 2 Unificado

> Nota de estado (2026-07-25): las secciones iniciales conservan el diagnóstico de la auditoría previa. La sección **Resultado de la reintegración arquitectónica** al final documenta el código actual y prevalece cuando describe un problema ya corregido.

Fuente revisada: `REVISION_INTEGRACION_P2.zip`.

## Veredicto

El Programa 2 **funciona como una ruta independiente y está parcialmente integrado con la arquitectura común**, pero todavía conserva varios acoplamientos especiales que no conviene repetir para los cinco programas restantes.

No recomiendo hacer commit final de esta versión sin una refactorización pequeña de arquitectura y una corrección de seguridad de STOP.

## Lo que está bien integrado

- Usa el mismo `HeadCommandProfile` y el mismo runtime Unificado que P1/P3.
- Reutiliza el `head_state_manager` para DEN, SIC, J, Yarn y Stitch.
- Reutiliza el mismo driver TWAI/CAN, colas y arbitraje.
- La UI P2 ya está separada en `CabezalDashboardUnificadoPrograma2.razor/.cs`.
- La UI P2 actual del ZIP ya usa el lenguaje visual de la app; las expresiones Razor literales de la captura anterior ya están corregidas.
- P1, P3 y Modular no aparecen modificados en el diff contra `035fc23`.
- La secuencia automática fue convertida en ejecución no bloqueante y cancelable.

## Acoplamientos que deben corregirse antes de escalar

### 1. El procesador Unificado conoce demasiado a P2

`command_unified_head_processor.cpp` contiene:

- `app_unified_program2_direct()`;
- `app_unified_program2_all_outputs()`;
- preparación especial de J;
- comandos de Feet, Stitch, SIC y secuencia;
- un `if (profile->program_id == APP_HEAD_PROGRAM_2)`.

Con cinco programas más, este archivo se convertirá en una cadena de excepciones P2/P3/P4/etc.

**Recomendación:** crear un manejador de comandos especiales por programa o una tabla de operaciones/capacidades. El procesador principal debe despachar, no conocer los detalles de cada programa.

### 2. El state manager común llama directamente una secuencia de P2

`head_state_manager.cpp` llama directamente:

`app_unified_program2_sequence_tick(...)`

Esto acopla el motor común al Programa 2.

**Recomendación:** crear `head_sequence_runner.cpp/.h` genérico. P2 debe aportar únicamente una definición de secuencia y sus datos.

### 3. El frame builder “común” contiene formatos llamados PROGRAM2

`head_command_profile.h` y `head_command_frame_builder.cpp` contienen:

- `APP_HEAD_FRAME_LAYOUT_PROGRAM2_DEN`;
- `PROGRAM2_SIC`;
- `PROGRAM2_FEET`;
- `PROGRAM2_J`;
- `PROGRAM2_YARN`;
- `PROGRAM2_STITCH`;
- tabla de posiciones Stitch codificada dentro del builder.

La extensión de CAN ID por instancia, selector por instancia y máscara activa sí es útil y genérica. Los nombres y datos específicos de P2 no lo son.

**Recomendación:** renombrar los layouts por protocolo físico/genérico o mover plantillas de trama al perfil del programa.

### 4. La secuencia de 576 pasos puede comprimirse mucho

Datos observados:

- 576 pasos;
- 11 tramas CAN únicas;
- 38 combinaciones únicas `(frame, wait)`;
- 456 de los 576 pasos pertenecen a repeticiones del motivo de tres pasos `{2,0}, {4,0}, {3,150}`;
- existen 15 segmentos repetitivos de ese motivo.

El archivo `.inc` ocupa unos 9–12 KB como texto, pero eso no equivale a flash. La tabla compilada actual probablemente ocupa cerca de 2.3 KB por alineación, más las 11 tramas y el ejecutor. El crecimiento total de 8,096 bytes también incluye strings de INIT, parser, builder y comandos nuevos.

**Recomendación:** representar la secuencia con operaciones `SEND`, `WAIT`, `REPEAT_BEGIN/END` o bloques repetibles. Esto puede reducir sustancialmente la tabla antes de agregar cinco programas.

## Riesgos funcionales encontrados

### STOP no garantiza apagado físico

La versión revisada cancela máquinas de estado y transmisiones futuras. El perfil P2 tiene:

`stop.sends_can_frame = false`

Por tanto, el último estado físico enviado puede permanecer aplicado. No debe considerarse una parada física segura hasta definir tramas verificadas de STOP/OFF por subsistema.

### Errores no correlacionados en la UI P2

El componente P2 procesa cualquier línea que empiece con `ERR` y falla **todos** los comandos pendientes. Un error de TESTEO, CHECK u otra operación puede borrar pendientes P2 no relacionados.

**Recomendación:** correlacionar errores con comando/ID o filtrar exclusivamente respuestas `ERR|UNI|...` relacionadas.

### Doble suscripción a `LineReceived`

El dashboard padre y el componente P2 se suscriben por separado a `Connection.LineReceived`. Funciona, pero duplica parseo y estado.

**Recomendación:** el padre debería despachar respuestas P2 al hijo o usar un servicio de estado compartido.

### Botones globales duplicados

En la cabecera, `RUN ALL` y `RUN ALL (J)` envían ambos `j_run_all`; `STOP ALL` y `STOP ALL (J)` envían ambos `j_stop_all`. Esto es ambiguo y no representa un RUN ALL general.

### OFF ALL / ON ALL no significa “todo”

En P2 estos comandos solo actúan sobre J y Yarn. No actúan sobre DEN, Feet, Stitch ni SIC. Coincide con la referencia, pero el texto del botón puede inducir a error.

### Texto con codificación dañada

En `CabezalDashboardUnificado.razor` aparecen secuencias como `Ã‚Â·`. Deben corregirse a `·`.

## Archivos accidentales fuera de lugar

El `git status` del ZIP muestra archivos no seguidos en la raíz del repositorio:

- `AcuratexControlApp.csproj`;
- `AcuratexControlApp.sln`;
- `CMakeLists.txt`;
- `UnifiedSystemForm.cs`;
- `partitions.csv`.

Parecen copias accidentales. No deben entrar al commit. Verificar y eliminar únicamente esas copias de la raíz después de confirmar que los originales correctos existen en sus carpetas.

También deben mantenerse fuera del commit los dos `MAPA_*.txt`, salvo decisión expresa.

## Documentación desactualizada

`MAPEO_PROGRAMA2_FINAL.md` todavía afirma que el HTML es la fuente visual y que se reprodujo su estética. El código actual del ZIP ya fue corregido para usar la estética del Programa 1. El documento debe actualizarse para no confundir futuras migraciones.

## Arquitectura recomendada antes de agregar P4–P8

```text
command_unified_head_processor
        ↓ despacho genérico
UnifiedProgramDefinition / capabilities
        ├── perfil de motores
        ├── constructor de tramas / layouts
        ├── comandos especiales opcionales
        └── secuencia automática opcional
                ↓
head_sequence_runner genérico
                ↓
head_state_manager + TWAI
```

Cada programa conserva sus datos y comandos, pero comparte:

- parser/dispatch;
- ejecutor de secuencias;
- temporización;
- cancelación;
- arbitraje;
- envío CAN;
- manejo de errores.

## Orden recomendado

1. Guardar un ZIP/checkpoint local del estado actual.
2. No probar `RUN ALL` ni la secuencia de 576 pasos hasta resolver STOP físico.
3. Refactorizar el runner de secuencias a genérico sin cambiar ninguna trama ni timing.
4. Extraer comandos especiales P2 fuera del procesador principal.
5. Renombrar/generalizar los layouts de trama.
6. Comprimir la secuencia y comparar los 576 pasos expandidos contra la referencia.
7. Compilar y medir con `idf.py size-files`.
8. Probar P2 físicamente por subsistema.
9. Recién después usar la misma plantilla para P4–P8.

## Conclusión

La implementación no está “mal hecha” completa: ya reutiliza bastante infraestructura común. Pero P2 quedó como una excepción incrustada en tres capas comunes. Esa excepción debe convertirse ahora en una **plantilla genérica de programa**; hacerlo después de añadir cinco programas sería mucho más caro y consumiría más flash.

## Resultado de la reintegración arquitectónica — 2026-07-25

### Arquitectura aplicada

```text
command_processor
        -> command_unified_head_processor
        -> HeadUnifiedProgramDefinition activa
        -> handler/tablas del programa activo
        -> head_sequence_executor neutral / head_state_manager neutral
        -> TWAI/CAN
```

Se crearon `head_sequence_executor.*`, `head_unified_program_definition.h`, `head_unified_program_2_sequence.*` y `head_unified_program_2_command_handler.*`. El procesador común conserva parseo, validación y delegación; el scheduler común solo llama mecanismos neutrales.

Comprobación estática del código actual:

- `head_state_manager.cpp` ya no llama `app_unified_program2_sequence_tick()` ni contiene nombres P2;
- `head_command_profile.h` ya no contiene `PROGRAM2_*`;
- `head_command_frame_builder.cpp` ya no conoce layouts P2;
- `command_unified_head_processor.cpp` ya no contiene tablas, secuencias ni nombres P2;
- layouts, comandos especiales y parámetros P2 viven en archivos P2;
- P1, P3 y Modular no reciben datos ni tramas P2.

### Compactación y equivalencia

La tabla expandida de 576 entradas se sustituyó, después de validarla, por:

- catálogo de 11 tramas CAN;
- catálogo de 38 combinaciones trama/espera;
- stream de 123 índices;
- 30 bloques;
- 15 bloques repetidos, 152 repeticiones del motivo de tres pasos y 456 acciones repetidas.

La validación host comparó primero la representación compacta contra la tabla previa de 576 entradas y obtuvo cero diferencias. Después comparó contra los 1.151 ítems de `firmware_programa2_final.ino`: 576 acciones, ID, DLC, bytes, espera y orden, también con cero diferencias. La tabla expandida duplicada se retiró solo después de ambos resultados.

### WAIT 0 y cancelación

`head_sequence_executor` procesa pasos consecutivos con WAIT 0 en el mismo tick. El lote termina al encontrar espera positiva, STOP, emergencia, error CAN o el máximo defensivo. El máximo configurado es 16: P2 necesita como máximo tres transmisiones consecutivas, y 16 deja margen reutilizable sin monopolizar la tarea. El estado/revisión se vuelve a comprobar entre transmisiones.

El gestor común añade una barrera neutral para J, Yarn, Stitch, DEN, SIC y Feet: STOP invalida runtimes y espera cualquier transmisión ya iniciada antes de permitir que un snapshot viejo continúe. Una transmisión CAN que ya entró al driver no puede retirarse físicamente; no se envían pasos posteriores tras observar la cancelación.

### STOP verificable y no verificable

| Subsistema | Cancela runtime | Trama física conocida | Fuente | Resultado |
|---|---|---|---|---|
| DEN | Sí | No | INO: STOP desactiva runtime; posición 0 no demuestra OFF | Solicitada; físico no confirmado |
| SIC | Sí | No | INO: STOP desactiva runtime; SYNC no es OFF | Solicitada; físico no confirmado |
| Feet | Sí | No | INO sin STOP/OFF seguro de Feet | Solicitada; físico no confirmado |
| J | Sí | Sí: registro `FF` | `can_send_j()` del INO, builder P2 validado | OFF enviado; cada fallo TX se registra |
| Yarn | Sí | Sí: estado `00` por canal | `yarn_send_state()` del INO, builder P2 validado | OFF enviado; cada fallo TX se registra |
| Stitch | Sí | No | INO: STOP solo runtime; RESET no equivale a OFF | Solicitada; físico no confirmado |
| Secuencia automática | Sí | No global | runner/INO cancelan acciones futuras | Cola cancelada; físico final no confirmado |

STOP/emergencia intentan todas las tramas conocidas aunque una transmisión falle y responden `ERR|UNI|STOP_TX|SUBSYSTEM=...` si corresponde. No se inventaron bytes. `uni_emergency_stop` conserva prioridad sobre STOP normal. `uni_run_all` se bloquea con `ERR|UNI|RUN_ALL_UNSAFE` mientras no pueda garantizarse una salida segura global.

### UI, respuestas y botones

El dashboard padre quedó como única suscripción permanente a `LineReceived` para la vista Unificada; entrega cada línea al componente P2. La correlación de errores usa comando explícito cuando exista, luego familia y, solo para errores UNI genéricos, la operación pendiente más antigua. Nunca borra todos los pendientes. La prueba automatizada confirma que `ERR|UNI|J_RUN` no cancela DEN y que un error de otro protocolo no selecciona ninguna operación P2.

RUN ALL general usa `uni_run_all` en P2 y RUN ALL (J) conserva `uni_j_run_all`; STOP ALL general usa `uni_stop` y STOP ALL (J) conserva `uni_j_stop_all`. Yarn y Stitch mantienen sus acciones exclusivas. Para P1/P3 se conserva el comportamiento anterior.

OFF/ON ALL conserva el alcance CAN verificable J+Yarn y cancela runtimes J/Yarn/Stitch/secuencia. La auditoría previa decía que Stitch podía seguir activo; el código actual ya lo cancela. DEN no pertenece a ese alcance y su animación ya no se detiene al confirmar OFF/ON ALL. Tras STOP/emergencia la UI indica explícitamente qué estado físico no está confirmado.

### Diferencias y riesgos pendientes

- La seguridad física completa sigue pendiente para DEN, SIC, Feet, Stitch y el último estado emitido por la secuencia.
- Se requiere validación con cabezal real de polaridad, reacción mecánica y latencia de las tramas OFF conocidas J/Yarn.
- La comparación automatizada demuestra equivalencia de salida nominal, pero no sustituye prueba de bus bajo carga ni validación de emergencia real.
- Las copias exactas sin seguimiento en la raíz (`AcuratexControlApp.csproj`, `AcuratexControlApp.sln`, `CMakeLists.txt`, `UnifiedSystemForm.cs`, `partitions.csv`) y `MAPA_*.txt` no se usaron, modificaron ni eliminaron; siguen como limpieza local pendiente y no deben agregarse.

### Evidencia automática y tamaño

- Suite host del ejecutor: PASS; 576 pasos, 11 tramas, 38 pares, `ino_diff=0`, WAIT 0, espera positiva, STOP, emergencia y error CAN.
- Suite .NET: 37/37 PASS, incluidas tres pruebas nuevas de correlación P2.
- `dotnet build app_windows/AcuratexControlApp/AcuratexControlApp.sln`: correcto, cero errores; cinco advertencias preexistentes en `CabezalDashboardTarjetas.razor`.
- `idf.py build`: correcto; solo tres advertencias preexistentes de funciones no usadas en `command_head_program_runner.cpp`.
- Baseline: `0xee250` (975.440 bytes). Resultado: `0xee5b0` (976.304 bytes), aumento de 864 bytes; quedan `0x11a50` (72.272 bytes, 6,89 %, mostrado por IDF como 7 %).
- Objetos relevantes actuales: `head_sequence_executor` 1.277 bytes; datos compactos de secuencia P2 671 bytes; `head_unified_program_2_commands` 1.774 bytes (1.176 de rodata); `head_unified_program_2_command_handler` 1.858 bytes; `command_unified_head_processor` 4.030 bytes.
