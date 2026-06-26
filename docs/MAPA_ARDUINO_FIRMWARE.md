# Mapa Arduino Antiguo vs Firmware ESP-IDF

## Objetivo

Trasladar el comportamiento fisico probado del Arduino antiguo a `UsbSmokeIdf`
sin copiar repositorios antiguos ni depender de archivos TXT.

## Base de la fase

### J

Estado local minimo:

```text
J1.Register = 0xFF
J2.Register = 0xFF
...
J8.Register = 0xFF
```

Regla activa-bajo:

- bit 0 = canal encendido
- bit 1 = canal apagado

Trama J:

- `ID = 0x320`
- `0x1D`
- subindice `J` de `0` a `7`
- registro fisico actual

Ejemplo:

```text
320 1D 00 FE
```

## Cascade J

Cada `J` corre de forma independiente.

```text
struct Cascade
{
bool running;
uint8_t phase;
uint8_t p;
uint32_t next_ms;
uint16_t delay_ms;
};
```

Periodo inicial:

- `80 ms`

Fase 0:

- encender canales progresivamente del 1 al 8

Fase 1:

- apagar canales progresivamente del 1 al 8

Al terminar:

- volver a fase 0
- repetir mientras `running == true`

## Yarn

Yarn 1:

- `0x18, 0x19, 0x1A, 0x1B,`
- `0x1C, 0x1D, 0x1E, 0x1F`

Yarn 2:

- `0x24, 0x25, 0x26, 0x27,`
- `0x20, 0x21, 0x22, 0x23`

## Stitch

Stitch 1:

- `0x00, 0x01, 0x02, 0x05`

Stitch 2:

- `0x06, 0x07, 0x08, 0x0B`

Stitch 3:

- `0x0C, 0x0D, 0x0E, 0x11`

Stitch 4:

- `0x12, 0x13, 0x14, 0x17`

## DEN

Posiciones:

- `0x0000`
- `0x00A2`
- `0x0145`
- `0x01E7`
- `0x028A`

Secuencia RUN:

- `1, 3, 5, 2, 4`

Periodo:

- `80 ms`

Secuencia RUN1:

- `1, 3, 5`

Periodo:

- `300 ms`

## SIC

Posiciones:

- `0x0000`
- `0x0176`
- `0x02EE`

Secuencia:

- `1, 2, 3`

Periodo:

- `300 ms`

## Estado actual

En esta fase rapida ya estan portados:

- cola de comandos
- `J` local y visual
- envio directo de CAN
- respuesta inmediata `QUEUED`
- control en Core 1

Pendiente para fases siguientes:

- ciclos repetitivos completos de Yarn
- ciclos repetitivos completos de Stitch
- secuencias completas de DEN
- secuencias completas de SIC

