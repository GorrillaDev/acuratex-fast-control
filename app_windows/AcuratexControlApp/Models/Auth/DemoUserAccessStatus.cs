// [ACURATEX] Estado resumido de acceso para los usuarios demo mostrados en UI.
// [FLUJO] Calculo de fechas -> estado -> color o etiqueta visual.
namespace AcuratexControlApp.Models.Auth;

/// <summary>
/// [POR QUÃ‰ EXISTE]
/// Este enum existe para resumir en una sola palabra el estado de acceso de un usuario demo.
///
/// [QUIEN LO USA]
/// Lo usa la pantalla de gestion para pintar estado y color.
///
/// [CUANDO SE USA]
/// Se usa al listar usuarios y calcular vencimiento.
///
/// [ENTRADAS]
/// No recibe entradas.
///
/// [SALIDAS]
/// Expone el estado calculado.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Fecha actual -> calculo de acceso -> estado visible.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una maquina de estados simple con pocos valores.
///
/// [SI NO EXISTIERA]
/// Habria que inferir el estado con multiples banderas o cadenas.
/// </summary>
public enum DemoUserAccessStatus
{
    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este valor existe para indicar que el usuario demo sigue habilitado y utilizable.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la UI de gestion y el calculo de acceso.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando la fecha de vigencia todavia es valida.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el estado activo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Fecha valida -> `Activo` -> acceso normal.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una salida habilitada lista para operar.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podria marcar un usuario como completamente vigente.
    /// </summary>
    Activo,

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este valor existe para indicar que el usuario sigue funcionando pero esta cerca del vencimiento.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usa la UI para advertir al operador.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando faltan pocos dias para expirar.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el estado de aviso previo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Fecha cercana al limite -> `PorVencer` -> advertencia.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una alarma temprana antes del fallo.
    ///
    /// [SI NO EXISTIERA]
    /// El sistema no distinguiria entre vigente y casi vencido.
    /// </summary>
    PorVencer,

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este valor existe para indicar que la cuenta ya no deberia usarse normalmente.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la UI, filtros y validaciones de acceso.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando la fecha de vigencia ya paso.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el estado de vencimiento.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Fecha vencida -> `Vencido` -> acceso restringido.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una condicion de timeout ya superado.
    ///
    /// [SI NO EXISTIERA]
    /// No habria una etiqueta clara para cuentas expiradas.
    /// </summary>
    Vencido,

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este valor existe para marcar una cuenta bloqueada por seguridad.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la UI y las reglas de autorizacion.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando existe un bloqueo manual o automatico.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el estado de bloqueo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Seguridad -> `Bloqueado` -> acceso negado.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un latch de seguridad activado.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria representar un bloqueo separado del vencimiento.
    /// </summary>
    Bloqueado
}
