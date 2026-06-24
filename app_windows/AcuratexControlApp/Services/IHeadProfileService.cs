// Servicio de perfiles de cabezal: parsea programas, resuelve bindings y expone el estado activo.
namespace AcuratexControlApp.Services;

/// <summary>
/// [POR QUÉ EXISTE]
/// Este enum existe para distinguir si un perfil pertenece al sistema unificado o al modular.
///
/// [QUIÉN LO USA]
/// Lo usan el servicio de perfiles, la UI y la logica de arranque.
///
/// [CUÁNDO SE USA]
/// Se usa al leer archivos y al resolver perfiles activos.
///
/// [ENTRADAS]
/// No recibe entradas.
///
/// [SALIDAS]
/// Devuelve el tipo de sistema asociado al perfil.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Archivo de perfil -> `HeadSystemKind` -> lógica de selección.
///
/// [EQUIVALENCIA MCU]
/// Se parece a elegir entre dos familias de firmware.
///
/// [SI NO EXISTIERA]
/// La app no podria separar los perfiles por sistema.
/// </summary>
public enum HeadSystemKind
{
    Unified = 1,
    Modular = 2
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Este enum existe para decidir si la validacion del perfil debe ser flexible o estricta.
///
/// [QUIÉN LO USA]
/// Lo usa el servicio de perfiles al parsear archivos.
///
/// [CUÁNDO SE USA]
/// Se usa cuando la app valida un perfil contra reglas de formato.
///
/// [ENTRADAS]
/// No recibe entradas.
///
/// [SALIDAS]
/// Devuelve el modo de validacion.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Parser -> `HeadProfileValidationMode` -> validacion.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un modo de chequeo que tolera o no tolera diferencias de formato.
///
/// [SI NO EXISTIERA]
/// La validacion no podria ajustar su rigidez.
/// </summary>
public enum HeadProfileValidationMode
{
    Flexible = 1,
    Strict = 2
}

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para describir un modulo fisico o logico del cabezal dentro del perfil.
///
/// [QUIÉN LO USA]
/// Lo usa el servicio de perfiles y la UI de diagnostico.
///
/// [CUÁNDO SE USA]
/// Se usa al cargar y mostrar la estructura del perfil.
///
/// [ENTRADAS]
/// Recibe nombre, cantidad de instancias, acciones y la linea original.
///
/// [SALIDAS]
/// Devuelve un bloque de datos inmutable.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Archivo -> `HeadModuleDefinition` -> perfil.
///
/// [EQUIVALENCIA MCU]
/// Se parece a describir un bloque de periféricos o canales del cabezal.
///
/// [SI NO EXISTIERA]
/// El perfil perderia la descripcion estructurada de sus modulos.
/// </summary>
public sealed record HeadModuleDefinition(
    string Name,
    int Count,
    IReadOnlyList<string> Actions,
    string RawLine);

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para guardar la asociacion entre una instancia y la accion que la activa.
///
/// [QUIÉN LO USA]
/// Lo usan el servicio de perfiles y los servicios de comandos.
///
/// [CUÁNDO SE USA]
/// Se usa al resolver que script o accion corresponde a una combinacion.
///
/// [ENTRADAS]
/// Recibe instancia, accion, archivo de script, valor y linea original.
///
/// [SALIDAS]
/// Devuelve un binding listo para usar.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Perfil -> `HeadButtonBinding` -> comando/script.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla que mapea boton y funcion de firmware.
///
/// [SI NO EXISTIERA]
/// No se podria saber que accion corresponde a cada boton.
/// </summary>
public sealed record HeadButtonBinding(
    string InstanceName,
    string ActionName,
    string ScriptFileName,
    int? Value,
    string RawLine);

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para representar un perfil de cabezal ya interpretado y listo para usar.
///
/// [QUIÉN LO USA]
/// Lo usa el servicio de perfiles y la UI.
///
/// [CUÁNDO SE USA]
/// Se usa al activar o consultar un programa.
///
/// [ENTRADAS]
/// Recibe nombre de archivo, sistema, numero de programa, nombre de perfil, script init, modulos y bindings.
///
/// [SALIDAS]
/// Devuelve el perfil activo.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Archivo -> `HeadProfile` -> estado activo.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un bloque de configuracion completo de una maquina de estados.
///
/// [SI NO EXISTIERA]
/// La app tendria que mantener los datos del perfil repartidos en varias estructuras.
/// </summary>
public sealed record HeadProfile(
    string FileName,
    HeadSystemKind SystemKind,
    int ProgramNumber,
    string ProfileName,
    string? InitScript,
    IReadOnlyList<HeadModuleDefinition> Modules,
    IReadOnlyDictionary<string, HeadButtonBinding> BindingsByKey);

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para resumir la informacion visible de un programa disponible.
///
/// [QUIÉN LO USA]
/// Lo usan la pantalla de programas y el selector de perfiles.
///
/// [CUÁNDO SE USA]
/// Se usa al listar programas compatibles y al mostrar estado.
///
/// [ENTRADAS]
/// Recibe datos de archivo, validacion y conteo.
///
/// [SALIDAS]
/// Devuelve una tarjeta de informacion para la UI.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Perfil -> `HeadProgramInfo` -> lista visual.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una ficha de programa en un menu de arranque.
///
/// [SI NO EXISTIERA]
/// La UI no tendria una forma compacta de mostrar cada programa.
/// </summary>
public sealed record HeadProgramInfo(
    string FileName,
    HeadSystemKind SystemKind,
    int ProgramNumber,
    string DisplayName,
    bool IsValid,
    string? ValidationError,
    IReadOnlyList<string> Warnings,
    string? ProfileName,
    int BindingCount,
    int ModuleCount,
    bool IsActive);

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para devolver el resultado completo de cargar un perfil.
///
/// [QUIÉN LO USA]
/// Lo usan la UI y la logica de activacion.
///
/// [CUÁNDO SE USA]
/// Se usa al intentar aplicar un programa.
///
/// [ENTRADAS]
/// Recibe exito, mensaje, perfil, errores y warnings.
///
/// [SALIDAS]
/// Devuelve el resultado de carga.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Carga -> `HeadProfileLoadResult` -> UI/servicio.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un resultado de inicializacion con diagnostico.
///
/// [SI NO EXISTIERA]
/// La app no podria reportar claramente si la carga fue correcta.
/// </summary>
public sealed record HeadProfileLoadResult(
    bool Success,
    string Message,
    HeadProfile? Profile,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para devolver el resultado de resolver un binding concreto.
///
/// [QUIÉN LO USA]
/// Lo usan el servicio de comandos y la UI del cabezal.
///
/// [CUÁNDO SE USA]
/// Se usa cuando una accion necesita traducirse a script o valor.
///
/// [ENTRADAS]
/// Recibe exito, mensaje de error, binding y perfil.
///
/// [SALIDAS]
/// Devuelve el resultado de resolucion.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Búsqueda -> `HeadBindingResolveResult` -> comando/script.
///
/// [EQUIVALENCIA MCU]
/// Se parece a traducir un boton a una accion interna del firmware.
///
/// [SI NO EXISTIERA]
/// No quedaria claro si la resolucion de la accion fue exitosa.
/// </summary>
public sealed record HeadBindingResolveResult(
    bool Success,
    string? ErrorMessage,
    HeadButtonBinding? Binding,
    HeadProfile? Profile);

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta interfaz existe para centralizar la lectura, activacion y resolucion de perfiles
/// de cabezal sin que la UI tenga que conocer el formato de los archivos.
///
/// [QUIÉN LA USA]
/// La usan las vistas de cabezal, los servicios de comandos y la logica de arranque.
///
/// [CUÁNDO SE USA]
/// Se usa al listar programas, activar uno y resolver acciones asociadas.
///
/// [ENTRADAS]
/// Recibe nombres de archivo, sistema, instancia, accion y valores logicos.
///
/// [SALIDAS]
/// Devuelve informacion de programas, perfiles y resoluciones de binding.
///
/// [EFECTOS SECUNDARIOS]
/// Puede activar perfiles, limpiar el activo y notificar a la UI.
///
/// [FLUJO ACURATEX]
/// UI o servicio -> `IHeadProfileService` -> perfil activo -> scripts/firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla de configuracion que decide que rutina disparar para cada boton.
///
/// [SI NO EXISTIERA]
/// Cada componente tendria que parsear archivos y resolver acciones por su cuenta.
/// </summary>
public interface IHeadProfileService
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este evento existe para avisar que el perfil activo cambio y la UI debe refrescarse.
    ///
    /// [QUIÉN LO USA]
    /// Lo suscriben vistas Razor y formularios que muestran estado del cabezal.
    ///
    /// [CUÁNDO SE USA]
    /// Se dispara al activar, limpiar o restablecer un perfil.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Provoca repintado de la UI.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> `StateChanged` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupcion de refresco cuando cambia la configuracion.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla no sabria que el perfil activo cambio.
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para interpretar nombres de archivo como `cbz.uni.prog1.txt`.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan el cargador de perfiles y las pantallas de diagnostico.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al listar o validar archivos de programa.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre de archivo y devuelve sistema y numero de programa.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el nombre tiene el formato esperado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Nombre de archivo -> `TryParseProgramFileName()` -> metadata.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a decodificar el nombre de una tabla de configuracion en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// La app tendria que parsear ese formato en muchos sitios.
    /// </summary>
    bool TryParseProgramFileName(
        string fileName,
        out HeadSystemKind systemKind,
        out int programNumber);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para listar todos los programas compatibles con un sistema.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa la UI para llenar listas y menus.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando la pantalla necesita mostrar programas disponibles.
    ///
    /// [ENTRADAS]
    /// Recibe el sistema y un token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve una lista inmutable de informacion de programas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede leer archivos del perfil en disco.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ListProgramsAsync()` -> archivos -> lista.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a recorrer una memoria de configuraciones disponibles.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla no podria enumerar los programas disponibles.
    /// </summary>
    Task<IReadOnlyList<HeadProgramInfo>> ListProgramsAsync(
        HeadSystemKind systemKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para activar un perfil concreto y dejarlo como el actual.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la UI y la logica de arranque al elegir programa.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el operador aplica un programa.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, archivo y token de cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de carga.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede cambiar el perfil activo y notificar a la UI.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ApplyProgramAsync()` -> perfil activo -> scripts.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar una configuracion activa en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una operacion clara para activar el programa seleccionado.
    /// </summary>
    Task<HeadProfileLoadResult> ApplyProgramAsync(
        HeadSystemKind systemKind,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para recuperar el perfil activo de un sistema.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa la UI cuando necesita pintar el programa actual.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al refrescar la vista o al ejecutar un comando dependiente del perfil.
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
    /// Sistema -> `GetActiveProfile()` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer la configuracion activa de una maquina.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no podria saber qué perfil esta en uso.
    /// </summary>
    HeadProfile? GetActiveProfile(HeadSystemKind systemKind);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para consultar rapidamente si hay un perfil activo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa la UI para habilitar o deshabilitar acciones.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al renderizar o antes de enviar comandos.
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
    /// Sistema -> `HasActiveProfile()` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un flag de programa cargado.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que examinar el perfil completo para saber si existe.
    /// </summary>
    bool HasActiveProfile(HeadSystemKind systemKind);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para devolver el nombre del archivo activo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan vistas de estado y diagnostico.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al mostrar el programa actual.
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
    /// Sistema -> `GetActiveProgramFileName()` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer el nombre de la configuracion cargada.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria mostrar que archivo quedo activo.
    /// </summary>
    string? GetActiveProgramFileName(HeadSystemKind systemKind);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para limpiar el perfil activo de un sistema.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la UI y los flujos de reinicio.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando el usuario quiere quitar el programa cargado.
    ///
    /// [ENTRADAS]
    /// Recibe el sistema.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Borra el perfil activo y dispara actualizacion.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ClearActiveProfile()` -> estado limpio.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a borrar la seleccion activa de una maquina de estados.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria soltar un perfil cargado sin reiniciar la app.
    /// </summary>
    void ClearActiveProfile(HeadSystemKind systemKind);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para resolver una binding exacta por instancia y accion.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan los servicios de comandos del cabezal.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta al pulsar un boton o al traducir una accion a script.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, instancia y accion.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de resolucion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Resolve()` -> binding -> script/comando.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a buscar una entrada de tabla por nombre exacto.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podria traducir una combinacion exacta a una accion.
    /// </summary>
    HeadBindingResolveResult Resolve(
        HeadSystemKind systemKind,
        string instanceName,
        string actionName);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este metodo existe para resolver la binding mas cercana cuando la UI solo tiene un valor.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan rutas de busqueda y selección aproximada.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando la accion se busca por valor y no por clave exacta.
    ///
    /// [ENTRADAS]
    /// Recibe sistema, instancia y valor.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado de resolucion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Valor -> `ResolveByNearestValue()` -> binding mas cercana.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a buscar el canal o accion más cercana por numero.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria resolver una accion cuando la UI solo conoce el valor.
    /// </summary>
    HeadBindingResolveResult ResolveByNearestValue(
        HeadSystemKind systemKind,
        string instanceName,
        int value);
}
