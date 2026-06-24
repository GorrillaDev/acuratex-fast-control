// [ACURATEX] Acciones criticas que pueden requerir confirmacion adicional.
// [FLUJO] Accion sensible -> confirmacion -> ejecucion o bloqueo.
namespace AcuratexControlApp.Models.Auth;

/// <summary>
/// [POR QUÃ‰ EXISTE]
/// Esta clase existe para agrupar acciones que la app considera sensibles.
///
/// [QUIEN LA USA]
/// La usan permisos, alertas y validaciones de confirmacion.
///
/// [CUANDO SE USA]
/// Se usa cuando la UI necesita saber si debe pedir una confirmacion extra.
///
/// [ENTRADAS]
/// No recibe entradas.
///
/// [SALIDAS]
/// Expone constantes y la lista completa de acciones criticas.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Accion sensible -> comprobacion -> confirmacion o bloqueo.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un conjunto de interrupciones o comandos que requieren proteccion extra.
///
/// [SI NO EXISTIERA]
/// Cada pantalla tendria que mantener su propia lista de acciones sensibles.
/// </summary>
public static class CriticalUserAction
{
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta constante existe para identificar el borrado de una cuenta demo o administrada.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan permisos, alertas y validaciones de confirmacion.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando la UI necesita clasificar una eliminacion como sensible.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador simbolico de la accion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Accion -> `DeleteUser` -> confirmacion o bloqueo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un comando de borrado protegido por seguridad.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendria que usar cadenas sueltas para nombrar este caso.
    /// </summary>
    public const string DeleteUser = "DELETE_USER";
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta constante existe para identificar la creacion de una cuenta de privilegio maximo.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan permisos y pantallas de administracion.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando la app necesita tratar esa creacion como critica.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador simbolico de la accion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Accion -> `CreateSuperRoot` -> confirmacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a programar una cuenta maestra en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// La creacion de cuentas maestras quedaria sin nombre estandar.
    /// </summary>
    public const string CreateSuperRoot = "CREATE_SUPER_ROOT";
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta constante existe para identificar una promocion de privilegio hacia Super Root.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan las validaciones de jerarquia.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa al clasificar una subida de rol como operacion sensible.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador simbolico de la accion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Accion -> `PromoteToSuperRoot` -> confirmacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un cambio de nivel de privilegio en un sistema embebido.
    ///
    /// [SI NO EXISTIERA]
    /// La promocion mas alta no tendria una referencia fija.
    /// </summary>
    public const string PromoteToSuperRoot = "PROMOTE_TO_SUPER_ROOT";
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta constante existe para identificar la limpieza de logs de auditoria.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan permisos, trazabilidad y modales de confirmacion.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando una pantalla ofrece borrar historiales.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador simbolico de la accion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Accion -> `ClearLogs` -> confirmacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a vaciar un buffer de diagnostico protegido.
    ///
    /// [SI NO EXISTIERA]
    /// La limpieza de auditoria quedaria sin etiqueta comun.
    /// </summary>
    public const string ClearLogs = "CLEAR_LOGS";
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta constante existe para identificar un restablecimiento total del sistema demo.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan administracion, alertas y validaciones extremas.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando la app debe volver a valores base.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador simbolico de la accion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Accion -> `FactoryReset` -> confirmacion fuerte.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un reset de fabrica de un microcontrolador.
    ///
    /// [SI NO EXISTIERA]
    /// El reset total no tendria un nombre comun y estable.
    /// </summary>
    public const string FactoryReset = "FACTORY_RESET";
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta constante existe para identificar la regeneracion del archivo de usuarios demo.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan utilidades de recuperacion y administracion.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando el sistema vuelve a sembrar credenciales demo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador simbolico de la accion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Accion -> `ResetUsersFile` -> confirmacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a reescribir una tabla de usuarios en flash.
    ///
    /// [SI NO EXISTIERA]
    /// La regeneracion de usuarios no tendria una clave estable.
    /// </summary>
    public const string ResetUsersFile = "RESET_USERS_FILE";
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta constante existe para identificar la activacion del modo de desarrollo.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan permisos de mantenimiento y herramientas de soporte.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando la app habilita funciones de diagnostico profundas.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador simbolico de la accion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Accion -> `EnableDevMode` -> confirmacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a levantar una bandera de debug en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// El modo desarrollo no tendria un nombre uniforme.
    /// </summary>
    public const string EnableDevMode = "ENABLE_DEV_MODE";
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta constante existe para identificar el envio de un comando raw clasificado como sensible.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan la consola avanzada y las reglas de seguridad.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando el operador intenta enviar texto directo al firmware.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador simbolico de la accion.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Accion -> `SendCriticalRawCommand` -> confirmacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a enviar un comando directo protegido por permisos.
    ///
    /// [SI NO EXISTIERA]
    /// El comando raw sensible no tendria etiqueta estable.
    /// </summary>
    public const string SendCriticalRawCommand = "SEND_CRITICAL_RAW_COMMAND";

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Esta coleccion existe para agrupar todas las acciones que requieren confirmacion extra.
    ///
    /// [QUIÃ‰N LA USA]
    /// La usan `PermissionService`, menus de confirmacion y alertas.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa al evaluar si una accion debe bloquearse hasta que el operador confirme.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la lista completa de acciones criticas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Accion critica -> `All` -> confirmacion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una tabla de comandos protegidos por seguridad adicional.
    ///
    /// [SI NO EXISTIERA]
    /// Cada pantalla tendria que repetir su propia lista de acciones sensibles.
    /// </summary>
    public static IReadOnlyCollection<string> All { get; } = new[]
    {
        DeleteUser,
        CreateSuperRoot,
        PromoteToSuperRoot,
        ClearLogs,
        FactoryReset,
        ResetUsersFile,
        EnableDevMode,
        SendCriticalRawCommand
    };
}
