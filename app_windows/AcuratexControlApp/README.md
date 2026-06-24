# AcuratexControlApp

Base de app `C# WinForms` para operar el `ESP32-S3` por:

- `USB nativo` usando `WinUSB`
- `WiFi` usando `TCP`

## Modo USB

La app busca un dispositivo `vendor-specific` del `ESP32-S3` con:

- `VID`: `0xCAFE`
- `PID`: `0x4030`
- `GUID`: `{D7761D50-5F1B-4D33-95F2-733B0E5F2EED}`

Ese `USB` no aparece como `COM` para la app.

## Modo WiFi

La app se conecta por socket `TCP` a:

- `192.168.137.2`
- puerto `3333`

Estos valores siguen editables en la interfaz.

## Publicar como EXE portable

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Ruta de salida

- `bin\Release\net8.0-windows\win-x64\publish`

## Ejecutable portable

- `bin\Release\net8.0-windows\win-x64\publish\AcuratexControlApp.exe`

## Notas

- La app envia lineas de texto compatibles con tu protocolo actual, por ejemplo `320 07`.
- La parte de `WiFi` usa el mismo protocolo por lineas que el firmware.
- El `UART/FTDI` del equipo queda fuera de la app; ese puerto es solo para `flash` y servicio.
