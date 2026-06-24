namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para separar la vista modular de cabezal de la traduccion real
/// hacia comandos CAN, DO y acciones de perfil.
///
/// [QUIÉN LA USA]
/// La usan los componentes Razor del cabezal modular.
///
/// [CUÁNDO SE USA]
/// Se usa cuando el operador pulsa controles de J, DEN, SIC o bloques fisicos.
///
/// [ENTRADAS]
/// Recibe lineas, comandos, indices, valores y estados on/off.
///
/// [SALIDAS]
/// Devuelve tareas asincronas.
///
/// [EFECTOS SECUNDARIOS]
/// Puede traducir la accion a scripts, perfiles o envios directos al firmware.
///
/// [FLUJO ACURATEX]
/// Razor -> servicio -> perfil/script -> conexion -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un driver de alto nivel que empaqueta varias salidas logicas.
///
/// [SI NO EXISTIERA]
/// La UI tendria que calcular el protocolo y el mapping del cabezal por su cuenta.
/// </summary>
public interface ICabezalDashboardTarjetasCommandService
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para reenviar una linea CAN textual sin reinterpretarla.
    ///
    /// [QUIÉN LA USA]
    /// La usan los editores y controles manuales del cabezal modular.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando la UI ya tiene la linea CAN lista para salir.
    ///
    /// [ENTRADAS]
    /// Recibe la linea de texto y un token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar la linea al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SendCanLineAsync()` -> conexion -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a poner una trama CAN directamente en la cola de salida.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendria que llamar al transporte por su cuenta.
    /// </summary>
    Task SendCanLineAsync(string line, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para traducir una orden DO de alto nivel a una accion concreta.
    ///
    /// [QUIÉN LA USA]
    /// La usan los botones y atajos de la vista modular.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario pide una accion DO.
    ///
    /// [ENTRADAS]
    /// Recibe el comando y un token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede traducir el comando en `HEAD_ACTION`, en comandos directos o en scripts.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> servicio -> script/HEAD_ACTION -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un dispatcher que decide que orden de control enviar.
    ///
    /// [SI NO EXISTIERA]
    /// Cada boton tendria que duplicar la traduccion del comando.
    /// </summary>
    Task SendDoCommandAsync(string command, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para mandar una posicion logica de un motor DEN sin que la UI
    /// conozca el detalle del perfil activo.
    ///
    /// [QUIÉN LA USA]
    /// La usan los componentes de motor.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario selecciona una posicion de DEN.
    ///
    /// [ENTRADAS]
    /// Recibe indice del motor, posicion y numero de posicion seleccionada.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede terminar enviando una accion de perfil al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> servicio -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a traducir un valor de nivel alto a una orden de salida concreta.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendria que saber como se representan las posiciones del perfil.
    /// </summary>
    Task SendDenPositionAsync(int motorIndex, int position, int selectedPositionNumber = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para mandar una posicion logica de un motor SIC.
    ///
    /// [QUIÉN LA USA]
    /// La usan los componentes de motor SIC.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario cambia la posicion de SIC.
    ///
    /// [ENTRADAS]
    /// Recibe indice, posicion, numero de posicion seleccionada y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede traducir la accion a un script o a un `HEAD_ACTION`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> servicio -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a ordenar un canal concreto de un subsistema de salidas.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que implementar la logica de traduccion por su cuenta.
    /// </summary>
    Task SendSicPositionAsync(int sicIndex, int position, int selectedPositionNumber = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para traducir el registro global de J a una accion de perfil.
    ///
    /// [QUIÉN LA USA]
    /// La usan los bloques J de la vista.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando la UI necesita reescribir el registro logico del grupo.
    ///
    /// [ENTRADAS]
    /// Recibe indice J, valor de registro y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede desencadenar un comando de perfil hacia el firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> servicio -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir un registro completo de salida.
    ///
    /// [SI NO EXISTIERA]
    /// El grupo J no podria abstraerse como registro.
    /// </summary>
    Task SendJRegisterAsync(int jIndex, byte value, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para encender o apagar todos los canales de un grupo J con una
    /// sola intencion de alto nivel.
    ///
    /// [QUIÉN LA USA]
    /// La llama la UI al pulsar ON ALL u OFF ALL en un grupo J.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario aplica el estado global de grupo.
    ///
    /// [ENTRADAS]
    /// Recibe el indice del grupo, la intencion on/off y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Se traduce en una accion sobre el perfil activo.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> servicio -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a poner todas las salidas de un puerto en el mismo nivel.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que repetir la misma orden por canal.
    /// </summary>
    Task SendJAllAsync(int jIndex, bool on, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para mandar una accion a un canal concreto dentro de un grupo J.
    ///
    /// [QUIÉN LA USA]
    /// La llaman los botones individuales J1..J8.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario pulsa un boton de canal.
    ///
    /// [ENTRADAS]
    /// Recibe indice J, indice de canal y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Se traduce a una accion concreta de perfil.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> servicio -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar un bit individual dentro de un registro.
    ///
    /// [SI NO EXISTIERA]
    /// No habria un canal fino para cada boton J.
    /// </summary>
    Task SendJChannelAsync(int jIndex, int channelIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para traducir un pin de un bloque fisico a la accion del perfil
    /// asociado.
    ///
    /// [QUIÉN LA USA]
    /// La llaman los bloques Yarn y Stitch.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario pulsa un pin de bloque.
    ///
    /// [ENTRADAS]
    /// Recibe la clave del bloque, el pin y el estado on/off.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede generar una accion de perfil concreta.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> servicio -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a resolver que salida fisica corresponde a un canal logico.
    ///
    /// [SI NO EXISTIERA]
    /// Los bloques tendrian que conocer su mapping interno.
    /// </summary>
    Task SendBlockPinAsync(string blockKey, int pinIndex, bool on, CancellationToken cancellationToken = default);
}
