namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Este enum existe para representar de forma compacta el canal de conexion elegido por la UI.
///
/// [QUIÉN LO USA]
/// Lo usan el panel principal, la pantalla de conexion y `ConnectionController`.
///
/// [CUÁNDO SE USA]
/// Se usa al decidir si la app hablara por USB, WiFi o Serial.
///
/// [ENTRADAS]
/// No recibe entradas; solo define valores.
///
/// [SALIDAS]
/// Proporciona el valor elegido como resultado de la seleccion visual.
///
/// [EFECTOS SECUNDARIOS]
/// No modifica estado por si mismo.
///
/// [FLUJO ACURATEX]
/// UI -> seleccion de canal -> `ConnectionMode` -> transporte concreto.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un selector de periferico que decide por que bus se comunicara el micro.
///
/// [SI NO EXISTIERA]
/// La app tendria que usar cadenas sueltas para identificar el tipo de conexion.
/// </summary>
public enum ConnectionMode
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para indicar que la conexion se hara por USB WinUSB.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la pantalla de conexion, la factoría de transporte y el controlador central.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando el usuario elige USB como medio principal.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la opcion USB como resultado de la seleccion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ConnectionMode.Usb` -> transporte WinUSB.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a seleccionar el periferico USB en una maquina de estados.
    ///
    /// [SI NO EXISTIERA]
    /// La app no tendria una forma clara de representar el enlace por USB.
    /// </summary>
    Usb,

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para indicar que la conexion se hara por red local o WiFi.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la UI, el descubrimiento de red y el transporte TCP.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando el equipo se conecta por IP y puerto.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la opcion de red.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ConnectionMode.Wifi` -> discovery/TCP.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a elegir Ethernet o WiFi como bus externo.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria diferenciar una conexion de red de una serial.
    /// </summary>
    Wifi,

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para indicar que la conexion se hara por puerto serial.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la UI y el transporte serial.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando la app habla por puerto COM.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la opcion serial.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ConnectionMode.Serial` -> `SerialPort`.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a seleccionar una UART como canal de comunicacion.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podria representar la conexion por COM de forma tipada.
    /// </summary>
    Serial,
}
