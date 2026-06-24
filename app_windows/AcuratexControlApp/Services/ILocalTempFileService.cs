// [ACURATEX] Servicio de acceso a la cache local de archivos de firmware y comandos.
namespace AcuratexControlApp.Services;

// [ACURATEX] Este contrato separa la UI de la carpeta temporal donde viven copias locales de archivos.
// [FLUJO] UI -> servicio local -> disco temporal -> vistas y subida posterior.
// [EQUIV MCU] Se parece a un buffer de almacenamiento local que la HMI puede inspeccionar.
// [ACURATEX] Este contrato abstrae la carpeta temporal local que usa la app como cache.
/// <summary>
/// [POR QUE EXISTE]
/// Este `record` existe para representar una fila de archivo temporal con metadatos de
/// validacion y estado.
///
/// [QUIEN LO USA]
/// Lo usan la UI de archivos locales y los flujos de subida.
///
/// [CUANDO SE USA]
/// Se crea al listar la caché temporal.
///
/// [ENTRADAS]
/// Recibe nombre, ruta, tamaño, fecha, validez y banderas de estado.
///
/// [SALIDAS]
/// Expone los datos de una fila local.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Disco temporal -> `LocalTempFileEntry` -> tabla visible.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una estructura de inventario de bloques locales con estado de uso.
///
/// [SI NO EXISTIERA]
/// La UI tendria que armar su propia fila con varios campos sueltos.
/// </summary>
public sealed record LocalTempFileEntry(
    string FileName,
    string FullPath,
    long SizeBytes,
    DateTime LastModified,
    bool IsValid,
    string? ValidationError,
    bool ExistsOnTester = false,
    bool IsSelected = false,
    string UploadState = "Nuevo");

/// <summary>
/// [POR QUE EXISTE]
/// Este contrato existe para unificar el acceso a la carpeta temporal local donde la app guarda
/// copias de trabajo de archivos del tester.
///
/// [QUIEN LO USA]
/// Lo usan las pantallas de archivos y los flujos de copia/edicion.
///
/// [CUANDO SE USA]
/// Se usa cuando la UI necesita listar, abrir, copiar o leer copias temporales.
///
/// [ENTRADAS]
/// Recibe nombres de archivo y cancelacion segun el metodo.
///
/// [SALIDAS]
/// Devuelve rutas, listas o bytes segun la operacion.
///
/// [EFECTOS SECUNDARIOS]
/// Puede crear la carpeta temporal, abrir el explorador o escribir en el portapapeles.
///
/// [FLUJO ACURATEX]
/// UI -> `ILocalTempFileService` -> carpeta temporal -> usuario o editor.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una pequeña RAM de trabajo accesible desde la HMI.
///
/// [SI NO EXISTIERA]
/// La UI tendria que manejar la carpeta temporal directamente.
/// </summary>
public interface ILocalTempFileService
{
    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para devolver la ruta base de la caché temporal.
    ///
    /// [QUIEN LO USA]
    /// Lo usan la UI y otros servicios que necesitan construir rutas.
    ///
    /// [CUANDO SE USA]
    /// Se consulta cuando hace falta ubicar archivos temporales.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la ruta local de la carpeta temporal.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Servicio -> `GetTempFolderPath()` -> ruta base.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer la direccion base de un buffer local.
    ///
    /// [SI NO EXISTIERA]
    /// Cada parte del codigo tendria que reconstruir la ruta base.
    /// </summary>
    string GetTempFolderPath();

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para garantizar que la carpeta temporal exista antes de usarla.
    ///
    /// [QUIEN LO USA]
    /// Lo usan las operaciones de listado, lectura y copia.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta antes de tocar archivos locales.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// No devuelve valor.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede crear la carpeta en disco.
    ///
    /// [FLUJO ACURATEX]
    /// Operacion local -> `EnsureTempFolder()` -> carpeta lista.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a reservar RAM o flash antes de escribir.
    ///
    /// [SI NO EXISTIERA]
    /// Cada operacion tendria que comprobar y crear la carpeta por su cuenta.
    /// </summary>
    void EnsureTempFolder();

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para listar los archivos temporales locales con sus metadatos.
    ///
    /// [QUIEN LO USA]
    /// Lo usan las pantallas de archivos.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta al abrir o refrescar la cache local.
    ///
    /// [ENTRADAS]
    /// Recibe cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve una lista de entradas locales.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede crear la carpeta temporal.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ListTempFilesAsync()` -> disco temporal -> lista.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a recorrer un directorio local y leer sus atributos.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla no podria mostrar la cache local.
    /// </summary>
    Task<IReadOnlyList<LocalTempFileEntry>> ListTempFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para abrir el explorador de Windows en la carpeta temporal.
    ///
    /// [QUIEN LO USA]
    /// Lo usan botones de soporte y diagnostico.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta cuando el usuario quiere ver la carpeta de trabajo.
    ///
    /// [ENTRADAS]
    /// Recibe cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea completada cuando el shell ya se lanzo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Puede abrir una ventana externa del explorador.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `OpenTempFolderAsync()` -> Explorer -> carpeta local.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a abrir una consola de soporte en una zona de memoria.
    ///
    /// [SI NO EXISTIERA]
    /// El usuario tendria que navegar manualmente hasta la carpeta.
    /// </summary>
    Task OpenTempFolderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para copiar la ruta de la carpeta temporal al portapapeles.
    ///
    /// [QUIEN LO USA]
    /// Lo usan usuarios y soporte tecnico.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta al pulsar copiar ruta.
    ///
    /// [ENTRADAS]
    /// Recibe cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve una tarea completada cuando el texto ya quedo copiado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Escribe en el portapapeles de Windows.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `CopyTempFolderPathAsync()` -> portapapeles.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a copiar una direccion interna para usarla en otra herramienta.
    ///
    /// [SI NO EXISTIERA]
    /// El usuario tendria que escribir la ruta manualmente.
    /// </summary>
    Task CopyTempFolderPathAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUE EXISTE]
    /// Este metodo existe para leer bytes de un archivo temporal ya validado.
    ///
    /// [QUIEN LO USA]
    /// Lo usan editores y flujos de reenvio.
    ///
    /// [CUANDO SE USA]
    /// Se ejecuta cuando hay que volver a cargar un TXT local.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre del archivo y cancelacion.
    ///
    /// [SALIDAS]
    /// Devuelve el contenido en bytes.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Lee desde disco.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> `ReadTempFileBytesAsync()` -> bytes locales.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un buffer de trabajo desde RAM o flash.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no podria volver a cargar un archivo temporal.
    /// </summary>
    Task<byte[]> ReadTempFileBytesAsync(string fileName, CancellationToken cancellationToken = default);
}
