// [ACURATEX] Modelos de apoyo del tablero servo modular.
namespace AcuratexControlApp.Components;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para representar un interruptor logico del tablero servo.
///
/// [QUIEN LA USA]
/// La usa la vista modular de servo.
///
/// [CUANDO SE USA]
/// Se usa al mostrar o alternar una opcion de control.
///
/// [ENTRADAS]
/// Recibe una clave y textos de encendido/apagado.
///
/// [SALIDAS]
/// Devuelve un modelo simple de UI.
///
/// [EFECTOS SECUNDARIOS]
/// Tiene solo sobre la propiedad `Enabled`.
///
/// [FLUJO ACURATEX]
/// UI -> toggle -> comando de servo.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una bandera de habilitacion para un canal de salida.
///
/// [SI NO EXISTIERA]
/// La vista no tendria un estado simple para cada interruptor.
/// </summary>
public sealed class ServoDashboardTarjetasToggle
{
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para dejar definido desde el inicio el texto y la clave de la opcion que la UI va a mostrar.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el componente de servo modular cuando arma sus listas de controles.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al crear el modelo de la pantalla, no cuando el usuario pulsa un boton.
    ///
    /// [ENTRADAS]
    /// Recibe una clave interna y dos textos visibles.
    ///
    /// [SALIDAS]
    /// Devuelve un objeto preparado para representar un interruptor logico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene efectos fuera del objeto.
    ///
    /// [FLUJO ACURATEX]
    /// Protocolo de UI -> toggle -> render.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de habilitacion para una salida.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendria que construir el texto de cada interruptor por separado.
    /// </summary>
    public ServoDashboardTarjetasToggle(string key, string onText, string offText)
    {
        Key = key;
        OnText = onText;
        OffText = offText;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para identificar sin ambiguedad cada interruptor del tablero servo.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la vista y la logica de comandos.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta al renderizar o activar una opcion.
    ///
    /// [ENTRADAS]
    /// Recibe una clave textual.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador interno del toggle.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Key` -> comando o etiqueta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una direccion simbolica de canal.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no sabria que toggle esta cambiando.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para guardar el texto que se muestra cuando la opcion esta activa.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa el render de la tarjeta de servo.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta al pintar el estado encendido.
    ///
    /// [ENTRADAS]
    /// Recibe un texto visible.
    ///
    /// [SALIDAS]
    /// Devuelve la etiqueta cuando `Enabled` es true.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado activo -> `OnText` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una etiqueta de estado de salida activa.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que hardcodear el texto de encendido.
    /// </summary>
    public string OnText { get; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para guardar el texto que se muestra cuando la opcion esta inactiva.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la tarjeta de servo y el texto alternativo del boton.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta al pintar el estado apagado.
    ///
    /// [ENTRADAS]
    /// Recibe un texto visible.
    ///
    /// [SALIDAS]
    /// Devuelve la etiqueta cuando `Enabled` es false.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Estado inactivo -> `OffText` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una etiqueta de salida deshabilitada.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que deducir el texto de apagado por su cuenta.
    /// </summary>
    public string OffText { get; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para decir si el toggle esta activo en pantalla.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la UI y la logica de envio.
    ///
    /// [CUÃNDO SE USA]
    /// Se cambia cuando el operador pulsa el interruptor.
    ///
    /// [ENTRADAS]
    /// Recibe un booleano.
    ///
    /// [SALIDAS]
    /// Devuelve el estado actual del toggle.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia el texto y el comando que la vista mostrara.
    ///
    /// [FLUJO ACURATEX]
    /// Click -> `Enabled` -> texto y comando.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de habilitacion de canal.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no sabria si el interruptor esta encendido o apagado.
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para guardar una posicion editable del servo.
///
/// [QUIEN LA USA]
/// La usa la vista de servo modular.
///
/// [CUANDO SE USA]
/// Se usa cuando el usuario cambia una posicion o sus vueltas.
///
/// [ENTRADAS]
/// Recibe el numero de posicion.
///
/// [SALIDAS]
/// Devuelve una ficha de posicion.
///
/// [EFECTOS SECUNDARIOS]
/// Solo mantiene estado de UI.
///
/// [FLUJO ACURATEX]
/// UI -> posicion -> comando.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una entrada de tabla de setpoint.
///
/// [SI NO EXISTIERA]
/// Cada posicion tendria que manejar dos valores sueltos.
/// </summary>
public sealed class ServoDashboardTarjetasPosition
{
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para fijar el numero de posicion al crear la ficha editable.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama la vista de servo al construir la tabla de posiciones.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al preparar el modelo, antes de que el usuario cambie valores.
    ///
    /// [ENTRADAS]
    /// Recibe el numero visible de la posicion.
    ///
    /// [SALIDAS]
    /// Devuelve un objeto con `Target` y `Turns` listos para editarse.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Solo fija `Number`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> posicion editable -> comando de servo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una entrada de tabla de setpoint en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla tendria que pasar el numero de posicion por separado a cada campo.
    /// </summary>
    public ServoDashboardTarjetasPosition(int number)
    {
        Number = number;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para identificar la posicion logica dentro de la tarjeta servo.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la UI y el servicio que manda cambios de posicion.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta al editar una posicion concreta.
    ///
    /// [ENTRADAS]
    /// Recibe un entero de posicion.
    ///
    /// [SALIDAS]
    /// Devuelve el numero de posicion visible.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Number` -> posicion y comando.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al indice de una tabla de setpoints.
    ///
    /// [SI NO EXISTIERA]
    /// No se sabria a que posicion apunta cada cambio.
    /// </summary>
    public int Number { get; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para guardar el objetivo decimal de la posicion.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la UI y el servicio de ejecucion de comandos.
    ///
    /// [CUÃNDO SE USA]
    /// Se actualiza cuando el operador cambia el valor deseado.
    ///
    /// [ENTRADAS]
    /// Recibe un numero decimal.
    ///
    /// [SALIDAS]
    /// Devuelve el objetivo configurado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia lo que se enviara al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Editor -> `Target` -> comando de posicion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un setpoint de control.
    ///
    /// [SI NO EXISTIERA]
    /// El operador no tendria donde editar el valor objetivo.
    /// </summary>
    public decimal Target { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para contar cuantas vueltas usa la posicion.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la vista, el protocolo y el calculo de comando.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta cuando el operador ajusta las vueltas.
    ///
    /// [ENTRADAS]
    /// Recibe un entero.
    ///
    /// [SALIDAS]
    /// Devuelve el numero de vueltas configurado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia el comando que saldra al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Ajuste -> `Turns` -> comando de servo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un contador de vueltas o pasos de control.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria representar el ajuste de vueltas.
    /// </summary>
    public int Turns { get; set; }
}

/// <summary>
/// [POR QUÃ‰ EXISTE]
/// Esta estructura existe para transportar el cambio de vueltas de una posicion concreta sin repartir dos enteros sueltos por la UI.
///
/// [QUIÃ‰N LA USA]
/// La usan handlers de click, servicios de servo y la logica de comandos.
///
/// [CUÃNDO SE USA]
/// Se usa cuando el operador incrementa o decrementa vueltas en una posicion.
///
/// [ENTRADAS]
/// Recibe el numero de posicion y el delta de vueltas.
///
/// [SALIDAS]
/// Devuelve un paquete compacto con el cambio solicitado.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene por si misma; solo agrupa datos.
///
/// [FLUJO ACURATEX]
/// Click de UI -> `ServoDashboardTarjetasTurnsChange` -> servicio -> comando.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un pequeño mensaje de ajuste con indice y delta.
///
/// [SI NO EXISTIERA]
/// Cada evento tendria que pasar dos enteros sueltos y seria mas facil mezclar campos.
/// </summary>
public readonly record struct ServoDashboardTarjetasTurnsChange(int PositionNumber, int Delta);

/// <summary>
/// [POR QUÃ‰ EXISTE]
/// Esta estructura existe para transportar el nuevo objetivo decimal de una posicion concreta.
///
/// [QUIÃ‰N LA USA]
/// La usan la UI de servo y el servicio que convierte cambios en comandos.
///
/// [CUÃNDO SE USA]
/// Se usa cuando el operador edita el valor objetivo de una posicion.
///
/// [ENTRADAS]
/// Recibe el numero de posicion y el nuevo objetivo decimal.
///
/// [SALIDAS]
/// Devuelve un paquete de datos con el cambio pedido.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene por si misma.
///
/// [FLUJO ACURATEX]
/// Editor de posicion -> `ServoDashboardTarjetasTargetChange` -> servicio -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a actualizar un setpoint en una tabla de control.
///
/// [SI NO EXISTIERA]
/// La UI tendria que enviar dos valores separados y mantenerlos sincronizados.
/// </summary>
public readonly record struct ServoDashboardTarjetasTargetChange(int PositionNumber, decimal Target);
