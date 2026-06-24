// [ACURATEX] Este helper busca dispositivos WinUSB que coincidan con el GUID de interfaz
// de la aplicación.
namespace AcuratexControlApp;

// [C#] `static` porque solo ofrece funciones utilitarias.
public static class WinUsbDeviceEnumerator
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para preguntar a Windows qué interfaces USB Acuratex están presentes.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la ventana principal cuando necesita refrescar la lista de dispositivos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al refrescar endpoints USB.
    ///
    /// [ENTRADAS]
    /// Recibe el GUID de interfaz que identifica la familia de dispositivos.
    ///
    /// [SALIDAS]
    /// Devuelve una lista de `UsbVendorDeviceInfo`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Consulta APIs de configuración de Windows y crea objetos informativos.
    ///
    /// [FLUJO ACURATEX]
    /// Form1 -> WinUsbDeviceEnumerator.Enumerate -> Windows -> lista de dispositivos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a recorrer un bus y construir una tabla de periféricos presentes.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podría saber qué WinUSB están conectados.
    /// </summary>
    public static IReadOnlyList<UsbVendorDeviceInfo> Enumerate(Guid interfaceGuid)
    {
        // [ACURATEX] La API nativa devuelve una lista doble-null terminada; por eso hay que recorrer el buffer completo.
        // [C#] `Guid` se copia a una variable local para pasarla por referencia a la API nativa.
        Guid guid = interfaceGuid;
        int status = WinUsbNative.CM_Get_Device_Interface_List_SizeW(
            out uint bufferLength,
            ref guid,
            null,
            WinUsbNative.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);

        if (status != WinUsbNative.CR_SUCCESS || bufferLength <= 1) {
            return Array.Empty<UsbVendorDeviceInfo>();
        }

        // [ACURATEX] La API nativa llena un buffer doble-null terminado.
        char[] buffer = new char[bufferLength];
        status = WinUsbNative.CM_Get_Device_Interface_ListW(
            ref guid,
            null,
            buffer,
            bufferLength,
            WinUsbNative.CM_GET_DEVICE_INTERFACE_LIST_PRESENT);

        if (status != WinUsbNative.CR_SUCCESS) {
            return Array.Empty<UsbVendorDeviceInfo>();
        }

        // [C#] `List<T>` acumula resultados antes de exponerlos como lista solo lectura.
        List<UsbVendorDeviceInfo> devices = new();
        int startIndex = 0;

        // [ACURATEX] Cada cadena del buffer representa una ruta distinta separada por `\0`.
        // [C#] El bucle avanza sobre el arreglo completo porque el ultimo elemento tambien puede ser un nulo extra.
        for (int i = 0; i < buffer.Length; i++) {
            if (buffer[i] != '\0') {
                continue;
            }

            if (i == startIndex) {
                break;
            }

            // [ACURATEX] Cada segmento entre nulos es una ruta de dispositivo.
            string devicePath = new(buffer, startIndex, i - startIndex);
            devices.Add(new UsbVendorDeviceInfo(devicePath));
            startIndex = i + 1;
        }

        return devices;
    }
}
