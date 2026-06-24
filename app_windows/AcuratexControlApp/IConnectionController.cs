// [ACURATEX] Este archivo define el contrato que usan la ventana principal, los servicios
// y los componentes para hablar con el transporte real.
namespace AcuratexControlApp;

// [C#] `interface` no implementa comportamiento: solo describe qué operaciones deben existir.
// [ACURATEX] Aquí se modela el punto único de control de conexión para toda la app.
/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para que toda la aplicacion hable con un solo controlador de conexion,
/// sin saber si por dentro se usa USB, TCP o Serial.
///
/// [QUIÉN LA USA]
/// La usan la ventana principal, los formularios y los servicios que envian comandos.
///
/// [CUÁNDO SE USA]
/// Sus miembros se consultan durante el arranque, la conexion, el envio y la desconexion.
///
/// [ENTRADAS]
/// Define parametros de conexion, mensajes enviados y tokens de cancelacion.
///
/// [SALIDAS]
/// Expone estado, notificaciones de recepcion, avisos de envio y caidas de enlace.
///
/// [EFECTOS SECUNDARIOS]
/// Las implementaciones pueden abrir hardware, suscribir callbacks y cerrar recursos nativos.
///
/// [FLUJO ACURATEX]
/// UI -> IConnectionController -> ConnectionController -> transporte concreto -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un modulo de comunicacion de alto nivel que decide por que periferico hablar.
///
/// [SI NO EXISTIERA]
/// Cada pantalla tendria que conocer el transporte real y repetir la misma logica.
/// </summary>
public interface IConnectionController : IDisposable
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para que la UI sepa si hay un enlace valido antes de habilitar acciones.
    ///
    /// [QUIÉN LA USA]
    /// La usan botones, paneles y estados visuales.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cada vez que la interfaz necesita saber si puede mandar datos.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `IsConnected` -> habilitar o deshabilitar acciones.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un flag de periferico listo.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que preguntar por separado a cada transporte.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar que llego una linea completa desde el firmware.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la interfaz y los parsers de estado.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el transporte reconstruye una linea terminada.
    ///
    /// [ENTRADAS]
    /// Entrega el texto recibido.
    ///
    /// [SALIDAS]
    /// No devuelve valor; dispara callbacks.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede actualizar estados, luces y paneles.
    ///
    /// [FLUJO ACURATEX]
    /// Firmware -> transporte -> `LineReceived` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de recepcion que entrega la trama completa.
    ///
    /// [SI NO EXISTIERA]
    /// La app no sabria reaccionar cuando el firmware responde.
    /// </summary>
    event Action<string>? LineReceived;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para notificar que una linea ya salio hacia el firmware.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan los paneles de trazado y diagnostico.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta justo antes o junto al envio efectivo de la linea.
    ///
    /// [ENTRADAS]
    /// Entrega el texto transmitido.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede alimentar logs o una vista de eco local.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `LineSent` -> diagnostico visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un callback de "trama transmitida".
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz perderia una señal clara de lo que realmente se envio.
    /// </summary>
    event Action<string>? LineSent;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar que la conexion se perdio de forma involuntaria.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la UI y los servicios que deben limpiar estado.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el enlace cae sin una desconexion solicitada por la app.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede disparar reinicios visuales y limpieza de estado.
    ///
    /// [FLUJO ACURATEX]
    /// Transporte -> `ConnectionLost` -> controlador -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de falla de enlace.
    ///
    /// [SI NO EXISTIERA]
    /// La aplicacion podria quedarse en un estado falso de conexion activa.
    /// </summary>
    event Action? ConnectionLost;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para abrir el transporte correcto segun el modo elegido.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController` desde la UI principal o desde servicios que
    /// piden iniciar sesion de comunicacion.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pulsa Conectar o cuando la app necesita rehacer el enlace.
    ///
    /// [ENTRADAS]
    /// Recibe el modo, un dispositivo USB opcional, host, puerto TCP, puerto serial,
    /// baudrate y un `CancellationToken` para permitir cancelacion cooperativa.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` porque abrir el transporte puede esperar E/S real.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Abre hardware, sockets o puertos serie y puede activar eventos de recepcion.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> IConnectionController.ConnectAsync -> transporte concreto -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inicializar UART, USB o Ethernet antes de empezar a intercambiar tramas.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una forma uniforme de abrir la comunicacion desde la UI.
    /// </summary>
    Task ConnectAsync(
        ConnectionMode mode,
        UsbVendorDeviceInfo? device,
        string host,
        int tcpPort,
        string serialPort,
        int baudRate,
        CancellationToken cancellationToken);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para cerrar el transporte de manera explicita y ordenada.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI, el cierre de la ventana o el controlador central cuando cambia de modo.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario desconecta o cuando la app necesita liberar recursos.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// Devuelve `Task` porque el cierre puede esperar a que terminen tareas en segundo plano.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Desuscribe eventos, detiene lectura y libera recursos nativos.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> IConnectionController.DisconnectAsync -> transporte -> liberacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar y deshabilitar perifericos antes de cortar alimentacion.
    ///
    /// [SI NO EXISTIERA]
    /// La aplicacion no tendria un cierre ordenado del enlace.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operacion existe para mandar una linea de texto ya lista al firmware.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman la ventana principal y los servicios que construyen comandos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pulsa enviar o cuando una accion de la UI dispara un comando.
    ///
    /// [ENTRADAS]
    /// Recibe la linea a transmitir y un `CancellationToken`.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Transmite una trama al firmware y puede disparar errores de conexion.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> IConnectionController.SendLineAsync -> transporte -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir un comando en un buffer de salida UART/CAN/USB.
    ///
    /// [SI NO EXISTIERA]
    /// No se podrian mandar comandos de texto por la ruta centralizada.
    /// </summary>
    Task SendLineAsync(string line, CancellationToken cancellationToken);
}
