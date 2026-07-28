# MAPEO PROGRAMA 2 FINAL

Auditoría iniciada antes de la implementación y actualizada con la reintegración arquitectónica el 2026-07-25.

## Punto de partida verificado

- Rama: `nuevo-enfoque-2026-07-22`.
- HEAD y checkpoint: `035fc23 feat: separar cabezal unificado en app y firmware`.
- El commit `035fc23` existe en el historial y es el checkpoint comparado.
- No se cambio de rama, no se hizo commit, push ni flash.
- Los dos `MAPA_*.txt` no se modificaron.

## Referencias inspeccionadas recursivamente

El contenido completo encontrado inicialmente en `referencias/programa2_final/` fue:

1. `programa2_final.html`: HTML principal, 3.868 lineas y 113.646 bytes.
2. `firmware_programa2_final.ino`: firmware principal, 2.574 lineas y 91.918 bytes.
3. `MAPEO_PROGRAMA2_FINAL.md`: unica excepcion de escritura autorizada.

No existen carpetas `css/`, `js/`, `img/`, `assets/` o `firmware/`, ni archivos `.h`, `.cpp`, JSON, fuentes, imagenes o SVG auxiliares. Todo el CSS y JavaScript visual esta inline en el HTML; el firmware Arduino es autocontenido. No hay recurso visual externo que deba copiarse. Los originales HTML/INO no se modificaron y no se leen en runtime.

## Resumen de las fuentes de verdad

- Apariencia: **Programa 1 Unificado** es la referencia visual. P2 conserva la apariencia ya corregida de la aplicación; el estilo del HTML antiguo no debe reinstalarse.
- Contenido y comportamiento: `programa2_final.html` determina las secciones, controles, acciones y relaciones funcionales que deben representarse, no la estética final.
- CAN y temporización: `firmware_programa2_final.ino` determina IDs, DLC, bytes, orden, esperas nominales, repeticiones, INIT, TESTEO y comandos propios P2.
- Integración: la arquitectura actual Razor/C# -> servicio Unificado -> comandos `uni_` -> ESP-IDF -> ejecutor/gestor neutral -> TWAI es la referencia vigente.

## Inventario visual exacto

Los 63 elementos `<button>` del documento completo se dividen en 27 controles del shell HTML y 36 botones del cuerpo P2. De los 36 del cuerpo, 34 son generados por JavaScript y Feet aporta dos botones estaticos.

| Elemento | HTML cuerpo P2 | Razor final | Clasificacion |
|---|---:|---:|---|
| Bloques operativos | 13 | 13 | Migrado |
| Secciones | 5 | 5 | Migrado |
| Tarjetas | 13 | 13 | Migrado |
| Botones del cuerpo | 36 | 36 | Migrado |
| Controles clicables no `button` | 70 | 70 | Migrado |
| Sliders | 4 | 4 | Migrado |
| Indicadores seleccion/LED | 70 | 70 | Migrado |
| LED binarios J + Yarn | 40 | 40 | Migrado |
| Indicadores de tarjeta `running` | 11 | 11 | Migrado |
| Graficas | 0 | 0 | No aplicable |
| SVG | 0 | 0 | No aplicable |
| Canvas | 0 | 0 | No aplicable |
| Contadores | 0 | 0 | No aplicable |
| DEN visibles | 4 (fisicos 1,2,5,6) | 4 | Migrado |
| Feet visibles | 2 | 2 | Migrado |
| J visibles | 4 (fisicos 1,2,5,6) | 4 | Migrado |
| Yarn visibles | 1 | 1 | Migrado |
| Stitch visibles | 2 | 2 | Migrado |
| Posiciones DEN | 20 | 20 | Migrado |
| Posiciones Stitch | 10 | 10 | Migrado |
| Canales J | 32 | 32 | Migrado |
| Canales Yarn | 8 | 8 | Migrado |
| RUN individuales | 11 | 11 | Migrado |
| STOP individuales | 11 | 11 | Migrado |
| RESET individuales | 2 | 2 | Migrado |

