// [ACURATEX] Identificadores estables de los roles del sistema demo.
// [FLUJO] Sesión -> comparación de rol -> reglas de acceso.
namespace AcuratexControlApp.Models.Auth;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para dar nombres fijos y faciles de comparar a los roles demo.
///
/// [QUIEN LA USA]
/// La usan permisos, sesiones y validaciones de UI.
///
/// [CUANDO SE USA]
/// Se usa cada vez que la app compara un rol.
///
/// [ENTRADAS]
/// No recibe entradas.
///
/// [SALIDAS]
/// Expone cadenas constantes.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Sesion -> comparacion -> habilitar o bloquear acciones.
///
/// [EQUIVALENCIA MCU]
/// Se parece a definir IDs simbolicos para modos de operacion.
///
/// [SI NO EXISTIERA]
/// La aplicacion tendria que comparar nombres de rol dispersos.
/// </summary>
public static class AppRoleIds
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para representar el rol más alto de la demo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan permisos, sesión y reglas de jerarquía.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al comparar autoridad máxima.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador del rol maestro.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// `SuperRoot` -> permisos máximos.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al modo privilegiado de fábrica o mantenimiento.
    ///
    /// [SI NO EXISTIERA]
    /// No habría un identificador fijo para la cuenta maestra.
    /// </summary>
    public const string SuperRoot = "SUPER_ROOT";
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para representar el rol técnico administrador.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan permisos y validaciones de jerarquía.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando hay que comparar un rol intermedio de alta confianza.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el ID del rol administrativo técnico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// `AdminTecnico` -> permisos altos -> reglas especiales.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un supervisor de línea con facultad de ajuste.
    ///
    /// [SI NO EXISTIERA]
    /// La jerarquía intermedia no tendría un nombre estable.
    /// </summary>
    public const string AdminTecnico = "ADMIN_TECNICO";
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para representar el rol operativo básico.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la sesión, permisos y filtros de UI.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa como nivel técnico estándar.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el ID del rol operativo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// `Tecnico` -> permisos base -> operación normal.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al perfil de operador normal de una máquina.
    ///
    /// [SI NO EXISTIERA]
    /// La app no tendría un nombre común para el rol base.
    /// </summary>
    public const string Tecnico = "TECNICO";
}
