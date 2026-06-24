# Mapa HTML Antigua vs App Modular

## Regla de esta fase

No se copia el HTML antiguo como visual nuevo.  
Solo se copia el comportamiento funcional que ya estaba probado.

## Mapa funcional

| HTML antiguo | App Modular actual |
| --- | --- |
| Botones CH1..CH8 | Misma distribucion visual existente |
| Memoria local de estado | Registro local `J1..J8` en memoria |
| RUN / STOP por canal | Comandos `j_run_N` / `j_stop_N` |
| RUN ALL / STOP ALL | Comandos `j_run_all` / `j_stop_all` |
| Cambio visual inmediato | Repintado inmediato del boton |
| Animacion de recorrido | Animacion local solo visual |
| Sin espera larga | Respuesta inmediata `QUEUED` |
| CAN directo corto | Envio de linea corta o frame directo |

## Comportamiento retenido

- el clic cambia el estado visual al instante
- el canal recuerda su ultimo registro local
- `STOP` restaura el estado visual previo
- varios `RUN` pueden convivir al mismo tiempo
- pulsar un canal no pausa otros canales en ejecucion

## Comportamiento no heredado

- no hay reinferencias desde `HEAD_STATUS`
- no hay reconstruccion visual desde firmware
- no se usa TXT como flujo operativo del cabezal
- no se usa `FILE_SELECT`
- no se espera `DONE`

## Implementacion actual

- `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`
- `app_windows/AcuratexControlApp/Services/FastDashboardCommandService.cs`
- `app_windows/AcuratexControlApp/CardSystemForm.cs`

