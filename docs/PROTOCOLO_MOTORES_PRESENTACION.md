# Protocolo de motores principales — presentación

El dashboard `ServoDashboardUnificado` usa líneas de texto terminadas por el
transporte existente USB/TCP. El firmware responde `OK|...` o `ERR|U3|...`.
Los nombres Servo CAN 1 y Servo CAN 2 identifican motores; ambos se transmiten
por el transceiver físico CAN2/U3.

| Operación | Comando |
|---|---|
| INIT motores | `U3_INIT` |
| Estado INIT/motores | `U3_STATUS` |
| Configurar e iniciar raqueo | `U3_S1_CONFIG|P1=0|P2=500|P3=1000|SEQ=123|HZ=1` |
| Detener raqueo | `U3_S1_STOP` |
| Cabezal derecha | `U3_S2_DIR|RIGHT` |
| Cabezal izquierda | `U3_S2_DIR|LEFT` |
| Velocidad cabezal | `U3_S2_SPEED|LEVEL=10` |
| Arrancar cabezal | `U3_S2_RUN` |
| Detener cabezal (ESC) | `U3_S2_STOP` |
| Dirección rodillo | `LD_STEP_DIR|0` / `LD_STEP_DIR|1` |
| Frecuencia rodillo | `LD_STEP_FREQ|HZ=200` |
| Marcha rodillo | `LD_STEP_RUN|ON` / `LD_STEP_RUN|OFF` |

`U3_STATUS` responde con:

```text
U3_STATE|INIT=IDLE|STEP=0/296|S1=STOP|S2=STOP|DIR=RIGHT|LEVEL=10|MSG=LISTO
```

Durante `INIT=INICIALIZANDO` los comandos U3 de movimiento responden
`ERR|U3|BUSY_INIT`. La secuencia tiene 148 tramas y 148 esperas de 200 ms,
copiadas literalmente de `referenciaspresentacion/dasboardservo/firmware.ino`.

## CAN físico

- CAN1 Cabezal: TX GPIO4, RX GPIO5, STBY GPIO6, 1 Mbit/s.
- CAN2 Motores U3: TX GPIO7, RX GPIO15, STBY GPIO16, 1 Mbit/s.
- Un único worker serializa todas las solicitudes y reconfigura el único TWAI.
- Antes de cambiar de bus espera TX vacío, pone ambos STBY en HIGH, reinicia
  TWAI con los pines del destino y sólo entonces baja el STBY seleccionado.
- El namespace `uni_*` del Cabezal Unificado queda fijado a CAN1 físico.

La emergencia cancela INIT/secuencia de raqueo, envía ESC al Servo CAN 2 si
estaba en RUN y ejecuta `app_line_drive_safe_stop()` para el rodillo.