Desglose de botones: DEN 8, Feet 2, J 16, Yarn 4 y Stitch 6. Los controles no `button` son DEN 20 + J 32 + Yarn 8 + Stitch 10. SIC no tiene contenedor DOM y por ello no cuenta como bloque visual.

## Tabla funcional completa

| Seccion | Nombre visible | Identificador HTML | Boton/control y tipo | Rango | Posiciones | Inicial | Comando HTML | Funcion firmware de referencia | ID CAN | DLC | Bytes enviados | Delay | Repeticiones | Finalizacion | Cancelacion | Comando `uni_` | Respuesta | Destino app | Destino firmware | Estado |
|---|---|---|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|
| Programa | Programa 2 | `visualProgramStatus`, selector del shell | selector | 1..3 | P2 | P1 | seleccion web local | seleccion logica | - | - | - | - | una | `ACTIVE=2` | error/timeout conserva anterior | `uni_program_select_2`, `uni_program_status` | `OK ...`, luego `UNI_PROGRAM_STATE\|ACTIVE=2` | Dashboard principal | processor unificado | Migrado |
| Global | INIT | `btnInit`, `overlay` | boton + overlay | - | INIT1 | idle | `init` | ejecuta INIT1 cancelable; INIT2 esta comentado/vacio | varios | 1..8 | 69 tramas exactas | 80 ms tras cada trama + 68 WAIT explicitos + GAP 5000 ms | 137 items | `INIT\|DONE` | `uni_stop`, emergencia, error CAN | `uni_init` | `OK`, `INIT\|STEP`, `DONE/CANCELLED/ERROR` | Cabecera existente | P2 profile + `head_fast_diag.cpp` | Migrado |
| Global | TESTEO | `btnTesteo`, `testBox` | boton/estado | 25 intentos | codigos CB, BC, BF, A1, A2 | listo | `testeo` | ping y clasificacion RX | 320; RX 700; reset 702 | 1; reset 2 | `07`; reset `3F 00` | timeout 300, retry 60, debounce 250 ms | max 25 | patron o timeout | STOP/emergencia | `uni_testeo` | `OK`, `TESTEO\|...` | Cabecera existente | `head_fast_diag.cpp` | Ya existente, verificado |
| Global | STATUS | `btnStatus` | boton | - | - | listo | GET `/status` | estado, sin CAN | - | - | - | - | una | respuesta | timeout | `uni_status` | `OK uni_status` | Cabecera existente | processor | Ya existente |
| Global | OFF ALL | `btnAllOff` | boton enclavado | 4 J + Yarn1 | OFF | FF/false | CAN directo | detiene runtimes y envia J FF + Yarn 00 | 363 | 6 | J `06 03 sel 00 FF 00`; Yarn `05 01 00 ch 00 00` | por TX | 4 + 8 | todas TX OK | STOP/emergencia/fallo | `uni_off_all` | `OK` o `ERR\|UNI\|ALL_*` | Cabecera -> componente P2 | processor + builders | Creado/migrado |
| Global | ON ALL | `btnAllOn` | boton enclavado | 4 J + Yarn1 | ON | FF/false | CAN directo | detiene runtimes y envia J 00 + Yarn 01 | 363 | 6 | J `06 03 sel 00 00 00`; Yarn `05 01 00 ch 00 01` | por TX | 4 + 8 | todas TX OK | STOP/emergencia/fallo | `uni_on_all` | `OK` o `ERR\|UNI\|ALL_*` | Cabecera -> componente P2 | processor + builders | Creado/migrado |
| Global | STOP / emergencia | HALT y error overlay | boton/alarma | todo P2 | - | idle | varios `*_stop_*` | cancela maquinas; referencia no define trama fisica global segura | - | - | ninguna trama global | inmediato | una | runtimes inactivos | prioridad maxima | `uni_stop`, `uni_emergency_stop` | `OK ...` | Cabecera existente | processor/state manager/diag | Migrado; ID 0 eliminado |
| DEN | DEN 1..4 | `rowDEN`, `.posBtn`, slider | 20 posiciones + 4 sliders | 0..650, step 1 | 0,162,325,487,650 | 0/POS1 | CAN directo | posicion absoluta | 363 para fisicos 1,2,5,6 | 6 | `03 03 selector 00 lo hi`; selectores 00,01,03,02 | TX | una | CAN TX OK | manual/STOP/emergencia | `uni_den_select_n\|p`, `uni_den_pos_n\|v` | `OK`/`ERR` | componente P2 | P2 profile/frame builder | Migrado |
| DEN | RUN/STOP individual y ALL | `[data-act=run/stop]`; ALL usado por emergencia | 8 botones; ALL sin boton propio | fisicos 1,2,5,6 | POS1..POS5 | detenido | `den_run_n`, `den_stop_n`, `den_run_all`, `den_stop_all` | ciclo infinito de posiciones | como DEN | 6 | como DEN | 300 ms | infinito | STOP | STOP/emergencia/fallo CAN | `uni_den_run/stop_n`, `uni_den_run_all`, `uni_den_stop_all` | `OK`/`ERR`, eventos estado disponibles | componente P2/cabecera | state manager | Migrado |
| DEN | RUN1/STOP1 | no existe | sin control | - | - | - | no existe | no existe en firmware final; tabla alternativa vacia | - | - | - | - | - | - | - | familia parseada pero P2 responde error | `ERR\|UNI\|DEN_RUN1` | no UI | processor | No aplicable |
| Feet | FEET 1/2 | `btnFeet1`, `btnFeet2` | 2 pulsadores momentaneos | 1..2 | disparo | reposo | CAN directo | envia una trama | 363 | 6 | `04 01 02 00 00 80`; `04 01 02 01 00 80` | ninguno | una | CAN TX OK | fallo/emergencia | `uni_feet_trigger_1/2` | `OK`/`ERR\|UNI\|FEET_TRIGGER` | componente P2 | processor/frame builder | Creado/migrado |
| SIC | SIC 1/2 | sin `rowSIC`; JS muerto | no visible | 1..2 | +180,+360,-180,-360,SYNC | ninguna | `sic_pos`, `sic_sync`, `sic_run`, `sic_run_all`, `sic_stop_all` en JS/firmware no renderizado | 3 ciclos 1..4 y SYNC | 363/364 | 8 | pos `04 01 00 sel lo hi 00 00`; sync `04 01 00 sel 00 00 10 00` | 300 ms | 3 ciclos por vuelta RUN | SYNC/repite | STOP/emergencia/fallo | `uni_sic_select/pos/run/stop`, `uni_sic_sync_n`, `uni_sic_run_all`, `uni_sic_stop_all` | `OK`/`ERR` | sin UI | P2 profile/state manager | Contradictorio; funcional migrado, visual no aplicable |
| J | J1..J4, OFF/ON, celdas | `rowJ`, `.J`, `.cell` | 16 botones + 32 celdas | 8 bits activo-bajo | fisicos 1,2,5,6 | FF | `sendJ` CAN directo | registro completo o toggle | 363 | 6 | `06 03 selector 00 mask 00`; selectores 00,01,02,03 | TX | una | CAN TX OK | nuevo comando/STOP | `uni_j_set_n\|v`, `uni_j_ch_n_p` | `OK`/`ERR` | componente P2 | processor/frame builder | Migrado |
| J | RUN/STOP/RUN ALL/STOP ALL | botones de tarjeta y `btnRunAllJ/btnStopAllJ` | momentaneo + running | 8 canales | ON 1..8, OFF 1..8 | detenido | `j_run_n`, `j_stop_n` | primero FF; alterna ON/OFF por canal | 363 | 6 | mismo formato J | CAN 80 ms; visual 120 ms | infinito | STOP | STOP/emergencia/fallo | `uni_j_run/stop_*`, `uni_j_run_all`, `uni_j_stop_all` | `OK`/`ERR` | componente + cabecera | state manager | Migrado; cadencias documentadas |
| Yarn | Yarn 1 | `rowYarn`, `.pinBtn` | 4 botones + 8 celdas | canales 1..8 | OFF/ON | todos OFF | CAN directo | salida individual | 363 | 6 | `05 01 00 channel(0..7) 00 state` | TX | una | CAN TX OK | nuevo comando/STOP | `uni_yarn_pin_1\|p\|state` | `OK`/`ERR` | componente P2 | processor/frame builder | Migrado |
| Yarn | RUN/STOP/RUN ALL/STOP ALL | tarjeta y `btnRunAllY/btnStopAllY` | botones + running | 8 canales | ON 1..8, OFF 1..8 | detenido | `y1_run/stop` | cascada binaria | 363 | 6 | formato Yarn | 80 ms fisico e individual visual; 120 ms visual RUN ALL | infinito | STOP | STOP/emergencia/fallo | `uni_y1_run/stop`, `uni_y_run_all`, `uni_y_stop_all` | `OK`/`ERR` | componente + cabecera | state manager | Migrado |
| Yarn | Yarn 2 | codigo muerto/no render | sin control | - | - | - | no visible | firmware final rechaza Yarn2 | - | - | - | - | - | - | - | `uni_y2_*` no habilitado para P2 | error | no UI | processor | No aplicable |
| Stitch | Stitch 1/2 posiciones | `rowStitch`, `.pinBtn` | 10 celdas | 1..5 | 0,320,480,160,640 | ninguna | CAN directo | posicion exclusiva | 363 | 8 | `04 01 01 selector posL posH typeL typeH`; types 0004/0005 | TX | una | CAN TX OK | nuevo comando/RESET/STOP | `uni_stitch_pos_n\|p` | `OK`/`ERR\|UNI\|STITCH_POSITION` | componente P2 | processor/frame builder | Creado/migrado |
| Stitch | RESET | `[data-act=reset]` | 2 botones | 1..2 | sin seleccion | ninguna | CAN directo | reset posicional | 363 | 6 | `04 02 01 selector 01 00` | TX | una | CAN TX OK | nuevo comando/emergencia | `uni_stitch_reset_n` | `OK`/`ERR\|UNI\|STITCH_RESET` | componente P2 | processor/frame builder | Creado/migrado |
| Stitch | RUN/STOP/RUN ALL/STOP ALL | tarjeta y `btnRunAllS/btnStopAllS` | 4 + 2 globales | dos motores | RESET,POS1..5 | detenido | `s_run_n`, `s_stop_n` | seis pasos ciclicos | 363 | 6/8 | RESET + cinco posiciones | 120 ms | infinito | STOP conserva ultimo paso visual/fisico | STOP/emergencia/fallo | `uni_s_run/stop_*`, `uni_s_run_all`, `uni_s_stop_all` | `OK`/`ERR` | componente + cabecera | state manager | Migrado |
| Secuencia | Secuencia de prueba | sin control HTML | funcion interna | 576 pasos | 11 tramas unicas | inactiva | serial/DO `secuencia` | ejecucion unica no bloqueante | 363,364,733 | 6/8 | tabla exacta comprimida | 27 waits unicos, 0..13225 ms | una vez | paso 576 | `uni_sequence_stop`, STOP, emergencia, fallo CAN | `uni_sequence_start/stop/status` | `OK`, `UNI_SEQUENCE_STATE\|ACTIVE=...\|STEP=...\|TOTAL=576` | sin boton (igual al HTML) | P2 commands/state manager | Migrado funcionalmente |

