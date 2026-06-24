using AcuratexControlApp.Models;

namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para aislar la lógica que convierte una línea cruda del firmware en
/// un objeto de estado usable por la UI.
///
/// [QUIÉN LA USA]
/// La usa la vista del cabezal y cualquier otro componente que reciba líneas `427|...|420`.
///
/// [CUÁNDO SE USA]
/// Se usa cuando llega una línea entrante desde la conexión activa.
///
/// [ENTRADAS]
/// Recibe una línea de texto y devuelve un `HeadStateEvent` por `out` si la línea es válida.
///
/// [SALIDAS]
/// Devuelve `bool` para indicar si el texto se pudo interpretar.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene; solo define contrato.
///
/// [FLUJO ACURATEX]
/// Firmware -> parser -> `HeadStateEvent` -> UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una función de decodificación de trama que devuelve éxito o fracaso.
///
/// [SI NO EXISTIERA]
/// La UI tendría que conocer el formato textual del firmware.
/// </summary>
public interface IHeadStateEventParser
{
    /// <summary>
    /// Intenta convertir una línea `427|...|420` en un objeto de estado ya interpretado.
    /// </summary>
    bool TryParse(string line, out HeadStateEvent? stateEvent);
}
