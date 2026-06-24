// [ACURATEX] Este contrato conecta los componentes Razor de la pantalla principal con la
// ventana WinForms que realmente controla la conexion.
// [FLUJO] Razor -> host -> Form1 -> ConnectionController -> firmware.
using AcuratexControlApp.Components;

namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para que la UI principal pueda pedir acciones a la ventana WinForms
/// sin conocer la clase concreta `Form1`.
///
/// [QUIÉN LA LLAMA]
/// La usan los componentes Razor de la pantalla principal.
///
/// [CUÁNDO SE USA]
/// Se usa durante el render y en respuesta a clicks, cambios de texto y comandos del usuario.
///
/// [ENTRADAS]
/// Define métodos que reciben el modo de conexión, endpoint, host, puerto, baudrate y comandos.
///
/// [SALIDAS]
/// Expone estado compartido y tareas asincrónicas para el ciclo de la UI.
///
/// [EFECTOS SECUNDARIOS]
/// Cada método puede cambiar estado visual, disparar conexión o enviar comandos.
///
/// [FLUJO ACURATEX]
/// Componentes Razor -> IMainControlPanelHost -> Form1 -> conexión.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla de callbacks que permite a una interfaz gráfica tocar un controlador
/// central sin acoplarse a su implementación.
///
/// [SI NO EXISTIERA]
/// Los componentes tendrían que conocer `Form1` y mezclarían UI con lógica de ventana.
/// </summary>
public interface IMainControlPanelHost
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para que la UI lea el estado compartido del panel principal.
    ///
    /// [QUIÉN LA USA]
    /// La usan los componentes Razor para dibujar texto, botones y listas.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cada vez que Blazor repinta la vista.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el estado actual del panel.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene; solo expone datos.
    ///
    /// [FLUJO ACURATEX]
    /// Form1 -> State -> Razor.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer registros de estado que el firmware actualiza.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabría qué mostrar.
    /// </summary>
    // [C#] Una propiedad de interfaz define el contrato que la clase concreta debe cumplir.
    MainControlPanelState State { get; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar a Blazor que el estado cambió.
    ///
    /// [QUIÉN LO USA]
    /// Lo suscriben los componentes Razor.
    ///
    /// [CUÁNDO SE USA]
    /// Se dispara después de modificar el estado compartido.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor; notifica.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Provoca repintado de la interfaz.
    ///
    /// [FLUJO ACURATEX]
    /// Form1 -> `StateChanged` -> Blazor refresca.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupción de actualización visual.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabría cuándo repintarse.
    /// </summary>
    // [C#] `Action?` es un delegado sin argumentos; `?` permite que no haya suscriptores.
    event Action? StateChanged;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para guardar el modo de conexion elegido por el usuario.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama el componente Razor cuando el operador cambia entre USB, red o serial.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al seleccionar una opcion de conexion.
    ///
    /// [ENTRADAS]
    /// Recibe el modo de conexion seleccionado.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el estado compartido del panel principal.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetConnectionModeAsync()` -> estado del panel.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar un selector de canal antes de arrancar una comunicacion.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabria que tipo de transporte configurar.
    /// </summary>
    Task SetConnectionModeAsync(ConnectionMode mode);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para registrar que endpoint de red o USB quedo seleccionado.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la UI cuando el usuario elige un destino concreto.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al cambiar la seleccion de endpoint.
    ///
    /// [ENTRADAS]
    /// Recibe el identificador del endpoint.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el estado visual y el endpoint activo.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetSelectedEndpointAsync()` -> estado -> conexion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a elegir un nodo o direccion concreta antes de transmitir.
    ///
    /// [SI NO EXISTIERA]
    /// La aplicacion no podria recordar a que destino conectarse.
    /// </summary>
    Task SetSelectedEndpointAsync(string endpointId);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para guardar el host o IP escrita por el usuario.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la UI de configuracion de red.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando cambia el texto del host.
    ///
    /// [ENTRADAS]
    /// Recibe una cadena con la direccion host.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el campo de host en el estado compartido.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetHostAsync()` -> estado de red.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar la direccion destino de una trama.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria recordar la IP escrita.
    /// </summary>
    Task SetHostAsync(string host);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para guardar el puerto de red o el puerto elegido para la conexion.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la UI cuando el operador cambia el puerto.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al editar el valor de puerto.
    ///
    /// [ENTRADAS]
    /// Recibe el texto del puerto.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el estado compartido.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetPortAsync()` -> estado de conexion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a ajustar el puerto de un canal de comunicacion.
    ///
    /// [SI NO EXISTIERA]
    /// La app no sabria a que puerto debe apuntar.
    /// </summary>
    Task SetPortAsync(string port);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para guardar el baudrate textual elegido en la interfaz.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la UI cuando se configura un puerto serial.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al cambiar la velocidad serial.
    ///
    /// [ENTRADAS]
    /// Recibe el baudrate como texto.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el estado que luego se convierte en numero.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetBaudAsync()` -> estado serial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar el divisor de velocidad de una UART.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria recordar la velocidad seleccionada.
    /// </summary>
    Task SetBaudAsync(string baud);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para guardar el comando manual que el usuario quiere enviar.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa la caja de texto manual de comandos.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al escribir o confirmar un comando.
    ///
    /// [ENTRADAS]
    /// Recibe la linea de comando manual.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el comando que luego se transmitira.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SetCommandAsync()` -> comando pendiente.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar un buffer de transmision antes de enviarlo.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria mantener un comando manual listo para enviar.
    /// </summary>
    Task SetCommandAsync(string command);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para volver a pedir la lista de endpoints visibles.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la UI cuando refresca el listado de conexiones.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al pulsar refrescar o al cambiar la red.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza la lista de destinos disponibles.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `RefreshEndpointsAsync()` -> endpoints actualizados.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a volver a escanear una tabla de nodos activos.
    ///
    /// [SI NO EXISTIERA]
    /// La lista de destinos podria quedarse obsoleta.
    /// </summary>
    Task RefreshEndpointsAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para disparar el descubrimiento WiFi de equipos Acuratex.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la UI cuando el usuario quiere buscar dispositivos en la red.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al pedir descubrimiento de red.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede llenar la lista de endpoints y mostrar resultados nuevos.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `DiscoverWifiAsync()` -> discovery UDP -> lista de dispositivos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un broadcast de bus que busca nodos disponibles.
    ///
    /// [SI NO EXISTIERA]
    /// La app no tendria una forma formal de buscar equipos por red.
    /// </summary>
    Task DiscoverWifiAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para pedir al formulario que conecte con los valores actuales.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la UI principal cuando el usuario pulsa Conectar.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al iniciar la conexion.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Inicia la conexion real y actualiza el estado.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `ConnectAsync()` -> Form1 -> ConnectionController.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a arrancar el canal de comunicacion principal.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendria un punto unico para disparar la conexion.
    /// </summary>
    Task ConnectAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para pedir la desconexion ordenada del enlace actual.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la UI principal o el cierre de la ventana.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario desconecta.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cierra recursos y limpia el estado de enlace.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `DisconnectAsync()` -> Form1 -> ConnectionController.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a apagar un bus antes de salir del modo activo.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una forma formal de cortar la comunicacion desde la UI.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para mandar la linea manual que el usuario preparo en pantalla.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa la UI cuando el operador confirma el envio manual.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al pulsar enviar en el comando manual.
    ///
    /// [ENTRADAS]
    /// No recibe entradas directas porque toma el comando ya guardado en el estado.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía texto al firmware y actualiza trazas.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SendManualLineAsync()` -> estado -> envio.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a tomar un comando ya cargado en buffer y disparar la transmision.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria enviar la linea manual sin rehacer el flujo por otro lado.
    /// </summary>
    Task SendManualLineAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para mandar un comando predefinido del catalogo.
    ///
    /// [QUIÉN LO USA]
    /// Lo llaman los botones de comandos fijos.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario pulsa una accion predefinida.
    ///
    /// [ENTRADAS]
    /// Recibe la cadena de comando a transmitir.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía la orden seleccionada al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `SendPresetAsync()` -> comando -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a seleccionar una rutina fija de prueba en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// Los botones predefinidos tendrian que replicar logica de envio.
    /// </summary>
    Task SendPresetAsync(string command);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para abrir el dialogo de login desde la UI principal.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama la pantalla principal cuando el usuario quiere autenticarse otra vez.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al pedir login manual o cambio de sesion.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mostrar el formulario de login.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> `OpenLoginAsync()` -> LoginForm.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a mostrar un menu de autenticacion antes de continuar.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendria un punto formal para pedir autenticacion.
    /// </summary>
    Task OpenLoginAsync();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para abrir la GUI principal del sistema elegido.
    ///
    /// [QUIÉN LO USA]
    /// Lo llama el selector de sistema.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta despues de elegir el sistema unificado o modular.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea la ventana elegida y cierra el selector.
    ///
    /// [FLUJO ACURATEX]
    /// Selector -> `LaunchGuiAsync()` -> ventana activa.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a salir del bootloader y entrar a la aplicacion activa.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una ruta encapsulada para lanzar la GUI principal.
    /// </summary>
    Task LaunchGuiAsync();
}
