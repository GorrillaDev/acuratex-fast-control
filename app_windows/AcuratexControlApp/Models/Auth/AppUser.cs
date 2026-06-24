// [ACURATEX] Representa al usuario autenticado o gestionado por la demo.
namespace AcuratexControlApp.Models.Auth;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para transportar la información principal de un usuario.
///
/// [QUIÉN LA USA]
/// La usan la sesión, la pantalla de usuarios y el sistema de permisos.
///
/// [CUÁNDO SE USA]
/// Se usa al iniciar sesión, mostrar perfiles o editar usuarios demo.
///
/// [ENTRADAS]
/// Recibe datos de identidad, rol y auditoría.
///
/// [SALIDAS]
/// Devuelve un contenedor de datos editable.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene lógica propia; solo datos.
///
/// [FLUJO ACURATEX]
/// Login o gestión -> `AppUser` -> permisos y vistas.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una estructura de configuración de operario en RAM.
///
/// [SI NO EXISTIERA]
/// Habría que repartir la identidad del usuario en varias variables sueltas.
/// </summary>
public sealed class AppUser
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para identificar de forma única al usuario en toda la app.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan sesión, permisos, auditoría y pantallas de edición.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al construir, comparar o mostrar un usuario.
    ///
    /// [ENTRADAS]
    /// Recibe un identificador generado o persistido.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador del usuario.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login o gestión -> `AppUser.Id` -> trazabilidad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al número de serie interno de una tarjeta o registro.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podría diferenciar dos usuarios con el mismo nombre visible.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar el nombre de inicio de sesion que escribe o ve el operador.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la pantalla de login, el encabezado y la auditoria.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al autenticar, mostrar y buscar usuarios.
    ///
    /// [ENTRADAS]
    /// Recibe una cadena de usuario.
    ///
    /// [SALIDAS]
    /// Devuelve el nombre de acceso.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `Username` -> sesión y trazabilidad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al identificador visible de un operador en una consola.
    ///
    /// [SI NO EXISTIERA]
    /// La app no tendria un nombre de acceso estable para el usuario.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para mostrar un nombre humano legible en la interfaz.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan encabezados, listas de usuarios y mensajes de sesión.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al pintar la cuenta actual o una cuenta administrable.
    ///
    /// [ENTRADAS]
    /// Recibe un texto de display.
    ///
    /// [SALIDAS]
    /// Devuelve el nombre visible.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario -> `DisplayName` -> etiqueta visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un alias o etiqueta de operador en una consola.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que mostrar el nombre técnico o el username.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para indicar qué rol gobierna los permisos del usuario.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan autenticación, permisos y pantallas de administración.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cada vez que la app decide qué puede hacer este usuario.
    ///
    /// [ENTRADAS]
    /// Recibe el ID de un rol.
    ///
    /// [SALIDAS]
    /// Devuelve el rol asignado al usuario.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene por sí mismo.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario -> `RoleId` -> permisos -> acciones visibles.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nivel de privilegio que activa o bloquea periféricos.
    ///
    /// [SI NO EXISTIERA]
    /// La app no sabría qué conjunto de permisos aplicar.
    /// </summary>
    public string RoleId { get; set; } = AppRoleIds.Tecnico;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para marcar si el usuario esta habilitado para iniciar sesion o
    /// aparecer activo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan validaciones de login y pantallas de administracion.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta antes de aceptar credenciales o mostrar acciones.
    ///
    /// [ENTRADAS]
    /// Recibe `true` o `false`.
    ///
    /// [SALIDAS]
    /// Devuelve el estado de habilitacion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene por si mismo.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario -> `IsEnabled` -> login permitido o bloqueado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de habilitacion de cuenta.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria bloquear una cuenta sin agregar otra variable.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para marcar si la cuenta quedo bloqueada por seguridad o demo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan login, auditoria y administracion de usuarios.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta cuando la app decide si el acceso debe rechazarse.
    ///
    /// [ENTRADAS]
    /// Recibe un estado booleano.
    ///
    /// [SALIDAS]
    /// Devuelve el estado de bloqueo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene por si mismo.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario -> `IsLocked` -> acceso denegado o no.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de bloqueo de seguridad.
    ///
    /// [SI NO EXISTIERA]
    /// La cuenta bloqueada no tendria un estado dedicado.
    /// </summary>
    public bool IsLocked { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para indicar si el usuario debe cambiar contrasena al entrar.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la UI de login y las reglas de autenticacion.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al terminar un login.
    ///
    /// [ENTRADAS]
    /// Recibe `true` o `false`.
    ///
    /// [SALIDAS]
    /// Devuelve la marca de cambio obligatorio.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene por si mismo.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `MustChangePassword` -> flujo de cambio de clave.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de mantenimiento de contraseña.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podria forzar el cambio de clave cuando corresponde.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar la fecha de creacion y tener trazabilidad.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan auditoria y vistas administrativas.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al mostrar historiales o datos de cuenta.
    ///
    /// [ENTRADAS]
    /// Recibe una fecha.
    ///
    /// [SALIDAS]
    /// Devuelve la fecha de creacion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario -> `CreatedAt` -> auditoria.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a la marca de tiempo de alta de una cuenta.
    ///
    /// [SI NO EXISTIERA]
    /// Se perderia la fecha de alta del usuario.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar el ultimo acceso conocido.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan auditoria y pantallas de administracion.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al revisar actividad reciente.
    ///
    /// [ENTRADAS]
    /// Recibe una fecha opcional.
    ///
    /// [SALIDAS]
    /// Devuelve la fecha del ultimo login o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario -> `LastLoginAt` -> historial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un registro de ultimo arranque de un operador.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria mostrar el ultimo acceso del usuario.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para contar intentos fallidos de acceso.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan login y politicas de bloqueo.
    ///
    /// [CUÁNDO SE USA]
    /// Se incrementa o consulta durante autenticacion.
    ///
    /// [ENTRADAS]
    /// Recibe un entero no negativo.
    ///
    /// [SALIDAS]
    /// Devuelve el conteo de fallos.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene por si mismo.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `FailedLoginAttempts` -> bloqueo o alerta.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un contador de reintentos de acceso.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podria medir fallos de autentificacion.
    /// </summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para registrar quien creo el usuario.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan auditoria y administracion.
    ///
    /// [CUÁNDO SE USA]
    /// Se muestra al revisar el origen de la cuenta.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre del creador.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador del creador.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario -> `CreatedBy` -> trazabilidad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al origen que crea una entrada de configuracion.
    ///
    /// [SI NO EXISTIERA]
    /// No quedaria claro quien dio de alta la cuenta.
    /// </summary>
    public string CreatedBy { get; set; } = "system";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para registrar quien modifico por ultima vez el usuario.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan auditoria y administracion.
    ///
    /// [CUÁNDO SE USA]
    /// Se actualiza al guardar cambios.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre del editor.
    ///
    /// [SALIDAS]
    /// Devuelve el ultimo modificador.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene por si mismo.
    ///
    /// [FLUJO ACURATEX]
    /// Usuario -> `UpdatedBy` -> trazabilidad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a la marca de quien hizo la ultima escritura en un registro.
    ///
    /// [SI NO EXISTIERA]
    /// No se sabria quien hizo el ultimo cambio.
    /// </summary>
    public string UpdatedBy { get; set; } = "system";
}