## Auditoria del protocolo `uni_` final

| Comando aceptado | Formato/validacion | Respuesta | Estado afectado | Implementacion |
|---|---|---|---|---|
| `uni_program_select_1..3` | exacto; rechaza movimiento/diag activo | `OK` o `ERR UNI_PROGRAM_BUSY` | programa solicitado | processor + runtime |
| `uni_program_status` | exacto | `UNI_PROGRAM_STATE\|ACTIVE=n` | programa confirmado | processor |
| `uni_init`, `uni_testeo`, `uni_status` | exactos; arbitraje de actividad | `OK`, eventos o error | diagnostico | processor + fast diag |
| `uni_stop`, `uni_emergency_stop` | exactos, prioridad; no transmite ID 0 | `OK` | cancela todos los runtimes y diag | processor + state manager |
| `uni_den_select_n\|p`, `uni_den_pos_n\|v` | instancia 1..8; mascara P2 solo 1,2,5,6 para RUN | `OK`/`ERR` | DEN | processor/builder |
| `uni_den_run/stop_n`, `uni_den_run_all`, `uni_den_stop_all` | perfil activo; ALL usa mascara fisica 1,2,5,6 | `OK`/`ERR` | runtime DEN | processor/state manager |
| `uni_sic_select/pos/run/stop_n`, `uni_sic_sync_n`, `uni_sic_run_all`, `uni_sic_stop_all` | SIC 1..2 | `OK`/`ERR` | SIC | processor/state manager/builder |
| `uni_feet_trigger_1/2` | solo 1..2 | `OK`/`ERR` | pulso Feet | processor/builder |
| `uni_j_set_n\|v`, `uni_j_ch_n_p`, `uni_j_run/stop_*`, ALL | J fisicos activos 1,2,5,6 | `OK`/`ERR` | J | processor/state manager/builder |
| `uni_yarn_pin_1\|p\|state`, `uni_y1_run/stop`, ALL | Yarn1, pin 1..8 | `OK`/`ERR` | Yarn | processor/state manager/builder |
| `uni_stitch_reset_n`, `uni_stitch_pos_n\|p`, `uni_s_run/stop_*`, ALL | Stitch 1..2, pos 1..5 | `OK`/`ERR` | Stitch | processor/state manager/builder |
| `uni_off_all`, `uni_on_all` | solo J visibles y Yarn1; cancela runtimes antes | `OK`/`ERR` | salidas P2 | processor/builder |
| `uni_sequence_start/stop/status` | solo P2; excluye otros movimientos | `OK`, error busy o estado | secuencia automatica | P2 commands/processor/state manager |

