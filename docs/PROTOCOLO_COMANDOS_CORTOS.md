# Protocolo de Comandos Cortos

## Regla

La interfaz opera con comandos cortos de texto.  
La app no espera finalizacion fisica para continuar.

## Respuestas base

- `QUEUED`: el comando fue aceptado por la conexion y encolado por firmware
- `OK ...`: comando reconocido y aplicado
- `TX_OK ...`: la trama CAN fue aceptada por el driver
- `ERR ...`: comando invalido, cola llena o error de transporte

## J

### RUN

- `j_run_1`
- `j_run_2`
- `j_run_3`
- `j_run_4`
- `j_run_5`
- `j_run_6`
- `j_run_7`
- `j_run_8`
- `j_run_all`

### STOP

- `j_stop_1`
- `j_stop_2`
- `j_stop_3`
- `j_stop_4`
- `j_stop_5`
- `j_stop_6`
- `j_stop_7`
- `j_stop_8`
- `j_stop_all`

## Canales directos

Ejemplo:

```text
send 320 1D 00 FE
```

Tambien se acepta la linea directa:

```text
320 1D 00 FE
```

## Posiciones directas

### DEN

```text
den_pos_1|162
```

### SIC

```text
sic_pos_1|374
```

## Otros comandos

- `init`
- `testeo`
- `start`
- `stop`
- `can1`
- `can2`
- `ping`
- `status`

## Lo que no participa en esta fase

- `HEAD_STATUS`
- `HEAD_ACTION|...`
- `FILE_SELECT`
- `BEGIN|...`
- `END`
- waits largos de finalizacion

