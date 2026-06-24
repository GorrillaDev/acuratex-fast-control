# Pruebas Fisicas

## Alcance de la fase

Primero se valida solo:

- `CH1` a `CH8`
- `ON ALL`
- `OFF ALL`
- `RUN`
- `STOP`
- `RUN ALL`
- `STOP ALL`

## Casos de prueba

### 1. Clic en un canal

Pasos:

1. Pulsar `CH1`.
2. Verificar cambio visual instantaneo.
3. Verificar envio del comando.
4. Verificar trama CAN casi inmediata.

Esperado:

- el boton cambia al instante
- el registro local cambia antes de cualquier respuesta fisica
- no hay espera de `DONE`

### 2. RUN de dos canales

Pasos:

1. Pulsar `J1.RUN`.
2. Pulsar `J2.RUN` mientras `J1` sigue activo.
3. Observar continuidad de ambos.

Esperado:

- `J1` sigue corriendo
- `J2` arranca sin pausar `J1`
- cada canal conserva su propio estado

### 3. STOP individual

Pasos:

1. Arrancar `J1` y `J2`.
2. Pulsar `J1.STOP`.

Esperado:

- solo se detiene `J1`
- `J2` continua
- la UI restaura el estado visual anterior de `J1`

### 4. RUN ALL / STOP ALL

Pasos:

1. Pulsar `RUN ALL`.
2. Verificar ejecucion simultanea.
3. Pulsar `STOP ALL`.

Esperado:

- todos los canales cambian de forma coherente
- la memoria visual local queda consistente

## Observaciones de banco

Completar aqui con el resultado real de la prueba fisica.

