// [ACURATEX] Interop Win32/WinUSB usado por el transporte USB nativo.
// [FLUJO] WinUsbControllerTransport -> WinUsbNative -> Win32/WinUSB -> dispositivo.
// [EQUIV MCU] Se parece a una capa de registros nativos que permiten hablar con el bus USB.
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Esta clase existe para concentrar las llamadas nativas de Win32 y WinUSB que el
/// transporte USB necesita para abrir el dispositivo, leer y escribir.
///
/// [QUIÉN LA USA]
/// La usa `WinUsbControllerTransport`.
///
/// [CUÁNDO SE USA]
/// Se usa únicamente cuando el transporte elegido es WinUSB.
///
/// [ENTRADAS]
/// Recibe handles, GUIDs, buffers y parámetros de interop.
///
/// [SALIDAS]
/// Expone firmas nativas y estructuras que Windows entiende.
///
/// [EFECTOS SECUNDARIOS]
/// No implementa lógica propia; solo conecta C# con APIs nativas.
///
/// [FLUJO ACURATEX]
/// WinUsbControllerTransport -> WinUsbNative -> Win32/WinUSB.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una capa HAL que traduce llamadas de alto nivel a registros del sistema.
///
/// [SI NO EXISTIERA]
/// El transporte USB tendría que declarar interop repartido por varias clases.
/// </summary>
internal static class WinUsbNative
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para reconocer el codigo de exito que devuelve Configuration Manager.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan las rutinas de enumeracion USB al validar llamadas nativas.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa despues de pedir listas de interfaces USB.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el codigo numerico que significa exito.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Win32 -> `CR_SUCCESS` -> enumeracion valida.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bit o codigo de estado de hardware que confirma una operacion correcta.
    ///
    /// [SI NO EXISTIERA]
    /// El codigo tendria que usar literales numericos repetidos.
    /// </summary>
    internal const int CR_SUCCESS = 0;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para pedir solo las interfaces que realmente estan presentes en Windows.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan las llamadas a Configuration Manager.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al enumerar hardware conectado.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la bandera de consulta para interfaces presentes.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Enumeracion -> `CM_GET_DEVICE_INTERFACE_LIST_PRESENT` -> dispositivos visibles.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a consultar solo los perifericos habilitados y no los reservados.
    ///
    /// [SI NO EXISTIERA]
    /// La consulta podria incluir rutas que no estan activas.
    /// </summary>
    internal const int CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0x00000000;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para pedir acceso de lectura al abrir el handle del dispositivo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `CreateFileW` al abrir un dispositivo WinUSB.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando el transporte necesita leer bytes del dispositivo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la mascara de acceso de lectura.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `GenericRead` -> lectura del endpoint.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a habilitar una UART para leer.
    ///
    /// [SI NO EXISTIERA]
    /// El handle no quedaria abierto con permiso de lectura.
    /// </summary>
    internal const uint GenericRead = 0x80000000;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para pedir acceso de escritura al abrir el handle del dispositivo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `CreateFileW` al preparar salida hacia el dispositivo.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando el transporte necesita escribir bytes al dispositivo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la mascara de acceso de escritura.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `GenericWrite` -> escritura del endpoint.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a habilitar una UART para transmitir.
    ///
    /// [SI NO EXISTIERA]
    /// El handle no quedaria abierto con permiso de escritura.
    /// </summary>
    internal const uint GenericWrite = 0x40000000;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para permitir compartir el handle con otros lectores.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `CreateFileW`.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al abrir el dispositivo para no bloquear otros accesos compatibles.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la mascara de comparticion de lectura.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `FileShareRead` -> acceso compartido.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a permitir que otros modulos consulten un bus sin bloquearlo.
    ///
    /// [SI NO EXISTIERA]
    /// El handle podria abrirse demasiado exclusivo.
    /// </summary>
    internal const uint FileShareRead = 0x00000001;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para permitir compartir el handle con escritores.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `CreateFileW`.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa junto con `FileShareRead` al abrir el dispositivo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la mascara de comparticion de escritura.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `FileShareWrite` -> acceso compartido.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a permitir que otro canal siga usando el recurso mientras este activo.
    ///
    /// [SI NO EXISTIERA]
    /// Otros procesos no podrian abrir el mismo dispositivo en paralelo.
    /// </summary>
    internal const uint FileShareWrite = 0x00000002;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para abrir un recurso que ya debe existir.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `CreateFileW`.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al abrir el dispositivo USB por su ruta.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la disposicion de apertura correcta.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Ruta del dispositivo -> `OpenExisting` -> handle.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a pedir acceso a un periferico que ya esta configurado.
    ///
    /// [SI NO EXISTIERA]
    /// La apertura del dispositivo podria intentar crear algo que no debe crearse.
    /// </summary>
    internal const uint OpenExisting = 3;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para marcar el handle con un atributo normal de archivo.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `CreateFileW`.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al abrir el dispositivo como recurso normal.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el atributo de archivo normal.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `FileAttributeNormal` -> handle normal.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a abrir un recurso de periferico sin banderas especiales.
    ///
    /// [SI NO EXISTIERA]
    /// Habria que elegir otro atributo cada vez que se abre el dispositivo.
    /// </summary>
    internal const uint FileAttributeNormal = 0x00000080;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para permitir operaciones superpuestas en Win32.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan los accesos de lectura y escritura con espera no bloqueante.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando el transporte trabaja con I/O asincrona nativa.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve la bandera overlapped.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `FileFlagOverlapped` -> I/O asincrona.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a disparar una transferencia que luego completa por interrupcion.
    ///
    /// [SI NO EXISTIERA]
    /// El transporte perderia la posibilidad de usar I/O superpuesta.
    /// </summary>
    internal const uint FileFlagOverlapped = 0x40000000;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para reconocer un timeout correctamente reportado por WinUSB.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan las rutinas de lectura y escritura del transporte USB.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando una transferencia tarda demasiado.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el codigo nativo de timeout.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `ErrorSemTimeout` -> manejo de espera.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un timeout de periferico que no entrego datos a tiempo.
    ///
    /// [SI NO EXISTIERA]
    /// Habria que depender de numeros magicos para detectar el timeout.
    /// </summary>
    internal const int ErrorSemTimeout = 121;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para reconocer una operacion abortada por Windows o por cierre.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan las rutinas de lectura al cancelar o desconectar.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando el sistema aborta una transferencia pendiente.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el codigo nativo de operacion abortada.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `ErrorOperationAborted` -> cierre cooperativo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a cancelar una interrupcion o transferencia pendiente.
    ///
    /// [SI NO EXISTIERA]
    /// La cancelacion del transporte seria mas dificil de interpretar.
    /// </summary>
    internal const int ErrorOperationAborted = 995;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para reconocer un fallo generico de la capa nativa.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan los handlers de error del transporte USB.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando la API devuelve un error que no se clasifica mejor.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el codigo de error generico.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `ErrorGenFailure` -> error generico.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un error generico de bus o periferico.
    ///
    /// [SI NO EXISTIERA]
    /// Se perderia una referencia clara para fallos no especificos.
    /// </summary>
    internal const int ErrorGenFailure = 31;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para reconocer que el dispositivo fue desconectado.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan las rutinas de lectura y cierre del transporte.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al perder el enlace fisico con el dispositivo.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el codigo nativo de dispositivo desconectado.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `ErrorDeviceNotConnected` -> desconexion.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a detectar que el cable o el bus ya no esta presente.
    ///
    /// [SI NO EXISTIERA]
    /// La app tendria menos contexto para saber si el dispositivo desaparecio.
    /// </summary>
    internal const int ErrorDeviceNotConnected = 1167;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para reconocer un handle invalido al operar con la API nativa.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan los bloques de manejo de errores del transporte USB.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando el recurso nativo ya no es valido.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el codigo nativo de handle invalido.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `ErrorInvalidHandle` -> limpieza.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a usar una referencia de periférico que ya fue liberada.
    ///
    /// [SI NO EXISTIERA]
    /// No se podria discriminar un handle roto con claridad.
    /// </summary>
    internal const int ErrorInvalidHandle = 6;

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este valor existe para indicar que la politica WinUSB que se quiere tocar es la de timeout.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `WinUsb_SetPipePolicy`.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al configurar el tiempo maximo de espera de un pipe.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador de la politica de timeout.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `PipeTransferTimeoutPolicy` -> timeout de pipe.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un registro de configuracion de timeout de periferico.
    ///
    /// [SI NO EXISTIERA]
    /// El codigo no sabria que politica configurar.
    /// </summary>
    internal const uint PipeTransferTimeoutPolicy = 0x03;

    // [ACURATEX] Descritor que WinUSB devuelve para una interfaz con sus endpoints.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta estructura existe para recibir el descriptor de una interfaz USB tal como lo
    /// entrega WinUSB.
    ///
    /// [QUIÉN LA USA]
    /// La usa `WinUsb_QueryInterfaceSettings()`.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al enumerar los endpoints de una interfaz.
    ///
    /// [ENTRADAS]
    /// No recibe entradas directas; WinUSB la rellena por interop.
    ///
    /// [SALIDAS]
    /// Devuelve campos descriptivos de la interfaz.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `UsbInterfaceDescriptor` -> descubrimiento de pipes.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un bloque descriptivo de registros USB.
    ///
    /// [SI NO EXISTIERA]
    /// El código no tendría una estructura exacta donde WinUSB escriba los datos.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct UsbInterfaceDescriptor
    {
        // [EQUIV MCU] Esta estructura equivale a un bloque descriptivo de registros USB.
        // [C#] Los campos publicos se usan aqui porque WinUSB rellena la estructura completa por interop.
        public byte Length;
        public byte DescriptorType;
        public byte InterfaceNumber;
        public byte AlternateSetting;
        public byte NumEndpoints;
        public byte InterfaceClass;
        public byte InterfaceSubClass;
        public byte InterfaceProtocol;
        public byte Interface;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este enum existe para representar el tipo de pipe que WinUSB reporta en un endpoint.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa `WinUsb_QueryPipe()` y el transporte USB.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al distinguir pipes bulk, interrupt, control o isócronos.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Expone valores cerrados de tipo de canal.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Descriptor USB -> `UsbdPipeType` -> selección de endpoint.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a clasificar canales de comunicación por su función.
    ///
    /// [SI NO EXISTIERA]
    /// El transporte no podría identificar qué tipo de endpoint encontró.
    /// </summary>
    internal enum UsbdPipeType : int
    {
        // [ACURATEX] No se usa para datos de usuario.
        // [C#] El valor numérico del enum debe coincidir con el que espera WinUSB.
        Control,
        // [ACURATEX] Transferencia isócrona.
        Isochronous,
        // [ACURATEX] Canal bulk usado por el tester.
        Bulk,
        // [ACURATEX] Canal de interrupción.
        Interrupt,
    }

    // [ACURATEX] Información de un endpoint WinUSB al consultarlo por índice.
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta estructura existe para almacenar la información de un endpoint WinUSB concreto.
    ///
    /// [QUIÉN LA USA]
    /// La usa `WinUsb_QueryPipe()`.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al recorrer los pipes de una interfaz.
    ///
    /// [ENTRADAS]
    /// No recibe entradas directas.
    ///
    /// [SALIDAS]
    /// Devuelve tipo, ID, tamaño máximo e intervalo.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// WinUSB -> `WinUsbPipeInformation` -> pipe concreto.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un descriptor de canal o FIFO de un periférico USB.
    ///
    /// [SI NO EXISTIERA]
    /// No habría un contenedor tipado para la información de cada endpoint.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WinUsbPipeInformation
    {
        // [ACURATEX] Describe cada pipe que WinUSB reporta para una interfaz concreta.
        // [C#] Esta estructura debe coincidir exactamente con el layout que espera la DLL nativa.
        public UsbdPipeType PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    // [ACURATEX] Pide el tamaño del buffer necesario para listar interfaces USB presentes.
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern int CM_Get_Device_Interface_List_SizeW(
        out uint pulLen,
        ref Guid interfaceClassGuid,
        string? pDeviceID,
        int ulFlags);

    // [ACURATEX] Recupera la lista real de rutas de dispositivos para un GUID de interfaz.
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    internal static extern int CM_Get_Device_Interface_ListW(
        ref Guid interfaceClassGuid,
        string? pDeviceID,
        char[] buffer,
        uint bufferLen,
        int ulFlags);

    // [ACURATEX] Abre el handle nativo del dispositivo.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    // [ACURATEX] Inicializa la sesión WinUSB sobre un handle abierto.
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_Initialize(
        SafeFileHandle deviceHandle,
        out IntPtr interfaceHandle);

    // [ACURATEX] Libera la sesión WinUSB.
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_Free(
        IntPtr interfaceHandle);

    // [ACURATEX] Consulta la descripción de una interfaz WinUSB.
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_QueryInterfaceSettings(
        IntPtr interfaceHandle,
        byte alternateInterfaceNumber,
        out UsbInterfaceDescriptor usbAltInterfaceDescriptor);

    // [ACURATEX] Consulta información de un endpoint dentro de una interfaz.
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_QueryPipe(
        IntPtr interfaceHandle,
        byte alternateInterfaceNumber,
        byte pipeIndex,
        out WinUsbPipeInformation pipeInformation);

    // [ACURATEX] Configura políticas de un pipe, como el timeout.
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_SetPipePolicy(
        IntPtr interfaceHandle,
        byte pipeId,
        uint policyType,
        uint valueLength,
        ref uint value);

    // [ACURATEX] Lee bytes desde un endpoint bulk IN.
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_ReadPipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        int bufferLength,
        out int lengthTransferred,
        IntPtr overlapped);

    // [ACURATEX] Escribe bytes hacia un endpoint bulk OUT.
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_WritePipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        int bufferLength,
        out int lengthTransferred,
        IntPtr overlapped);

    // [ACURATEX] Aborta una operación pendiente en un endpoint.
    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WinUsb_AbortPipe(
        IntPtr interfaceHandle,
        byte pipeId);
}
