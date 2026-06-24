// [ACURATEX] Definicion inmutable de un rol y su lista de permisos.
namespace AcuratexControlApp.Models.Auth;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para describir un rol completo con sus permisos y jerarquia.
///
/// [QUIEN LA USA]
/// La usan servicios de roles, permisos y autenticacion.
///
/// [CUANDO SE USA]
/// Se usa al cargar permisos y al renderizar opciones segun rol.
///
/// [ENTRADAS]
/// Recibe metadatos de rol y la lista de permisos.
///
/// [SALIDAS]
/// Devuelve un objeto inmutable de definicion.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Catálogo de roles -> `RoleDefinition` -> permisos/visibilidad.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una ficha de configuracion con prioridad y bits de permiso.
///
/// [SI NO EXISTIERA]
/// Los roles tendrian que repartirse en varias estructuras separadas.
/// </summary>
public sealed class RoleDefinition
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para identificar de forma única el rol.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan permisos, sesión y pantallas de administración.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al comparar, buscar o mostrar un rol.
    ///
    /// [ENTRADAS]
    /// Recibe un identificador textual.
    ///
    /// [SALIDAS]
    /// Devuelve el ID del rol.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Catálogo de roles -> `Id` -> reglas.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a la dirección lógica de un bloque funcional.
    ///
    /// [SI NO EXISTIERA]
    /// No se podría distinguir un rol de otro con certeza.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para mostrar el nombre legible del rol.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan menús, tablas y mensajes de autorización.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al pintar el catálogo o los mensajes de permiso.
    ///
    /// [ENTRADAS]
    /// Recibe un texto humano.
    ///
    /// [SALIDAS]
    /// Devuelve el nombre visible.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Rol -> `Name` -> etiqueta visible.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nombre de una función de alto nivel frente a su dirección interna.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que mostrar solo el ID técnico.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para explicar en lenguaje humano qué hace el rol.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan pantallas administrativas y herramientas de soporte.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando el operador necesita entender la intención del rol.
    ///
    /// [ENTRADAS]
    /// Recibe un texto descriptivo.
    ///
    /// [SALIDAS]
    /// Devuelve la descripción del rol.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Rol -> `Description` -> ayuda visual.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al comentario técnico que explica una máscara de bits.
    ///
    /// [SI NO EXISTIERA]
    /// Los roles serían más difíciles de entender para un operador nuevo.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para marcar si el rol forma parte de la base del sistema.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan administracion, permisos y pantallas de edicion.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al decidir si un rol puede editarse o borrarse.
    ///
    /// [ENTRADAS]
    /// Recibe `true` o `false`.
    ///
    /// [SALIDAS]
    /// Devuelve la marca de rol de sistema.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Rol -> `IsSystemRole` -> proteccion de edicion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una bandera de configuracion inmutable.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria proteger un rol base del sistema.
    /// </summary>
    public bool IsSystemRole { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para indicar si la UI o el gestor pueden editar el rol.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan formularios y reglas de administracion.
    ///
    /// [CUÁNDO SE USA]
    /// Se consulta al mostrar opciones de edicion.
    ///
    /// [ENTRADAS]
    /// Recibe `true` o `false`.
    ///
    /// [SALIDAS]
    /// Devuelve la marca de editabilidad.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Rol -> `IsEditable` -> controles visibles.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un permiso de escritura sobre una tabla de configuracion.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabria si un rol puede editarse.
    /// </summary>
    public bool IsEditable { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para ordenar roles por jerarquía.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan servicios de roles, permisos y listas de administración.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al comparar privilegios o mostrar el catálogo ordenado.
    ///
    /// [ENTRADAS]
    /// Recibe un número de nivel.
    ///
    /// [SALIDAS]
    /// Devuelve la prioridad del rol.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Rol -> `HierarchyLevel` -> orden y jerarquía.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nivel de prioridad de una interrupción o supervisor.
    ///
    /// [SI NO EXISTIERA]
    /// El sistema no podría comparar autoridad entre roles.
    /// </summary>
    public int HierarchyLevel { get; init; }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar la lista de permisos que define el rol.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan el servicio de permisos y la UI.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al decidir si una acción está habilitada.
    ///
    /// [ENTRADAS]
    /// Recibe una colección de permisos simbólicos.
    ///
    /// [SALIDAS]
    /// Devuelve la máscara lógica de permisos.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Rol -> `Permissions` -> validación de acceso.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una tabla de bits que habilita periféricos.
    ///
    /// [SI NO EXISTIERA]
    /// Los permisos tendrían que consultarse en otro lugar.
    /// </summary>
    public IReadOnlyCollection<string> Permissions { get; init; } = Array.Empty<string>();
}
