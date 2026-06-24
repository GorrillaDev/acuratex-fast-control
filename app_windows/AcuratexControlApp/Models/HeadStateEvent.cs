namespace AcuratexControlApp.Models;

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para transportar a la UI el estado ya interpretado que llega desde el
/// firmware en una línea `427|...|420`.
///
/// [QUIÉN LO USA]
/// Lo usa el parser de estado y la vista que pinta botones, luces y registros del cabezal.
///
/// [CUÁNDO SE USA]
/// Se crea cada vez que llega una línea de estado válida desde la conexión activa.
///
/// [ENTRADAS]
/// Recibe nombre de instancia, tipo de estado, máscara ya convertida, máscara textual y
/// línea original.
///
/// [SALIDAS]
/// Devuelve una foto inmutable del estado recibido.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene efectos secundarios; solo agrupa datos.
///
/// [FLUJO ACURATEX]
/// Firmware -> parser -> `HeadStateEvent` -> UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una estructura de estado que ya fue decodificada desde una trama serie o CAN.
///
/// [SI NO EXISTIERA]
/// La UI tendría que volver a interpretar el texto crudo cada vez que quiera repintar.
/// </summary>
public sealed record HeadStateEvent(
    /// <summary>
    /// Nombre lógico de la instancia afectada, por ejemplo `J1`, `DEN2` o `SIC1`.
    /// </summary>
    string InstanceName,
    /// <summary>
    /// Tipo de estado recibido, como `POS`, `CH`, `RUN` o `RUN1`.
    /// </summary>
    string StateType,
    /// <summary>
    /// Máscara ya convertida a entero para que la UI la compare sin volver a parsear texto.
    /// </summary>
    int Mask,
    /// <summary>
    /// Máscara exactamente como llegó del firmware, útil para trazas y depuración.
    /// </summary>
    string RawMask,
    /// <summary>
    /// Valor opcional enviado por el firmware cuando el evento lo incluye.
    /// </summary>
    int? Value,
    /// <summary>
    /// Línea completa original, guardada para diagnóstico y logging.
    /// </summary>
    string RawLine);
