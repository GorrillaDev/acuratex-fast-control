// [FLUJO] AuthStateService define usuario/rol -> PermissionService responde si una acciÃ³n es vÃ¡lida.
using AcuratexControlApp.Models.Auth;

namespace AcuratexControlApp.Services.Auth;
// [FLUJO] AuthStateService + RoleService -> PermissionService -> botones y pantallas.
// [EQUIV MCU] Se parece a un comparador de bits de habilitacion.
/// <summary>
/// [POR QUÃ‰ EXISTE]
/// Esta clase existe para responder preguntas del tipo "puede hacer esto?" sin dispersar
/// reglas de permisos en cada pantalla.
///
/// [QUIÃ‰N LA LLAMA]
/// La llaman componentes Razor, formularios y servicios de negocio.
///
/// [CUÃNDO SE EJECUTA]
/// Se ejecuta cada vez que la UI necesita habilitar, ocultar o bloquear una acciÃ³n.
///
/// [ENTRADAS]
/// Recibe nombres de permisos, roles o usuarios objetivo.
///
/// [SALIDAS]
/// Devuelve `true` o `false` segÃºn la regla aplicada.
///
/// [EFECTOS SECUNDARIOS]
/// No modifica estado, solo consulta la sesiÃ³n y el repositorio de roles.
///
/// [FLUJO ACURATEX]
/// Pantalla -> permiso -> consulta -> decisiÃ³n visual o funcional.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un comparador de bits que decide si una acciÃ³n estÃ¡ habilitada.
///
/// [SI NO EXISTIERA]
/// Cada vista tendrÃ­a que repetir la lÃ³gica de autorizaciÃ³n.
/// </summary>
public sealed class PermissionService
{
    private static readonly HashSet<string> CriticalActions = new(CriticalUserAction.All, StringComparer.OrdinalIgnoreCase)
    {
        UserPermission.LogClear,
        UserPermission.SystemFactoryReset,
        UserPermission.SystemDevMode,
        UserPermission.CommandSendRaw
    };

    private readonly AuthStateService _authState;
    private readonly RoleService _roleService;

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este constructor existe para fijar las dos fuentes de verdad que consulta el servicio:
    /// la sesion actual y la tabla de roles.
    ///
    /// [QUIEN LO LLAMA]
    /// Lo llama el contenedor de dependencias al crear el servicio.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una sola vez por instancia del servicio.
    ///
    /// [ENTRADAS]
    /// Recibe el estado de autenticacion y el servicio de roles.
    ///
    /// [SALIDAS]
    /// Devuelve el servicio inicializado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Guarda las dependencias para consultas posteriores.
    ///
    /// [FLUJO ACURATEX]
    /// DI -> `PermissionService()` -> consulta de permisos lista.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a conectar una tabla de permisos y una tabla de jerarquia al arranque.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio no sabria de donde leer usuario y roles.
    /// </summary>
    public PermissionService(AuthStateService authState, RoleService roleService)
    {
        _authState = authState;
        _roleService = roleService;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para comprobar si el usuario actual posee un permiso concreto.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman botones, paneles y validaciones de negocio.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando hay que decidir si una acciÃ³n queda disponible.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre del permiso.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el permiso estÃ¡ presente.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `HasPermission()` -> permisos activos -> habilitar o bloquear.
    ///
    /// [EQUIVALENCIA MCU]
    /// Es como leer un bit de control en un registro de habilitaciÃ³n.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podrÃ­a preguntar de forma uniforme si una acciÃ³n estÃ¡ autorizada.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para comprobar si el usuario actual posee un permiso concreto.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman botones, paneles y validaciones de negocio.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando hay que decidir si una acciÃ³n queda disponible.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre del permiso.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el permiso estÃ¡ presente.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `HasPermission()` -> permisos activos -> habilitar o bloquear.
    ///
    /// [EQUIVALENCIA MCU]
    /// Es como leer un bit de control en un registro de habilitaciÃ³n.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podrÃ­a preguntar de forma uniforme si una acciÃ³n estÃ¡ autorizada.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para comprobar si el usuario actual posee un permiso concreto.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman botones, paneles y validaciones de negocio.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando hay que decidir si una acciÃ³n queda disponible.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre del permiso.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el permiso estÃ¡ presente.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `HasPermission()` -> permisos activos -> habilitar o bloquear.
    ///
    /// [EQUIVALENCIA MCU]
    /// Es como leer un bit de control en un registro de habilitaciÃ³n.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podrÃ­a preguntar de forma uniforme si una acciÃ³n estÃ¡ autorizada.
    /// </summary>
    public bool HasPermission(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission)) {
            return false;
        }

