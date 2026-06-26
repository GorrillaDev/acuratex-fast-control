# Arquitectura Rapida

Fecha de referencia: 2026-06-24

## Objetivo

Dejar una ruta funcional rapida para Acuratex usando solo:

- `app_windows`
- `UsbSmokeIdf`

Sin tocar:

- `referencia_antigua`
- el Dashboard Unificado
- el markup visual del Dashboard Modular

## Principio de la fase

La UI del Dashboard Modular conserva su estructura, colores, tipografias, botones e iconos.  
Solo cambia el comportamiento:

- memoria visual local por canal J
- RUN / STOP no bloqueantes
- envio de comandos cortos
- respuesta inmediata `QUEUED`
- ejecucion fisica en firmware

## Flujo corto

1. El usuario pulsa un boton en `CabezalDashboardTarjetas`.
2. La app actualiza el estado visual local de inmediato.
3. `FastDashboardCommandService` envia una linea corta.
4. La conexion acepta la escritura.
5. El firmware encola el comando.
6. La tarea de control en Core 1 ejecuta la accion fisica.
7. La app no espera `DONE`.

## Reparto por core

### Core 0

- WiFi
- TCP
- USB
- recepcion de comandos
- envio de respuestas
- encolado de trabajo

### Core 1

- cola de comandos
- registros `J`
- RUN / STOP de `J`
- Yarn
- Stitch
- DEN
- SIC
- testeo
- CAN RX
- CAN TX
- scheduler fisico

## Estado actual de la fase

Ya esta aplicada la base rapida para `J1` a `J8`:

- registro local inicial `0xFF`
- activo-bajo
- animacion visual local
- `RUN` independiente por canal
- `STOP` de canal individual
- `RUN ALL` / `STOP ALL`

## Pines CAN confirmados en firmware

Confirmacion actual del codigo:

- `CAN TX = GPIO4`
- `CAN RX = GPIO5`
- `CAN STBY = GPIO6`
- `CAN STBY activo = LOW`
- `CAN bitrate = 1 Mbps`

## Pendientes de fases posteriores

- Yarn
- Stitch
- DEN repetitivo
- SIC repetitivo
- TESTEO / INIT completos
- medicion fisica en banco

