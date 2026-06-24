using System.Text.RegularExpressions;

namespace AcuratexControlApp.Services;

public sealed class CabezalDashboardUnificadoCommandService : ICabezalDashboardUnificadoCommandService
{
    private static readonly Regex JSingleRegex = new("^j_(run|stop)_([1-9][0-9]*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YarnSingleRegex = new("^y([1-9][0-9]*)_(run|stop)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StitchSingleRegex = new("^s_(run|stop)_([1-9][0-9]*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> DirectPassthroughCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "testeo",
        "start",
        "emergency_stop"
    };

    private readonly IConnectionController _connection;
    private readonly IHeadProfileService _profiles;
    private readonly IAppScriptExecutionService _scripts;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para enlazar la conexión, los perfiles y el motor de scripts.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama el contenedor de dependencias cuando crea el servicio para la UI.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al construir el servicio.
    ///
    /// [ENTRADAS]
    /// Recibe conexión, perfiles y scripts.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda referencias a los servicios que hacen el trabajo real.
    ///
    /// [FLUJO ACURATEX]
    /// DI -> servicio de cabezal -> conexión/perfiles/scripts.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inyectar punteros a periféricos y tablas de configuración.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendría que resolver dependencias por su cuenta.
    /// </summary>
    public CabezalDashboardUnificadoCommandService(
        IConnectionController connection,
        IHeadProfileService profiles,
        IAppScriptExecutionService scripts)
    {
        _connection = connection;
        _profiles = profiles;
        _scripts = scripts;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para reenviar texto CAN directo sin modificarlo.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la vista cuando necesita mandar una línea CAN directa.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al pulsar acciones o ingresar comandos manuales CAN.
    ///
    /// [ENTRADAS]
    /// Recibe la línea CAN y un token.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede terminar en un envío al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> SendCanLineAsync -> SendLineAsync -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una trama CAN ya formada.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que distinguir entre envío CAN y DO.
    /// </summary>
    public Task SendCanLineAsync(string line, CancellationToken cancellationToken = default)
    {
        return SendLineAsync(line, cancellationToken);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para traducir órdenes DO de alto nivel a acciones concretas.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la vista unificada al pulsar botones de estado o atajos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pide una acción lógica del cabezal.
    ///
    /// [ENTRADAS]
    /// Recibe el comando y el token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede ejecutar `HEAD_ACTION`, reenviar texto directo o disparar scripts.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> servicio -> clasificación DO -> firmware/script.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un dispatcher que decide qué rutina de salida ejecutar.
    ///
    /// [SI NO EXISTIERA]
    /// Los botones DO no tendrían traducción centralizada.
    /// </summary>
    public async Task SendDoCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        string cleanCommand = (command ?? string.Empty).Trim();
        if (cleanCommand.Length == 0) {
            return;
        }

        if (cleanCommand.Equals("init", StringComparison.OrdinalIgnoreCase)) {
            await ExecuteHeadActionRawAsync("INIT", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (cleanCommand.Equals("status", StringComparison.OrdinalIgnoreCase)) {
            await SendLineAsync("HEAD_STATUS", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (cleanCommand.Equals("stop", StringComparison.OrdinalIgnoreCase)) {
            await SendLineAsync("HEAD_STOP", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (DirectPassthroughCommands.Contains(cleanCommand)) {
            await SendLineAsync(cleanCommand, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (TryMapDoCommandToProfileActions(cleanCommand, out IReadOnlyList<MappedAction> actions)) {
            await ExecuteMappedActionsAsync(actions, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendLineAsync(cleanCommand, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para convertir una posición de DEN en una acción de perfil o script.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los componentes de motor DEN.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pulsa una posición de DEN.
    ///
    /// [ENTRADAS]
    /// Recibe índice de motor, posición y número de posición seleccionada.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede resolver una binding y ejecutar una acción concreta.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendDenPositionAsync -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a convertir un número de posición en una salida concreta.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que resolver la binding por su cuenta.
    /// </summary>
    public async Task SendDenPositionAsync(
        int motorIndex,
        int position,
        int selectedPositionNumber = 0,
        CancellationToken cancellationToken = default)
    {
        if (motorIndex < 0) {
            return;
        }

        string instanceName = $"DEN{motorIndex + 1}";
        if (selectedPositionNumber > 0) {
            await ExecuteProfileActionAsync(instanceName, $"POS{selectedPositionNumber}", cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteProfileActionByValueAsync(instanceName, position, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para convertir una posición de SIC en una acción de perfil.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los componentes de motor SIC.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario selecciona una posición en SIC.
    ///
    /// [ENTRADAS]
    /// Recibe índice, posición, selección y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede ejecutar una acción asociada al perfil activo.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendSicPositionAsync -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una salida lógica con una tabla de traducción.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que conocer el mapeo del perfil.
    /// </summary>
    public async Task SendSicPositionAsync(
        int sicIndex,
        int position,
        int selectedPositionNumber = 0,
        CancellationToken cancellationToken = default)
    {
        if (sicIndex < 0) {
            return;
        }

        string instanceName = $"SIC{sicIndex + 1}";
        if (selectedPositionNumber > 0) {
            await ExecuteProfileActionAsync(instanceName, $"POS{selectedPositionNumber}", cancellationToken).ConfigureAwait(false);
            return;
        }

        await ExecuteProfileActionByValueAsync(instanceName, position, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para traducir un registro J completo a una acción de grupo.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman rutas que trabajan con el estado agregado del grupo J.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la UI quiere actuar sobre el registro completo.
    ///
    /// [ENTRADAS]
    /// Recibe índice J, valor y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede redirigir a ON_ALL u OFF_ALL.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendJRegisterAsync -> SendJAllAsync -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir un puerto entero en lugar de un pin individual.
    ///
    /// [SI NO EXISTIERA]
    /// El registro J no tendría traducción compacta.
    /// </summary>
    public async Task SendJRegisterAsync(int jIndex, byte value, CancellationToken cancellationToken = default)
    {
        if (value == 0x00) {
            await SendJAllAsync(jIndex, on: true, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (value == 0xFF) {
            await SendJAllAsync(jIndex, on: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException($"SendJRegisterAsync solo soporta ON_ALL/OFF_ALL para J{jIndex}.");
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para aplicar ON/OFF a todo un grupo J.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman botones globales J y otras rutinas de agregación.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario quiere encender o apagar todo el grupo.
    ///
    /// [ENTRADAS]
    /// Recibe índice J, intención on/off y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Llama a una acción de perfil con `ON_ALL` o `OFF_ALL`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendJAllAsync -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a poner todos los bits de un puerto en el mismo estado.
    ///
    /// [SI NO EXISTIERA]
    /// La operación de grupo completo no tendría atajo propio.
    /// </summary>
    public async Task SendJAllAsync(int jIndex, bool on, CancellationToken cancellationToken = default)
    {
        if (jIndex < 1 || jIndex > 8) {
            throw new ArgumentOutOfRangeException(nameof(jIndex), "jIndex debe estar entre 1 y 8.");
        }

        await ExecuteProfileActionAsync($"J{jIndex}", on ? "ON_ALL" : "OFF_ALL", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para enviar una acción a un canal específico dentro de J.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los botones individuales J1..J8.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pulsa un canal J.
    ///
    /// [ENTRADAS]
    /// Recibe índice J, canal y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Ejecuta la acción de perfil del canal pedido.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendJChannelAsync -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a manipular un bit concreto dentro de un registro.
    ///
    /// [SI NO EXISTIERA]
    /// Los botones de canal no tendrían traducción directa.
    /// </summary>
    public async Task SendJChannelAsync(int jIndex, int channelIndex, CancellationToken cancellationToken = default)
    {
        if (jIndex < 1 || jIndex > 8) {
            throw new ArgumentOutOfRangeException(nameof(jIndex), "jIndex debe estar entre 1 y 8.");
        }

        if (channelIndex < 1 || channelIndex > 8) {
            throw new ArgumentOutOfRangeException(nameof(channelIndex), "channelIndex debe estar entre 1 y 8.");
        }

        await ExecuteProfileActionAsync($"J{jIndex}", $"CH{channelIndex}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para traducir un pin de Yarn o Stitch a su acción de perfil.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los bloques de salida físicos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pulsa un pin de bloque.
    ///
    /// [ENTRADAS]
    /// Recibe bloque, pin, estado y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Ejecuta una acción de perfil en el bloque asociado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendBlockPinAsync -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a resolver qué canal físico corresponde a una salida lógica.
    ///
    /// [SI NO EXISTIERA]
    /// Yarn y Stitch tendrían que resolver su mapping en la vista.
    /// </summary>
    public async Task SendBlockPinAsync(string blockKey, int pinIndex, bool on, CancellationToken cancellationToken = default)
    {
        _ = on;

        if (!TryMapBlockPinToProfileAction(blockKey, pinIndex, out MappedAction action)) {
            throw new InvalidOperationException($"No se pudo resolver bloque configurado: {blockKey}.");
        }

        await ExecuteProfileActionAsync(action.InstanceName, action.ActionName, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteMappedActionsAsync(IReadOnlyList<MappedAction> actions, CancellationToken cancellationToken)
    {
        foreach (MappedAction action in actions) {
            await ExecuteProfileActionAsync(action.InstanceName, action.ActionName, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ExecuteHeadActionRawAsync(string actionName, CancellationToken cancellationToken)
    {
        if (!_profiles.HasActiveProfile(HeadSystemKind.Unified)) {
            throw new InvalidOperationException("No hay programa de Cabezal activo para Sistema Unificado.");
        }

        AppScriptExecutionResult executed = await _scripts
            .ExecuteHeadActionAsync(actionName, cancellationToken)
            .ConfigureAwait(false);
        if (!executed.Success) {
            throw new InvalidOperationException(executed.Message);
        }
    }

    private async Task ExecuteProfileActionAsync(string instanceName, string actionName, CancellationToken cancellationToken)
    {
        AppScriptExecutionResult executed = await _scripts
            .ExecuteActionAsync(HeadSystemKind.Unified, instanceName, actionName, cancellationToken)
            .ConfigureAwait(false);
        if (!executed.Success) {
            throw new InvalidOperationException(executed.Message);
        }
    }

    private async Task ExecuteProfileActionByValueAsync(string instanceName, int value, CancellationToken cancellationToken)
    {
        HeadBindingResolveResult resolve = _profiles.ResolveByNearestValue(HeadSystemKind.Unified, instanceName, value);
        if (!resolve.Success || resolve.Binding is null) {
            throw new InvalidOperationException(resolve.ErrorMessage ?? "No se pudo resolver la accion configurada.");
        }

        await ExecuteProfileActionAsync(resolve.Binding.InstanceName, resolve.Binding.ActionName, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryMapDoCommandToProfileActions(string command, out IReadOnlyList<MappedAction> actions)
    {
        if (command.Equals("j_run_all", StringComparison.OrdinalIgnoreCase)) {
            actions = BuildRangeActions("J", 1, 8, "RUN");
            return true;
        }

        if (command.Equals("j_stop_all", StringComparison.OrdinalIgnoreCase)) {
            actions = BuildRangeActions("J", 1, 8, "STOP");
            return true;
        }

        if (command.Equals("y_run_all", StringComparison.OrdinalIgnoreCase)) {
            actions = BuildRangeActions("YARN", 1, 2, "RUN");
            return true;
        }

        if (command.Equals("y_stop_all", StringComparison.OrdinalIgnoreCase)) {
            actions = BuildRangeActions("YARN", 1, 2, "STOP");
            return true;
        }

        if (command.Equals("s_run_all", StringComparison.OrdinalIgnoreCase)) {
            actions = BuildRangeActions("STITCH", 1, 4, "RUN");
            return true;
        }

        if (command.Equals("s_stop_all", StringComparison.OrdinalIgnoreCase)) {
            actions = BuildRangeActions("STITCH", 1, 4, "STOP");
            return true;
        }

        Match jMatch = JSingleRegex.Match(command);
        if (jMatch.Success && int.TryParse(jMatch.Groups[2].Value, out int jIndex) && jIndex > 0) {
            actions = new[] { new MappedAction($"J{jIndex}", jMatch.Groups[1].Value.Equals("run", StringComparison.OrdinalIgnoreCase) ? "RUN" : "STOP") };
            return true;
        }

        Match yarnMatch = YarnSingleRegex.Match(command);
        if (yarnMatch.Success && int.TryParse(yarnMatch.Groups[1].Value, out int yarnIndex) && yarnIndex > 0) {
            actions = new[] { new MappedAction($"YARN{yarnIndex}", yarnMatch.Groups[2].Value.Equals("run", StringComparison.OrdinalIgnoreCase) ? "RUN" : "STOP") };
            return true;
        }

        Match stitchMatch = StitchSingleRegex.Match(command);
        if (stitchMatch.Success && int.TryParse(stitchMatch.Groups[2].Value, out int stitchIndex) && stitchIndex > 0) {
            actions = new[] { new MappedAction($"STITCH{stitchIndex}", stitchMatch.Groups[1].Value.Equals("run", StringComparison.OrdinalIgnoreCase) ? "RUN" : "STOP") };
            return true;
        }

        actions = Array.Empty<MappedAction>();
        return false;
    }

    private static IReadOnlyList<MappedAction> BuildRangeActions(string prefix, int start, int end, string action)
    {
        return Enumerable.Range(start, end - start + 1)
            .Select(index => new MappedAction($"{prefix}{index}", action))
            .ToArray();
    }

    private static bool TryMapBlockPinToProfileAction(string blockKey, int pinIndex, out MappedAction action)
    {
        action = default;
        if (pinIndex <= 0) {
            return false;
        }

        string cleanKey = (blockKey ?? string.Empty).Trim().ToLowerInvariant();
        if (cleanKey.StartsWith("yarn", StringComparison.Ordinal)) {
            if (int.TryParse(cleanKey["yarn".Length..], out int yarnIndex) && yarnIndex > 0) {
                action = new MappedAction($"YARN{yarnIndex}", $"CH{pinIndex}");
                return true;
            }

            return false;
        }

        if (cleanKey.StartsWith("stitch", StringComparison.Ordinal)) {
            if (int.TryParse(cleanKey["stitch".Length..], out int stitchIndex) && stitchIndex > 0) {
                action = new MappedAction($"STITCH{stitchIndex}", $"POS{pinIndex}");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta funcion existe para hacer un unico punto de salida de texto hacia la conexion activa.
    ///
    /// [QUIÉN LA USA]
    /// La usan las rutas que ya decidieron que el texto puede salir al firmware.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta justo antes de mandar una linea limpia.
    ///
    /// [ENTRADAS]
    /// Recibe la linea y el token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede escribir en la conexion si esta activa.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> `SendLineAsync()` -> `IConnectionController` -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una trama limpia en el bus de salida.
    ///
    /// [SI NO EXISTIERA]
    /// El envio final quedaria repetido en varios metodos.
    /// </summary>
    private Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        string cleanLine = line.Trim();
        if (cleanLine.Length == 0 || !_connection.IsConnected) {
            return Task.CompletedTask;
        }

        return _connection.SendLineAsync(cleanLine, cancellationToken);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este record existe para representar una accion ya resuelta para un perfil o bloque.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan las rutinas que traducen comandos compactos en acciones concretas.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa durante el mapeo de comandos de alto nivel.
    ///
    /// [ENTRADAS]
    /// Recibe instancia y nombre de accion.
    ///
    /// [SALIDAS]
    /// Devuelve una pareja inmutable de valores.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Comando -> `MappedAction` -> perfil/script.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un par de registros que identifican canal y operacion.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio tendria que transportar ambas cadenas por separado todo el tiempo.
    /// </summary>
    private readonly record struct MappedAction(string InstanceName, string ActionName);
}
