namespace AcuratexControlApp.Services;

// [ACURATEX] Este bloque agrupa los tipos de transferencia de archivos del tester.
// [FLUJO] UI -> servicio -> FILE_* -> firmware -> archivos y progreso.
// [EQUIV MCU] Se parece a una tabla de mensajes y resultados de una memoria externa.
// [ACURATEX] Estos registros representan progreso, resultados y listados de archivos del tester.
/// <summary>
/// [POR QUE EXISTE]
/// Este `record` existe para describir el progreso de una subida sin acoplar la UI a la
/// implementacion interna de la transferencia.
///
/// [QUIEN LO USA]
/// Lo usan barras de progreso y pantallas de carga.
///
/// [CUANDO SE USA]
/// Se crea mientras se sube un archivo por chunks.
///
/// [ENTRADAS]
/// Recibe cantidad enviada, total, porcentaje y etapa textual.
///
/// [SALIDAS]
/// Expone el estado actual del progreso.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Servicio de archivos -> `CommandFileUploadProgress` -> UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un contador de bloques enviados en una rutina de escritura.
///
/// [SI NO EXISTIERA]
/// La UI tendria que inventar su propio modelo de progreso.
/// </summary>
public sealed record CommandFileUploadProgress(
    int SentChunks,
    int TotalChunks,
    int ProgressPercent,
    string Stage);

/// <summary>
/// [POR QUE EXISTE]
/// Este `record` existe para resumir el resultado de una subida de archivo al tester.
///
/// [QUIEN LO USA]
/// Lo usan la UI y los servicios que guardan archivos.
///
/// [CUANDO SE USA]
/// Se crea cuando termina una carga de archivo.
///
/// [ENTRADAS]
/// Recibe exito, mensaje, nombre, tamano, chunks e informacion adicional.
///
/// [SALIDAS]
/// Expone el resultado de la transferencia.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Transferencia -> `CommandFileUploadResult` -> UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un estado final de escritura en memoria externa.
///
/// [SI NO EXISTIERA]
/// La UI tendria que separar exito, mensaje y metadatos por su cuenta.
/// </summary>
public sealed record CommandFileUploadResult(
    bool Success,
    string Message,
    string FileName,
    int FileSizeBytes,
    int TotalChunks,
    string? InfoLine);

/// <summary>
/// [POR QUE EXISTE]
/// Este `record` existe para transportar el resumen de un archivo listado en el tester.
///
/// [QUIEN LO USA]
/// Lo usan las pantallas que enumeran archivos remotos.
///
/// [CUANDO SE USA]
/// Se crea al consultar metadatos de un archivo.
///
/// [ENTRADAS]
/// Recibe nombre, tamaño, selección y la linea cruda del firmware.
///
/// [SALIDAS]
/// Expone esos metadatos.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Firmware -> `CommandFileInfo` -> lista enriquecida en UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una estructura de directorio con atributos basicos.
///
/// [SI NO EXISTIERA]
/// La UI solo tendria el nombre de archivo y nada mas.
/// </summary>
public sealed record CommandFileInfo(
    string FileName,
    int SizeBytes,
    bool IsSelected,
    string RawLine);

/// <summary>
/// [POR QUE EXISTE]
/// Este `record` existe para mostrar una fila de archivo del tester con contexto adicional.
///
/// [QUIEN LO USA]
/// Lo usan vistas de archivos y editores.
///
/// [CUANDO SE USA]
/// Se crea al listar archivos con informacion opcional.
///
/// [ENTRADAS]
/// Recibe nombre, tamaño, seleccion, linea cruda y fecha local opcional.
///
/// [SALIDAS]
/// Expone los datos de la fila.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Firmware -> `TesterFileEntry` -> tabla visible.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una fila de inventario con algunos campos opcionales.
///
/// [SI NO EXISTIERA]
/// La vista no podria representar archivos remotos con contexto local.
/// </summary>
public sealed record TesterFileEntry(
    string FileName,
    int? SizeBytes,
    bool IsSelected,
    string? InfoRaw,
    DateTime? LocalDownloadedAt = null);

