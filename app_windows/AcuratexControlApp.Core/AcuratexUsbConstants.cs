namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para concentrar los valores USB fijos del dispositivo Acuratex.
///
/// [QUIEN LA USA]
/// La usan el enumerador WinUSB, el transporte y la UI de conexion.
///
/// [CUANDO SE USA]
/// Se usa al detectar hardware o al construir una consulta de identificacion.
///
/// [ENTRADAS]
/// No recibe entradas; solo expone constantes.
///
/// [SALIDAS]
/// Devuelve identificadores estables del dispositivo.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Detector USB -> constantes -> enumeracion -> conexion.
///
/// [EQUIVALENCIA MCU]
/// Se parece a dejar fijos el VID, PID y un ID de interfaz en firmware.
///
/// [SI NO EXISTIERA]
/// El detector USB tendria que repetir literales en varios lugares.
/// </summary>
public static class AcuratexUsbConstants
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para fijar el Vendor ID del fabricante.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan el enumerador USB y el transporte WinUSB.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al buscar dispositivos Acuratex en Windows.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el VID del fabricante.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Detector USB -> `VendorId` -> coincidencia de hardware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al ID de fabricante que identifica una familia de placas.
    ///
    /// [SI NO EXISTIERA]
    /// Windows no sabría qué familia de dispositivo buscar.
    /// </summary>
    // [ACURATEX] VID fijo del fabricante para que Windows identifique la familia de dispositivos.
    public const ushort VendorId = 0xCAFE;
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para fijar el Product ID del dispositivo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan el enumerador y el transporte.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al distinguir este producto de otros del mismo fabricante.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el PID del producto.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Detector USB -> `ProductId` -> hardware correcto.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al número de producto grabado en un periférico.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podría separar este producto de otros parecidos.
    /// </summary>
    // [ACURATEX] PID fijo del producto Acuratex.
    public const ushort ProductId = 0x4030;
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para fijar el GUID de interfaz usado por WinUSB.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan el enumerador y APIs nativas de Windows.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al localizar la interfaz correcta del dispositivo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el GUID como cadena.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Enumeración -> `InterfaceGuidString` -> interfaz WinUSB.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un identificador de canal/periférico reservado en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// La app no sabría qué interfaz WinUSB buscar.
    /// </summary>
    // [ACURATEX] GUID de la interfaz WinUSB usada por el enumerador.
    public const string InterfaceGuidString = "{D7761D50-5F1B-4D33-95F2-733B0E5F2EED}";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para tener el GUID ya convertido al tipo que entiende Windows.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan las APIs que esperan un `Guid` en vez de texto.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando la app pasa del identificador textual a la API nativa.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve un `Guid` listo para usar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// `InterfaceGuidString` -> `InterfaceGuid` -> WinUSB.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a convertir un ID textual en un número de registro usable por firmware.
    ///
    /// [SI NO EXISTIERA]
    /// Habría que reconstruir el GUID cada vez que se usa.
    /// </summary>
    // [ACURATEX] Objeto `Guid` ya listo para pasarlo a APIs de Windows.
    public static readonly Guid InterfaceGuid = new(InterfaceGuidString);
}
