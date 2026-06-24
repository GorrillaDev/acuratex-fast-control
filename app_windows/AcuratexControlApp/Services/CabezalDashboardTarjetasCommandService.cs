// [ACURATEX] Traduce la UI del cabezal modular a comandos de perfil, scripts y lineas CAN/DO.
// [FLUJO] Boton o control -> servicio -> perfil/script/conexion -> firmware.
// [EQUIV MCU] Se parece a un despachador de eventos que decide que rutina de salida ejecutar.
using System.Text.RegularExpressions;

namespace AcuratexControlApp.Services;

public sealed class CabezalDashboardTarjetasCommandService : ICabezalDashboardTarjetasCommandService
{
    // [ACURATEX] Estas expresiones regulares decodifican comandos compactos escritos por la UI.
    private static readonly Regex JSingleRegex = new("^j_(run|stop)_([1-9][0-9]*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex YarnSingleRegex = new("^y([1-9][0-9]*)_(run|stop)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StitchSingleRegex = new("^s_(run|stop)_([1-9][0-9]*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // [ACURATEX] Algunos comandos no se traducen; se reenvian tal cual al firmware.
    private static readonly HashSet<string> DirectPassthroughCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "testeo",
        "start",
        "emergency_stop"
    };

    // [ACURATEX] La conexion manda las lineas, el perfil resuelve acciones y el motor de scripts
    // ejecuta archivos asociados. Este servicio solo coordina esas tres piezas.
    private readonly IConnectionController _connection;
    private readonly IHeadProfileService _profiles;
    private readonly IAppScriptExecutionService _scripts;
    // [ACURATEX] Guarda el ultimo valor enviado a cada J para poder detectar que bit cambio.
    private readonly Dictionary<int, byte> _jRegisters = new();

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para recibir la conexion, los perfiles y el motor de scripts
    /// que necesita el servicio para traducir la UI modular a comandos reales.
    ///
    /// [QUIEN LA LLAMA]
    /// Lo llama el contenedor de dependencias al construir la pagina o panel modular.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una sola vez durante el arranque del servicio.
    ///
    /// [ENTRADAS]
    /// Recibe el controlador de conexion, el servicio de perfiles y el servicio de scripts.
    ///
    /// [SALIDAS]
    /// Devuelve el servicio listo para aceptar ordenes.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda referencias a los colaboradores que haran el trabajo real.
    ///
    /// [FLUJO ACURATEX]
    /// DI -> CabezalDashboardTarjetasCommandService -> perfiles/scripts/conexion -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a inicializar una capa intermedia con punteros a driver, tabla de mapeo y UART.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que hablar directo con conexion, perfiles y scripts por separado.
    /// </summary>
    public CabezalDashboardTarjetasCommandService(
        IConnectionController connection,
        IHeadProfileService profiles,
        IAppScriptExecutionService scripts)
    {
        _connection = connection;
        _profiles = profiles;
        _scripts = scripts;
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta operacion existe para reenviar una linea CAN ya formada sin interpretar el contenido.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la UI cuando ya construyo la trama completa.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al mandar una linea CAN manual o calculada por la vista.
    ///
    /// [ENTRADAS]
    /// Recibe la linea y un token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede terminar en un envio directo al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendCanLineAsync -> SendLineAsync -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una trama ya empaquetada en un bus serial.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que conocer el metodo interno de envio.
    /// </summary>
    public Task SendCanLineAsync(string line, CancellationToken cancellationToken = default)
    {
        return SendLineAsync(line, cancellationToken);
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta operacion existe para traducir comandos DO de alto nivel a una accion concreta
    /// del firmware o del motor de scripts.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la UI modular cuando el operador pulsa botones de accion.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando la vista emite un comando semantico como init, status o stop.
    ///
    /// [ENTRADAS]
    /// Recibe el comando y el token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede ejecutar `HEAD_ACTION`, reenviar texto tal cual o disparar una lista de acciones.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendDoCommandAsync -> clasificacion -> firmware/script.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un dispatcher de estado que decide que rutina de salida correr.
    ///
    /// [SI NO EXISTIERA]
    /// Cada boton tendria que repetir la traduccion a comando.
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
    /// [POR QUE EXISTE]
    /// Esta operacion existe para convertir una posicion numerica de DEN en una accion
    /// de perfil o de script.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman los componentes visuales del sistema DEN.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el usuario pulsa una posicion o cuando la vista resuelve una lectura.
    ///
    /// [ENTRADAS]
    /// Recibe el indice del motor, la posicion actual, la posicion seleccionada y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede resolver bindings y mandar comandos de cabezal.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendDenPositionAsync -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a convertir una lectura analogica en una accion discreta.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz tendria que calcular la accion adecuada por su cuenta.
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
    /// [POR QUE EXISTE]
    /// Esta operacion existe para convertir una posicion de SIC en una accion de perfil.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman los componentes de SIC de la pantalla modular.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando el operador pulsa una posicion de SIC.
    ///
    /// [ENTRADAS]
    /// Recibe el indice del motor, la posicion, la seleccion y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede resolver una binding y mandar una accion al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendSicPositionAsync -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a elegir una salida segun un indice de tabla.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendria que mapear SIC manualmente.
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
    /// [POR QUE EXISTE]
    /// Esta operacion existe para resolver el cambio de bits de un registro J y traducirlo
    /// a una accion de perfil concreta.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la UI modular cuando cambia la mascara completa de un J.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al escribir un nuevo valor de bits para el registro.
    ///
    /// [ENTRADAS]
    /// Recibe el indice J, el valor nuevo y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza el cache del ultimo valor y puede enviar una accion al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendJRegisterAsync -> comparacion de bits -> accion de perfil.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a detectar que bit cambio en un registro de entrada.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria traducir una mascara J a una accion puntual.
    /// </summary>
    public async Task SendJRegisterAsync(int jIndex, byte value, CancellationToken cancellationToken = default)
    {
        if (jIndex <= 0) {
            return;
        }

        string instanceName = $"J{jIndex}";
        if (value == 0x00) {
            await ExecuteProfileActionAsync(instanceName, "ON_ALL", cancellationToken).ConfigureAwait(false);
            _jRegisters[jIndex] = value;
            return;
        }

        if (value == 0xFF) {
            await ExecuteProfileActionAsync(instanceName, "OFF_ALL", cancellationToken).ConfigureAwait(false);
            _jRegisters[jIndex] = value;
            return;
        }

        byte previous = _jRegisters.TryGetValue(jIndex, out byte saved) ? saved : (byte)0xFF;
        int delta = previous ^ value;
        if (!TryGetChangedBit(delta, out int bitIndex)) {
            throw new InvalidOperationException($"No se pudo resolver cambio de pines para {instanceName}.");
        }

        await ExecuteProfileActionAsync(instanceName, $"CH{bitIndex + 1}", cancellationToken).ConfigureAwait(false);
        _jRegisters[jIndex] = value;
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta operacion existe para que la UI pueda pedir un ON ALL u OFF ALL sobre un grupo J
    /// sin conocer el detalle de bits individuales.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la vista modular cuando el usuario pulsa un boton global del grupo J.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cuando un grupo J debe quedar completamente encendido o apagado.
    ///
    /// [ENTRADAS]
    /// Recibe el indice del grupo, la intencion on/off y un token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar una accion de perfil al motor de scripts.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> SendJAllAsync -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir un registro completo de salida con todos los bits alineados.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que invocar una accion por pin en lugar de una sola orden global.
    /// </summary>
    public async Task SendJAllAsync(int jIndex, bool on, CancellationToken cancellationToken = default)
    {
        if (jIndex <= 0) {
            return;
        }

        string instanceName = $"J{jIndex}";
        string actionName = on ? "ON_ALL" : "OFF_ALL";
        await ExecuteProfileActionAsync(instanceName, actionName, cancellationToken).ConfigureAwait(false);
        _jRegisters[jIndex] = on ? (byte)0x00 : (byte)0xFF;
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta operacion existe para traducir una accion de un canal J individual a una orden
    /// de perfil concreta.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama la vista cuando el usuario pulsa un boton J1..J8 dentro del grupo.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cada vez que un canal J especifico cambia.
    ///
    /// [ENTRADAS]
    /// Recibe el indice del grupo, el indice del canal y el token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar una accion de perfil al motor de scripts.
    ///
    /// [FLUJO ACURATEX]
    /// Razor -> SendJChannelAsync -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a resolver un bit puntual dentro de un registro de salidas.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no podria disparar el canal individual sin copiar logica.
    /// </summary>
    public async Task SendJChannelAsync(int jIndex, int channelIndex, CancellationToken cancellationToken = default)
    {
        if (jIndex <= 0 || channelIndex <= 0) {
            return;
        }

        string instanceName = $"J{jIndex}";
        await ExecuteProfileActionAsync(instanceName, $"CH{channelIndex}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta operacion existe para traducir un pin fisico de bloque a una accion del perfil.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman los componentes de bloques de pines.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta al pulsar un pin del bloque modular.
    ///
    /// [ENTRADAS]
    /// Recibe la clave del bloque, el indice de pin, el estado on/off y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve `Task`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede resolver una accion de perfil y mandar la orden al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SendBlockPinAsync -> mapeo -> perfil/script -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a mapear un pin de entrada a una rutina concreta.
    ///
    /// [SI NO EXISTIERA]
    /// Los bloques no tendrian forma centralizada de disparar acciones.
    /// </summary>
    public async Task SendBlockPinAsync(string blockKey, int pinIndex, bool on, CancellationToken cancellationToken = default)
    {
        _ = on;

        if (!TryMapBlockPinToProfileAction(blockKey, pinIndex, out MappedAction action)) {
            throw new InvalidOperationException($"No se pudo resolver bloque configurado: {blockKey}.");
        }

        await ExecuteProfileActionAsync(action.InstanceName, action.ActionName, cancellationToken).ConfigureAwait(false);
    }

    // [ACURATEX] Recorre varias acciones ya resueltas y las ejecuta en orden.
    private async Task ExecuteMappedActionsAsync(IReadOnlyList<MappedAction> actions, CancellationToken cancellationToken)
    {
        foreach (MappedAction action in actions) {
            await ExecuteProfileActionAsync(action.InstanceName, action.ActionName, cancellationToken).ConfigureAwait(false);
        }
    }

    // [ACURATEX] INIT modular se manda como HEAD_ACTION directo porque el firmware lo interpreta con semantica especial.
    private async Task ExecuteHeadActionRawAsync(string actionName, CancellationToken cancellationToken)
    {
        if (!_profiles.HasActiveProfile(HeadSystemKind.Modular)) {
            throw new InvalidOperationException("No hay programa de Cabezal activo para Sistema Modular.");
        }

        AppScriptExecutionResult executed = await _scripts
            .ExecuteHeadActionAsync(actionName, cancellationToken)
            .ConfigureAwait(false);
        if (!executed.Success) {
            throw new InvalidOperationException(executed.Message);
        }
    }

    // [ACURATEX] Usa el motor de scripts para una accion ya resuelta en el perfil.
    private async Task ExecuteProfileActionAsync(string instanceName, string actionName, CancellationToken cancellationToken)
    {
        AppScriptExecutionResult executed = await _scripts
            .ExecuteActionAsync(HeadSystemKind.Modular, instanceName, actionName, cancellationToken)
            .ConfigureAwait(false);
        if (!executed.Success) {
            throw new InvalidOperationException(executed.Message);
        }
    }

    // [ACURATEX] Convierte un valor numerico en la accion de perfil mas cercana.
    private async Task ExecuteProfileActionByValueAsync(string instanceName, int value, CancellationToken cancellationToken)
    {
        HeadBindingResolveResult resolve = _profiles.ResolveByNearestValue(HeadSystemKind.Modular, instanceName, value);
        if (!resolve.Success || resolve.Binding is null) {
            throw new InvalidOperationException(resolve.ErrorMessage ?? "No se pudo resolver la accion configurada.");
        }

        await ExecuteProfileActionAsync(resolve.Binding.InstanceName, resolve.Binding.ActionName, cancellationToken).ConfigureAwait(false);
    }

    // [ACURATEX] Traduce nombres compactos como j_run_all o s_stop_all a una lista de acciones.
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

    // [ACURATEX] Convierte una familia de comandos en una secuencia de acciones ordenadas.
    private static IReadOnlyList<MappedAction> BuildRangeActions(string prefix, int start, int end, string action)
    {
        return Enumerable.Range(start, end - start + 1)
            .Select(index => new MappedAction($"{prefix}{index}", action))
            .ToArray();
    }

    // [ACURATEX] Convierte una clave de bloque y un pin en una accion concreta de perfil.
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

    // [ACURATEX] Localiza el bit que cambio entre dos mascaras para mapearlo a CHx.
    private static bool TryGetChangedBit(int delta, out int bitIndex)
    {
        bitIndex = -1;
        if (delta <= 0 || (delta & (delta - 1)) != 0) {
            return false;
        }

        int index = 0;
        int value = delta;
        while ((value & 0x01) == 0) {
            value >>= 1;
            index++;
        }

        bitIndex = index;
        return true;
    }

    // [ACURATEX] Evita duplicar la logica de conexion activa antes de enviar.
    private Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        string cleanLine = line.Trim();
        if (cleanLine.Length == 0 || !_connection.IsConnected) {
            return Task.CompletedTask;
        }

        return _connection.SendLineAsync(cleanLine, cancellationToken);
    }

    private readonly record struct MappedAction(string InstanceName, string ActionName);
}