Comandos creados: `uni_feet_trigger_*`, `uni_stitch_reset_*`, `uni_stitch_pos_*`, `uni_sic_sync_*`, `uni_den_run_all`, `uni_den_stop_all`, `uni_sic_run_all`, `uni_sic_stop_all`, `uni_off_all`, `uni_on_all` y `uni_sequence_start/stop/status`. Las familias individuales DEN/J/Yarn/Stitch ya existian, pero su construccion de tramas P2 fue sustituida por los formatos finales. `FastUnifiedDashboardCommandService` agrega `uni_` una sola vez y no genera `uni_uni_`.

## Animaciones y estados

| Elemento | Inicial | Activador | Clase/animacion | Duracion/easing/color | Detencion/error | Equivalente final |
|---|---|---|---|---|---|---|
| Boton general | reposo | hover/press | transform/transition | 120-150 ms; elevacion/sombra | release/disabled | CSS local `:hover/:active/:disabled` |
| Comando pendiente | reposo | click | `pending`, pulso amarillo | 900 ms ease-in-out | `OK`, `ERR`, timeout 5 s | Razor + keyframe scoped |
| DEN | POS1 | `OK uni_den_run_*` | tarjeta `running`, posicion secuencial | 300 ms | STOP/manual/emergencia/error | Razor timer cancelable |
| J | FF | `OK uni_j_run_*` | tarjeta `running`, un LED verde | 120 ms visual; CAN 80 ms | STOP restaura registro previo | Razor timer cancelable |
| Yarn individual | todo OFF | `OK uni_y1_run` | tarjeta `running`, un LED | 80 ms | STOP restaura estados previos | Razor timer cancelable |
| Yarn RUN ALL | todo OFF | `OK uni_y_run_all` | igual | 120 ms visual | STOP ALL restaura | Razor timer cancelable |
| Stitch | sin seleccion | `OK uni_s_run_*` | RESET visual + POS1..5 | 120 ms | STOP conserva ultimo paso; emergencia restaura previo | Razor timer cancelable |
| Seleccion | no/on inicial | `OK` del comando | fondo/borde verde | transicion 120 ms | otra seleccion/reset | clase `on` |
| Error/alarma | oculto | `ERR`, timeout, detector CAN | mensaje rojo/HALT | persistente segun shell | operador/INIT | shell existente + estado P2 |

