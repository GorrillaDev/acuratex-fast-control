# Resultados de Latencia

Fecha de referencia: 2026-06-24

## Estado

No se han tomado aun mediciones con instrumental de banco dentro de esta
sesion.  
El camino de software ya incluye:

- `J1..J8`
- `Yarn 1 / Yarn 2`
- `Stitch 1..4`
- parada global sobre `stop` / `emergency_stop`

Este archivo deja la comparacion y registra los valores observables por codigo.

## Comparacion

| Item | Arduino antiguo + HTML | ESP-IDF nuevo + Dashboard Modular |
| --- | --- | --- |
| Latencia de clic | Referencia historica funcional | Cambio visual inmediato en UI |
| Inicio CAN | Referencia historica funcional | Envio en cola y `QUEUED` inmediato |
| Continuidad RUN | Referencia historica funcional | `RUN` local independiente por canal |
| Ejecucion simultanea | Referencia historica funcional | J1/J2 y Yarn/Stitch corren en paralelo |
| Jitter aproximado | Pendiente de medir | Scheduler de 1 ms, cascade de 80 ms |
| Resultado fisico observado | Pendiente de registrar | Pendiente de validar en hardware |

## Valores observables por software

- actualizacion visual local: inmediata
- aceptacion de comando: inmediata
- respuesta del firmware: `QUEUED`
- transmision CAN fisica: asincrona respecto a la UI
- timeout de `twai_transmit`: `5 ms`
- animacion local J: `120 ms` aproximado
- cascadas Yarn/Stitch: `80 ms` aproximado
- parada global: inmediata para `stop` y `emergency_stop`

## Pendientes

- medicion real con hardware
- comparacion de jitter con captura externa
- verificacion de continuidad RUN en banco
- registro de resultados finales por canal
- prueba fisica de Yarn/Stitch en paralelo con J