/// <summary>
/// [POR QUE EXISTE]
/// Este `record` existe para devolver el resultado completo de descargar un archivo.
///
/// [QUIEN LO USA]
/// Lo usan editores, vistas previas y rutas de copia local.
///
/// [CUANDO SE USA]
/// Se crea al terminar una descarga de archivo.
///
/// [ENTRADAS]
/// Recibe exito, error, nombre, bytes, texto y ruta temporal.
///
/// [SALIDAS]
/// Expone los datos descargados y la copia temporal.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene por si mismo; la descarga real ya ocurrio antes.
///
/// [FLUJO ACURATEX]
/// DownloadFileAsync -> `CommandFileDownloadResult` -> editor/UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece a un buffer de lectura con una copia temporal ya preparada.
///
/// [SI NO EXISTIERA]
/// La UI no tendria un contenedor unico para bytes, texto y ruta local.
/// </summary>
public sealed record CommandFileDownloadResult(
    bool Success,
    string? ErrorMessage,
    string FileName,
    byte[] Bytes,
    string Text,
    string TempPath);

/// <summary>
/// [POR QUE EXISTE]
/// Este `record` existe para resumir el resultado de guardar un archivo editado.
///
/// [QUIEN LO USA]
/// Lo usan editores y validaciones de guardado.
///
/// [CUANDO SE USA]
/// Se crea cuando termina una escritura al tester.
///
/// [ENTRADAS]
/// Recibe exito, error, nombre, tamaño y ruta temporal.
///
/// [SALIDAS]
/// Expone el resultado del guardado.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene por si mismo.
///
/// [FLUJO ACURATEX]
/// SaveEditedTextAsync -> `CommandFileSaveResult` -> UI.
///
/// [EQUIVALENCIA MCU]
/// Se parece al retorno de una rutina de escritura en flash con metadatos.
///
/// [SI NO EXISTIERA]
/// La UI no tendria un resumen unico del guardado.
/// </summary>
public sealed record CommandFileSaveResult(
    bool Success,
    string? ErrorMessage,
    string FileName,
    int SizeBytes,
    string? TempPath);