La respuesta tactil inmediata es CSS. Los estados persistentes no se aplican al click: se marcan pendientes y cambian despues de `OK`. No existe telemetria de posicion alcanzada por paso; por tanto `OK` confirma aceptacion/transmision del firmware, no medicion mecanica.

## Contradicciones y decisiones

1. SIC tiene funciones JavaScript y firmware, pero no existe `rowSIC` ni llamada a `renderSIC()`. Se migro la funcion CAN, pero no se invento una tarjeta visual.
2. J usa 120 ms visuales y 80 ms fisicos. Se conservaron ambas cadencias en sus capas respectivas.
3. Yarn individual usa 80 ms; RUN ALL visual usa 120 ms. Se conservaron ambos casos.
4. Stitch STOP conserva el último paso físico conocido. STOP/emergencia cancelan su runtime, pero no existe una trama OFF segura verificable; la UI lo presenta como parada solicitada con estado físico no confirmado.
5. ON/OFF ALL conserva el alcance físico J+Yarn y cancela antes los runtimes J, Yarn, Stitch y la secuencia. No cancela DEN; la UI ya no detiene visualmente DEN al confirmar ON/OFF ALL.
6. El HTML usa `fetch`, HTTP, Wi-Fi, Serial y CAN crudo. Nada de ese transporte se migro: la ruta final es Razor -> servicio Fast -> `uni_` -> ESP-IDF -> TWAI.
7. El titulo modular y la navegacion `/b` del HTML pertenecen al shell antiguo. Se conserva la cabecera global Unificada exigida y se migra solamente el cuerpo P2.
8. INIT2 contiene texto comentado, pero el arreglo ejecutable esta vacio. No se activo codigo comentado.
9. RUN1/STOP1 no existen en el HTML ni en el firmware final P2; la tabla alternativa queda vacia y la accion no se expone.

