// [ACURATEX] Este contrato define el "enchufe" común para USB, TCP y Serial.
// La UI no conoce el detalle del medio fisico: solo sabe que puede conectar, enviar y desconectar.
namespace AcuratexControlApp;

// [C#] `interface` solo define capacidades, no estado concreto.
/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para que la capa de UI y la capa de control trabajen con un solo
/// contrato, aunque por debajo el enlace real sea WinUSB, TCP o un puerto serial.
///
/// [QUIÉN LA USA]
/// La usa `ConnectionController`, que actúa como mediador entre la UI y el transporte concreto.
///
/// [CUÁNDO SE EJECUTA]
/// No se ejecuta por si sola; sus miembros se invocan cuando la app abre, usa o cierra una conexion.
///
/// [ENTRADAS]
/// Define operaciones que reciben tokens de cancelacion, texto de envio y eventos de recepcion.
///
/// [SALIDAS]
/// Expone si el medio esta conectado y permite recibir lineas y avisos de desconexion.
///
/// [EFECTOS SECUNDARIOS]
/// Cada implementacion puede abrir handles, sockets o puertos COM y puede lanzar eventos.
///
/// [FLUJO ACURATEX]
/// UI -> ConnectionController -> IControllerTransport -> hardware/red.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un driver de periferico: el firmware sabe que "hay un canal", pero no quien lo implementa.
///
/// [SI NO EXISTIERA]
/// Cada medio tendria su propia ruta de llamada y la app se llenaria de `if` por tecnologia.
/// </summary>
public interface IControllerTransport : IDisposable
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para que el controlador sepa si puede enviar comandos sin intentar reabrir el enlace.
    ///
    /// [QUIÉN LA USA]
    /// La usa `ConnectionController` y, en algunos casos, la UI de estado.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se consulta cada vez que la app necesita decidir si el enlace sigue valido.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el transporte esta abierto y `false` si ya no lo esta.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ConnectionController -> `IsConnected` -> decision de envio.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de estado que indica si una UART, USB o socket sigue habilitado.
    ///
    /// [SI NO EXISTIERA]
    /// La app tendria que adivinar si el medio sigue vivo antes de enviar.
    /// </summary>
    // [C#] Una propiedad de interfaz fija el contrato que la clase concreta debe cumplir.
    bool IsConnected { get; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para notificar lineas completas recibidas desde el firmware o desde la red.
    ///
    /// [QUIÉN LO USA]
    /// Lo suscribe `ConnectionController`, que luego redistribuye el dato a la UI o a otros servicios.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el transporte ya reconstruyo una linea valida.
    ///
    /// [ENTRADAS]
    /// Entrega una cadena con el texto recibido.
    ///
    /// [SALIDAS]
    /// No devuelve valor; su salida es el callback suscrito.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede disparar actualizaciones de estado, parsers y eventos de la interfaz.
    ///
    /// [FLUJO ACURATEX]
    /// Firmware/red -> transporte -> `LineReceived` -> ConnectionController.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion o callback de recepcion por UART/CAN.
    ///
    /// [SI NO EXISTIERA]
    /// La app tendria que sondear el medio en lugar de reaccionar a cada linea recibida.
    /// </summary>
    event Action<string>? LineReceived;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar que el enlace se perdio sin una desconexion ordenada.
    ///
    /// [QUIÉN LO USA]
    /// Lo suscribe `ConnectionController` para limpiar estado y avisar a la UI.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el transporte detecta error, perdida fisica o cierre remoto.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// No devuelve valor; solo activa el callback registrado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede provocar el cierre de la sesion activa y la invalidacion de la conexion visual.
    ///
    /// [FLUJO ACURATEX]
    /// Transporte -> `ConnectionLost` -> ConnectionController -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de error de bus o a un flag de link caido.
    ///
    /// [SI NO EXISTIERA]
    /// La aplicacion podria quedarse creyendo que el medio sigue activo aunque ya se cayo.
    /// </summary>
    event Action? ConnectionLost;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para abrir el medio concreto sin que la UI sepa cuál es.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al iniciar una conexión.
    ///
    /// [ENTRADAS]
    /// Recibe `CancellationToken` para permitir cancelación cooperativa.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede abrir un handle USB, un socket TCP o un puerto serie.
    ///
    /// [FLUJO ACURATEX]
    /// ConnectionController -> IControllerTransport.ConnectAsync -> hardware/red.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a configurar una interfaz periférica antes de habilitar el tráfico.
    ///
    /// [SI NO EXISTIERA]
    /// Cada transporte tendría una API distinta y la UI se llenaría de ramas.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para cerrar el medio concreto de forma ordenada.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al desconectar o al cambiar de transporte.
    ///
    /// [ENTRADAS]
    /// No recibe parámetros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cancela bucles de lectura y libera handles o sockets.
    ///
    /// [FLUJO ACURATEX]
    /// ConnectionController -> IControllerTransport.DisconnectAsync -> cleanup.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a deshabilitar una UART o cerrar un socket antes de liberar recursos.
    ///
    /// [SI NO EXISTIERA]
    /// No habría forma uniforme de liberar el transporte activo.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para enviar texto al medio ya abierto.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la app manda un comando.
    ///
    /// [ENTRADAS]
    /// Recibe la línea y un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Escribe bytes o texto y puede producir errores de E/S.
    ///
    /// [FLUJO ACURATEX]
    /// ConnectionController -> IControllerTransport.SendLineAsync -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a transmitir una trama por un bus serie o por red.
    ///
    /// [SI NO EXISTIERA]
    /// El transporte no tendría una operación común de escritura.
    /// </summary>
    Task SendLineAsync(string line, CancellationToken cancellationToken);
}
