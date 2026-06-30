// [ACURATEX] Modelos y protocolo del tablero de cabezal para sistema modular.
namespace AcuratexControlApp.Components;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para guardar la configuracion visual y de control de un motor del cabezal.
///
/// [QUIEN LA USA]
/// La usa la vista modular de cabezal.
///
/// [CUANDO SE USA]
/// Se usa al construir el tablero y cuando cambia una posicion.
///
/// [ENTRADAS]
/// Recibe indices, posiciones y datos de UI.
///
/// [SALIDAS]
/// Devuelve el estado de un motor.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene por si misma.
///
/// [FLUJO ACURATEX]
/// Protocolo -> modelo -> vista -> comandos.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una estructura de motor con una tabla de posiciones.
///
/// [SI NO EXISTIERA]
/// La UI tendria que guardar cada campo del motor por separado.
/// </summary>
public sealed class CabezalMotorTarjetas
{
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para dejar el objeto listo desde el inicio, sin obligar a la UI a rellenar cada campo uno por uno.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el protocolo de la vista cuando arma las listas de motores DEN o SIC.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante la creacion de los modelos, antes de que el usuario toque la pantalla.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// Devuelve una instancia con valores por defecto para listas, textos y estados.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No produce efectos fuera del objeto; solo deja propiedades preparadas.
    ///
    /// [FLUJO ACURATEX]
    /// Protocolo de cabecera -> new CabezalMotorTarjetas -> coleccion visible -> render.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inicializar una estructura en RAM antes de usarla.
    ///
    /// [SI NO EXISTIERA]
    /// La clase dependeria de inicializaciones dispersas o podria empezar incompleta.
    /// </summary>
    public CabezalMotorTarjetas()
    {
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para separar el indice visual del motor del indice real del protocolo.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la vista de cabezal y la construccion de listas de motores.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa al crear y renderizar cada tarjeta de motor.
    ///
    /// [ENTRADAS]
    /// Recibe un numero logico.
    ///
    /// [SALIDAS]
    /// Devuelve el indice visible en la UI.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Protocolo -> `LogicalIndex` -> presentacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al numero de canal que muestra una interfaz HMI.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que usar el indice fisico para todo.
    /// </summary>
    public int LogicalIndex { get; init; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para guardar el numero real de motor que entiende el protocolo.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa el formateo de comandos y la vista.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando se construye la orden enviada al firmware.
    ///
    /// [ENTRADAS]
    /// Recibe el numero fisico del motor.
    ///
    /// [SALIDAS]
    /// Devuelve el indice que se transmite en el comando.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `MotorIndex` -> texto de comando -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al numero real de periferico en un bus.
    ///
    /// [SI NO EXISTIERA]
    /// Habria que derivar el indice fisico en otro lado.
    /// </summary>
    public int MotorIndex { get; init; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para mostrar un nombre legible del motor en la tarjeta.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la vista para los titulos visibles.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta al renderizar la tarjeta del motor.
    ///
    /// [ENTRADAS]
    /// Recibe un texto ya preparado para usuario.
    ///
    /// [SALIDAS]
    /// Devuelve el titulo visual.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Datos -> `Title` -> encabezado de tarjeta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a la etiqueta que identifica una seccion del panel.
    ///
    /// [SI NO EXISTIERA]
    /// La tarjeta tendria que generar un nombre por su cuenta.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para guardar la lista de posiciones permitidas para ese motor.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la vista y las rutinas de seleccion de posicion.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa para dibujar las opciones disponibles.
    ///
    /// [ENTRADAS]
    /// Recibe una coleccion inmutable de posiciones.
    ///
    /// [SALIDAS]
    /// Devuelve las posiciones validas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Protocolo -> `Positions` -> botones de posicion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una tabla de setpoints permitidos.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que calcular o duplicar la tabla de posiciones.
    /// </summary>
    public IReadOnlyList<CabezalPositionTarjetas> Positions { get; init; } = Array.Empty<CabezalPositionTarjetas>();

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para guardar el limite maximo de recorrido permitido para el motor.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan validaciones y la propia vista.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa al limitar ediciones o al mostrar el rango del motor.
    ///
    /// [ENTRADAS]
    /// Recibe una posicion maxima.
    ///
    /// [SALIDAS]
    /// Devuelve el limite de recorrido.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Protocolo -> `MaxPosition` -> validacion de UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al tope de un registro de posicion.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabria hasta donde puede llegar el motor.
    /// </summary>
    public int MaxPosition { get; init; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para indicar si el motor admite un modo alternativo RUN1.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la vista para mostrar o esconder ese modo.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta al construir controles y botones.
    ///
    /// [ENTRADAS]
    /// Recibe un valor booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si el modo extra estÃ¡ disponible.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Configuracion -> `HasRun1` -> opciones visibles.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit que habilita una funcion extra del periferico.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que asumir que todos los motores tienen ese modo.
    /// </summary>
    public bool HasRun1 { get; init; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para guardar la posicion actual mostrada en la interfaz.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la vista y cualquier refresco visual.
    ///
    /// [CUÃNDO SE USA]
    /// Se actualiza cuando cambia la posicion real o simulada.
    ///
    /// [ENTRADAS]
    /// Recibe un entero de posicion.
    ///
    /// [SALIDAS]
    /// Devuelve la posicion que ve el usuario.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede cambiar el dibujo de la tarjeta.
    ///
    /// [FLUJO ACURATEX]
    /// Estado -> `Position` -> pantalla.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al valor de un registro de posicion actual.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendria donde reflejar la posicion activa.
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para guardar quÃ© opcion selecciono el operador.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la vista de cabezal al preparar el siguiente comando.
    ///
    /// [CUÃNDO SE USA]
    /// Se actualiza cuando el usuario toca una posicion concreta.
    ///
    /// [ENTRADAS]
    /// Recibe el numero de posicion elegido.
    ///
    /// [SALIDAS]
    /// Devuelve la seleccion actual.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia la opcion resaltada en la pantalla.
    ///
    /// [FLUJO ACURATEX]
    /// Click -> `SelectedPositionNumber` -> comando siguiente.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un registro de seleccion de setpoint.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabria que posicion editar o enviar.
    /// </summary>
    public int SelectedPositionNumber { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para mostrar el modo de ejecucion actual del motor.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la tarjeta visual del motor.
    ///
    /// [CUÃNDO SE USA]
    /// Se actualiza cuando cambian los modos de trabajo.
    ///
    /// [ENTRADAS]
    /// Recibe un texto corto.
    ///
    /// [SALIDAS]
    /// Devuelve el modo actual visible.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia la etiqueta mostrada en UI.
    ///
    /// [FLUJO ACURATEX]
    /// Estado -> `RunMode` -> etiqueta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un registro que indica el modo operativo.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no podria mostrar el modo activo.
    /// </summary>
    public string RunMode { get; set; } = string.Empty;

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta propiedad existe para mostrar el texto exacto del comando que representa el motor en la interfaz.
    ///
    /// [QUIEN LA USA]
    /// La usa la propia vista de cabezal al renderizar etiquetas, botones o ayudas visuales.
    ///
    /// [CUANDO SE USA]
    /// Se calcula cuando Blazor vuelve a dibujar el componente y necesita el texto del comando.
    ///
    /// [ENTRADAS]
    /// No recibe parametros; usa `MotorIndex` y el helper hexadecimal del protocolo.
    ///
    /// [SALIDAS]
    /// Devuelve una cadena con la forma `320 1C XX LSB MSB`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// Modelo de motor -> `CommandLabel` -> UI -> usuario.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una constante de depuracion que resume un acceso a registro.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendria que repetir este formateo en varios sitios.
    /// </summary>
    public string CommandLabel => $"320 1C {CabezalDashboardTarjetasProtocol.HexByte(MotorIndex)} LSB MSB";
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta estructura existe para transportar el numero de posicion y el valor real.
///
/// [QUIEN LA USA]
/// La usa el motor modular al mapear posiciones.
///
/// [CUANDO SE USA]
/// Se usa al construir listas de posiciones.
///
/// [ENTRADAS]
/// Recibe numero y valor.
///
/// [SALIDAS]
/// Devuelve un registro compacto.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Tabla de posiciones -> record struct -> UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un par indice/valor de una tabla de calibracion.
///
/// [SI NO EXISTIERA]
/// Habria que usar dos campos sueltos por cada posicion.
/// </summary>
public readonly record struct CabezalPositionTarjetas(int Number, int Value);

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para guardar un registro de J con su byte de bits.
///
/// [QUIEN LA USA]
/// La usa la pantalla de cabezal modular para los grupos J.
///
/// [CUANDO SE USA]
/// Se usa al pintar y modificar registros de bits.
///
/// [ENTRADAS]
/// Recibe el numero del grupo.
///
/// [SALIDAS]
/// Devuelve un contenedor con el registro y utilidades de bits.
///
/// [EFECTOS SECUNDARIOS]
/// Cambia el byte `Register` cuando se activa o desactiva un pin.
///
/// [FLUJO ACURATEX]
/// UI -> grupo J -> byte de registro -> comando al firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un registro de salida de 8 bits.
///
/// [SI NO EXISTIERA]
/// La UI tendria que manipular el byte manualmente en cada boton.
/// </summary>
public sealed class CabezalJGroupTarjetas
{
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para fijar el numero del grupo J al crear el objeto.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llaman las rutinas que construyen los grupos J visibles en pantalla.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta en la creacion del modelo, antes de cualquier click.
    ///
    /// [ENTRADAS]
    /// Recibe el numero logico del grupo.
    ///
    /// [SALIDAS]
    /// Devuelve una instancia con el registro inicial en `0xFF`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Asigna el valor a `Number`.
    ///
    /// [FLUJO ACURATEX]
    /// Construccion de UI -> grupo J -> registro de 8 bits.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a configurar la base de un puerto antes de manipular bits.
    ///
    /// [SI NO EXISTIERA]
    /// Habria que asignar el numero del grupo despues de crear el objeto.
    /// </summary>
    public CabezalJGroupTarjetas(int number)
    {
        Number = number;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para identificar el grupo J dentro del tablero.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la vista al pintar grupos y al calcular registro.
    ///
    /// [CUÃNDO SE USA]
    /// Se fija al crear el grupo y se lee durante toda la vida del objeto.
    ///
    /// [ENTRADAS]
    /// Recibe el numero logico del grupo.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador del grupo J.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No cambia luego de construirse.
    ///
    /// [FLUJO ACURATEX]
    /// Construccion -> `Number` -> mapeo de botones.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al numero de un puerto o grupo de salidas.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que pasar el numero del grupo por otro medio.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para almacenar el registro completo de 8 bits del grupo J.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la vista y los metodos que cambian pines.
    ///
    /// [CUÃNDO SE USA]
    /// Se lee al pintar y se modifica al tocar botones.
    ///
    /// [ENTRADAS]
    /// Recibe un byte con los bits del grupo.
    ///
    /// [SALIDAS]
    /// Devuelve el valor actual del registro.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Su modificacion cambia el estado visual y el comando enviado.
    ///
    /// [FLUJO ACURATEX]
    /// Click -> `Register` -> boton y protocolo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un puerto de salida de 8 bits.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pin tendria que guardarse por separado.
    /// </summary>
    public byte Register { get; set; } = 0xFF;

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para traducir el indice humano de un pin a su estado visual real.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la vista cuando necesita pintar cada boton del grupo J.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante el render o cuando la pantalla recalcula colores y textos.
    ///
    /// [ENTRADAS]
    /// Recibe el indice del pin, empezando normalmente en 1.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el pin esta encendido segun el registro y `false` si esta apagado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica nada; solo lee `Register`.
    ///
    /// [FLUJO ACURATEX]
    /// Registro J -> `IsPinOn()` -> estado visual del boton.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un bit concreto de un puerto de entrada/salida.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que repetir el desplazamiento de bits para cada boton.
    /// </summary>
    public bool IsPinOn(int pinIndex)
    {
        int bit = pinIndex - 1;
        return ((Register >> bit) & 0x01) == 0;
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para cambiar un solo bit del registro J sin que la UI tenga que construir la mascara a mano.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la pantalla de cabezal cuando el usuario activa o desactiva un pin.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta en respuesta a un click o a una accion de cambio masivo del grupo.
    ///
    /// [ENTRADAS]
    /// Recibe el indice del pin y el estado deseado.
    ///
    /// [SALIDAS]
    /// No devuelve valor; deja el nuevo estado guardado en `Register`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Modifica directamente el byte `Register`.
    ///
    /// [FLUJO ACURATEX]
    /// Click de UI -> `SetPin()` -> byte J actualizado -> comando al firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a poner o quitar un bit de un registro de salida.
    ///
    /// [SI NO EXISTIERA]
    /// Cada click tendria que rehacer la mascara de bits fuera del modelo.
    /// </summary>
    public void SetPin(int pinIndex, bool on)
    {
        int bit = pinIndex - 1;
        if (on) {
            Register = (byte)(Register & ~(1 << bit));
        } else {
            Register = (byte)(Register | (1 << bit));
        }
    }
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para representar un bloque de salidas con varios pines.
///
/// [QUIEN LA USA]
/// La usa la vista del cabezal modular.
///
/// [CUANDO SE USA]
/// Se usa al construir bloques de hilo o puntadas.
///
/// [ENTRADAS]
/// Recibe clave, titulo, subtitulo, direcciones y comandos.
///
/// [SALIDAS]
/// Devuelve un bloque con estados internos.
///
/// [EFECTOS SECUNDARIOS]
/// Inicializa el arreglo de estados.
///
/// [FLUJO ACURATEX]
/// Modelo -> bloque -> botones -> comando.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un grupo de salidas de un puerto con varios bits.
///
/// [SI NO EXISTIERA]
/// La UI tendria que administrar cada pin manualmente.
/// </summary>
public sealed class CabezalOutputBlockTarjetas
{
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para reunir en un solo objeto el bloque, sus direcciones y sus comandos de encendido o apagado.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama `CabezalDashboardTarjetasProtocol` al crear los bloques Yarn y Stitch.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al construir la lista de bloques visibles en la pantalla.
    ///
    /// [ENTRADAS]
    /// Recibe identificador, textos visibles, direcciones y comandos opcionales.
    ///
    /// [SALIDAS]
    /// Devuelve un bloque inicializado con un arreglo `States` del mismo tamano que `addresses`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Reserva el arreglo `States` para llevar el estado de cada pin.
    ///
    /// [FLUJO ACURATEX]
    /// Protocolo de UI -> bloque -> botones -> transmision.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a reservar una tabla de bits para un puerto de salida.
    ///
    /// [SI NO EXISTIERA]
    /// Habria que crear el arreglo de estados aparte y sincronizarlo manualmente.
    /// </summary>
    public CabezalOutputBlockTarjetas(
        string key,
        string title,
        string subtitle,
        IReadOnlyList<int> addresses,
        string? runCommand,
        string? stopCommand)
    {
        Key = key;
        Title = title;
        Subtitle = subtitle;
        Addresses = addresses;
        RunCommand = runCommand;
        StopCommand = stopCommand;
        States = new bool[addresses.Count];
    }

    public string Key { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public IReadOnlyList<int> Addresses { get; }

    public string? RunCommand { get; }

    public string? StopCommand { get; }

    public bool[] States { get; }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta propiedad existe para devolver rapidamente cuantas lineas de control tiene el bloque.
    ///
    /// [QUIEN LA USA]
    /// La usa la UI para recorrer los pines visibles y para mostrar resumentes.
    ///
    /// [CUANDO SE USA]
    /// Se consulta durante el render y al construir textos auxiliares.
    ///
    /// [ENTRADAS]
    /// No recibe datos; usa el arreglo interno `States`.
    ///
    /// [SALIDAS]
    /// Devuelve el numero de pines del bloque.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// Bloque -> `PinCount` -> bucles de interfaz.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al tamano de una mascara de bits fija.
    ///
    /// [SI NO EXISTIERA]
    /// Cada vista tendria que calcular la longitud del arreglo cada vez.
    /// </summary>
    public int PinCount => States.Length;
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta estructura existe para notificar un cambio de un pin concreto dentro de un bloque.
///
/// [QUIEN LA USA]
/// La usan handlers de click y servicios de la vista.
///
/// [CUANDO SE USA]
/// Se usa al activar o desactivar un pin.
///
/// [ENTRADAS]
/// Recibe bloque, indice y estado.
///
/// [SALIDAS]
/// Devuelve un registro compacto del cambio.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// UI -> record struct -> comando.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un cambio de bit en un registro de salida.
///
/// [SI NO EXISTIERA]
/// La UI tendria que pasar tres parametros sueltos por todas partes.
/// </summary>
public readonly record struct CabezalBlockPinChangeTarjetas(string BlockKey, int PinIndex, bool On);

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta estructura existe para notificar un cambio global de un bloque completo.
///
/// [QUIEN LA USA]
/// La usan rutinas de encendido/apagado total.
///
/// [CUANDO SE USA]
/// Se usa al activar o desactivar todos los pines del bloque.
///
/// [ENTRADAS]
/// Recibe bloque y estado global.
///
/// [SALIDAS]
/// Devuelve el cambio agrupado.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// UI -> cambio global -> comando de bloque.
///
/// [EQUIVALENCIA MCU]
/// Se parece a escribir todo un puerto de salida a la vez.
///
/// [SI NO EXISTIERA]
/// Habria que enviar cada pin por separado incluso en acciones globales.
/// </summary>
public readonly record struct CabezalBlockAllChangeTarjetas(string BlockKey, bool On);

/// <summary>
/// [POR QUE EXISTE]
/// Esta estructura existe para guardar una traza simple con hora, direccion y mensaje.
///
/// [QUIEN LA USA]
/// La usa la vista de cabezal y cualquier panel que muestre diagnostico de intercambio.
///
/// [CUANDO SE USA]
/// Se usa cuando la app quiere mostrar lo que salio o entro por el protocolo.
///
/// [ENTRADAS]
/// Recibe instante, direccion y texto.
///
/// [SALIDAS]
/// Devuelve un registro inmutable de traza.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene; solo agrupa datos.
///
/// [FLUJO ACURATEX]
/// Evento de protocolo -> `CabezalTraceEntryTarjetas` -> consola o historial visual.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un registro de log circular con timestamp.
///
/// [SI NO EXISTIERA]
/// La UI tendria que guardar la hora y el mensaje por separado.
/// </summary>
public sealed record CabezalTraceEntryTarjetas(DateTime Timestamp, string Direction, string Message);

/// <summary>
/// [POR QUÉ EXISTE]
/// Este helper existe para concentrar constantes, tablas y formateo del protocolo modular.
///
/// [QUIEN LA USA]
/// La usa la vista modular y sus servicios asociados.
///
/// [CUANDO SE USA]
/// Se usa al construir comandos y colecciones de motores.
///
/// [ENTRADAS]
/// Recibe numeros de posicion, bloque o motor.
///
/// [SALIDAS]
/// Devuelve listas, bytes hexadecimales y comandos de texto.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// UI -> protocolo -> texto -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla de constantes y helpers de formato en firmware.
///
/// [SI NO EXISTIERA]
/// Cada vista tendria que duplicar los mismos codigos hexadecimales.
/// </summary>
public static class CabezalDashboardTarjetasProtocol
{
    public const int DenMaxPosition = 0x028A;
    public const int SicMaxPosition = 0x02EE;
    public const int DenRunPeriodMs = 80;
    public const int DenRun1PeriodMs = 300;
    public const int SicRunPeriodMs = 300;

    public static readonly int[] SicMotorIndexes = { 0x08, 0x09 };
    public static readonly int[] DenRunSequence = { 1, 3, 5, 2, 4 };
    public static readonly int[] DenRun1Sequence = { 1, 3, 5 };
    public static readonly int[] SicRunSequence = { 1, 2, 3 };

    private static readonly CabezalPositionTarjetas[] DenPositions =
    {
        new(1, 0x0000),
        new(2, 0x00A2),
        new(3, 0x0145),
        new(4, 0x01E7),
        new(5, 0x028A),
    };

    private static readonly CabezalPositionTarjetas[] SicPositions =
    {
        new(1, 0x0000),
        new(2, 0x0176),
        new(3, 0x02EE),
    };

    private static readonly CabezalPositionTarjetas[] FeetPositions =
    {
        new(1, 0x0000),
        new(2, 0x0176),
    };

    public static IReadOnlyList<CabezalMotorTarjetas> CreateDenMotors()
    {
        return Enumerable.Range(0, 8)
            .Select(index => new CabezalMotorTarjetas
            {
                LogicalIndex = index,
                MotorIndex = index,
                Title = $"DEN {index + 1}",
                Positions = DenPositions,
                MaxPosition = DenMaxPosition,
                HasRun1 = true,
            })
            .ToArray();
    }

    public static IReadOnlyList<CabezalMotorTarjetas> CreateSicMotors()
    {
        return Enumerable.Range(0, SicMotorIndexes.Length)
            .Select(index => new CabezalMotorTarjetas
            {
                LogicalIndex = index,
                MotorIndex = SicMotorIndexes[index],
                Title = $"SIC {index + 1}",
                Positions = SicPositions,
                MaxPosition = SicMaxPosition,
            })
            .ToArray();
    }

    public static IReadOnlyList<CabezalMotorTarjetas> CreateFeetMotors()
    {
        return Enumerable.Range(0, SicMotorIndexes.Length)
            .Select(index => new CabezalMotorTarjetas
            {
                LogicalIndex = index,
                MotorIndex = SicMotorIndexes[index],
                Title = $"Feet{index + 1}",
                Positions = FeetPositions,
                MaxPosition = SicMaxPosition,
            })
            .ToArray();
    }

    public static IReadOnlyList<CabezalOutputBlockTarjetas> CreateYarnBlocks()
    {
        return new[]
        {
            new CabezalOutputBlockTarjetas("yarn1", "Yarn 1", "320 1E 18..1F", new[] { 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F }, "y1_run", "y1_stop"),
            new CabezalOutputBlockTarjetas("yarn2", "Yarn 2", "320 1E 24..27 / 20..23", new[] { 0x24, 0x25, 0x26, 0x27, 0x20, 0x21, 0x22, 0x23 }, "y2_run", "y2_stop"),
        };
    }

    public static IReadOnlyList<CabezalOutputBlockTarjetas> CreateStitchBlocks()
    {
        return new[]
        {
            new CabezalOutputBlockTarjetas("stitch1", "Stitch 1", "320 1E 00,01,02,05", new[] { 0x00, 0x01, 0x02, 0x05 }, "s_run_1", "s_stop_1"),
            new CabezalOutputBlockTarjetas("stitch2", "Stitch 2", "320 1E 06,07,08,0B", new[] { 0x06, 0x07, 0x08, 0x0B }, "s_run_2", "s_stop_2"),
            new CabezalOutputBlockTarjetas("stitch3", "Stitch 3", "320 1E 0C,0D,0E,11", new[] { 0x0C, 0x0D, 0x0E, 0x11 }, "s_run_3", "s_stop_3"),
            new CabezalOutputBlockTarjetas("stitch4", "Stitch 4", "320 1E 12,13,14,17", new[] { 0x12, 0x13, 0x14, 0x17 }, "s_run_4", "s_stop_4"),
        };
    }

    public static string FormatPositionLine(int motorIndex, int position)
    {
        int value = Math.Clamp(position, 0, 0xFFFF);
        return $"320 1C {HexByte(motorIndex)} {HexByte(value & 0xFF)} {HexByte((value >> 8) & 0xFF)}";
    }

    public static string FormatJRegisterLine(int jIndex, byte value)
    {
        return $"320 1D {HexByte(jIndex - 1)} {HexByte(value)}";
    }

    public static string FormatBlockPinLine(string blockKey, int pinIndex, bool on)
    {
        IReadOnlyList<int> addresses = GetBlockAddresses(blockKey);
        if (pinIndex < 1 || pinIndex > addresses.Count) {
            throw new ArgumentOutOfRangeException(nameof(pinIndex));
        }

        return $"320 1E {HexByte(addresses[pinIndex - 1])} {(on ? "01" : "00")}";
    }

    public static string HexByte(int value)
    {
        return (value & 0xFF).ToString("X2");
    }

    public static string HexWord(int value)
    {
        return (value & 0xFFFF).ToString("X4");
    }

    private static IReadOnlyList<int> GetBlockAddresses(string blockKey)
    {
        return blockKey switch
        {
            "yarn1" => new[] { 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F },
            "yarn2" => new[] { 0x24, 0x25, 0x26, 0x27, 0x20, 0x21, 0x22, 0x23 },
            "stitch1" => new[] { 0x00, 0x01, 0x02, 0x05 },
            "stitch2" => new[] { 0x06, 0x07, 0x08, 0x0B },
            "stitch3" => new[] { 0x0C, 0x0D, 0x0E, 0x11 },
            "stitch4" => new[] { 0x12, 0x13, 0x14, 0x17 },
            _ => Array.Empty<int>(),
        };
    }
}