## Secuencias no bloqueantes

- INIT: tarea cancelable; no mantiene mutex durante esperas; 69 tramas, 68 WAIT, delay de 80 ms tras trama y GAP de 5.000 ms; STOP/emergencia/fallo cancelan.
- DEN, SIC, J, Yarn y Stitch: snapshots bajo mutex, TX fuera del mutex y commit por revision solo tras CAN OK.
- Secuencia automática: 1.151 ítems originales = 576 tramas/pasos (la última sin WAIT), 11 tramas únicas, 38 pares trama/espera y 27 esperas únicas. La representación compacta expande con cero diferencias contra el INO. Los WAIT 0 encadenan pasos en el mismo tick; una espera positiva, STOP, emergencia, error CAN o el límite defensivo detienen el lote.
- Estado despues de cancelar: no se inventa una trama de apagado que la referencia no define; se detienen nuevas transmisiones y se conserva el ultimo estado enviado. Esto debe validarse fisicamente.

## Dependencias runtime

La ruta activa P2 no usa `IHeadProfileService`, `IAppScriptExecutionService`, `HEAD_ACTION`, `HEAD_PROGRAM_SELECT`, SQLite, ACX, TXT, LittleFS, WebSocket, `fetch(`, iframe, HTTP/HTTPS, CDN ni archivos de referencia. No se envia CAN crudo desde Razor.

