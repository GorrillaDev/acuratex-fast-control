// [ACURATEX] Credenciales demo cargadas en memoria para la pantalla de login.
namespace AcuratexControlApp.Models.Auth;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para transportar credenciales demo de forma simple.
///
/// [QUIEN LA USA]
/// La usa `AuthStateService` al validar el login.
///
/// [CUANDO SE USA]
/// Se usa solo durante la autenticacion demo.
///
/// [ENTRADAS]
/// Recibe usuario, contrasena, nombre visible y rol.
///
/// [SALIDAS]
/// Devuelve un contenedor inmutable de datos.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Login -> credenciales demo -> validacion -> sesion.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una tarjeta de acceso con usuario y clave guardados.
///
/// [SI NO EXISTIERA]
/// La autentificacion demo tendria que usar pares de cadenas sueltas.
/// </summary>
public sealed class DemoUser
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar el nombre de acceso que escribe el operador.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `AuthStateService` al validar el login.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa durante la autenticación demo.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre de usuario elegido.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador textual de la cuenta.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `Username` -> búsqueda de credencial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nombre de una cuenta almacenada en una tabla de acceso.
    ///
    /// [SI NO EXISTIERA]
    /// No habría forma de buscar la cuenta por nombre.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar la clave demo asociada a la cuenta.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa el validador de login.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al comprobar credenciales de demostración.
    ///
    /// [ENTRADAS]
    /// Recibe la contraseña escrita por el operador.
    ///
    /// [SALIDAS]
    /// Devuelve la contraseña guardada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `Password` -> comparación.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un pin de acceso guardado junto al usuario.
    ///
    /// [SI NO EXISTIERA]
    /// El login demo no podría validarse.
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para mostrar un nombre humano después del login.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la barra superior y las listas de sesión.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando la autenticación ya fue exitosa.
    ///
    /// [ENTRADAS]
    /// Recibe un nombre visible.
    ///
    /// [SALIDAS]
    /// Devuelve la etiqueta visual de la cuenta.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `DisplayName` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una etiqueta serigrafiada sobre una credencial.
    ///
    /// [SI NO EXISTIERA]
    /// La UI solo mostraría el username técnico.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para asignar el rol efectivo al usuario demo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan el estado de sesión y permisos.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al convertir credenciales en sesión activa.
    ///
    /// [ENTRADAS]
    /// Recibe el ID del rol.
    ///
    /// [SALIDAS]
    /// Devuelve el rol asociado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Credencial -> `RoleId` -> permisos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al perfil de privilegios que se carga junto con un operador.
    ///
    /// [SI NO EXISTIERA]
    /// No se sabría qué permisos aplicar al iniciar sesión.
    /// </summary>
    public required string RoleId { get; init; }
}
