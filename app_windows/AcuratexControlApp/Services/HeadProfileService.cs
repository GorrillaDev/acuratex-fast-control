// [ACURATEX] Este servicio interpreta archivos de programa de cabezal, resuelve bindings
// y mantiene el perfil activo en memoria.
// [FLUJO] Archivo TXT -> parseo -> perfil activo -> decisiones de UI y firmware.
using System.Globalization;
using System.Text.RegularExpressions;

namespace AcuratexControlApp.Services;

// [C#] `sealed` cierra la implementacion concreta del servicio.
public sealed class HeadProfileService : IHeadProfileService
{
    // [ACURATEX] Limite compartido con el protocolo de nombres de archivo.
    private const int MaxFileNameLength = 48;
    // [ACURATEX] Modo de validacion configurable en la implementacion actual.
    private static HeadProfileValidationMode ValidationMode { get; } = HeadProfileValidationMode.Flexible;
    // [ACURATEX] Formato aceptado para nombres de programa.
    private static readonly Regex ProgramFileNameRegex = new(
        "^cbz\\.(uni|mod)\\.prog([1-9][0-9]*)\\.txt$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // [ACURATEX] Estado compartido del perfil activo por sistema.
    // [C#] `Dictionary<TKey,TValue>` guarda el perfil actual por tipo de sistema.
    private static readonly object ActiveProfilesGate = new();
    private static readonly Dictionary<HeadSystemKind, HeadProfile> ActiveProfiles = new();

    private readonly ICommandFileTransferService _commandFiles;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para conectar el servicio de perfiles con el servicio de
    /// transferencia de archivos, que es quien lee los programas reales.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama el contenedor de dependencias.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al crear el servicio.
    ///
    /// [ENTRADAS]
    /// Recibe el servicio de archivos.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda la dependencia necesaria para listar y descargar programas.
    ///
    /// [FLUJO ACURATEX]
    /// DI -> HeadProfileService -> CommandFileTransferService -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a conectar un parser a una memoria externa de configuración.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio no podría leer los archivos de programa.
    /// </summary>
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este constructor existe para conectar el servicio de perfiles con el servicio de archivos.
    ///
    /// [QUIÉN LO LLAMA]
    /// Lo llama el contenedor de dependencias.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al crear el servicio.
    ///
    /// [ENTRADAS]
    /// Recibe el servicio de transferencia de archivos.
    ///
    /// [SALIDAS]
    /// Devuelve el servicio inicializado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda la dependencia que luego carga y aplica programas.
    ///
    /// [FLUJO ACURATEX]
    /// DI -> HeadProfileService -> archivos de programa -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a conectar el gestor de perfiles con la memoria donde viven los programas.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio no podría leer ni aplicar programas de cabezal.
    /// </summary>
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para conectar el servicio de perfiles con el servicio de
    /// transferencia de archivos que provee los programas reales.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el contenedor de dependencias.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una vez al crear el servicio.
    ///
    /// [ENTRADAS]
    /// Recibe el servicio de archivos.
    ///
    /// [SALIDAS]
    /// Devuelve el servicio listo para leer y aplicar perfiles.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda la dependencia para usarla en listados, inspecciones y aplicaciones.
    ///
    /// [FLUJO ACURATEX]
    /// DI -> `HeadProfileService()` -> programas de cabezal.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a conectar un parser a una memoria externa de configuracion.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio no podria leer archivos de programa.
    /// </summary>
    public HeadProfileService(ICommandFileTransferService commandFiles)
    {
        _commandFiles = commandFiles;
    }

    // [ACURATEX] Aviso para la UI cuando el perfil activo cambia.
    // [FLUJO] ApplyProgram/ClearActiveProfile -> StateChanged -> repintado.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar que el perfil activo cambió y la UI debe repintarse.
    ///
    /// [QUIÉN LO USA]
    /// Lo suscriben componentes Razor y servicios que dependen del programa activo.
    ///
    /// [CUÁNDO SE USA]
    /// Se dispara al cargar, limpiar o cambiar un perfil.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Provoca repintado o actualización de listas de programas.
    ///
    /// [FLUJO ACURATEX]
    /// Perfil activo cambia -> `StateChanged` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupción de refresco cuando cambia la configuración de trabajo.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no sabría cuándo debe actualizar el programa visible.
    /// </summary>
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este evento existe para avisar que cambio el programa o perfil activo y la UI debe
    /// repintarse.
    ///
    /// [QUIEN LO USA]
    /// Lo suscriben componentes Razor y servicios dependientes del programa activo.
    ///
    /// [CUANDO SE USA]
    /// Se dispara al aplicar o limpiar un programa.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Despierta a la UI para refrescar catalogos y estados.
    ///
    /// [FLUJO ACURATEX]
    /// Perfil activo cambia -> `StateChanged` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de refresco cuando cambia la configuracion de trabajo.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no sabria cuando debe actualizar el programa visible.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para decidir si un nombre de archivo corresponde a un programa
    /// válido del sistema de cabezal.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los listados y el cargador de programas.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al inspeccionar nombres de archivos.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre de archivo y devuelve sistema y número.
    ///
    /// [SALIDAS]
    /// Devuelve `bool`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Solo parsea texto.
    ///
    /// [FLUJO ACURATEX]
    /// Archivo -> TryParseProgramFileName -> sistema + progX.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar un nombre de archivo en una tabla de configuración.
    ///
    /// [SI NO EXISTIERA]
    /// La app tendría que adivinar qué TXT pertenece a cabezal.
    /// </summary>
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para interpretar el nombre de un archivo como programa de cabezal.
    ///
    /// [QUIÉN LA USA]
    /// La usan listados, validaciones y la UI al mostrar programas.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al descubrir o seleccionar archivos `cbz.*.txt`.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre de archivo y devuelve sistema/número por parámetros de salida.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el nombre cumple el patrón esperado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Nombre de archivo -> `TryParseProgramFileName()` -> sistema/número.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a decodificar un identificador de programa en una etiqueta de almacenamiento.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que interpretar nombres de archivo por su cuenta.
    /// </summary>
    public bool TryParseProgramFileName(string fileName, out HeadSystemKind systemKind, out int programNumber)
    {
        systemKind = default;
        programNumber = 0;

        // [C#] `Path.GetFileName` elimina cualquier carpeta para quedarse solo con el nombre del archivo.
        // [ACURATEX] Asi la validacion trabaja solo sobre el TXT, no sobre una ruta completa.
        string cleanName = Path.GetFileName((fileName ?? string.Empty).Trim());
        // [ACURATEX] La expresion regular define el formato oficial de los programas.
        Match match = ProgramFileNameRegex.Match(cleanName);
        if (!match.Success) {
            return false;
        }

        if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out programNumber)
            || programNumber <= 0) {
            return false;
        }

