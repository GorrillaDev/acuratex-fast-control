// [FLUJO] PermissionService pregunta aquÃ­ quÃ© puede hacer cada rol.
using AcuratexControlApp.Models.Auth;

namespace AcuratexControlApp.Services.Auth;
// [FLUJO] Sesion -> RoleService -> permisos -> visibilidad de la UI.
// [EQUIV MCU] Se parece a una tabla de privilegios preconfigurada en firmware.
/// <summary>
/// [POR QUÃ‰ EXISTE]
/// Esta clase existe para concentrar la tabla de roles demo en un Ãºnico sitio.
///
/// [QUIÃ‰N LA LLAMA]
/// La llaman servicios de sesiÃ³n, permisos y pantallas de administraciÃ³n.
///
/// [CUÃNDO SE EJECUTA]
/// Se usa durante el arranque y en cada consulta de rol o permiso.
///
/// [ENTRADAS]
/// Recibe identificadores de rol.
///
/// [SALIDAS]
/// Devuelve definiciones de rol y colecciones de permisos.
///
/// [EFECTOS SECUNDARIOS]
/// No cambia estado despuÃ©s de construirse.
///
/// [FLUJO ACURATEX]
/// SesiÃ³n -> rol -> permisos -> decisiones de UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tabla fija de mÃ¡scaras de permiso en memoria flash.
///
/// [SI NO EXISTIERA]
/// Los permisos quedarÃ­an dispersos en varias pantallas y el control serÃ­a inconsistente.
/// </summary>
public sealed class RoleService
{
    private readonly IReadOnlyDictionary<string, RoleDefinition> _roles;

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// El constructor existe para materializar el catÃ¡logo de roles de la demo.
    ///
    /// [QUIÃ‰N LO LLAMA]
    /// Lo llama el contenedor de dependencias.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta una vez por instancia.
    ///
    /// [ENTRADAS]
    /// No recibe parÃ¡metros.
    ///
    /// [SALIDAS]
    /// Devuelve el servicio inicializado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Construye roles y permisos.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `RoleService` -> lista fija de roles.
    ///
    /// [EQUIVALENCIA MCU]
    /// Es como precargar una tabla de configuraciÃ³n en RAM.
    ///
    /// [SI NO EXISTIERA]
    /// No habrÃ­a una fuente central de roles para la sesiÃ³n demo.
    /// </summary>
    public RoleService()
    {
        HashSet<string> superRootPermissions = new(UserPermission.All, StringComparer.OrdinalIgnoreCase);
        HashSet<string> adminPermissions = new(StringComparer.OrdinalIgnoreCase)
        {
            UserPermission.UserCreate,
            UserPermission.UserEdit,
            UserPermission.UserDisable,
            UserPermission.UserEnable,
            UserPermission.UserResetPassword,
            UserPermission.UserForcePasswordChange,
            UserPermission.UserUnlock,
            UserPermission.UserViewTechnicians,
            UserPermission.UserManageTechnicians,
            UserPermission.UserChangeOwnPassword,

            UserPermission.RoleView,
            UserPermission.RoleAssign,
            UserPermission.PermissionView,

            UserPermission.ConnectionSearch,
            UserPermission.ConnectionConnect,
            UserPermission.ConnectionDisconnect,
            UserPermission.ConnectionSelectType,
            UserPermission.ConnectionConfigure,
            UserPermission.ConnectionViewDetails,

            UserPermission.TestRun,
            UserPermission.TestStop,
            UserPermission.TestReset,
            UserPermission.TestOpenGraphicInterface,
            UserPermission.TestRunTarjetas,
            UserPermission.TestRunUnificado,
            UserPermission.HeadProgramSelect,

            UserPermission.ConfigView,
            UserPermission.ConfigEditBasic,
            UserPermission.ConfigExport,
            UserPermission.ConfigValidate,

            UserPermission.FirmwareInfoView,
            UserPermission.FirmwareStatusView,
            UserPermission.DeviceInfoView,

            UserPermission.LogViewSession,
            UserPermission.LogViewAll,
            UserPermission.LogExport,
            UserPermission.AuditView,
            UserPermission.AuditFilter
        };

        HashSet<string> tecnicoPermissions = new(StringComparer.OrdinalIgnoreCase)
        {
            UserPermission.UserViewSelf,
            UserPermission.UserChangeOwnPassword,

            UserPermission.ConnectionSearch,
            UserPermission.ConnectionConnect,
            UserPermission.ConnectionDisconnect,
            UserPermission.ConnectionSelectType,
            UserPermission.ConnectionViewDetails,

            UserPermission.TestRun,
            UserPermission.TestStop,
            UserPermission.TestReset,
            UserPermission.TestOpenGraphicInterface,
            UserPermission.TestRunTarjetas,
            UserPermission.TestRunUnificado,
            UserPermission.HeadProgramSelect,

            UserPermission.DeviceInfoView,
            UserPermission.FirmwareInfoView,
            UserPermission.FirmwareStatusView,

            UserPermission.LogViewSession,
            UserPermission.LogExport
        };

        RoleDefinition[] roles =
        {
            new RoleDefinition
            {
                Id = AppRoleIds.SuperRoot,
                Name = "SUPER ROOT",
                Description = "Cuenta maestra de fabrica, recuperacion, desarrollo y administracion total.",
                IsSystemRole = true,
                IsEditable = false,
                HierarchyLevel = 0,
                Permissions = superRootPermissions.ToArray()
            },
            new RoleDefinition
            {
                Id = AppRoleIds.AdminTecnico,
                Name = "ADMIN TECNICO",
                Description = "Responsable tecnico con administracion de tecnicos y operacion avanzada.",
                IsSystemRole = true,
                IsEditable = false,
                HierarchyLevel = 1,
                Permissions = adminPermissions.ToArray()
            },
            new RoleDefinition
            {
                Id = AppRoleIds.Tecnico,
                Name = "TECNICO",
                Description = "Usuario operativo para conexion, diagnostico y pruebas autorizadas.",
                IsSystemRole = true,
                IsEditable = false,
                HierarchyLevel = 2,
                Permissions = tecnicoPermissions.ToArray()
            }
        };

        _roles = roles.ToDictionary(static role => role.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Devuelve todos los roles para que la UI pueda listarlos sin conocer el diccionario interno.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman pantallas de administraciÃ³n y menÃºs de selecciÃ³n.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando hace falta mostrar el catÃ¡logo completo de roles.
    ///
    /// [ENTRADAS]
    /// No recibe parÃ¡metros.
    ///
    /// [SALIDAS]
    /// Devuelve una lista ordenada de roles.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `GetAllRoles()` -> catÃ¡logo ordenado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a enumerar una tabla de tipos de operaciÃ³n en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla tendrÃ­a que reconstruir su propia lista de roles.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Devuelve todos los roles para que la UI pueda listarlos sin conocer el diccionario interno.
    ///
    /// [QUIÃ‰N LA USA]
    /// La llaman pantallas de administraciÃ³n y menÃºs de selecciÃ³n.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando hace falta mostrar el catÃ¡logo completo de roles.
    ///
    /// [ENTRADAS]
    /// No recibe parÃ¡metros.
    ///
    /// [SALIDAS]
    /// Devuelve una lista ordenada de roles.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `GetAllRoles()` -> catÃ¡logo ordenado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a enumerar una tabla de tipos de operaciÃ³n en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla tendrÃ­a que reconstruir su propia lista de roles.
    /// </summary>
    public IReadOnlyList<RoleDefinition> GetAllRoles()
    {
        // [FLUJO] La UI recibe la lista ya ordenada por jerarquia, no el diccionario interno.
        return _roles.Values
            .OrderBy(static role => role.HierarchyLevel)
            .ThenBy(static role => role.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para buscar un rol por su identificador lÃ³gico.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan servicios de sesiÃ³n, permisos y pantallas administrativas.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando hay que resolver un rol concreto.
    ///
    /// [ENTRADAS]
    /// Recibe el identificador del rol.
    ///
    /// [SALIDAS]
    /// Devuelve el rol o `null` si no existe.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// ID de rol -> `GetRoleById()` -> definiciÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a buscar una entrada de tabla por clave.
    ///
    /// [SI NO EXISTIERA]
    /// Cada consumidor tendrÃ­a que resolver el diccionario por su cuenta.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para buscar una definiciÃ³n de rol por su identificador.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan la sesiÃ³n, los permisos y la administraciÃ³n de roles.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando hace falta resolver un `RoleId` a su objeto completo.
    ///
    /// [ENTRADAS]
    /// Recibe el identificador de rol.
    ///
    /// [SALIDAS]
    /// Devuelve el rol o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// ID de rol -> `GetRoleById()` -> definiciÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a buscar una entrada de tabla por clave.
    ///
    /// [SI NO EXISTIERA]
    /// Cada consumidor tendrÃ­a que resolver el diccionario por su cuenta.
    /// </summary>
    public RoleDefinition? GetRoleById(string roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId)) {
            return null;
        }

        return _roles.TryGetValue(roleId.Trim(), out RoleDefinition? role) ? role : null;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para acceder rÃ¡pido al rol Super Root.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan reglas internas y atajos de configuraciÃ³n.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando se necesita la definiciÃ³n mÃ¡xima del sistema.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el rol Super Root.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// RoleService -> `GetSuperRootRole()` -> rol maestro.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un registro fijo de privilegio mÃ¡ximo.
    ///
    /// [SI NO EXISTIERA]
    /// HabrÃ­a que buscar siempre el rol por ID.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para acceder rÃ¡pido al rol Super Root.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan reglas internas y atajos de configuraciÃ³n.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando se necesita la definiciÃ³n mÃ¡xima del sistema.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el rol Super Root.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// RoleService -> `GetSuperRootRole()` -> rol maestro.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un registro fijo de privilegio mÃ¡ximo.
    ///
    /// [SI NO EXISTIERA]
    /// HabrÃ­a que buscar siempre el rol por ID.
    /// </summary>
    public RoleDefinition GetSuperRootRole()
    {
        return _roles[AppRoleIds.SuperRoot];
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para acceder rÃ¡pido al rol Admin TÃ©cnico.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan reglas internas de jerarquÃ­a.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando se necesita el rol tÃ©cnico administrativo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el rol Admin TÃ©cnico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// RoleService -> `GetAdminTecnicoRole()` -> rol intermedio.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una constante de configuraciÃ³n fija.
    ///
    /// [SI NO EXISTIERA]
    /// HabrÃ­a que buscar siempre el rol por ID.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para acceder rÃ¡pido al rol Admin TÃ©cnico.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan reglas internas de jerarquÃ­a.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando se necesita el rol tÃ©cnico administrativo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el rol Admin TÃ©cnico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// RoleService -> `GetAdminTecnicoRole()` -> rol intermedio.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una constante de configuraciÃ³n fija.
    ///
    /// [SI NO EXISTIERA]
    /// HabrÃ­a que buscar siempre el rol por ID.
    /// </summary>
    public RoleDefinition GetAdminTecnicoRole()
    {
        return _roles[AppRoleIds.AdminTecnico];
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para acceder rÃ¡pido al rol TÃ©cnico.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan reglas internas y cÃ³digo de sesiÃ³n.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando se necesita el rol operativo base.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el rol TÃ©cnico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// RoleService -> `GetTecnicoRole()` -> rol operativo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una constante de operaciÃ³n comÃºn.
    ///
    /// [SI NO EXISTIERA]
    /// HabrÃ­a que buscar siempre el rol por ID.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para acceder rÃ¡pido al rol TÃ©cnico.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan reglas internas y cÃ³digo de sesiÃ³n.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando se necesita el rol operativo base.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el rol TÃ©cnico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// RoleService -> `GetTecnicoRole()` -> rol operativo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una constante de operaciÃ³n comÃºn.
    ///
    /// [SI NO EXISTIERA]
    /// HabrÃ­a que buscar siempre el rol por ID.
    /// </summary>
    public RoleDefinition GetTecnicoRole()
    {
        return _roles[AppRoleIds.Tecnico];
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para extraer los permisos de un rol sin exponer el catÃ¡logo completo.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan `PermissionService` y pantallas administrativas.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al consultar la mÃ¡scara de permisos de un rol concreto.
    ///
    /// [ENTRADAS]
    /// Recibe el ID del rol.
    ///
    /// [SALIDAS]
    /// Devuelve la colecciÃ³n de permisos del rol o vacÃ­a si no existe.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Rol -> permisos -> UI o validaciÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una tabla de bits de autorizaciÃ³n.
    ///
    /// [SI NO EXISTIERA]
    /// Cada consumidor tendrÃ­a que sacar los permisos del objeto completo.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para extraer los permisos de un rol sin exponer el catÃ¡logo completo.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan `PermissionService` y pantallas administrativas.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al consultar la mÃ¡scara de permisos de un rol concreto.
    ///
    /// [ENTRADAS]
    /// Recibe el ID del rol.
    ///
    /// [SALIDAS]
    /// Devuelve la colecciÃ³n de permisos del rol o vacÃ­a si no existe.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Rol -> permisos -> UI o validaciÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer una tabla de bits de autorizaciÃ³n.
    ///
    /// [SI NO EXISTIERA]
    /// Cada consumidor tendrÃ­a que sacar los permisos del objeto completo.
    /// </summary>
    public IReadOnlyCollection<string> GetPermissionsForRole(string roleId)
    {
        RoleDefinition? role = GetRoleById(roleId);
        return role?.Permissions ?? Array.Empty<string>();
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para saber si un rol forma parte del sistema base.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan pantallas de ediciÃ³n y reglas de seguridad.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al decidir si un rol puede editarse o borrarse.
    ///
    /// [ENTRADAS]
    /// Recibe un ID de rol.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// ID de rol -> `IsSystemRole()` -> regla de ediciÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de sistema que protege configuraciones fijas.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabrÃ­a quÃ© roles tratar como inmutables.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para saber si un rol forma parte del sistema base.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan pantallas de ediciÃ³n y reglas de seguridad.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al decidir si un rol puede editarse o borrarse.
    ///
    /// [ENTRADAS]
    /// Recibe un ID de rol.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// ID de rol -> `IsSystemRole()` -> regla de ediciÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de sistema que protege configuraciones fijas.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabrÃ­a quÃ© roles tratar como inmutables.
    /// </summary>
    public bool IsSystemRole(string roleId)
    {
        RoleDefinition? role = GetRoleById(roleId);
        return role?.IsSystemRole ?? false;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si un rol puede modificar a otro rol.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan flujos de administraciÃ³n y permisos.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta antes de habilitar ediciÃ³n de roles.
    ///
    /// [ENTRADAS]
    /// Recibe el rol del actor y el rol objetivo.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si la modificaciÃ³n estÃ¡ permitida.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Actor -> `CanRoleModifyRole()` -> regla jerÃ¡rquica.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una prioridad de supervisor sobre un perfil inferior.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla tendrÃ­a que reimplementar la jerarquÃ­a.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si un rol puede modificar a otro rol.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan flujos de administraciÃ³n y permisos.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta antes de habilitar ediciÃ³n de roles.
    ///
    /// [ENTRADAS]
    /// Recibe el rol del actor y el rol objetivo.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si la modificaciÃ³n estÃ¡ permitida.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Actor -> `CanRoleModifyRole()` -> regla jerÃ¡rquica.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una prioridad de supervisor sobre un perfil inferior.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla tendrÃ­a que reimplementar la jerarquÃ­a.
    /// </summary>
    public bool CanRoleModifyRole(string actorRoleId, string targetRoleId)
    {
        RoleDefinition? actor = GetRoleById(actorRoleId);
        RoleDefinition? target = GetRoleById(targetRoleId);
        if (actor == null || target == null) {
            return false;
        }

        if (string.Equals(actor.Id, AppRoleIds.SuperRoot, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (string.Equals(actor.Id, AppRoleIds.AdminTecnico, StringComparison.OrdinalIgnoreCase)) {
            return string.Equals(target.Id, AppRoleIds.Tecnico, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}

