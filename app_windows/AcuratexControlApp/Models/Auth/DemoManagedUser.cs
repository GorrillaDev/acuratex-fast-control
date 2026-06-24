// [ACURATEX] Usuario demo editable o visualizable en la gestion de usuarios.
namespace AcuratexControlApp.Models.Auth;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para representar a un usuario de demo con datos de gestion.
///
/// [QUIEN LA USA]
/// La usan la pantalla de administracion y los servicios de demo.
///
/// [CUANDO SE USA]
/// Se usa al listar usuarios, calcular estado y mostrar estadisticas.
///
/// [ENTRADAS]
/// Recibe identidad, rol, fechas, horas y contadores.
///
/// [SALIDAS]
/// Devuelve un objeto de datos para la UI.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene logica de negocio propia.
///
/// [FLUJO ACURATEX]
/// Servicio demo -> `DemoManagedUser` -> pantalla de gestion.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una ficha de operario con metadatos de mantenimiento.
///
/// [SI NO EXISTIERA]
/// La UI tendria que construir esa ficha a partir de muchos campos sueltos.
/// </summary>
public sealed class DemoManagedUser
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para identificar de forma única la ficha del usuario demo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan filtros, listas y edición.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al actualizar o comparar usuarios.
    ///
    /// [ENTRADAS]
    /// Recibe un ID textual.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador del registro.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Gestión -> `Id` -> selección y edición.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al número de serie de una ficha de operario.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podría distinguir fichas iguales por nombre.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para buscar al usuario por su nombre de acceso.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan filtros y formularios.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al listar o buscar usuarios demo.
    ///
    /// [ENTRADAS]
    /// Recibe un texto de username.
    ///
    /// [SALIDAS]
    /// Devuelve el nombre técnico de acceso.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Gestión -> `Username` -> búsqueda.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nombre de una cuenta registrada en una EEPROM.
    ///
    /// [SI NO EXISTIERA]
    /// La búsqueda tendría que usar solo el nombre visible.
    /// </summary>
    public required string Username { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para mostrar el nombre legible del usuario demo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan tablas, tarjetas y resúmenes.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al renderizar la ficha visual.
    ///
    /// [ENTRADAS]
    /// Recibe un nombre visible.
    ///
    /// [SALIDAS]
    /// Devuelve el nombre para la interfaz.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Datos -> `DisplayName` -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a la etiqueta visible de un operador en una pantalla HMI.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que mostrar solo el username.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para enlazar el usuario demo con su nivel de acceso.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan permisos, filtros y ordenamiento.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al decidir visibilidad y acciones permitidas.
    ///
    /// [ENTRADAS]
    /// Recibe un identificador de rol.
    ///
    /// [SALIDAS]
    /// Devuelve el rol asignado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Registro demo -> `RoleId` -> jerarquía.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al selector de modo que cambia qué periféricos quedan activos.
    ///
    /// [SI NO EXISTIERA]
    /// No podría saberse qué nivel de acceso tiene el usuario.
    /// </summary>
    public required string RoleId { get; init; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para indicar si el usuario demo puede usarse o debe quedar visible
    /// pero inactivo.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la UI de gestion y los filtros de estado.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa al pintar el listado y al decidir si la cuenta aparece habilitada.
    ///
    /// [ENTRADAS]
    /// Recibe `true` o `false`.
    ///
    /// [SALIDAS]
    /// Devuelve el estado de habilitacion de la ficha.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Permite que la pantalla simule cambios de activacion.
    ///
    /// [FLUJO ACURATEX]
    /// Gestion -> `Enabled` -> estado visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit de habilitacion de periferico.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria distinguir entre un usuario activo y uno deshabilitado.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para marcar una cuenta como bloqueada por seguridad o por demo.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la vista de usuarios, las validaciones y los indicadores de riesgo.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando la app necesita impedir el uso normal de una cuenta.
    ///
    /// [ENTRADAS]
    /// Recibe un valor booleano.
    ///
    /// [SALIDAS]
    /// Devuelve si la cuenta esta bloqueada o no.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede cambiar la apariencia de la ficha en pantalla.
    ///
    /// [FLUJO ACURATEX]
    /// Seguridad -> `Locked` -> bloqueo visual y funcional.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un flag de interlock o proteccion.
    ///
    /// [SI NO EXISTIERA]
    /// No habria forma de representar un bloqueo temporal o permanente.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para registrar la fecha de alta del usuario demo.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la UI, ordenamientos y calculos de antiguedad.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa al mostrar historial y tiempo de permanencia.
    ///
    /// [ENTRADAS]
    /// Recibe una marca temporal.
    ///
    /// [SALIDAS]
    /// Devuelve la fecha de creacion del registro.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Alta -> `CreatedAt` -> historial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al instante en que un equipo fue registrado en memoria.
    ///
    /// [SI NO EXISTIERA]
    /// No podria calcularse antiguedad del usuario.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para guardar la fecha en que el usuario demo deja de ser valido.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan la pantalla de gestion y las reglas de vencimiento.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa al calcular si la cuenta sigue vigente.
    ///
    /// [ENTRADAS]
    /// Recibe una fecha futura o pasada.
    ///
    /// [SALIDAS]
    /// Devuelve la fecha de vencimiento.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Permite simular renovacion manual desde la UI.
    ///
    /// [FLUJO ACURATEX]
    /// Gestion -> `ExpiresAt` -> vigencia.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una fecha de caducidad cargada en una tabla.
    ///
    /// [SI NO EXISTIERA]
    /// No habria referencia temporal para saber si el usuario ya vencio.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para registrar el ultimo acceso de la cuenta.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan auditoria, ordenamiento y resumenes visuales.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa cuando la app muestra actividad reciente.
    ///
    /// [ENTRADAS]
    /// Recibe una fecha y hora o puede quedar vacia.
    ///
    /// [SALIDAS]
    /// Devuelve la marca temporal del ultimo login.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Login -> `LastLoginAt` -> historial.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al timestamp del ultimo paquete recibido.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podria mostrar la ultima actividad.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para acumular horas de trabajo mostradas en resumenes.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan tarjetas, tablas y calculos estadisticos.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa al representar progreso o antiguedad operacional.
    ///
    /// [ENTRADAS]
    /// Recibe un numero decimal.
    ///
    /// [SALIDAS]
    /// Devuelve las horas acumuladas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede ser modificado por la UI o por calculos de demo.
    ///
    /// [FLUJO ACURATEX]
    /// Tiempo -> `WorkedHours` -> resumen.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un contador de tiempo de funcionamiento.
    ///
    /// [SI NO EXISTIERA]
    /// La vista no podria mostrar horas acumuladas.
    /// </summary>
    public double WorkedHours { get; set; }

    /// <summary>
    /// [POR QUÃ‰ EXISTE]
    /// Este campo existe para contar cuantas veces se ha usado la cuenta demo.
    ///
    /// [QUIÃ‰N LO USA]
    /// Lo usan resumenes y estadisticas de uso.
    ///
    /// [CUÃNDO SE USA]
    /// Se usa al mostrar actividad historica.
    ///
    /// [ENTRADAS]
    /// Recibe un entero.
    ///
    /// [SALIDAS]
    /// Devuelve el total de sesiones registradas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede aumentar cuando la UI simula nuevas sesiones.
    ///
    /// [FLUJO ACURATEX]
    /// Sesion -> `SessionCount` -> estadistica.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un contador de ciclos o arranques.
    ///
    /// [SI NO EXISTIERA]
    /// No podria mostrarse la frecuencia de uso del usuario.
    /// </summary>
    public int SessionCount { get; set; }
}