## Validacion visual

La comparacion estructural DOM/CSS/Razor confirma las cantidades, orden, textos, grids, colores, estados y cadencias anteriores. No se pudo abrir el Sistema Unificado conectado ni obtener `UNI_PROGRAM_STATE|ACTIVE=2` con hardware durante esta ejecucion; por ello la captura comparativa y la validacion visual humana/pixel a pixel quedan pendientes y no se declara identidad visual observada.

## Reintegración arquitectónica 2026-07-25

La ruta P2 vigente es:

```text
command_processor
        -> command_unified_head_processor (parseo/validación/delegación)
        -> HeadUnifiedProgramDefinition activa
        -> handler y tablas propias P2
        -> head_sequence_executor neutral / head_state_manager neutral
        -> TWAI/CAN
```

- `head_state_manager.cpp`, `head_command_profile.h`, `head_command_frame_builder.cpp` y `command_unified_head_processor.cpp` ya no contienen nombres `program2`, `unified_program2` ni `PROGRAM2_*`.
- Los seis constructores de layout P2 viven en `head_unified_program_2_commands.cpp` y conservan sus valores.
- La secuencia usa 11 tramas, un catálogo de 38 pares trama/espera, un stream de 123 índices y 30 bloques. Quince bloques repiten 152 veces el motivo de tres pasos: 456 de 576 acciones.
- El ejecutor neutral permite hasta 16 pasos por lote como límite defensivo; P2 requiere como máximo tres transmisiones consecutivas. Revalida cancelación entre transmisiones.
- La prueba host expande 576 pasos y compara ID, DLC, bytes, espera y orden contra el INO: `ino_diff=0`.

### Parada verificable

| Subsistema | Cancela runtime | Trama física conocida | Fuente verificable | Resultado actual |
|---|---|---|---|---|
| DEN | Sí | No | INO: STOP solo desactiva su runtime | Parada solicitada; físico no confirmado |
| SIC | Sí | No | INO: STOP solo desactiva su runtime | Parada solicitada; físico no confirmado |
| Feet | Sí | No | INO sin STOP/OFF seguro para Feet | Parada solicitada; físico no confirmado |
| J | Sí | Sí, registro `FF` | `can_send_j()` del INO y builder P2 validado | OFF enviado; fallo TX se registra |
| Yarn | Sí | Sí, estado `00` por canal | `yarn_send_state()` del INO y builder P2 validado | OFF enviado; fallo TX se registra |
| Stitch | Sí | No | INO: STOP solo desactiva su runtime; RESET no es OFF | Parada solicitada; físico no confirmado |
| Secuencia automática | Sí | No global | INO/tabla: cancelación de pasos futuros | Cola cancelada; último frame físico no confirmado |

`uni_emergency_stop` tiene prioridad superior a STOP. Primero cancela runtimes, diagnóstico y transmisiones futuras; luego intenta todas las tramas OFF verificadas de J/Yarn. `uni_run_all` queda rechazado con `ERR|UNI|RUN_ALL_UNSAFE` mientras no exista una salida segura verificable para todos los subsistemas.

El dashboard padre es ahora la única suscripción permanente a `LineReceived` para esta vista y entrega líneas tipadas al componente P2. Un `ERR|UNI|...` selecciona como máximo una operación pendiente por comando/familia/orden; errores de otros protocolos no cancelan pendientes P2. RUN/STOP ALL general y RUN/STOP ALL (J) son acciones distintas.
