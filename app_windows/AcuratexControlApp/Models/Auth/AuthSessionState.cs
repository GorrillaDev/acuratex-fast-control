// [ACURATEX] Esta clase guarda una foto de la sesión autenticada actual.
// [C#] El patrón `init` permite construir el objeto y luego dejar sus campos inmutables.
namespace AcuratexControlApp.Models.Auth;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para representar la sesión activa como una fotografía de estado.
///
/// [QUIÉN LA USA]
/// La usan los servicios de autenticación, permisos y auditoría.
///
/// [CUÁNDO SE USA]
/// Se usa después de iniciar sesión y cada vez que la UI consulta el contexto actual.
///
/// [ENTRADAS]
/// Recibe usuario, rol, identificador de sesión y hora de login.
///
/// [SALIDAS]
/// Devuelve un objeto de solo lectura por construcción.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene; solo expone datos del contexto.
///
/// [FLUJO ACURATEX]
/// Login -> `AuthSessionState` -> permisos, vistas y auditoría.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un snapshot de registros de contexto que no debería cambiar solo.
///
/// [SI NO EXISTIERA]
/// La sesión tendría que repartirse en varias variables sueltas.
/// </summary>
public sealed class AuthSessionState
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para decir si la sesión está activa o vacía.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la UI, permisos y navegación.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cada vez que la app decide mostrar acciones protegidas.
    ///
    /// [ENTRADAS]
    /// Recibe un estado lógico.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login/logout -> `IsAuthenticated` -> visibilidad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de "operador presente".
    ///
    /// [SI NO EXISTIERA]
    /// Habría que adivinar si existe una sesión válida.
    /// </summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar el usuario actual de la sesión.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan permisos, perfiles y pantallas que necesitan identidad.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al leer el operador autenticado.
    ///
    /// [ENTRADAS]
    /// Recibe un `AppUser` o puede quedar vacío.
    ///
    /// [SALIDAS]
    /// Devuelve el usuario actual o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `CurrentUser` -> UI y permisos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un registro de operador activo en RAM.
    ///
    /// [SI NO EXISTIERA]
    /// La sesión no tendría un usuario concreto asociado.
    /// </summary>
    public AppUser? CurrentUser { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar el rol efectivo que manda sobre los permisos.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la UI y el servicio de permisos.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando hay que decidir qué botones mostrar o bloquear.
    ///
    /// [ENTRADAS]
    /// Recibe una definición de rol.
    ///
    /// [SALIDAS]
    /// Devuelve el rol activo o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `CurrentRole` -> permisos efectivos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nivel de privilegio que se carga junto al usuario.
    ///
    /// [SI NO EXISTIERA]
    /// La app tendría que volver a resolver el rol por ID todo el tiempo.
    /// </summary>
    public RoleDefinition? CurrentRole { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para identificar la sesión de forma única.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan auditoría, trazabilidad y pantallas de diagnóstico.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al registrar o comparar una sesión concreta.
    ///
    /// [ENTRADAS]
    /// Recibe un texto de sesión.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador de sesión.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `SessionId` -> auditoría.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al número de transacción de una comunicación serial.
    ///
    /// [SI NO EXISTIERA]
    /// No habría forma fácil de rastrear una sesión concreta.
    /// </summary>
    public string SessionId { get; init; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar la hora en que comenzó la sesión.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan indicadores de tiempo de sesión y auditoría.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al mostrar cuánto tiempo lleva conectado el operador.
    ///
    /// [ENTRADAS]
    /// Recibe una fecha y hora.
    ///
    /// [SALIDAS]
    /// Devuelve la marca temporal de login.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `LoginTime` -> duración de sesión.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a registrar el instante de arranque de un sistema embebido.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podría calcular duración de sesión.
    /// </summary>
    public DateTime LoginTime { get; init; }
}
