# Documentacion de cambio: Yarn y Stitch visual local

## Alcance
- Archivo modificado: `app_windows/AcuratexControlApp/Components/CabezalDashboardTarjetas.razor`
- No se modifico firmware.
- No se modificaron servicios CAN.
- No se modificaron IDs ni datos CAN.

## Que quedo implementado
- Toggle visual inmediato para los botones numerados de Yarn 1, Yarn 2, Stitch 1, Stitch 2, Stitch 3 y Stitch 4.
- Memoria visual local por modulo mientras el Dashboard permanece abierto.
- `ON ALL` y `OFF ALL` pintan todos los canales del modulo de forma inmediata.
- `RUN` y `STOP` de Yarn/Stitch se reflejan con una animacion visual local por modulo.
- Se anulo la pintura de Yarn/Stitch desde `HEAD_STATE` para que no controle el color.

## Como se guarda el color local
- El estado visual vive en `CabezalOutputBlockTarjetas.States`.
- Cada bloque Yarn/Stitch mantiene su propio arreglo de booleanos.
- Un clic individual escribe el nuevo valor en ese arreglo antes de enviar el comando.
- `ON ALL` usa `Array.Fill(..., true)`.
- `OFF ALL` usa `Array.Fill(..., false)`.

## Como funciona el toggle individual
- Primer clic: `false -> true`
- Segundo clic: `true -> false`
- Tercer clic: `false -> true`
- El cambio de color ocurre antes de la respuesta del firmware.

## Como funciona RUN / STOP
- Se agrego un loop visual local por bloque.
- El loop recorre los canales del bloque en orden y alterna ON/OFF cada 80 ms.
- `STOP` cancela el loop local y conserva el ultimo estado visible.

## Comandos
- No se cambiaron los comandos que emite la vista.
- Se conservan los comandos actuales de Yarn y Stitch.

## Build
- `dotnet build .\AcuratexControlApp.sln -c Debug` no completo.
- Motivo: restore fallido por falta de acceso a NuGet y paquetes no resueltos en el entorno.
- Errores vistos:
  - `NU1301` al cargar `https://api.nuget.org/v3/index.json`
  - `NU1101` para paquetes como `Microsoft.AspNetCore.Components.WebView.WindowsForms` y `System.IO.Ports`

## Estado final
- La UI de Yarn y Stitch quedo con color local inmediato.
- No se agrego confirmacion fisica ni rollback.
- No se toco firmware ni protocolo.
