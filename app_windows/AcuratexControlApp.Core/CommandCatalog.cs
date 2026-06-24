using System.Text.Json;

namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para centralizar el catalogo base de comandos visibles en la shell.
///
/// [QUIEN LA USA]
/// La usan la shell principal, los paneles de comandos y los filtros de categoria.
///
/// [CUANDO SE USA]
/// Se usa al arrancar la UI o al filtrar comandos por categoria.
///
/// [ENTRADAS]
/// Recibe categorias, listas de comandos y un archivo opcional de overrides.
///
/// [SALIDAS]
/// Devuelve colecciones de `CommandDefinition` listas para presentar o enviar.
///
/// [EFECTOS SECUNDARIOS]
/// Puede leer un archivo local de configuracion.
///
/// [FLUJO ACURATEX]
/// Arranque -> `CommandCatalog.Load()` -> UI -> envio al firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla fija de comandos de prueba guardada en flash.
///
/// [SI NO EXISTIERA]
/// Cada pantalla tendria que construir su propio repertorio de comandos.
/// </summary>
public static class CommandCatalog
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta constante existe para dar un nombre fijo a la familia de comandos del tester de motor.
    ///
    /// [QUIÉN LA USA]
    /// La usan la UI, los filtros de categoria y los paneles que muestran botones agrupados.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al pintar la interfaz o al buscar comandos de motor.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una etiqueta estable de categoria.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `MotorCategory` -> filtrado de comandos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a separar rutinas de prueba por bloque funcional en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla inventaria su propio texto para esta categoria.
    /// </summary>
    public const string MotorCategory = "Motor Tester";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta constante existe para dar un nombre fijo a la familia de comandos del tester de cabezal.
    ///
    /// [QUIÉN LA USA]
    /// La usan la UI, los filtros de categoria y los paneles del cabezal.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al pintar la interfaz o al buscar comandos del cabezal.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una etiqueta estable de categoria.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `HeadCategory` -> filtrado de comandos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a separar pruebas de cabezal en otro grupo de comandos internos.
    ///
    /// [SI NO EXISTIERA]
    /// La UI perderia una referencia clara para agrupar comandos del cabezal.
    /// </summary>
    public const string HeadCategory = "Cabezal Tester";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para cargar el catalogo base y luego aplicar una capa local de overrides.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman las shells al arrancar.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta durante la inicializacion de la interfaz.
    ///
    /// [ENTRADAS]
    /// No recibe parametros.
    ///
    /// [SALIDAS]
    /// Devuelve una lista inmutable para el consumidor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Lee `command_overrides.json` si existe.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Load()` -> catalogo base -> overrides locales -> botones.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a precargar una tabla de comandos antes de entrar al loop principal.
    ///
    /// [SI NO EXISTIERA]
    /// La app perderia el punto unico donde se define el catalogo de comandos.
    /// </summary>
    public static IReadOnlyList<CommandDefinition> Load()
    {
        // [C#] `List<T>` empieza mutable porque primero se arma el catalogo y luego se ajusta.
        // [ACURATEX] La lista base concentra los comandos visibles para que la UI no los invente en cada pantalla.
        List<CommandDefinition> commands =
        [
            new() { Key = "motor-start", Title = "Start", Command = "start", Category = MotorCategory },
            new() { Key = "motor-stop", Title = "Stop", Command = "stop", Category = MotorCategory },
            new() { Key = "motor-status", Title = "Status", Command = "status", Category = MotorCategory },
            new() { Key = "head-test", Title = "Testeo", Command = "testeo", Category = HeadCategory },
            new() { Key = "head-ping", Title = "Ping", Command = "ping", Category = HeadCategory },
            new() { Key = "head-can1", Title = "CAN1", Command = "can1", Category = HeadCategory },
            new() { Key = "head-can2", Title = "CAN2", Command = "can2", Category = HeadCategory },
        ];

        ApplyOverrides(commands);
        return commands;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para separar los comandos que pertenecen a una categoria.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman los paneles que muestran solo una familia de botones.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al renderizar la UI o al recalcular una lista de comandos.
    ///
    /// [ENTRADAS]
    /// Recibe la secuencia de comandos y el nombre de categoria.
    ///
    /// [SALIDAS]
    /// Devuelve los comandos que coinciden con la categoria.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica la coleccion original.
    ///
    /// [FLUJO ACURATEX]
    /// Lista completa -> `ForCategory()` -> subconjunto visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a filtrar interrupciones por vector.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que hacer su propio filtrado cada vez.
    /// </summary>
    public static IEnumerable<CommandDefinition> ForCategory(IEnumerable<CommandDefinition> commands, string category)
    {
        return commands.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para mezclar el catálogo base con el archivo local de overrides.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `Load()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta durante la carga inicial del catálogo.
    ///
    /// [ENTRADAS]
    /// Recibe la lista mutable de comandos base.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede reemplazar comandos en memoria leyendo `command_overrides.json`.
    ///
    /// [FLUJO ACURATEX]
    /// `Load()` -> `ApplyOverrides()` -> catálogo final.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a aplicar correcciones de calibración sobre una tabla base.
    ///
    /// [SI NO EXISTIERA]
    /// No habría personalización local sin tocar el catálogo fuente.
    /// </summary>
    private static void ApplyOverrides(List<CommandDefinition> commands)
    {
        // [C#] `Path.Combine` arma una ruta correcta sin depender de `\\` manuales.
        // [ACURATEX] El archivo de overrides vive junto al ejecutable, no dentro del firmware.
        string path = Path.Combine(AppContext.BaseDirectory, "command_overrides.json");
        // [C#] `File.Exists` evita intentar leer un archivo que no esta presente.
        // [ACURATEX] Si no hay overrides, el catalogo base queda intacto.
        if (!File.Exists(path)) {
            return;
        }

        try {
            // [C#] `JsonSerializer` convierte texto JSON en objetos tipados.
            // [ACURATEX] Asi se aplican sobrescrituras locales sin cambiar el catalogo fuente.
            List<CommandOverride>? overrides = JsonSerializer.Deserialize<List<CommandOverride>>(File.ReadAllText(path));
            if (overrides == null) {
                return;
            }

            foreach (CommandOverride item in overrides) {
                // [C#] `FirstOrDefault` devuelve `null` si no encuentra coincidencia.
                // [ACURATEX] Se busca por clave para reemplazar un comando puntual sin recrear todo el catalogo.
                CommandDefinition? current = commands.FirstOrDefault(x => string.Equals(x.Key, item.Key, StringComparison.OrdinalIgnoreCase));
                if (current == null) {
                    continue;
                }

                // [C#] `IndexOf` recupera la posicion exacta para sobrescribir el elemento existente.
                // [ACURATEX] Eso preserva el orden visual del catalogo.
                int index = commands.IndexOf(current);
                commands[index] = new CommandDefinition
                {
                    Key = current.Key,
                    Title = current.Title,
                    Category = current.Category,
                    // [ACURATEX] Si el JSON no trae un campo, se conserva el valor base.
                    Command = string.IsNullOrWhiteSpace(item.Command) ? current.Command : item.Command!,
                    SpecialCommandKey = string.IsNullOrWhiteSpace(item.SpecialCommandKey) ? current.SpecialCommandKey : item.SpecialCommandKey,
                };
            }
        } catch {
        }
    }
}
