// [ACURATEX] Resultado de un anuncio UDP de un tester Acuratex.
using System.Net;

namespace AcuratexControlApp;

/// <summary>
/// [POR QUÉ EXISTE]
/// Este record existe para transportar la informacion de un dispositivo descubierto por red.
///
/// [QUIEN LO USA]
/// Lo usan el descubridor de red y la UI de conexion.
///
/// [CUANDO SE USA]
/// Se usa al listar equipos encontrados por UDP.
///
/// [ENTRADAS]
/// Recibe host, puerto, nombre y endpoint remoto.
///
/// [SALIDAS]
/// Devuelve un objeto de solo datos.
///
/// [EFECTOS SECUNDARIOS]
/// No tiene.
///
/// [FLUJO ACURATEX]
/// Red -> descubrimiento -> `AcuratexNetworkDeviceInfo` -> selector de conexion.
///
/// [EQUIVALENCIA MCU]
/// Se parece a una estructura de descubrimiento de nodos en una red CAN o serial.
///
/// [SI NO EXISTIERA]
/// La UI tendria que manejar varios campos sueltos al mismo tiempo.
/// </summary>
public sealed record AcuratexNetworkDeviceInfo(
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar la dirección IP que la UI usará al conectar por TCP.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la pantalla de conexión y el controlador TCP.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando un equipo se descubrió por red.
    ///
    /// [ENTRADAS]
    /// Recibe una IP anunciada o detectada.
    ///
    /// [SALIDAS]
    /// Devuelve el host donde conectar.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Discovery -> `Host` -> conexión TCP.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a la dirección de un nodo en una red de buses.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que reconstruir la IP desde el anuncio cada vez.
    /// </summary>
    string Host,
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar el puerto TCP anunciado por el firmware.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan la UI y el transporte TCP.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al abrir la conexión de red.
    ///
    /// [ENTRADAS]
    /// Recibe un puerto válido.
    ///
    /// [SALIDAS]
    /// Devuelve el puerto remoto.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Discovery -> `TcpPort` -> `TcpClient`.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al canal de comunicación asignado a un periférico.
    ///
    /// [SI NO EXISTIERA]
    /// La app no sabría a qué puerto conectarse.
    /// </summary>
    int TcpPort,
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar el nombre de host detectado o anunciado.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan etiquetas visuales y diagnósticos.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al mostrar el dispositivo en listas.
    ///
    /// [ENTRADAS]
    /// Recibe un nombre de host.
    ///
    /// [SALIDAS]
    /// Devuelve el hostname visible.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Discovery -> `Hostname` -> lista amigable.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nombre lógico de un nodo de red.
    ///
    /// [SI NO EXISTIERA]
    /// La lista de descubrimiento perdería una pista útil de identificación.
    /// </summary>
    string Hostname,
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para mostrar un nombre amigable al operador.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa la UI de conexión.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al presentar resultados de discovery.
    ///
    /// [ENTRADAS]
    /// Recibe una etiqueta visual.
    ///
    /// [SALIDAS]
    /// Devuelve un nombre claro para el usuario.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Discovery -> `Name` -> selector de conexión.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un alias de dispositivo en una consola de diagnóstico.
    ///
    /// [SI NO EXISTIERA]
    /// El operador vería solo datos técnicos.
    /// </summary>
    string Name,
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para mostrar el SSID asociado al dispositivo o a su red.
    ///
    /// [QUIÉN LO USA]
    /// Lo usan diagnósticos y la UI de conexión.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa al listar equipos encontrados por red.
    ///
    /// [ENTRADAS]
    /// Recibe el SSID o una cadena vacía.
    ///
    /// [SALIDAS]
    /// Devuelve el identificador de red.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Discovery -> `Ssid` -> contexto de red.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al nombre de la red en un módulo WiFi.
    ///
    /// [SI NO EXISTIERA]
    /// Se perdería una pista sobre dónde fue encontrado el equipo.
    /// </summary>
    string Ssid,
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este campo existe para guardar el endpoint que respondió al discovery.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa el diagnóstico y, a veces, el selector de red.
    ///
    /// [CUÁNDO SE USA]
    /// Se usa cuando conviene saber desde qué IP respondió el equipo.
    ///
    /// [ENTRADAS]
    /// Recibe un endpoint de red.
    ///
    /// [SALIDAS]
    /// Devuelve el endpoint remoto.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Discovery UDP -> `RemoteEndpoint` -> trazabilidad.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece al origen físico de una trama recibida por un bus.
    ///
    /// [SI NO EXISTIERA]
    /// Se perdería el dato de origen de la respuesta de discovery.
    /// </summary>
    IPEndPoint RemoteEndpoint)
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Este método existe para mostrar el dispositivo en una línea corta y legible.
    ///
    /// [QUIÉN LO USA]
    /// Lo usa la lista de resultados de discovery.
    ///
    /// [CUÁNDO SE USA]
    /// Se ejecuta cuando la UI necesita un texto breve.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una cadena resumida.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// `AcuratexNetworkDeviceInfo` -> `ToString()` -> lista.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una función de diagnóstico que imprime el nodo con su dirección.
    ///
    /// [SI NO EXISTIERA]
    /// La UI tendría que construir el texto breve por su cuenta.
    /// </summary>
    // [ACURATEX] Devuelve un texto corto para mostrar el dispositivo en listas.
    public override string ToString()
    {
        // [FLUJO] La lista de dispositivos usa esta cadena para mostrar algo breve y legible.
        string title = string.IsNullOrWhiteSpace(Name) ? "Acuratex" : Name;
        string hostname = string.IsNullOrWhiteSpace(Hostname) ? Host : Hostname;
        return $"{title} {hostname} ({Host}:{TcpPort})";
    }
}
