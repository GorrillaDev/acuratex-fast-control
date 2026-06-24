// [ACURATEX] Representa una ruta de dispositivo WinUSB detectada por GUID de interfaz.
namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para transportar la ruta nativa de un dispositivo WinUSB detectado.
///
/// [QUIEN LA USA]
/// La usan el enumerador USB y la factoría de transporte.
///
/// [CUANDO SE USA]
/// Se usa al convertir resultados de enumeracion en opciones de conexion.
///
/// [ENTRADAS]
/// Recibe la ruta del dispositivo.
///
/// [SALIDAS]
/// Devuelve la ruta como objeto y texto.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// WinUSB -> enumeracion -> `UsbVendorDeviceInfo` -> transporte.
///
/// [EQUIVALENCIA MCU]
/// Se parece a guardar el identificador de un periférico detectado por bus.
///
/// [SI NO EXISTIERA]
/// La ruta nativa quedaria como string suelta en varios puntos.
/// </summary>
public sealed class UsbVendorDeviceInfo
{
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para fijar la ruta WinUSB identificada por el enumerador.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama `WinUsbDeviceEnumerator`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando Windows reporta un dispositivo WinUSB valido.
    ///
    /// [ENTRADAS]
    /// Recibe la ruta nativa del dispositivo.
    ///
    /// [SALIDAS]
    /// Devuelve el objeto listo para usar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda la ruta exacta del dispositivo.
    ///
    /// [FLUJO ACURATEX]
    /// Enumeracion WinUSB -> `UsbVendorDeviceInfo` -> conexion USB.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a fijar la direccion de un periférico detectado en un bus.
    ///
    /// [SI NO EXISTIERA]
    /// La ruta tendria que viajar como texto suelto entre varias capas.
    /// </summary>
    public UsbVendorDeviceInfo(string devicePath)
    {
        // [ACURATEX] La ruta nativa queda fija porque identifica el dispositivo exacto detectado.
        // [C#] El constructor guarda el dato una sola vez y luego la propiedad solo se lee.
        DevicePath = devicePath;
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta propiedad existe para exponer la ruta WinUSB sin permitir que otras capas la
    /// modifiquen.
    ///
    /// [QUIEN LA USA]
    /// La usan el controlador de conexion y la UI de USB.
    ///
    /// [CUANDO SE USA]
    /// Se consulta al conectar por WinUSB.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la ruta nativa del dispositivo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// `UsbVendorDeviceInfo` -> `DevicePath` -> transporte WinUSB.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al identificador fisico unico de un modulo en un bus.
    ///
    /// [SI NO EXISTIERA]
    /// El identificador quedaria oculto dentro del objeto.
    /// </summary>
    public string DevicePath { get; }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para mostrar la ruta del dispositivo de forma directa en listas y
    /// trazas.
    ///
    /// [QUIEN LO USA]
    /// Lo usan diagnósticos y la UI al imprimir el dispositivo.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta cuando el objeto necesita convertirse a texto.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la ruta nativa como texto.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// `UsbVendorDeviceInfo` -> `ToString()` -> texto visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a imprimir un identificador de periferico para diagnostico.
    ///
    /// [SI NO EXISTIERA]
    /// Las listas tendrian que formatear la ruta por su cuenta.
    /// </summary>
    public override string ToString()
    {
        // [ACURATEX] La UI usa esta forma corta para mostrar la ruta en listas o depuracion.
        return DevicePath;
    }
}