        systemKind = match.Groups[1].Value.Equals("uni", StringComparison.OrdinalIgnoreCase)
            ? HeadSystemKind.Unified
            : HeadSystemKind.Modular;
        return true;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para listar los programas de cabezal compatibles con un sistema.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman la vista y los refrescos de perfil.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al abrir la lista de programas o refrescarla.
    ///
    /// [ENTRADAS]
    /// Recibe el sistema y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve una lista de `HeadProgramInfo`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Consulta el tester, inspecciona cada archivo y produce metadatos.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ListProgramsAsync -> files remotos -> inspección de programas.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un catálogo de programas desde memoria externa.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabría qué programas mostrar.
    /// </summary>
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para listar los programas compatibles con un sistema dado.
    ///
    /// [QUIÉN LA USA]
    /// La usan las vistas de selección de programas y la UI de refresco.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario abre o refresca la lista de programas.
    ///
    /// [ENTRADAS]
    /// Recibe el sistema y un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve la lista de programas con su estado y advertencias.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede consultar archivos remotos y actualizar el cache interno de activos.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ListProgramsAsync()` -> archivos/programas -> lista visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a enumerar presets almacenados en memoria externa.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla no podría mostrar qué programas están disponibles.
    /// </summary>
    public async Task<IReadOnlyList<HeadProgramInfo>> ListProgramsAsync(
        HeadSystemKind systemKind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // [ACURATEX] Se parte desde la lista real de archivos del tester.
        IReadOnlyList<string> fileNames = await _commandFiles.ListFilesAsync(cancellationToken).ConfigureAwait(false);
        // [C#] `List<T>` permite separar primero la seleccion de archivos y luego el orden final.
        List<ProgramCandidate> candidates = new();

        foreach (string fileName in fileNames) {
            // [ACURATEX] Solo los archivos que cumplen el patron oficial pueden entrar al catalogo.
            if (TryParseProgramFileName(fileName, out HeadSystemKind detectedKind, out int programNumber)
                && detectedKind == systemKind) {
                candidates.Add(new ProgramCandidate(fileName, detectedKind, programNumber));
            }
        }

        // [C#] El orden se fija por numero de programa y luego por nombre para que la UI sea estable.
        candidates = candidates
            .OrderBy(static candidate => candidate.ProgramNumber)
            .ThenBy(static candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // [ACURATEX] El perfil activo sirve para marcar visualmente cual programa ya quedo cargado.
        HeadProfile? activeProfile = GetActiveProfile(systemKind);
        List<HeadProgramInfo> result = new(candidates.Count);

        foreach (ProgramCandidate candidate in candidates) {
            // [ACURATEX] Cada archivo se inspecciona antes de mostrarse como disponible o invalido.
            ProgramInspection inspection = await InspectProgramAsync(candidate, cancellationToken).ConfigureAwait(false);
            bool isActive = activeProfile != null
                && string.Equals(activeProfile.FileName, candidate.FileName, StringComparison.OrdinalIgnoreCase);

            string displayName = $"Programa {candidate.ProgramNumber}";
            result.Add(new HeadProgramInfo(
                candidate.FileName,
                candidate.SystemKind,
                candidate.ProgramNumber,
                displayName,
                inspection.IsValid,
                inspection.Errors.Count > 0 ? string.Join(" | ", inspection.Errors) : null,
                inspection.Warnings,
                inspection.Profile?.ProfileName,
                inspection.Profile?.BindingsByKey.Count ?? 0,
                inspection.Profile?.Modules.Count ?? 0,
                isActive));
        }

        // [ACURATEX] La lista ya viene lista para pintar, sin que la UI tenga que recalcular estados.
        return result;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para validar un programa y dejarlo activo tanto en la app como
    /// en el firmware.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI cuando el usuario selecciona un programa.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al aplicar un programa nuevo.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, archivo y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de carga.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede mandar `HEAD_PROGRAM_SELECT|...` y actualizar el perfil activo.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ApplyProgramAsync -> archivo -> HEAD_PROGRAM_SELECT -> perfil activo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a seleccionar un programa de operación y validarlo antes de continuar.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podría activar perfiles.
    /// </summary>
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para aplicar un programa seleccionado y dejarlo activo.
    ///
    /// [QUIÉN LA USA]
    /// La usan las pantallas de selección de programa y los servicios de comandos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el operador elige un archivo de programa.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, nombre de archivo y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de carga y validación.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede actualizar el perfil activo, la caché local y el estado visual.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ApplyProgramAsync()` -> archivo -> perfil activo -> firmware/UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar un preset y dejarlo como configuración actual.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendría una forma central de activar un programa.
    /// </summary>
    public async Task<HeadProfileLoadResult> ApplyProgramAsync(
        HeadSystemKind systemKind,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // [ACURATEX] Primero se interpreta el nombre del archivo antes de hablar con el tester.
        if (!TryParseProgramFileName(fileName, out HeadSystemKind detectedKind, out int programNumber)) {
            return new HeadProfileLoadResult(
                false,
                "Nombre de programa invalido. Formato esperado: cbz.(uni|mod).progX.txt",
                null,
                new[] { "Nombre de programa invalido." },
                Array.Empty<string>());
        }

        // [ACURATEX] El archivo debe pertenecer al mismo sistema que la pantalla activa.
        if (detectedKind != systemKind) {
            return new HeadProfileLoadResult(
                false,
                $"El programa {Path.GetFileName(fileName)} no pertenece a {BuildSystemLabel(systemKind)}.",
                null,
                new[] { "Sistema del archivo no coincide con el sistema activo." },
                Array.Empty<string>());
        }

        // [ACURATEX] Se crea el candidato para inspeccionarlo con su nombre ya normalizado.
        ProgramCandidate candidate = new(Path.GetFileName(fileName), detectedKind, programNumber);
        // [ACURATEX] El archivo se descarga y se valida antes de considerarlo activo.
        ProgramInspection inspection = await InspectProgramAsync(candidate, cancellationToken).ConfigureAwait(false);
        if (!inspection.IsValid || inspection.Profile is null) {
            string message = inspection.Errors.Count > 0
                ? inspection.Errors[0]
                : "No se pudo cargar el programa.";

            return new HeadProfileLoadResult(false, message, null, inspection.Errors, inspection.Warnings);
        }

        try {
            // [ACURATEX] El firmware tambien debe confirmar que programa queda activo.
            bool firmwareSelected = await _commandFiles
                .SelectHeadProgramAsync(candidate.FileName, cancellationToken)
                .ConfigureAwait(false);

            if (!firmwareSelected) {
                return new HeadProfileLoadResult(
                    false,
                    $"El tester no acepto HEAD_PROGRAM_SELECT para {candidate.FileName}.",
                    null,
                    new[] { "No se pudo seleccionar el programa activo en el firmware." },
                    inspection.Warnings);
            }
        } catch (Exception ex) {
            // [ACURATEX] Si la seleccion remota falla, el perfil no puede darse por activo.
            string message = string.IsNullOrWhiteSpace(ex.Message)
                ? $"No se pudo seleccionar {candidate.FileName} en el firmware."
                : ex.Message;
            return new HeadProfileLoadResult(false, message, null, new[] { message }, inspection.Warnings);
        }

        // [ACURATEX] El perfil activo queda guardado para que la UI consulte el estado.
        lock (ActiveProfilesGate) {
            ActiveProfiles[systemKind] = inspection.Profile;
        }

        // [ACURATEX] Si no hay bindings configurados, el sistema avisa pero no inventa acciones.
        string suffix = inspection.Profile.BindingsByKey.Count == 0
            ? " Programa cargado sin acciones configuradas."
            : string.Empty;

        // [ACURATEX] Notifica a la UI que el catalogo activo cambio.
        StateChanged?.Invoke();
        return new HeadProfileLoadResult(
            true,
            $"Programa {inspection.Profile.ProgramNumber} aplicado en {BuildSystemLabel(systemKind)}.{suffix}",
            inspection.Profile,
            Array.Empty<string>(),
            inspection.Warnings);
    }

    // [ACURATEX] Devuelve el perfil activo actual.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para devolver el perfil activo actualmente.
    ///
    /// [QUIÉN LA USA]
    /// La usan servicios de comandos y pantallas de estado.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la UI necesita saber qué programa está cargado.
    ///
    /// [ENTRADAS]
    /// Recibe el sistema.
    ///
    /// [SALIDAS]
    /// Devuelve el perfil activo o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Sistema -> `GetActiveProfile()` -> perfil activo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer la configuración actualmente cargada en memoria.
    ///
    /// [SI NO EXISTIERA]
    /// Los comandos tendrían que preguntar a otra capa por el perfil actual.
    /// </summary>
    public HeadProfile? GetActiveProfile(HeadSystemKind systemKind)
    {
        // [ACURATEX] El acceso al diccionario va protegido para evitar lecturas y escrituras simultaneas.
        lock (ActiveProfilesGate) {
            return ActiveProfiles.TryGetValue(systemKind, out HeadProfile? profile)
                ? profile
                : null;
        }
    }

    // [ACURATEX] Indica si ya hay un programa de cabezal activo.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para saber si ya hay un perfil activo para un sistema.
    ///
    /// [QUIÉN LA USA]
    /// La usan comandos y pantallas antes de ejecutar acciones.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al validar si una acción de cabezal puede continuar.
    ///
    /// [ENTRADAS]
    /// Recibe el sistema.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Sistema -> `HasActiveProfile()` -> decisión de ejecución.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de programa cargado.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla tendría que comprobar el perfil completo.
    /// </summary>
    public bool HasActiveProfile(HeadSystemKind systemKind)
    {
        // [ACURATEX] Esta consulta rapida solo pregunta si ya quedo un perfil cargado.
        lock (ActiveProfilesGate) {
            return ActiveProfiles.ContainsKey(systemKind);
        }
    }

    // [ACURATEX] Devuelve el nombre de archivo del programa activo.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para recuperar el nombre de archivo del programa activo.
    ///
    /// [QUIÉN LA USA]
    /// La usan la UI y registros de trazabilidad.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando se quiere mostrar qué archivo quedó cargado.
    ///
    /// [ENTRADAS]
    /// Recibe el sistema.
    ///
    /// [SALIDAS]
    /// Devuelve el nombre de archivo activo o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Sistema -> `GetActiveProgramFileName()` -> nombre visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer qué preset quedó guardado como activo.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podría mostrar el nombre del programa activo.
    /// </summary>
    public string? GetActiveProgramFileName(HeadSystemKind systemKind)
    {
        // [ACURATEX] Devuelve solo el nombre del archivo para que la UI no dependa del objeto completo.
        return GetActiveProfile(systemKind)?.FileName;
    }

    // [ACURATEX] Limpia el perfil activo y notifica a la UI.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para limpiar el perfil activo de un sistema.
    ///
    /// [QUIÉN LA USA]
    /// La usan rutinas de cierre, errores o restablecimientos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el programa debe quedar sin perfil cargado.
    ///
    /// [ENTRADAS]
    /// Recibe el sistema.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Borra el perfil activo y notifica cambio de estado.
    ///
    /// [FLUJO ACURATEX]
    /// Sistema -> `ClearActiveProfile()` -> estado sin programa.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a borrar una selección activa de un registro de contexto.
    ///
    /// [SI NO EXISTIERA]
    /// El programa activo podría quedarse pegado tras un error.
    /// </summary>
    public void ClearActiveProfile(HeadSystemKind systemKind)
    {
        bool removed;
        lock (ActiveProfilesGate) {
            removed = ActiveProfiles.Remove(systemKind);
        }

        if (removed) {
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para resolver una acción exacta dentro del programa activo.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los servicios de comandos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la UI pide una acción concreta.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, instancia y acción.
    ///
    /// [SALIDAS]
    /// Devuelve la binding asociada o un error.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Solo consulta el perfil activo.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> Resolve -> binding exacta -> scripts o firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a buscar un canal exacto dentro de una tabla de mapeo.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que calcular la ruta de acción exacta.
    /// </summary>
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para resolver una binding exacta por instancia y acción.
    ///
    /// [QUIÉN LA USA]
    /// La usan los servicios de comandos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la UI pide una acción concreta del cabezal.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, instancia y nombre de acción.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de resolución y la binding encontrada o error.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Instancia/acción -> `Resolve()` -> binding/script.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a buscar una entrada exacta en una tabla de vectores.
    ///
    /// [SI NO EXISTIERA]
    /// Cada servicio tendría que resolver bindings por su cuenta.
    /// </summary>
    public HeadBindingResolveResult Resolve(
        HeadSystemKind systemKind,
        string instanceName,
        string actionName)
    {
        HeadProfile? profile = GetActiveProfile(systemKind);
        if (profile is null) {
            return new HeadBindingResolveResult(
                false,
                $"No hay programa de Cabezal activo para {BuildSystemLabel(systemKind)}.",
                null,
                null);
        }

        string cleanInstance = NormalizeToken(instanceName);
        string cleanAction = NormalizeToken(actionName);
        if (cleanInstance.Length == 0 || cleanAction.Length == 0) {
            return new HeadBindingResolveResult(
                false,
                "No se pudo resolver la accion solicitada.",
                null,
                profile);
        }

        string key = BuildBindingKey(cleanInstance, cleanAction);
        if (!profile.BindingsByKey.TryGetValue(key, out HeadButtonBinding? binding)) {
            return new HeadBindingResolveResult(
                false,
                $"No hay accion configurada para {cleanInstance}.{cleanAction} en el programa activo.",
                null,
                profile);
        }

        return new HeadBindingResolveResult(true, null, binding, profile);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para encontrar la acción más cercana a un valor numérico.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman sliders y controles que trabajan con valores continuos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la UI necesita mapear un valor a una binding.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, instancia y valor.
    ///
    /// [SALIDAS]
    /// Devuelve la binding más cercana o un error.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Solo consulta el perfil activo.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ResolveByNearestValue -> binding más cercana -> acción real.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a interpolar o elegir el escalón más cercano en una tabla.
    ///
    /// [SI NO EXISTIERA]
    /// Los sliders tendrían que saber la tabla exacta de valores.
    /// </summary>
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para resolver la binding más cercana por valor.
    ///
    /// [QUIÉN LA USA]
    /// La usan botones de posición y mapas de valores.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando la UI trabaja con posiciones numéricas.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, instancia y valor lógico.
    ///
    /// [SALIDAS]
    /// Devuelve la binding más cercana o un error.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Valor -> `ResolveByNearestValue()` -> binding adecuada.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a convertir una lectura analógica en una posición discreta.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que resolver ella misma las posiciones cercanas.
    /// </summary>
    public HeadBindingResolveResult ResolveByNearestValue(
        HeadSystemKind systemKind,
        string instanceName,
        int value)
    {
        HeadProfile? profile = GetActiveProfile(systemKind);
        if (profile is null) {
            return new HeadBindingResolveResult(
                false,
                $"No hay programa de Cabezal activo para {BuildSystemLabel(systemKind)}.",
                null,
                null);
        }

        string cleanInstance = NormalizeToken(instanceName);
        if (cleanInstance.Length == 0) {
            return new HeadBindingResolveResult(
                false,
                "No se pudo resolver la accion por valor.",
                null,
                profile);
        }

        HeadButtonBinding? nearest = profile.BindingsByKey.Values
            .Where(binding => string.Equals(binding.InstanceName, cleanInstance, StringComparison.OrdinalIgnoreCase))
            .Where(static binding => binding.Value.HasValue)
            .OrderBy(binding => Math.Abs(binding.Value!.Value - value))
            .ThenBy(static binding => binding.ActionName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (nearest is null) {
            return new HeadBindingResolveResult(
                false,
                $"No hay accion configurada por valor para {cleanInstance} en el programa activo.",
                null,
                profile);
        }

        return new HeadBindingResolveResult(true, null, nearest, profile);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para descargar un programa remoto y decidir si puede convertirse
    /// en perfil activo.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman `ListProgramsAsync()` y `ApplyProgramAsync()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta antes de mostrar o activar un programa.
    ///
    /// [ENTRADAS]
    /// Recibe el candidato de programa y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve una inspección con errores, advertencias y perfil parseado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Descarga el TXT remoto.
    ///
    /// [FLUJO ACURATEX]
    /// Archivo remoto -> InspectProgramAsync -> ParseProgramContent -> perfil.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una configuración desde EEPROM antes de aceptarla.
    ///
    /// [SI NO EXISTIERA]
    /// Habría que repetir la lectura del archivo en varios puntos.
    /// </summary>
    private async Task<ProgramInspection> InspectProgramAsync(ProgramCandidate candidate, CancellationToken cancellationToken)
    {
        CommandFileDownloadResult download = await _commandFiles
            .DownloadFileAsync(candidate.FileName, cancellationToken)
            .ConfigureAwait(false);

        if (!download.Success) {
            string error = string.IsNullOrWhiteSpace(download.ErrorMessage)
                ? $"No se pudo leer {candidate.FileName} desde el tester."
                : download.ErrorMessage!;
            return new ProgramInspection(false, null, new[] { error }, Array.Empty<string>());
        }

        return ParseProgramContent(candidate, download.Text);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para parsear el contenido del programa y construir perfil, módulos
    /// y bindings.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama el inspector de programas.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta después de descargar el TXT del tester.
    ///
    /// [ENTRADAS]
    /// Recibe el candidato y el texto del archivo.
    ///
    /// [SALIDAS]
    /// Devuelve inspección del programa.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Solo interpreta texto y llena colecciones locales.
    ///
    /// [FLUJO ACURATEX]
    /// Archivo TXT -> ParseProgramContent -> perfil + bindings.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a parsear un archivo de configuración de una máquina de estados.
    ///
    /// [SI NO EXISTIERA]
    /// No se podrían construir los perfiles desde texto.
    /// </summary>
    private static ProgramInspection ParseProgramContent(ProgramCandidate candidate, string text)
    {
        List<string> errors = new();
        List<string> warnings = new();
        List<HeadModuleDefinition> modules = new();
        Dictionary<string, HeadButtonBinding> bindings = new(StringComparer.OrdinalIgnoreCase);

        string? profileName = null;
        bool hasProfileName = false;
        string? initScript = null;
        HeadSystemKind? declaredSystem = null;
        int? declaredProgram = null;

        using StringReader reader = new(text ?? string.Empty);
        int lineNumber = 0;
        string? rawLine;

        while ((rawLine = reader.ReadLine()) != null) {
            lineNumber++;
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }

            if (line.StartsWith("PROFILE_NAME=", StringComparison.OrdinalIgnoreCase)) {
                string value = line["PROFILE_NAME=".Length..].Trim();
                if (value.Length > 0) {
                    profileName = value;
                    hasProfileName = true;
                }
                continue;
            }

            if (line.StartsWith("SYSTEM=", StringComparison.OrdinalIgnoreCase)) {
                string value = line["SYSTEM=".Length..].Trim();
                if (TryParseSystemToken(value, out HeadSystemKind parsedSystem)) {
                    declaredSystem = parsedSystem;
                } else {
                    AddValidationIssue(
                        $"Linea {lineNumber}: SYSTEM invalido ({value}). Se usara {BuildSystemToken(candidate.SystemKind)} inferido desde el nombre.",
                        errors,
                        warnings);
                }
                continue;
            }

            if (line.StartsWith("PROGRAM=", StringComparison.OrdinalIgnoreCase)) {
                string value = line["PROGRAM=".Length..].Trim();
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int programNumber)
                    && programNumber > 0) {
                    declaredProgram = programNumber;
                } else {
                    AddValidationIssue(
                        $"Linea {lineNumber}: PROGRAM invalido ({value}). Se usara PROGRAM={candidate.ProgramNumber} inferido desde el nombre.",
                        errors,
                        warnings);
                }
                continue;
            }

            if (line.StartsWith("INIT_SCRIPT=", StringComparison.OrdinalIgnoreCase)) {
                string script = Path.GetFileName(line["INIT_SCRIPT=".Length..].Trim());
                if (script.Length == 0) {
                    continue;
                }

                if (!IsValidScriptFileName(script)) {
                    AddValidationIssue($"Linea {lineNumber}: INIT_SCRIPT invalido ({script}).", errors, warnings);
                } else {
                    initScript = script;
                }
                continue;
            }

            if (line.StartsWith("MODULE|", StringComparison.OrdinalIgnoreCase)) {
                ParseModuleLine(line, lineNumber, modules, errors, warnings);
                continue;
            }

            if (line.StartsWith("BUTTON|", StringComparison.OrdinalIgnoreCase)) {
                ParseButtonLine(line, lineNumber, bindings, errors, warnings);
                continue;
            }

            if (line.StartsWith("BEGIN|", StringComparison.OrdinalIgnoreCase)) {
                ParseBeginLine(line, lineNumber, candidate.FileName, bindings, errors, warnings);
            }
        }

        ValidateRequiredMarkers(candidate, declaredSystem, declaredProgram, errors, warnings);
        if (!hasProfileName) {
            warnings.Add("No se encontro PROFILE_NAME. Se uso nombre automatico.");
        }

        if (modules.Count == 0) {
            warnings.Add("No se definieron modulos. Se usaran solo las acciones detectadas.");
        }

        if (bindings.Count == 0) {
            warnings.Add("No se encontraron BUTTON ni BEGIN de accion. El programa no tiene acciones configuradas.");
        }

        if (errors.Count > 0) {
            return new ProgramInspection(false, null, errors, warnings);
        }

        string finalProfileName = string.IsNullOrWhiteSpace(profileName)
            ? $"Programa {candidate.ProgramNumber} - {BuildSystemLabel(candidate.SystemKind)}"
            : profileName.Trim();
        IReadOnlyDictionary<string, HeadButtonBinding> readOnlyBindings =
            new Dictionary<string, HeadButtonBinding>(bindings, StringComparer.OrdinalIgnoreCase);

        HeadProfile profile = new(
            candidate.FileName,
            candidate.SystemKind,
            candidate.ProgramNumber,
            finalProfileName,
            initScript,
            modules.ToArray(),
            readOnlyBindings);

        return new ProgramInspection(true, profile, Array.Empty<string>(), warnings);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para interpretar una línea `MODULE|...` del perfil y convertirla
    /// en una definición de módulo.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ParseProgramContent()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta mientras se recorre el TXT del programa.
    ///
    /// [ENTRADAS]
    /// Recibe la línea cruda, el número de línea y las colecciones donde guardar resultado
    /// o errores.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede agregar un módulo o registrar un error/aviso.
    ///
    /// [FLUJO ACURATEX]
    /// TXT -> `ParseModuleLine()` -> `HeadModuleDefinition`.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una tabla de configuración de un periférico.
    ///
    /// [SI NO EXISTIERA]
    /// El parser no podría reconstruir los módulos del programa.
    /// </summary>
    private static void ParseModuleLine(
        string line,
        int lineNumber,
        ICollection<HeadModuleDefinition> modules,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        string[] parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 6) {
            AddValidationIssue($"Linea {lineNumber}: MODULE invalido.", errors, warnings);
            return;
        }

        string moduleName = NormalizeToken(parts[1]);
        if (moduleName.Length == 0) {
            AddValidationIssue($"Linea {lineNumber}: nombre de modulo invalido.", errors, warnings);
            return;
        }

        if (!string.Equals(parts[2], "COUNT", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int count)
            || count <= 0) {
            AddValidationIssue($"Linea {lineNumber}: COUNT invalido en MODULE.", errors, warnings);
            return;
        }

        if (!string.Equals(parts[4], "ACTIONS", StringComparison.OrdinalIgnoreCase)) {
            AddValidationIssue($"Linea {lineNumber}: falta ACTIONS en MODULE.", errors, warnings);
            return;
        }

        IReadOnlyList<string> actions = parts[5]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeToken)
            .Where(static action => action.Length > 0)
            .ToArray();

        modules.Add(new HeadModuleDefinition(moduleName, count, actions, line));
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para interpretar una línea `BUTTON|...` y convertirla en una
    /// binding de instancia/acción.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ParseProgramContent()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta durante el parseo del TXT del programa.
    ///
    /// [ENTRADAS]
    /// Recibe la línea, el número de línea y los contenedores de salida/error.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede agregar o reemplazar bindings y registrar validaciones.
    ///
    /// [FLUJO ACURATEX]
    /// TXT -> `ParseButtonLine()` -> binding instancia.accion -> script.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a fijar una tabla de vectores que asocia entrada con reacción.
    ///
    /// [SI NO EXISTIERA]
    /// Los botones configurados no se podrían traducir a scripts reales.
    /// </summary>
    private static void ParseButtonLine(
        string line,
        int lineNumber,
        IDictionary<string, HeadButtonBinding> bindings,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        string[] parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 4) {
            AddValidationIssue($"Linea {lineNumber}: BUTTON invalido.", errors, warnings);
            return;
        }

        string instanceName = NormalizeToken(parts[1]);
        string actionName = NormalizeToken(parts[2]);
        string scriptFileName = Path.GetFileName(parts[3].Trim());

        if (instanceName.Length == 0 || actionName.Length == 0) {
            AddValidationIssue($"Linea {lineNumber}: instancia/accion invalida en BUTTON.", errors, warnings);
            return;
        }

        if (!IsValidScriptFileName(scriptFileName)) {
            AddValidationIssue($"Linea {lineNumber}: script asociado invalido ({scriptFileName}).", errors, warnings);
            return;
        }

        int? value = null;
        foreach (string part in parts.Skip(4)) {
            if (!part.StartsWith("VALUE=", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            string valueText = part["VALUE=".Length..].Trim();
            if (int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)) {
                value = parsedValue;
            } else {
                AddValidationIssue($"Linea {lineNumber}: VALUE invalido ({valueText}).", errors, warnings);
            }
        }

        string key = BuildBindingKey(instanceName, actionName);
        if (bindings.ContainsKey(key)) {
            AddValidationIssue($"Linea {lineNumber}: binding duplicado para {instanceName}.{actionName}. Se usara la ultima definicion.", errors, warnings);
        }

        bindings[key] = new HeadButtonBinding(instanceName, actionName, scriptFileName, value, line);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para interpretar una línea `BEGIN|...` y convertirla en una
    /// binding inicial del programa.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ParseProgramContent()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el archivo trae una acción de arranque.
    ///
    /// [ENTRADAS]
    /// Recibe la línea, el número de línea, el nombre del programa y las colecciones de
    /// bindings y validación.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede registrar la acción inicial o marcar duplicados.
    ///
    /// [FLUJO ACURATEX]
    /// TXT -> `ParseBeginLine()` -> binding de inicio -> ejecución inicial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a fijar el vector de arranque de una rutina embebida.
    ///
    /// [SI NO EXISTIERA]
    /// El programa no tendría forma de declarar su acción inicial.
    /// </summary>
    private static void ParseBeginLine(
        string line,
        int lineNumber,
        string programFileName,
        IDictionary<string, HeadButtonBinding> bindings,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        string[] parts = line.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2) {
            AddValidationIssue($"Linea {lineNumber}: BEGIN invalido.", errors, warnings);
            return;
        }

        string logicalAction = NormalizeToken(parts[1]);
        if (logicalAction.Length == 0) {
            AddValidationIssue($"Linea {lineNumber}: accion invalida en BEGIN.", errors, warnings);
            return;
        }

        string instanceName;
        string actionName;
        int separatorIndex = logicalAction.IndexOf('.', StringComparison.Ordinal);
        if (separatorIndex > 0 && separatorIndex < logicalAction.Length - 1) {
            instanceName = NormalizeToken(logicalAction[..separatorIndex]);
            actionName = NormalizeToken(logicalAction[(separatorIndex + 1)..]);
        } else {
            instanceName = "HEAD";
            actionName = logicalAction;
        }

        int? value = null;
        foreach (string part in parts.Skip(2)) {
            if (!part.StartsWith("VALUE=", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            string valueText = part["VALUE=".Length..].Trim();
            if (int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)) {
                value = parsedValue;
            } else {
                AddValidationIssue($"Linea {lineNumber}: VALUE invalido ({valueText}).", errors, warnings);
            }
        }

        string key = BuildBindingKey(instanceName, actionName);
        if (bindings.ContainsKey(key)) {
            AddValidationIssue($"Linea {lineNumber}: accion duplicada para {instanceName}.{actionName}. Se usara la ultima definicion.", errors, warnings);
        }

        bindings[key] = new HeadButtonBinding(instanceName, actionName, programFileName, value, line);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para verificar que `SYSTEM` y `PROGRAM` coincidan con el nombre
    /// del archivo.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ParseProgramContent()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al final del parseo del TXT.
    ///
    /// [ENTRADAS]
    /// Recibe el candidato de archivo y lo que el TXT declaró explícitamente.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Agrega errores o advertencias según el modo de validación.
    ///
    /// [FLUJO ACURATEX]
    /// Nombre de archivo + cabeceras -> `ValidateRequiredMarkers()` -> warnings/errors.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar si la configuración en EEPROM coincide con la que dice la etiqueta.
    ///
    /// [SI NO EXISTIERA]
    /// El archivo podría declarar un sistema o programa diferente sin aviso.
    /// </summary>
    private static void ValidateRequiredMarkers(
        ProgramCandidate candidate,
        HeadSystemKind? declaredSystem,
        int? declaredProgram,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        if (!declaredSystem.HasValue) {
            AddValidationIssue(
                $"No se encontro SYSTEM. Se uso {BuildSystemToken(candidate.SystemKind)} inferido desde el nombre.",
                errors,
                warnings);
        } else if (declaredSystem.Value != candidate.SystemKind) {
            AddValidationIssue(
                $"SYSTEM={BuildSystemToken(declaredSystem.Value)} no coincide con el nombre {candidate.FileName}. Se usara {BuildSystemToken(candidate.SystemKind)} inferido desde el nombre.",
                errors,
                warnings);
        }

        if (!declaredProgram.HasValue) {
            AddValidationIssue(
                $"No se encontro PROGRAM. Se uso {candidate.ProgramNumber} inferido desde el nombre.",
                errors,
                warnings);
        } else if (declaredProgram.Value != candidate.ProgramNumber) {
            AddValidationIssue(
                $"PROGRAM={declaredProgram.Value} no coincide con prog{candidate.ProgramNumber}. Se usara PROGRAM={candidate.ProgramNumber} inferido desde el nombre.",
                errors,
                warnings);
        }
    }

    // [ACURATEX] Convierte tokens como UNI/MOD a enums internos.
    private static bool TryParseSystemToken(string value, out HeadSystemKind systemKind)
    {
        string cleanValue = NormalizeToken(value);
        if (cleanValue == "UNI" || cleanValue == "UNIFIED") {
            systemKind = HeadSystemKind.Unified;
            return true;
        }

        if (cleanValue == "MOD" || cleanValue == "MODULAR") {
            systemKind = HeadSystemKind.Modular;
            return true;
        }

        systemKind = default;
        return false;
    }

    // [ACURATEX] Normaliza texto para comparar nombres y acciones sin ruido de formato.
    private static string NormalizeToken(string value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    // [ACURATEX] Construye la clave de busqueda instancia.accion.
    private static string BuildBindingKey(string instanceName, string actionName)
    {
        return $"{NormalizeToken(instanceName)}.{NormalizeToken(actionName)}";
    }

    // [ACURATEX] Valida nombres de scripts que el perfil puede referenciar.
    private static bool IsValidScriptFileName(string fileName)
    {
        string cleanName = (fileName ?? string.Empty).Trim();
        if (cleanName.Length == 0 || cleanName.Length > MaxFileNameLength) {
            return false;
        }

        if (!cleanName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (cleanName.Contains('/')
            || cleanName.Contains('\\')
            || cleanName.Contains('|')
            || cleanName.Contains("..", StringComparison.Ordinal)
            || cleanName.Contains('\r')
            || cleanName.Contains('\n')) {
            return false;
        }

        if (string.Equals(cleanName, ".selected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(cleanName, ".upload.tmp", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return true;
    }

    // [ACURATEX] Devuelve una etiqueta humana del sistema.
    private static string BuildSystemLabel(HeadSystemKind systemKind)
    {
        return systemKind == HeadSystemKind.Unified
            ? "Sistema Unificado"
            : "Sistema Modular";
    }

    // [ACURATEX] Devuelve el token corto UNI/MOD.
    private static string BuildSystemToken(HeadSystemKind systemKind)
    {
        return systemKind == HeadSystemKind.Unified ? "UNI" : "MOD";
    }

    // [ACURATEX] Convierte validaciones en errores o avisos segun el modo.
    private static void AddValidationIssue(
        string message,
        ICollection<string> errors,
        ICollection<string> warnings)
    {
        if (ValidationMode == HeadProfileValidationMode.Strict) {
            errors.Add(message);
            return;
        }

        warnings.Add(message);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este record existe para transportar el nombre, sistema y numero de un archivo candidato a programa.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan los metodos de listado e inspeccion de programas.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa mientras el servicio filtra archivos remotos.
    ///
    /// [ENTRADAS]
    /// Recibe nombre de archivo, sistema y numero de programa.
    ///
    /// [SALIDAS]
    /// Devuelve una tupla inmutable de datos del candidato.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Lista de archivos -> `ProgramCandidate` -> inspeccion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una entrada de tabla que identifica un preset disponible.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio tendria que pasar tres variables sueltas por todas partes.
    /// </summary>
    private sealed record ProgramCandidate(string FileName, HeadSystemKind SystemKind, int ProgramNumber);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este record existe para reunir el resultado final de inspeccionar un programa.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan `ListProgramsAsync()` y `ApplyProgramAsync()`.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa despues de parsear un archivo TXT de cabezal.
    ///
    /// [ENTRADAS]
    /// Recibe si es valido, el perfil construido y los errores/avisos.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado completo de inspeccion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// TXT -> `ProgramInspection` -> UI/activacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bloque de diagnostico que resume validacion y datos.
    ///
    /// [SI NO EXISTIERA]
    /// La inspeccion tendria que devolver varios valores sueltos.
    /// </summary>
    private sealed record ProgramInspection(
        bool IsValid,
        HeadProfile? Profile,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings);
}
