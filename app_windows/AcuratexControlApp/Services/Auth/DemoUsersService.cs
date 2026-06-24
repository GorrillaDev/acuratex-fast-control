// [FLUJO] AuthStateService da el usuario actual -> DemoUsersService filtra los usuarios visibles.
using AcuratexControlApp.Models.Auth;

namespace AcuratexControlApp.Services.Auth;
// [FLUJO] Sesion actual -> filtrado -> filas visibles en la pantalla de gestion.
// [EQUIV MCU] Se parece a una tabla fija de operarios/credenciales cargada al arrancar.
/// <summary>
/// [POR QUÃ‰ EXISTE]
/// Esta clase existe para simular un pequeÃ±o repositorio de usuarios administrables.
///
/// [QUIÃ‰N LA LLAMA]
/// La llaman la pantalla de usuarios y los paneles que muestran datos de demo.
///
/// [CUÃNDO SE EJECUTA]
/// Se ejecuta cuando hay que listar usuarios, calcular estado o mostrar tiempo restante.
///
/// [ENTRADAS]
/// Recibe el usuario actual, el usuario de demo o la fecha/hora de referencia.
///
/// [SALIDAS]
/// Devuelve usuarios visibles, estado de acceso y etiquetas de vencimiento.
///
/// [EFECTOS SECUNDARIOS]
/// No modifica estado despuÃ©s de construirse.
///
/// [FLUJO ACURATEX]
/// SesiÃ³n -> filtrado de demo -> vista de usuarios.
///
/// [EQUIVALENCIA MCU]
/// Se parece a leer una tabla de mantenimiento de usuarios guardada en memoria.
///
/// [SI NO EXISTIERA]
/// La pantalla de usuarios no tendrÃ­a datos demo consistentes para mostrar.
/// </summary>
public sealed class DemoUsersService
{
    private readonly IReadOnlyList<DemoManagedUser> _users;

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// El constructor existe para cargar la lista fija de usuarios demo.
    ///
    /// [QUIÃ‰N LO LLAMA]
    /// Lo llama el contenedor de dependencias.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta una sola vez por instancia.
    ///
    /// [ENTRADAS]
    /// No recibe parÃ¡metros.
    ///
    /// [SALIDAS]
    /// Devuelve el servicio con su catÃ¡logo listo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Inicializa el listado interno.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `DemoUsersService` -> catÃ¡logo demo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Es como precargar una tabla de calibraciÃ³n en memoria.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabrÃ­a quÃ© usuarios demo dibujar ni quÃ© estado calcular.
    /// </summary>
    public DemoUsersService()
    {
        _users = BuildDemoUsers();
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para filtrar quÃ© usuarios demo puede ver el operador actual.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llama la pantalla de gestiÃ³n de usuarios.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al abrir o refrescar la lista visible de usuarios.
    ///
    /// [ENTRADAS]
    /// Recibe el usuario autenticado actual.
    ///
    /// [SALIDAS]
    /// Devuelve una lista filtrada y ordenada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// SesiÃ³n -> GetVisibleUsersFor -> subconjunto visible en UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a filtrar una tabla de registros segÃºn el nivel de acceso del operador.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendrÃ­a que repetir la lÃ³gica de filtrado y jerarquÃ­a.
    /// </summary>
    public IReadOnlyList<DemoManagedUser> GetVisibleUsersFor(AppUser? currentUser)
    {
        if (currentUser == null) {
            return Array.Empty<DemoManagedUser>();
        }

        if (string.Equals(currentUser.RoleId, AppRoleIds.SuperRoot, StringComparison.OrdinalIgnoreCase)) {
            return _users
                .OrderBy(static user => RoleOrder(user.RoleId))
                .ThenBy(static user => user.Username, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (string.Equals(currentUser.RoleId, AppRoleIds.AdminTecnico, StringComparison.OrdinalIgnoreCase)) {
            return _users
                .Where(static user => string.Equals(user.RoleId, AppRoleIds.Tecnico, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static user => user.Username, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Array.Empty<DemoManagedUser>();
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para convertir bloqueo y fechas en un estado operativo simple.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llama la pantalla de gestiÃ³n de usuarios.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta al pintar cada fila de usuario demo.
    ///
    /// [ENTRADAS]
    /// Recibe el usuario y la fecha/hora actual.
    ///
    /// [SALIDAS]
    /// Devuelve un estado enum.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// Fecha + bloqueo + vencimiento -> GetStatus -> etiqueta visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a evaluar una bandera de mantenimiento con una fecha lÃ­mite.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendrÃ­a que calcular el estado de acceso por su cuenta.
    /// </summary>
    public DemoUserAccessStatus GetStatus(DemoManagedUser user, DateTime now)
    {
        if (user.Locked) {
            return DemoUserAccessStatus.Bloqueado;
        }

        DateTime today = now.Date;
        DateTime expiresAt = user.ExpiresAt.Date;
        if (expiresAt < today) {
            return DemoUserAccessStatus.Vencido;
        }

        int remainingDays = (expiresAt - today).Days;
        if (remainingDays <= 7) {
            return DemoUserAccessStatus.PorVencer;
        }

        return DemoUserAccessStatus.Activo;
    }
    // [EQUIV MCU] Es como un mensaje corto de diagnÃ³stico de vigencia.
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para mostrar una etiqueta humana de vencimiento sin que la UI
    /// tenga que repetir el cÃ¡lculo de fechas.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llama la pantalla de gestiÃ³n de usuarios demo.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cuando la tabla necesita mostrar el tiempo restante de un usuario.
    ///
    /// [ENTRADAS]
    /// Recibe el usuario demo y la fecha/hora actual.
    ///
    /// [SALIDAS]
    /// Devuelve un texto corto como `Vence hoy` o `Quedan X dias`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `BuildRemainingTimeLabel()` -> cÃ¡lculo de fecha -> etiqueta visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una rutina que convierte una fecha de expiraciÃ³n en un mensaje de diagnÃ³stico.
    ///
    /// [SI NO EXISTIERA]
    /// La interfaz tendrÃ­a que repetir la lÃ³gica de fechas en varios sitios.
    /// </summary>
    public string BuildRemainingTimeLabel(DemoManagedUser user, DateTime now)
    {
        DateTime today = now.Date;
        DateTime expiresAt = user.ExpiresAt.Date;
        if (expiresAt < today) {
            return "Vencido";
        }

        if (expiresAt == today) {
            return "Vence hoy";
        }

        int remainingDays = (expiresAt - today).Days;
        return $"Quedan {remainingDays} dias";
    }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta funciÃ³n existe para convertir un `RoleId` en un orden estable de presentaciÃ³n.
    ///
    /// [QUIÃ‰N LA LLAMA]
    /// La llama el ordenamiento de la lista visible.
    ///
    /// [CUÃNDO SE EJECUTA]
    /// Se ejecuta cada vez que se ordenan usuarios demo.
    ///
    /// [ENTRADAS]
    /// Recibe el identificador de rol.
    ///
    /// [SALIDAS]
    /// Devuelve un nÃºmero de jerarquÃ­a.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// RoleId -> `RoleOrder()` -> orden visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a asignar prioridad a distintos niveles de acceso.
    ///
    /// [SI NO EXISTIERA]
    /// El orden de la lista dependerÃ­a solo del texto del rol.
    /// </summary>
    private static int RoleOrder(string roleId)
    {
        if (string.Equals(roleId, AppRoleIds.SuperRoot, StringComparison.OrdinalIgnoreCase)) {
            return 0;
        }

        if (string.Equals(roleId, AppRoleIds.AdminTecnico, StringComparison.OrdinalIgnoreCase)) {
            return 1;
        }

        return 2;
    }

    /// <summary>
    /// [POR QUE EXISTE]
    /// Esta funcion existe para construir la lista fija de usuarios demo que la UI puede mostrar.
    ///
    /// [QUIEN LA LLAMA]
    /// La llama el constructor del servicio.
    ///
    /// [CUANDO SE EJECUTA]
    /// Se ejecuta una sola vez al crear el servicio.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una lista completa de usuarios demo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Solo crea objetos iniciales en memoria.
    ///
    /// [FLUJO ACURATEX]
    /// Arranque -> `BuildDemoUsers()` -> catalogo demo disponible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar una tabla de operarios de prueba en RAM al inicio.
    ///
    /// [SI NO EXISTIERA]
    /// El servicio no tendria datos base para filtrar o mostrar usuarios.
    /// </summary>
    private static IReadOnlyList<DemoManagedUser> BuildDemoUsers()
    {
        return
        [
            new DemoManagedUser
            {
                Id = "demo-root",
                Username = "root",
                DisplayName = "Super Root",
                RoleId = AppRoleIds.SuperRoot,
                Enabled = true,
                Locked = false,
                CreatedAt = new DateTime(2026, 1, 10),
                ExpiresAt = new DateTime(2027, 1, 10),
                LastLoginAt = new DateTime(2026, 5, 7, 9, 25, 0),
                WorkedHours = 451.9,
                SessionCount = 312
            },
            new DemoManagedUser
            {
                Id = "demo-admin",
                Username = "admin",
                DisplayName = "Admin Tecnico",
                RoleId = AppRoleIds.AdminTecnico,
                Enabled = true,
                Locked = false,
                CreatedAt = new DateTime(2026, 1, 15),
                ExpiresAt = new DateTime(2026, 12, 31),
                LastLoginAt = new DateTime(2026, 5, 7, 8, 40, 0),
                WorkedHours = 366.2,
                SessionCount = 218
            },
            new DemoManagedUser
            {
                Id = "demo-tecnico-01",
                Username = "tecnico01",
                DisplayName = "Carlos Ramos",
                RoleId = AppRoleIds.Tecnico,
                Enabled = true,
                Locked = false,
                CreatedAt = new DateTime(2026, 2, 1),
                ExpiresAt = new DateTime(2026, 6, 15),
                LastLoginAt = new DateTime(2026, 5, 7, 7, 50, 0),
                WorkedHours = 128.5,
                SessionCount = 67
            },
            new DemoManagedUser
            {
                Id = "demo-tecnico-02",
                Username = "tecnico02",
                DisplayName = "Luis Torres",
                RoleId = AppRoleIds.Tecnico,
                Enabled = true,
                Locked = false,
                CreatedAt = new DateTime(2026, 2, 10),
                ExpiresAt = new DateTime(2026, 5, 12),
                LastLoginAt = new DateTime(2026, 5, 6, 17, 35, 0),
                WorkedHours = 84.0,
                SessionCount = 51
            },
            new DemoManagedUser
            {
                Id = "demo-tecnico-03",
                Username = "tecnico03",
                DisplayName = "Marco Vega",
                RoleId = AppRoleIds.Tecnico,
                Enabled = false,
                Locked = false,
                CreatedAt = new DateTime(2026, 1, 26),
                ExpiresAt = new DateTime(2026, 4, 28),
                LastLoginAt = new DateTime(2026, 4, 27, 12, 12, 0),
                WorkedHours = 203.7,
                SessionCount = 109
            },
            new DemoManagedUser
            {
                Id = "demo-tecnico-04",
                Username = "tecnico04",
                DisplayName = "Diego Salas",
                RoleId = AppRoleIds.Tecnico,
                Enabled = true,
                Locked = true,
                CreatedAt = new DateTime(2026, 3, 4),
                ExpiresAt = new DateTime(2026, 7, 1),
                LastLoginAt = new DateTime(2026, 5, 2, 16, 50, 0),
                WorkedHours = 32.2,
                SessionCount = 19
            }
        ];
    }
}