        IReadOnlyCollection<string> current = _authState.GetCurrentPermissions();
        return current.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para comprobar si basta con uno de varios permisos posibles.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman vistas y validaciones que aceptan varias rutas de acceso.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando una pantalla tiene alternativas de autorizaciÃ³n.
    ///
    /// [ENTRADAS]
    /// Recibe una lista variable de permisos.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si alguno estÃ¡ presente.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `HasAnyPermission()` -> comparaciÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comparar varios bits y aceptar con que uno estÃ© activo.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendrÃ­a que repetir la lÃ³gica de "cualquiera de estos permisos".
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para comprobar si basta con uno de varios permisos posibles.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman vistas y validaciones que aceptan varias rutas de acceso.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando una pantalla tiene alternativas de autorizaciÃ³n.
    ///
    /// [ENTRADAS]
    /// Recibe una lista variable de permisos.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si alguno estÃ¡ presente.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `HasAnyPermission()` -> comparaciÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comparar varios bits y aceptar con que uno estÃ© activo.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendrÃ­a que repetir la lÃ³gica de "cualquiera de estos permisos".
    /// </summary>
    public bool HasAnyPermission(params string[] permissions)
    {
        if (permissions == null || permissions.Length == 0) {
            return false;
        }

        IReadOnlyCollection<string> current = _authState.GetCurrentPermissions();
        if (current.Count == 0) {
            return false;
        }

        foreach (string permission in permissions) {
            if (string.IsNullOrWhiteSpace(permission)) {
                continue;
            }

            if (current.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase))) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para exigir que todos los permisos estÃ©n presentes.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman flujos de seguridad mÃ¡s estrictos.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando una acciÃ³n requiere varios permisos simultÃ¡neos.
    ///
    /// [ENTRADAS]
    /// Recibe una lista variable de permisos.
    ///
    /// [SALIDAS]
    /// Devuelve `true` solo si todos estÃ¡n presentes.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `HasAllPermissions()` -> comparaciÃ³n mÃºltiple -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a exigir varias condiciones de hardware a la vez.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla tendrÃ­a que escribir su propia validaciÃ³n compuesta.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para exigir que todos los permisos estÃ©n presentes.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman flujos de seguridad mÃ¡s estrictos.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando una acciÃ³n requiere varios permisos simultÃ¡neos.
    ///
    /// [ENTRADAS]
    /// Recibe una lista variable de permisos.
    ///
    /// [SALIDAS]
    /// Devuelve `true` solo si todos estÃ¡n presentes.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `HasAllPermissions()` -> comparaciÃ³n mÃºltiple -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a exigir varias condiciones de hardware a la vez.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla tendrÃ­a que escribir su propia validaciÃ³n compuesta.
    /// </summary>
    public bool HasAllPermissions(params string[] permissions)
    {
        if (permissions == null || permissions.Length == 0) {
            return false;
        }

        IReadOnlyCollection<string> current = _authState.GetCurrentPermissions();
        if (current.Count == 0) {
            return false;
        }

        foreach (string permission in permissions) {
            if (string.IsNullOrWhiteSpace(permission)) {
                continue;
            }

            if (!current.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase))) {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para preguntar si la sesiÃ³n actual pertenece al rol Super Root.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman validaciones que necesitan la mÃ¡xima autoridad.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando una regla depende del rol mÃ¡ximo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> rol actual -> comparaciÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar si un usuario tiene privilegio maestro.
    ///
    /// [SI NO EXISTIERA]
    /// La UI repetirÃ­a el mismo ID de rol en varios sitios.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para preguntar si la sesiÃ³n actual pertenece al rol Super Root.
    ///
    /// [QUIÃ‰N LA USA]
    /// La llaman validaciones que necesitan la mÃ¡xima autoridad.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando una regla depende del rol mÃ¡ximo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> rol actual -> comparaciÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar si un usuario tiene privilegio maestro.
    ///
    /// [SI NO EXISTIERA]
    /// La UI repetirÃ­a el mismo ID de rol en varios sitios.
    /// </summary>
    public bool CurrentUserIsSuperRoot()
    {
        return CurrentUserHasRole(AppRoleIds.SuperRoot);
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para preguntar si la sesiÃ³n actual pertenece al rol Admin TÃ©cnico.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman reglas de gestiÃ³n y administraciÃ³n parcial.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al filtrar permisos de administraciÃ³n tÃ©cnica.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> rol actual -> comparaciÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar un modo supervisor intermedio.
    ///
    /// [SI NO EXISTIERA]
    /// HabrÃ­a que repetir la comparaciÃ³n del ID de rol.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para preguntar si la sesiÃ³n actual pertenece al rol Admin TÃ©cnico.
    ///
    /// [QUIÃ‰N LA USA]
    /// La llaman reglas de gestiÃ³n y administraciÃ³n parcial.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al filtrar permisos de administraciÃ³n tÃ©cnica.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> rol actual -> comparaciÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar un modo supervisor intermedio.
    ///
    /// [SI NO EXISTIERA]
    /// HabrÃ­a que repetir la comparaciÃ³n del ID de rol.
    /// </summary>
    public bool CurrentUserIsAdminTecnico()
    {
        return CurrentUserHasRole(AppRoleIds.AdminTecnico);
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para preguntar si la sesiÃ³n actual pertenece al rol TÃ©cnico.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llaman validaciones y pantallas operativas.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando la UI necesita distinguir el rol bÃ¡sico operativo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> rol actual -> comparaciÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar un perfil de operador simple.
    ///
    /// [SI NO EXISTIERA]
    /// La UI repetirÃ­a la constante del rol tÃ©cnico.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para preguntar si la sesiÃ³n actual pertenece al rol TÃ©cnico.
    ///
    /// [QUIÃ‰N LA USA]
    /// La llaman validaciones y pantallas operativas.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando la UI necesita distinguir el rol bÃ¡sico operativo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> rol actual -> comparaciÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comprobar un perfil de operador simple.
    ///
    /// [SI NO EXISTIERA]
    /// La UI repetirÃ­a la constante del rol tÃ©cnico.
    /// </summary>
    public bool CurrentUserIsTecnico()
    {
        return CurrentUserHasRole(AppRoleIds.Tecnico);
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si el usuario actual puede administrar a otro usuario.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usa la pantalla de usuarios y cualquier flujo de ediciÃ³n de cuentas.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al habilitar botones de editar, bloquear o cambiar roles.
    ///
    /// [ENTRADAS]
    /// Recibe el usuario objetivo.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si la acciÃ³n estÃ¡ permitida.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `CanManageUser()` -> rol actual -> decisiÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una regla de prioridad entre usuarios.
    ///
    /// [SI NO EXISTIERA]
    /// Cada botÃ³n de administraciÃ³n tendrÃ­a que decidirlo por su cuenta.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si el usuario actual puede administrar a otro usuario.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usa la pantalla de usuarios y cualquier flujo de ediciÃ³n de cuentas.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al habilitar botones de editar, bloquear o cambiar roles.
    ///
    /// [ENTRADAS]
    /// Recibe el usuario objetivo.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si la acciÃ³n estÃ¡ permitida.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `CanManageUser()` -> rol actual -> decisiÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una regla de prioridad entre usuarios.
    ///
    /// [SI NO EXISTIERA]
    /// Cada botÃ³n de administraciÃ³n tendrÃ­a que decidirlo por su cuenta.
    /// </summary>
    public bool CanManageUser(AppUser targetUser)
    {
        if (targetUser == null || !_authState.IsAuthenticated) {
            return false;
        }

        if (CurrentUserIsSuperRoot()) {
            return true;
        }

        if (CurrentUserIsAdminTecnico()) {
            return string.Equals(targetUser.RoleId, AppRoleIds.Tecnico, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si la pantalla de usuarios debe mostrarse.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan menÃºs y pantallas de administraciÃ³n.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando la UI decide si pinta o esconde la secciÃ³n de usuarios.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> permiso -> visibilidad de usuarios.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de acceso a un submenÃº.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendrÃ­a que repetir el permiso de visibilidad de usuarios.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si la pantalla de usuarios debe mostrarse.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan menÃºs y pantallas de administraciÃ³n.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando la UI decide si pinta o esconde la secciÃ³n de usuarios.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> permiso -> visibilidad de usuarios.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de acceso a un submenÃº.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendrÃ­a que repetir el permiso de visibilidad de usuarios.
    /// </summary>
    public bool CanViewUsers()
    {
        return HasAnyPermission(UserPermission.UserViewAll, UserPermission.UserViewTechnicians);
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si la UI habilita la administraciÃ³n de tÃ©cnicos.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan pantallas de administraciÃ³n de personal.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al pintar acciones sensibles de tÃ©cnicos.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> permiso/rol -> control de UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una llave de acceso para suboperaciones.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla tendrÃ­a que repetir la regla cada vez.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si la UI habilita la administraciÃ³n de tÃ©cnicos.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan pantallas de administraciÃ³n de personal.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al pintar acciones sensibles de tÃ©cnicos.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> permiso/rol -> control de UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una llave de acceso para suboperaciones.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla tendrÃ­a que repetir la regla cada vez.
    /// </summary>
    public bool CanManageTechnicians()
    {
        return HasPermission(UserPermission.UserManageTechnicians) || CurrentUserIsSuperRoot();
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para saber si una acciÃ³n requiere confirmaciÃ³n especial.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan alertas, menÃºs y validaciones de seguridad.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta antes de lanzar una acciÃ³n sensible.
    ///
    /// [ENTRADAS]
    /// Recibe la acciÃ³n o permiso a comprobar.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si hace falta confirmaciÃ³n.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// AcciÃ³n -> `RequiresConfirmation()` -> modal o ejecuciÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una protecciÃ³n extra antes de ejecutar una orden crÃ­tica.
    ///
    /// [SI NO EXISTIERA]
    /// Las acciones crÃ­ticas no tendrÃ­an una marca de advertencia comÃºn.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para saber si una acciÃ³n requiere confirmaciÃ³n especial.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan alertas, menÃºs y validaciones de seguridad.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta antes de lanzar una acciÃ³n sensible.
    ///
    /// [ENTRADAS]
    /// Recibe la acciÃ³n o permiso a comprobar.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si hace falta confirmaciÃ³n.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// AcciÃ³n -> `RequiresConfirmation()` -> modal o ejecuciÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una protecciÃ³n extra antes de ejecutar una orden crÃ­tica.
    ///
    /// [SI NO EXISTIERA]
    /// Las acciones crÃ­ticas no tendrÃ­an una marca de advertencia comÃºn.
    /// </summary>
    public bool RequiresConfirmation(string actionOrPermission)
    {
        if (string.IsNullOrWhiteSpace(actionOrPermission)) {
            return false;
        }

        return CriticalActions.Contains(actionOrPermission.Trim());
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para limitar quÃ© rol puede asignar el usuario actual al crear una cuenta.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usa la pantalla de gestiÃ³n de usuarios.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta antes de habilitar la creaciÃ³n de una cuenta con rol especÃ­fico.
    ///
    /// [ENTRADAS]
    /// Recibe el identificador del rol objetivo.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `CanCreateUserWithRole()` -> regla -> permiso final.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una regla de jerarquÃ­a para programar niveles de acceso.
    ///
    /// [SI NO EXISTIERA]
    /// La UI podrÃ­a ofrecer roles que el operador no deberÃ­a crear.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para limitar quÃ© rol puede asignar el usuario actual al crear una cuenta.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usa la pantalla de gestiÃ³n de usuarios.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta antes de habilitar la creaciÃ³n de una cuenta con rol especÃ­fico.
    ///
    /// [ENTRADAS]
    /// Recibe el identificador del rol objetivo.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `CanCreateUserWithRole()` -> regla -> permiso final.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una regla de jerarquÃ­a para programar niveles de acceso.
    ///
    /// [SI NO EXISTIERA]
    /// La UI podrÃ­a ofrecer roles que el operador no deberÃ­a crear.
    /// </summary>
    public bool CanCreateUserWithRole(string targetRoleId)
    {
        if (string.IsNullOrWhiteSpace(targetRoleId)) {
            return false;
        }

        if (!HasPermission(UserPermission.UserCreate)) {
            return false;
        }

        if (CurrentUserIsSuperRoot()) {
            return string.Equals(targetRoleId, AppRoleIds.SuperRoot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetRoleId, AppRoleIds.AdminTecnico, StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetRoleId, AppRoleIds.Tecnico, StringComparison.OrdinalIgnoreCase);
        }

        if (CurrentUserIsAdminTecnico()) {
            return string.Equals(targetRoleId, AppRoleIds.Tecnico, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si el rol actual puede asignar otro rol.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan pantallas de administraciÃ³n de usuarios y roles.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta antes de cambiar un rol existente.
    ///
    /// [ENTRADAS]
    /// Recibe el rol objetivo.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `CanAssignRole()` -> RoleService -> decisiÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar si un maestro puede reprogramar un esclavo.
    ///
    /// [SI NO EXISTIERA]
    /// La asignaciÃ³n de roles tendrÃ­a que validar por otro lado.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para decidir si el rol actual puede asignar otro rol.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan pantallas de administraciÃ³n de usuarios y roles.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta antes de cambiar un rol existente.
    ///
    /// [ENTRADAS]
    /// Recibe el rol objetivo.
    ///
    /// [SALIDAS]
    /// Devuelve `true` o `false`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `CanAssignRole()` -> RoleService -> decisiÃ³n.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a validar si un maestro puede reprogramar un esclavo.
    ///
    /// [SI NO EXISTIERA]
    /// La asignaciÃ³n de roles tendrÃ­a que validar por otro lado.
    /// </summary>
    public bool CanAssignRole(string targetRoleId)
    {
        if (string.IsNullOrWhiteSpace(targetRoleId)) {
            return false;
        }

        RoleDefinition? currentRole = _authState.CurrentRole;
        if (currentRole == null || !HasPermission(UserPermission.RoleAssign)) {
            return false;
        }

        return _roleService.CanRoleModifyRole(currentRole.Id, targetRoleId);
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para saber si el usuario consultado es el mismo que el usuario activo.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan pantallas que deben distinguir entre "yo" y "otro usuario".
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al decidir permisos de acciones sobre la propia cuenta.
    ///
    /// [ENTRADAS]
    /// Recibe el usuario a comparar.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si coincide con la sesiÃ³n actual.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario objetivo -> comparaciÃ³n con sesiÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comparar si el comando viene del mismo operador que estÃ¡ en consola.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podrÃ­a distinguir acciones propias de acciones ajenas.
    /// </summary>
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para saber si el usuario consultado es el mismo que el usuario activo.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan pantallas que deben distinguir entre "yo" y "otro usuario".
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al decidir permisos de acciones sobre la propia cuenta.
    ///
    /// [ENTRADAS]
    /// Recibe el usuario a comparar.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si coincide con la sesiÃ³n actual.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario objetivo -> comparaciÃ³n con sesiÃ³n -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comparar si el comando viene del mismo operador que estÃ¡ en consola.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podrÃ­a distinguir acciones propias de acciones ajenas.
    /// </summary>
    public bool IsCurrentUser(AppUser user)
    {
        if (user == null) {
            return false;
        }

        AppUser? current = _authState.CurrentUser;
        if (current == null) {
            return false;
        }

        return string.Equals(current.Id, user.Id, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para concentrar la comparacion del rol actual y evitar repetirla
    /// en varios metodos publicos.
    ///
    /// [QUIEN LA LLAMA]
    /// La llaman los atajos `CurrentUserIsSuperRoot()`, `CurrentUserIsAdminTecnico()` y
    /// `CurrentUserIsTecnico()`.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta cada vez que una regla necesita comparar el rol activo con uno fijo.
    ///
    /// [ENTRADAS]
    /// Recibe el identificador del rol a comparar.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si el rol actual coincide.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Sesion -> rol activo -> `CurrentUserHasRole()` -> resultado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a comparar un registro de estado contra un valor fijo de referencia.
    ///
    /// [SI NO EXISTIERA]
    /// Cada atajo de rol repetirÃ­a la misma comparacion.
    /// </summary>
    private bool CurrentUserHasRole(string roleId)
    {
        RoleDefinition? role = _authState.CurrentRole;
        return role != null && string.Equals(role.Id, roleId, StringComparison.OrdinalIgnoreCase);
    }
}

