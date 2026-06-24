// [ACURATEX] Estado y opciones del panel principal de conexion y consola.
namespace AcuratexControlApp.Components;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para reunir el estado visual y operativo del panel principal.
///
/// [QUIEN LA USA]
/// La usa `MainControlPanel.razor`.
///
/// [CUANDO SE USA]
/// Se usa durante el arranque, la conexion y el envio de comandos.
///
/// [ENTRADAS]
/// Recibe valores de la logica de conexion y seleccion.
///
/// [SALIDAS]
/// Devuelve el estado necesario para pintar la UI.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene; es un contenedor de estado.
///
/// [FLUJO ACURATEX]
/// Servicios de conexion -> `MainControlPanelState` -> UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un bloque de variables globales que el firmware va actualizando para la pantalla.
///
/// [SI NO EXISTIERA]
/// El panel tendria campos sueltos y la vista quedaria mas dificil de seguir.
/// </summary>
public sealed class MainControlPanelState
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar el modo de conexión que la UI está mostrando.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `MainControlPanel.razor` y la ventana WinForms anfitriona.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al cambiar USB, WiFi o Serial.
    ///
    /// [ENTRADAS]
    /// Recibe un valor de `ConnectionMode`.
    ///
    /// [SALIDAS]
    /// Devuelve el modo activo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene por sí mismo.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Mode` -> panel de conexión.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit o registro que selecciona el bus de comunicación.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla no sabría qué transporte está en uso.
    /// </summary>
    public ConnectionMode Mode { get; set; } = ConnectionMode.Usb;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar la lista de endpoints visibles para la UI.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa el selector de conexión.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al refrescar dispositivos o puertos.
    ///
    /// [ENTRADAS]
    /// Recibe una colección de `MainEndpointOption`.
    ///
    /// [SALIDAS]
    /// Devuelve la lista mostrada en pantalla.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Descubrimiento -> `Endpoints` -> selector.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a la lista de periféricos detectados en un bus.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podría mostrar destinos disponibles.
    /// </summary>
    public IReadOnlyList<MainEndpointOption> Endpoints { get; set; } = Array.Empty<MainEndpointOption>();

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar qué endpoint escogió el operador.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa la lógica de conexión.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al pulsar Conectar.
    ///
    /// [ENTRADAS]
    /// Recibe el identificador del endpoint.
    ///
    /// [SALIDAS]
    /// Devuelve el endpoint seleccionado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `SelectedEndpointId` -> transporte.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una dirección de periférico elegida por el usuario.
    ///
    /// [SI NO EXISTIERA]
    /// La app no sabría a qué dispositivo apuntar.
    /// </summary>
    public string SelectedEndpointId { get; set; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para mostrar el titulo visual del bloque USB en la interfaz.
    ///
    /// [QUIÉN LA USA]
    /// La usa `MainControlPanel.razor`.
    ///
    /// [CUÁNDO SE USA]
    /// Se evalúa cuando la vista dibuja la tarjeta USB.
    ///
    /// [ENTRADAS]
    /// Recibe un texto ya preparado por la lógica de conexión.
    ///
    /// [SALIDAS]
    /// Devuelve el titulo visible del panel.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado -> `UsbPanelTitle` -> cabecera USB.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una etiqueta de modulo en una pantalla HMI.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que hardcodear el titulo en el markup.
    /// </summary>
    public string UsbPanelTitle { get; set; } = "USB nativo (WinUSB)";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para etiquetar el selector del dispositivo.
    ///
    /// [QUIÉN LA USA]
    /// La usa la UI del panel principal.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al renderizar el campo de endpoint.
    ///
    /// [ENTRADAS]
    /// Recibe un texto corto.
    ///
    /// [SALIDAS]
    /// Devuelve la etiqueta visual del selector.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado -> `EndpointLabel` -> etiqueta del combo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nombre de una entrada en un menú de periferico.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz tendría que repetir el label en el markup.
    /// </summary>
    public string EndpointLabel { get; set; } = "Dispositivo";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para mostrar si el valor que se edita es un baud o un GUID.
    ///
    /// [QUIÉN LA USA]
    /// La usa la UI principal según el modo de conexión.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al pintar el campo de valor técnico.
    ///
    /// [ENTRADAS]
    /// Recibe un texto de etiqueta.
    ///
    /// [SALIDAS]
    /// Devuelve la descripción visible del valor técnico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Modo -> `BaudLabel` -> etiqueta del campo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cambiar entre la descripción de un registro y otro.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no sabría cómo nombrar el valor técnico.
    /// </summary>
    public string BaudLabel { get; set; } = "GUID";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para guardar el valor textual del baud o del GUID.
    ///
    /// [QUIÉN LA USA]
    /// La usa el campo de texto del panel principal.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al mostrar o editar la configuración técnica.
    ///
    /// [ENTRADAS]
    /// Recibe una cadena ya preparada.
    ///
    /// [SALIDAS]
    /// Devuelve el valor visible del transporte.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado -> `BaudValue` -> textbox técnico.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un registro que muestra un parámetro de bus.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendría dónde reflejar el valor configurado.
    /// </summary>
    public string BaudValue { get; set; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para bloquear o permitir la edición del valor técnico.
    ///
    /// [QUIÉN LA USA]
    /// La usa la UI del panel principal.
    ///
    /// [CUÁNDO SE USA]
    /// Se evalúa al renderizar el input técnico.
    ///
    /// [ENTRADAS]
    /// Recibe un booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si el campo es de solo lectura.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado -> `BaudReadOnly` -> input bloqueado o editable.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de protección contra escritura.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que repetir la condición de edición en cada render.
    /// </summary>
    public bool BaudReadOnly { get; set; } = true;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para guardar el host textual del destino TCP o WiFi.
    ///
    /// [QUIÉN LA USA]
    /// La usan la UI y el host WinForms.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al configurar el enlace de red.
    ///
    /// [ENTRADAS]
    /// Recibe una dirección o nombre de host.
    ///
    /// [SALIDAS]
    /// Devuelve el host configurado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Host` -> conexión de red.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a fijar la direccion de un nodo en una red embebida.
    ///
    /// [SI NO EXISTIERA]
    /// La conexión TCP no sabría a qué destino apuntar.
    /// </summary>
    public string Host { get; set; } = "192.168.137.2";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para guardar el puerto TCP del destino.
    ///
    /// [QUIÉN LA USA]
    /// La usan la UI y la lógica de conexión.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al configurar la comunicación por red.
    ///
    /// [ENTRADAS]
    /// Recibe un puerto textual.
    ///
    /// [SALIDAS]
    /// Devuelve el puerto visible.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Port` -> socket remoto.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al canal numerado de un bus de comunicación.
    ///
    /// [SI NO EXISTIERA]
    /// La red no sabría qué puerto abrir.
    /// </summary>
    public string Port { get; set; } = "3333";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para guardar el comando visible en la consola manual.
    ///
    /// [QUIÉN LA USA]
    /// La usa la parte de consola del panel principal.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta antes de mandar una línea manual.
    ///
    /// [ENTRADAS]
    /// Recibe un comando de texto.
    ///
    /// [SALIDAS]
    /// Devuelve la línea a enviar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Command` -> consola manual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al contenido de un buffer de transmisión.
    ///
    /// [SI NO EXISTIERA]
    /// La consola no tendría un comando editable único.
    /// </summary>
    public string Command { get; set; } = "320 07";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para indicar si la conexión está activa.
    ///
    /// [QUIÉN LA USA]
    /// La usa la UI para mostrar el estado general.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al pintar la cabecera y los botones de conexión.
    ///
    /// [ENTRADAS]
    /// Recibe un valor lógico.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Conexión -> `IsConnected` -> interfaz.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de link up/down.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no sabría si el tester está conectado.
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para bloquear la UI mientras una operación está en curso.
    ///
    /// [QUIÉN LA USA]
    /// La usan botones y campos que deben quedar deshabilitados durante una acción.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cuando hay envíos, descubrimientos o guardados activos.
    ///
    /// [ENTRADAS]
    /// Recibe un valor lógico.
    ///
    /// [SALIDAS]
    /// Devuelve si la interfaz debe considerarse ocupada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Operación en curso -> `IsBusy` -> controles bloqueados.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un semáforo de "no aceptar otra orden" mientras corre una rutina.
    ///
    /// [SI NO EXISTIERA]
    /// La UI podría disparar dos acciones al mismo tiempo.
    /// </summary>
    public bool IsBusy { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para indicar que el panel estÃ¡ buscando dispositivos o puertos.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la UI principal y el servicio de descubrimiento.
    ///
    /// [CUÃNDO SE USA]
    /// Se activa mientras corre la busqueda de endpoints.
    ///
    /// [ENTRADAS]
    /// Recibe un valor booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si el panel estÃ¡ en modo de descubrimiento.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia el estado visual de botones y mensajes.
    ///
    /// [FLUJO ACURATEX]
    /// Descubrimiento -> `IsDiscovering` -> texto y botones.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de "escaneo activo".
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podrÃ­a saber cuÃ¡ndo mostrar que estÃ¡ buscando.
    /// </summary>
    public bool IsDiscovering { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para permitir o bloquear la seccion USB del panel.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la vista principal para decidir si pinta el bloque USB.
    ///
    /// [CUÃNDO SE USA]
    /// Se evalua al construir la pantalla y al cambiar de modo de conexion.
    ///
    /// [ENTRADAS]
    /// Recibe un valor booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si la seccion USB debe verse activa.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia la habilitacion visual del bloque USB.
    ///
    /// [FLUJO ACURATEX]
    /// Modo -> `IsUsbPanelEnabled` -> render del bloque USB.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a habilitar una interfaz fisica del sistema.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que decidir por otro lado si muestra USB.
    /// </summary>
    public bool IsUsbPanelEnabled { get; set; } = true;

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para permitir o bloquear la seccion WiFi o red del panel.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la vista principal para dibujar el bloque de red.
    ///
    /// [CUÃNDO SE USA]
    /// Se evalua cuando el modo de conexion exige o no interfaz de red.
    ///
    /// [ENTRADAS]
    /// Recibe un valor booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si la seccion WiFi debe verse activa.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia la habilitacion visual del bloque de red.
    ///
    /// [FLUJO ACURATEX]
    /// Modo -> `IsWifiPanelEnabled` -> render del bloque WiFi.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a habilitar una interfaz secundaria del sistema.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que inferir por su cuenta si mostrar WiFi.
    /// </summary>
    public bool IsWifiPanelEnabled { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para habilitar o bloquear el boton de conectar.
    ///
    /// [QUIÉN LA USA]
    /// La usa la UI del panel principal.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al renderizar la zona de conexion.
    ///
    /// [ENTRADAS]
    /// Recibe un valor booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si el boton puede usarse.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Permiso -> `CanConnect` -> boton conectar.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de habilitacion para arrancar una rutina.
    ///
    /// [SI NO EXISTIERA]
    /// El boton tendria que decidir su habilitacion con otra bandera.
    /// </summary>
    public bool CanConnect { get; set; } = true;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para habilitar o bloquear el boton de desconectar.
    ///
    /// [QUIÉN LA USA]
    /// La usa la pantalla principal.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cuando la conexion ya esta activa.
    ///
    /// [ENTRADAS]
    /// Recibe un booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si se puede cortar el enlace.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado -> `CanDisconnect` -> boton desconectar.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit que permite deshabilitar el bus.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que asumir la lógica de desenganche.
    /// </summary>
    public bool CanDisconnect { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para habilitar el envio manual de comandos.
    ///
    /// [QUIÉN LA USA]
    /// La usa la consola del panel principal.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cuando el operador escribe una linea.
    ///
    /// [ENTRADAS]
    /// Recibe un valor booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si la consola manual puede enviar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Permiso -> `CanSend` -> boton enviar.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de permiso para transmitir.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabría si el envio manual esta autorizado.
    /// </summary>
    public bool CanSend { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para habilitar el refresco de endpoints detectados.
    ///
    /// [QUIÉN LA USA]
    /// La usa el boton Buscar.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cuando el usuario quiere releer dispositivos.
    ///
    /// [ENTRADAS]
    /// Recibe un booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si el refresco esta disponible.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado -> `CanRefreshEndpoint` -> boton Buscar.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a permitir una nueva consulta de bus.
    ///
    /// [SI NO EXISTIERA]
    /// El botón de refresco tendría que calcular su propia habilitación.
    /// </summary>
    public bool CanRefreshEndpoint { get; set; } = true;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para habilitar la busqueda WiFi o de red.
    ///
    /// [QUIÉN LA USA]
    /// La usa el bloque WiFi del panel.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al pintar la accion de descubrimiento.
    ///
    /// [ENTRADAS]
    /// Recibe un valor booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si la busqueda esta permitida.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Permiso -> `CanDiscoverWifi` -> descubrimiento de red.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a permitir una exploracion de red local.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabría si debe mostrar la búsqueda de WiFi.
    /// </summary>
    public bool CanDiscoverWifi { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para habilitar los botones de presets.
    ///
    /// [QUIÉN LA USA]
    /// La usa la consola principal.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cuando la interfaz decide mostrar start/stop/test.
    ///
    /// [ENTRADAS]
    /// Recibe un valor booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si los presets pueden enviarse.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Permiso -> `CanSendPreset` -> botones de rutina.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a habilitar comandos pregrabados en un panel.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que deducir por otro lado si muestra presets.
    /// </summary>
    public bool CanSendPreset { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para mostrar un texto corto de estado en la cabecera.
    ///
    /// [QUIÉN LA USA]
    /// La usa el panel principal para la barra visual superior.
    ///
    /// [CUÁNDO SE USA]
    /// Se evalúa en cada render.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve "Conectado" o "Desconectado".
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado de conexión -> `StatusText` -> cabecera.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un LED de estado en un panel.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que repetir la cadena en varios sitios.
    /// </summary>
    public string StatusText => IsConnected ? "Conectado" : "Desconectado";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para cambiar el texto del botón de descubrimiento según el
    /// estado de búsqueda.
    ///
    /// [QUIÉN LA USA]
    /// La usa la UI del panel principal.
    ///
    /// [CUÁNDO SE USA]
    /// Se evalúa al pintar el botón.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el texto del botón.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado de búsqueda -> `DiscoverButtonText` -> texto visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un botón que cambia de etiqueta según el modo del sistema.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que usar condicionales repetidos para el texto.
    /// </summary>
    public string DiscoverButtonText => IsDiscovering ? "Buscando..." : "Detectar";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta lista existe para guardar los mensajes que la consola visual debe mostrar.
    ///
    /// [QUIÉN LA USA]
    /// La usa `MainControlPanel.razor`.
    ///
    /// [CUÁNDO SE USA]
    /// Se agrega texto cuando ocurren eventos o comandos.
    ///
    /// [ENTRADAS]
    /// Recibe cadenas de log.
    ///
    /// [SALIDAS]
    /// Devuelve la lista mutable de logs.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Permite agregar mensajes desde el host.
    ///
    /// [FLUJO ACURATEX]
    /// Eventos -> `Logs` -> consola.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una cola circular de trazas en RAM.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendría historial de mensajes.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta lista existe para guardar los mensajes visibles de la consola del panel.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usa `MainControlPanel.razor` y el host de ventana.
    ///
    /// [CUÃNDO SE USA]
    /// Se agrega texto cuando llegan eventos, errores o respuestas del firmware.
    ///
    /// [ENTRADAS]
    /// Recibe cadenas de log.
    ///
    /// [SALIDAS]
    /// Devuelve la lista mutable de mensajes.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Permite agregar mensajes nuevos en tiempo de ejecucion.
    ///
    /// [FLUJO ACURATEX]
    /// Eventos -> `Logs` -> consola.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una cola de trazas en RAM.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendria historial de mensajes.
    /// </summary>
    public List<string> Logs { get; } = new();
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta fila existe para representar un endpoint de conexion disponible.
///
/// [QUIEN LA USA]
/// La usa `MainControlPanelState` para mostrar opciones.
///
/// [CUANDO SE USA]
/// Se usa cuando la UI lista endpoints de USB o red.
///
/// [ENTRADAS]
/// Recibe identificador y texto visible.
///
/// [SALIDAS]
/// Devuelve un objeto de solo datos.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Descubrimiento -> endpoint -> selector visual.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un descriptor simple de periférico encontrado en bus.
///
/// [SI NO EXISTIERA]
/// La lista de endpoints tendria que manejar dos strings sueltos.
/// </summary>
/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para representar un endpoint disponible con su identificador y su etiqueta visible.
///
/// [QUIÉN LO USA]
/// Lo usa el selector de endpoints del panel principal.
///
/// [CUÁNDO SE USA]
/// Se usa al listar dispositivos o puertos detectados.
///
/// [ENTRADAS]
/// Recibe un ID técnico y un texto legible.
///
/// [SALIDAS]
/// Devuelve una ficha de solo datos.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Descubrimiento -> `MainEndpointOption` -> selector visual.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un descriptor simple de periférico encontrado en un bus.
///
/// [SI NO EXISTIERA]
/// La lista de endpoints tendría que manejar dos strings sueltos.
/// </summary>
public sealed record MainEndpointOption(string Id, string Label);