/// <summary>
/// [POR QUE EXISTE]
/// Este contrato existe para unificar todas las operaciones de archivos del tester: listar,
/// descargar, subir, seleccionar, obtener info y borrar.
///
/// [QUIEN LO USA]
/// Lo usan las pantallas de edicion, administracion y seleccion de archivos.
///
/// [CUANDO SE USA]
/// Se usa cada vez que la UI necesita hablar con FILE_*.
///
/// [ENTRADAS]
/// Recibe nombres, bytes, texto, progreso y cancelacion.
///
/// [SALIDAS]
/// Devuelve resultados de carga, descarga, lista o guardado.
///
/// [EFECTOS SECUNDARIOS]
/// Puede enviar comandos de archivo al firmware y actualizar caches locales.
///
/// [FLUJO ACURATEX]
/// UI -> `ICommandFileTransferService` -> FILE_* -> firmware.
///
/// [EQUIVALENCIA MCU]
/// Se parece a la interfaz de un filesystem remoto sobre un bus serial.
///
/// [SI NO EXISTIERA]
/// Cada pantalla tendria que hablar directamente con el protocolo de archivos.
/// </summary>
public interface ICommandFileTransferService
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para subir un archivo mínimo de prueba y verificar que la ruta
    /// FILE_* funciona sin depender de contenido real complejo.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI o herramientas internas de diagnóstico.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando hace falta comprobar el canal de archivos.
    ///
    /// [ENTRADAS]
    /// Puede recibir progreso y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve un resultado de carga.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía comandos `FILE_BEGIN`, `FILE_DATA`, `FILE_END` y `FILE_SELECT`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> servicio de archivos -> conexión -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cargar una trama de prueba para validar un canal de almacenamiento.
    ///
    /// [SI NO EXISTIERA]
    /// No habría un test rápido del protocolo de archivos.
    /// </summary>
    Task<CommandFileUploadResult> UploadMinimalTestFileAsync(
        IProgress<CommandFileUploadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para subir un texto arbitrario como archivo al tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman la UI de editor y los flujos de guardado.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario sube un TXT.
    ///
    /// [ENTRADAS]
    /// Recibe nombre, bytes, progreso opcional y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve un resultado de carga.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Manda el archivo por chunks Base64 al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> servicio -> FILE_BEGIN/FILE_DATA/FILE_END -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a escribir una imagen o tabla en memoria externa por bloques.
    ///
    /// [SI NO EXISTIERA]
    /// No habría carga de archivos de texto al tester.
    /// </summary>
    Task<CommandFileUploadResult> UploadTextFileAsync(
        string fileName,
        byte[] fileBytes,
        IProgress<CommandFileUploadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para enumerar los nombres de archivos del tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI para refrescar la lista remota.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando hay que reconstruir el listado de archivos.
    ///
    /// [ENTRADAS]
    /// Recibe cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve nombres de archivo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Hace una consulta al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ListFilesAsync -> firmware -> lista de nombres.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer el directorio de una memoria de almacenamiento.
    ///
    /// [SI NO EXISTIERA]
    /// La pantalla no podría mostrar archivos remotos.
    /// </summary>
    Task<IReadOnlyList<string>> ListFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para listar archivos del tester con más detalle que solo el nombre.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman pantallas de administración de archivos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al abrir la vista de archivos del tester.
    ///
    /// [ENTRADAS]
    /// Recibe cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve entradas con información adicional.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Consulta el firmware por cada archivo.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> ListTesterFilesAsync -> firmware -> filas enriquecidas.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a consultar metadatos de archivos en un sistema embebido.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría menos contexto para editar o seleccionar archivos.
    /// </summary>
    Task<IReadOnlyList<TesterFileEntry>> ListTesterFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para descargar un archivo completo del tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman editores, previews y flujos de copia local.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario abre o copia un archivo remoto.
    ///
    /// [ENTRADAS]
    /// Recibe el nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve bytes, texto y ruta temporal.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Genera una copia local en caché.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> DownloadFileAsync -> FILE_GET/FILE_GET_NEXT -> archivo local.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un bloque de memoria externa por segmentos.
    ///
    /// [SI NO EXISTIERA]
    /// La app no podría abrir archivos para editar o visualizar.
    /// </summary>
    Task<CommandFileDownloadResult> DownloadFileAsync(
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para guardar texto editado de vuelta en el tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI de edición.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al pulsar guardar en el editor.
    ///
    /// [ENTRADAS]
    /// Recibe nombre, texto, progreso y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve el resultado del guardado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía el archivo al firmware y actualiza la caché temporal.
    ///
    /// [FLUJO ACURATEX]
    /// Editor -> SaveEditedTextAsync -> FILE_* -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a reescribir una memoria de almacenamiento con datos nuevos.
    ///
    /// [SI NO EXISTIERA]
    /// El editor no podría persistir cambios en el tester.
    /// </summary>
    Task<CommandFileSaveResult> SaveEditedTextAsync(
        string fileName,
        string text,
        IProgress<CommandFileUploadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para marcar un archivo como seleccionado en el tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman flujos de selección de archivo.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario elige un archivo remoto.
    ///
    /// [ENTRADAS]
    /// Recibe nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `bool` según si el tester confirmó.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Cambia el archivo activo en firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> SelectFileAsync -> FILE_SELECT -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a seleccionar cuál banco de memoria queda activo.
    ///
    /// [SI NO EXISTIERA]
    /// No habría forma de marcar el archivo activo sin borrar o subir.
    /// </summary>
    Task<bool> SelectFileAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para seleccionar un programa de cabezal en el firmware.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman los servicios de perfil al aplicar un programa.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario activa un programa de cabezal.
    ///
    /// [ENTRADAS]
    /// Recibe nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `bool`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Ejecuta `HEAD_PROGRAM_SELECT|...` en firmware.
    ///
    /// [FLUJO ACURATEX]
    /// Perfil -> SelectHeadProgramAsync -> HEAD_PROGRAM_SELECT -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a seleccionar un programa activo en una EEPROM o flash.
    ///
    /// [SI NO EXISTIERA]
    /// El perfil de cabezal no podría activarse desde el tester.
    /// </summary>
    Task<bool> SelectHeadProgramAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para consultar metadatos de un archivo del tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llaman pantallas y servicios que necesitan tamaño o selección.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta bajo demanda.
    ///
    /// [ENTRADAS]
    /// Recibe nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve datos del archivo o `null`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Consulta al firmware.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> GetFileInfoAsync -> firmware -> información de archivo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un descriptor o cabecera de archivo desde memoria.
    ///
    /// [SI NO EXISTIERA]
    /// No habría forma de saber tamaño y estado de un archivo remoto.
    /// </summary>
    Task<CommandFileInfo?> GetFileInfoAsync(string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta operación existe para borrar un archivo del tester.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI de administración de archivos.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario confirma eliminación.
    ///
    /// [ENTRADAS]
    /// Recibe nombre y cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve `bool`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Ejecuta `FILE_DELETE|...`.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> DeleteFileAsync -> FILE_DELETE -> firmware.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a borrar un archivo en una memoria de almacenamiento.
    ///
    /// [SI NO EXISTIERA]
    /// No se podrían limpiar archivos obsoletos del tester.
    /// </summary>
    Task<bool> DeleteFileAsync(string fileName, CancellationToken cancellationToken = default);
}
