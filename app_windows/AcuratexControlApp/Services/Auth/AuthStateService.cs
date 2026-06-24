// [FLUJO] LoginForm o LoginView -> AuthStateService -> cambio de sesiÃ³n -> repintado de pantallas.
using AcuratexControlApp.Models.Auth;

namespace AcuratexControlApp.Services.Auth;
// [FLUJO] LoginView/LoginForm -> AuthStateService -> permisos -> repintado de pantallas.
// [EQUIV MCU] Se parece a una RAM de contexto donde queda el operador activo.
/// <summary>
/// [POR QUÃ‰ EXISTE]
/// Esta clase existe para centralizar el usuario autenticado, su rol y las credenciales demo.
///
/// [QUIÃ‰N LA LLAMA]
/// La llaman la pantalla de login, los paneles principales y servicios de permisos.
///
/// [CUÃNDO SE EJECUTA]
/// Sus mÃ©todos se usan al iniciar sesiÃ³n, cerrar sesiÃ³n o consultar el usuario actual.
///
/// [ENTRADAS]
/// Recibe credenciales de demo o un objeto `AppUser`.
///
/// [SALIDAS]
/// Devuelve Ã©xito o fallo de login, colecciones de permisos y usuarios demo.
///
/// [EFECTOS SECUNDARIOS]
/// Actualiza la sesiÃ³n interna y dispara `StateChanged`.
///
/// [FLUJO ACURATEX]
/// UI -> autenticaciÃ³n -> sesiÃ³n -> permisos -> visibilidad de acciones.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una mÃ¡quina de estados que pasa de `sin sesiÃ³n` a `operador activo`.
///
/// [SI NO EXISTIERA]
/// Cada componente tendrÃ­a que guardar su propio usuario y se perderÃ­a coherencia.
/// </summary>
public sealed class AuthStateService
{
    private readonly object _gate = new();
    private readonly RoleService _roleService;
    private readonly IReadOnlyDictionary<string, DemoUser> _demoUsers;
    private AuthSessionState _session = new();

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// El constructor existe para inyectar el servicio de roles y preparar las cuentas demo.
    ///
    /// [QUIÃ‰N LO LLAMA]
    /// Lo llama el contenedor de dependencias o el cÃ³digo de arranque.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta una sola vez por instancia del servicio.
    ///
    /// [ENTRADAS]
    /// Recibe el servicio de roles.
    ///
    /// [SALIDAS]
    /// Devuelve el objeto ya preparado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Construye el diccionario de usuarios demo.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `AuthStateService` -> carga de usuarios demo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Es parecido a inicializar una tabla de usuario/rol en RAM al arrancar.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio no sabrÃ­a quÃ© roles existen ni quÃ© usuarios demo aceptar.
    /// </summary>
    public AuthStateService(RoleService roleService)
    {
        _roleService = roleService;
        _demoUsers = BuildDemoUsers();
    }
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este evento existe para avisar que la sesiÃ³n cambiÃ³ y que la UI debe repintarse.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan formularios, componentes Razor y servicios que dependen del usuario actual.
    ///
    /// [CUÃNDO SE USA]
    /// Se dispara despuÃ©s de login, logout o reemplazo del usuario activo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Despierta a los suscriptores que estÃ¡n esperando cambios de sesiÃ³n.
    ///
    /// [FLUJO ACURATEX]
    /// Estado de sesiÃ³n -> `StateChanged` -> repintado de UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una interrupciÃ³n de "cambio de operador presente".
    ///
    /// [SI NO EXISTIERA]
    /// Las pantallas no sabrÃ­an cuÃ¡ndo refrescar lo que muestran.
    /// </summary>
    public event Action? StateChanged;
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta propiedad existe para exponer el usuario activo sin permitir que la UI lo cambie directamente.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan permisos, pantallas de encabezado y auditorÃ­a.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta cada vez que la app necesita saber quiÃ©n estÃ¡ conectado.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el `AppUser` actual o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Solo lee la sesiÃ³n bajo candado.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> `CurrentUser` -> permisos y UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer el registro del operador activo en una memoria de contexto.
    ///
    /// [SI NO EXISTIERA]
    /// Cada consumidor tendrÃ­a que acceder a la sesiÃ³n interna por otro camino.
    /// </summary>
    public AppUser? CurrentUser
    {
        get
        {
            lock (_gate) {
                return _session.CurrentUser;
            }
        }
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta propiedad existe para exponer el rol efectivo del usuario autenticado.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan permisos y pantallas de administraciÃ³n.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta cuando la UI decide quÃ© acciones mostrar.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la definiciÃ³n del rol activo o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Solo lee la sesiÃ³n bajo candado.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> `CurrentRole` -> reglas de acceso.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nivel de privilegio que viaja junto al usuario.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio de permisos tendrÃ­a que resolver el rol por ID todo el tiempo.
    /// </summary>
    public RoleDefinition? CurrentRole
    {
        get
        {
            lock (_gate) {
                return _session.CurrentRole;
            }
        }
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta propiedad existe para responder rÃ¡pido si hay una sesiÃ³n autenticada activa.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan menÃºs, botones y validaciones de seguridad.
    ///
    /// [CUÃNDO SE USA]
    /// Se consulta antes de habilitar acciones protegidas.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` cuando la sesiÃ³n estÃ¡ activa.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Solo lee el estado de sesiÃ³n.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> `IsAuthenticated` -> visibilidad y permisos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de "operador presente".
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendrÃ­a que inspeccionar el objeto de sesiÃ³n completo para saber si hay login.
    /// </summary>
    public bool IsAuthenticated
    {
        get
        {
            // [EQUIV MCU] Esta bandera funciona como un bit de "operador presente".
            lock (_gate) {
                return _session.IsAuthenticated;
            }
        }
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para validar credenciales demo y convertirlas en una sesiÃ³n activa.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llama la pantalla de login.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario presiona entrar.
    ///
    /// [ENTRADAS]
    /// Recibe usuario, contraseÃ±a y un mensaje de error por salida.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el login fue vÃ¡lido.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede cambiar la sesiÃ³n actual y disparar `StateChanged`.
    ///
    /// [FLUJO ACURATEX]
    /// LoginView -> `Login()` -> sesiÃ³n -> permisos -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comparar un PIN contra una tabla de acceso.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no tendrÃ­a una forma central de autenticar al operador.
    /// </summary>
    public bool Login(string username, string password, out string errorMessage)
    {
        errorMessage = string.Empty;
        string normalizedUsername = (username ?? string.Empty).Trim();
        string normalizedPassword = password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(normalizedPassword)) {
            errorMessage = "Ingresa usuario y contrasena.";
            return false;
        }

        if (!_demoUsers.TryGetValue(normalizedUsername, out DemoUser? demoUser)
            || !string.Equals(demoUser.Password, normalizedPassword, StringComparison.Ordinal)) {
            errorMessage = "Usuario o contrasena invalidos para la demo.";
            return false;
        }

        SetCurrentUser(CreateAppUserFromDemo(demoUser));
        return true;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para limpiar la sesiÃ³n activa y volver al estado de no autenticado.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman botones de cierre de sesiÃ³n o rutas de salida.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando el operador decide cerrar sesiÃ³n.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Borra la sesiÃ³n interna y notifica a la UI.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `Logout()` -> sesiÃ³n vacÃ­a -> repintado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a borrar un registro de contexto de usuario.
    ///
    /// [SI NO EXISTIERA]
    /// No habrÃ­a una salida estÃ¡ndar de la sesiÃ³n demo.
    /// </summary>
    public void Logout()
    {
        lock (_gate) {
            _session = new AuthSessionState
            {
                IsAuthenticated = false,
                CurrentUser = null,
                CurrentRole = null,
                SessionId = string.Empty,
                LoginTime = default
            };
        }

        NotifyStateChanged();
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para instalar un usuario ya validado como sesiÃ³n activa.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llama `Login()` y cualquier ruta interna que quiera forzar una sesiÃ³n.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando ya existe una cuenta vÃ¡lida y hay que publicar su contexto.
    ///
    /// [ENTRADAS]
    /// Recibe un `AppUser` ya construido.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Actualiza la sesiÃ³n interna, marca hora de login y dispara `StateChanged`.
    ///
    /// [FLUJO ACURATEX]
    /// Login o seed -> `SetCurrentUser()` -> sesiÃ³n activa -> permisos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar un contexto de operaciÃ³n en memoria.
    ///
    /// [SI NO EXISTIERA]
    /// Login y sesiÃ³n quedarÃ­an mezclados dentro de una sola funciÃ³n.
    /// </summary>
    public void SetCurrentUser(AppUser user)
    {
        if (user == null) {
            throw new ArgumentNullException(nameof(user));
        }

        RoleDefinition role = _roleService.GetRoleById(user.RoleId)
            ?? throw new InvalidOperationException($"Rol no registrado: {user.RoleId}");

        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedBy = user.Username;

        lock (_gate) {
            _session = new AuthSessionState
            {
                IsAuthenticated = true,
                CurrentUser = user,
                CurrentRole = role,
                SessionId = Guid.NewGuid().ToString("N"),
                LoginTime = DateTime.UtcNow
            };
        }

        NotifyStateChanged();
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para exponer los permisos del rol activo sin devolver el objeto rol.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman `PermissionService` y pantallas que solo necesitan permisos.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al validar si una acciÃ³n estÃ¡ permitida.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la colecciÃ³n de permisos actual o una colecciÃ³n vacÃ­a.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> permisos -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una mÃ¡scara de bits de habilitaciÃ³n.
    ///
    /// [SI NO EXISTIERA]
    /// Cada consumidor tendrÃ­a que inspeccionar el rol completo.
    /// </summary>
    public IReadOnlyCollection<string> GetCurrentPermissions()
    {
        lock (_gate) {
            return _session.CurrentRole?.Permissions ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para exponer las cuentas demo en un orden estable.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usa la pantalla de login cuando muestra usuarios de ejemplo.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando la UI necesita listar las credenciales disponibles.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una lista ordenada de usuarios demo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// AuthStateService -> demo users -> login UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a mostrar una tabla fija de cuentas de prueba en ROM.
    ///
    /// [SI NO EXISTIERA]
    /// La vista tendrÃ­a que construir su propio catÃ¡logo de usuarios demo.
    /// </summary>
    public IReadOnlyCollection<DemoUser> GetDemoUsers()
    {
        return _demoUsers.Values
            .OrderBy(static user => user.Username, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para construir el catÃ¡logo inicial de usuarios demo.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llama el constructor del servicio.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta una vez al crear el servicio.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve un diccionario indexado por usuario.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene; solo construye datos.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `BuildDemoUsers()` -> diccionario demo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a precargar credenciales de fÃ¡brica en memoria.
    ///
    /// [SI NO EXISTIERA]
    /// Las cuentas demo tendrÃ­an que definirse en varios puntos.
    /// </summary>
    private static IReadOnlyDictionary<string, DemoUser> BuildDemoUsers()
    {
        DemoUser[] users =
        {
            new DemoUser
            {
                Username = "root",
                Password = "root",
                DisplayName = "Super Root",
                RoleId = AppRoleIds.SuperRoot
            },
            new DemoUser
            {
                Username = "admin",
                Password = "admin",
                DisplayName = "Admin Tecnico",
                RoleId = AppRoleIds.AdminTecnico
            },
            new DemoUser
            {
                Username = "tecnico",
                Password = "tecnico",
                DisplayName = "Tecnico",
                RoleId = AppRoleIds.Tecnico
            }
        };

        return users.ToDictionary(static user => user.Username, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para transformar una credencial demo en el tipo de usuario usado por la sesiÃ³n.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llama `Login()` tras validar la contraseÃ±a.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando la autenticaciÃ³n demo fue correcta.
    ///
    /// [ENTRADAS]
    /// Recibe el usuario demo validado.
    ///
    /// [SALIDAS]
    /// Devuelve un `AppUser` listo para la sesiÃ³n.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene; solo construye un objeto nuevo.
    ///
    /// [FLUJO ACURATEX]
    /// DemoUser -> `CreateAppUserFromDemo()` -> AppUser -> sesiÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a copiar una configuraciÃ³n de fÃ¡brica a un registro de ejecuciÃ³n.
    ///
    /// [SI NO EXISTIERA]
    /// Login tendrÃ­a que armar el objeto de usuario dentro de su propia lÃ³gica.
    /// </summary>
    private static AppUser CreateAppUserFromDemo(DemoUser demoUser)
    {
        return new AppUser
        {
            Id = $"demo-{demoUser.Username}",
            Username = demoUser.Username,
            DisplayName = demoUser.DisplayName,
            RoleId = demoUser.RoleId,
            IsEnabled = true,
            IsLocked = false,
            MustChangePassword = false,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            FailedLoginAttempts = 0,
            CreatedBy = "demo-seed",
            UpdatedBy = "demo-seed"
        };
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para notificar que el estado de autenticaciÃ³n cambiÃ³.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman `Login()`, `Logout()` y `SetCurrentUser()`.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta despuÃ©s de una transiciÃ³n de sesiÃ³n.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Dispara el evento `StateChanged`.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n cambia -> `NotifyStateChanged()` -> UI repinta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a levantar una bandera de refresco de pantalla.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz no sabrÃ­a que el login cambiÃ³.
    /// </summary>
    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }
}


