using System.Text.Json.Serialization;

namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para describir un comando que la UI puede mostrar y enviar.
///
/// [QUIÉN LA USA]
/// La usan los paneles, los catálogos de comandos y la capa de envío al firmware.
///
/// [CUÁNDO SE USA]
/// Se usa al construir botones, listas o envíos de prueba.
///
/// [ENTRADAS]
/// Recibe datos de catálogo o de overrides locales.
///
/// [SALIDAS]
/// Devuelve una estructura de datos lista para la interfaz.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene, porque solo transporta datos.
///
/// [FLUJO ACURATEX]
/// Catálogo -> `CommandDefinition` -> vista -> envío al tester.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una entrada de tabla que guarda etiqueta, valor y categoría de un comando.
///
/// [SI NO EXISTIERA]
/// Cada comando tendría que manejarse con texto suelto y sin metadatos.
/// </summary>
public sealed class CommandDefinition
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para guardar una clave estable que identifica al comando en todo el catalogo.
    ///
    /// [QUIÉN LA USA]
    /// La usan el catalogo, los filtros de UI y las sobrescrituras locales.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al cargar, buscar o reemplazar un comando.
    ///
    /// [ENTRADAS]
    /// Recibe una cadena al construir el objeto.
    ///
    /// [SALIDAS]
    /// Devuelve la clave del comando.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Catálogo -> `Key` -> busqueda o reemplazo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un identificador fijo de tabla de comandos en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria reemplazar ni buscar un comando especifico con seguridad.
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para mostrar el nombre legible del comando en la interfaz.
    ///
    /// [QUIÉN LA USA]
    /// La usa la UI cuando arma botones, listas y tarjetas.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al pintar el catalogo en pantalla.
    ///
    /// [ENTRADAS]
    /// Recibe una cadena visible al construir el objeto.
    ///
    /// [SALIDAS]
    /// Devuelve el texto corto para el operador.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Catálogo -> `Title` -> texto en pantalla.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una etiqueta humana para una funcion tecnica.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que inventar el nombre visible a partir de la clave.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para guardar la cadena exacta que se manda al firmware.
    ///
    /// [QUIÉN LA USA]
    /// La usan los servicios de envio y los paneles de comandos.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa justo antes de transmitir el comando.
    ///
    /// [ENTRADAS]
    /// Recibe el texto que el firmware espera.
    ///
    /// [SALIDAS]
    /// Devuelve la orden de texto completa.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Command` -> transporte -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a la palabra exacta que se coloca en una trama serial.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una orden concreta para enviar.
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para agrupar el comando con otros que pertenecen a la misma familia.
    ///
    /// [QUIÉN LA USA]
    /// La usan los filtros y paneles de la UI.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al ordenar el catalogo por bloques funcionales.
    ///
    /// [ENTRADAS]
    /// Recibe una categoria al construir el objeto.
    ///
    /// [SALIDAS]
    /// Devuelve la categoria del comando.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Catálogo -> `Category` -> agrupacion visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un grupo de pruebas dentro de una tabla de firmware.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria agrupar el comando de forma clara.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para marcar un comando con una etiqueta especial, si hace falta.
    ///
    /// [QUIÉN LA USA]
    /// La usan reglas internas y diagnosticos que necesitan distinguir comandos singulares.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa solo cuando el comando necesita una marca adicional.
    ///
    /// [ENTRADAS]
    /// Recibe un texto opcional.
    ///
    /// [SALIDAS]
    /// Devuelve la marca especial o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Catálogo -> `SpecialCommandKey` -> regla especial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit o etiqueta extra sobre un comando normal.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria distinguir un comando normal de uno con comportamiento especial.
    /// </summary>
    public string? SpecialCommandKey { get; init; }
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para aplicar sobrescrituras locales sobre un comando del catálogo base.
///
/// [QUIÉN LA USA]
/// La usa `CommandCatalog` cuando existe el archivo `command_overrides.json`.
///
/// [CUÁNDO SE USA]
/// Se usa durante la carga del catálogo.
///
/// [ENTRADAS]
/// Recibe valores leídos desde JSON.
///
/// [SALIDAS]
/// Devuelve un objeto listo para comparar y reemplazar comandos.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// JSON local -> `CommandOverride` -> `CommandCatalog`.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla de calibración que corrige el valor base.
///
/// [SI NO EXISTIERA]
/// No se podría personalizar un comando sin cambiar el catálogo fuente.
/// </summary>
public sealed class CommandOverride
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para guardar la clave del comando que el JSON quiere modificar.
    ///
    /// [QUIÉN LA USA]
    /// La usa `CommandCatalog` cuando aplica sobrescrituras locales.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al deserializar `command_overrides.json`.
    ///
    /// [ENTRADAS]
    /// Recibe el valor del campo JSON `key`.
    ///
    /// [SALIDAS]
    /// Devuelve la clave del comando a reemplazar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// JSON -> `Key` -> reemplazo del comando base.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un indice de tabla que identifica la entrada a corregir.
    ///
    /// [SI NO EXISTIERA]
    /// No se sabria que comando debe sobrescribirse.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para guardar el nuevo texto del comando, si el JSON lo define.
    ///
    /// [QUIÉN LA USA]
    /// La usa `CommandCatalog` al fusionar datos.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando un override quiere cambiar la orden enviada.
    ///
    /// [ENTRADAS]
    /// Recibe un texto opcional desde JSON.
    ///
    /// [SALIDAS]
    /// Devuelve el comando corregido o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// JSON -> `Command` -> catalogo final.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a recalibrar el valor que una tabla de comandos va a transmitir.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria cambiar el texto transmitido sin tocar el catalogo base.
    /// </summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta propiedad existe para cambiar la marca especial del comando si el JSON la aporta.
    ///
    /// [QUIÉN LA USA]
    /// La usa `CommandCatalog` durante el ajuste local del catalogo.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al aplicar personalizacion local.
    ///
    /// [ENTRADAS]
    /// Recibe una marca opcional desde JSON.
    ///
    /// [SALIDAS]
    /// Devuelve la marca especial corregida o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// JSON -> `SpecialCommandKey` -> comando ajustado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a poner una etiqueta extra sobre una funcion de firmware.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria sobrescribir la marca especial de un comando.
    /// </summary>
    [JsonPropertyName("specialCommandKey")]
    public string? SpecialCommandKey { get; set; }
}
