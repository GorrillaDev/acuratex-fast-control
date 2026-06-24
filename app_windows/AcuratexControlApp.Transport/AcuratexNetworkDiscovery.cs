// [ACURATEX] Este helper hace descubrimiento UDP para encontrar equipos Acuratex en la red.
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace AcuratexControlApp;

// [C#] `static` porque todo aquí son utilidades de búsqueda, no estado de objeto.
public static class AcuratexNetworkDiscovery
{
    // [ACURATEX] Puerto fijo del protocolo de descubrimiento.
    public const int DiscoveryPort = 3334;
    // [ACURATEX] Consulta broadcast que el firmware reconoce para anunciarse.
    private const string Query = "ACURATEX_DISCOVER|1\n";
    // [ACURATEX] Prefijo esperado en la respuesta del dispositivo.
    private const string ResponsePrefix = "ACURATEX_DEVICE";

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para encontrar por UDP los equipos Acuratex disponibles en la red.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `Form1` cuando el usuario pulsa descubrir WiFi.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta bajo demanda, con un tiempo máximo de espera.
    ///
    /// [ENTRADAS]
    /// Recibe un timeout y un token de cancelación.
    ///
    /// [SALIDAS]
    /// Devuelve una lista de `AcuratexNetworkDeviceInfo`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Envía broadcast UDP, escucha respuestas y agrega dispositivos únicos.
    ///
    /// [FLUJO ACURATEX]
    /// UI -> AcuratexNetworkDiscovery.DiscoverAsync -> broadcast UDP -> respuestas -> lista.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a un barrido de red o a un discovery broadcast en un bus de dispositivos.
    ///
    /// [SI NO EXISTIERA]
    /// El usuario tendría que escribir a mano IP y puerto.
    /// </summary>
    public static async Task<IReadOnlyList<AcuratexNetworkDeviceInfo>> DiscoverAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // [ACURATEX] La búsqueda usa UDP broadcast porque todavía no existe un destino conectado.
        // [C#] `CreateLinkedTokenSource` combina el token externo con el timeout.
        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        // [ACURATEX] Se usa UDP porque discovery no necesita conexión previa.
        using UdpClient client = new(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
        };

        // [ACURATEX] Se escucha en cualquier interfaz local con un puerto efímero.
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        // [ACURATEX] El firmware entiende esta línea como petición de anuncio.
        byte[] payload = Encoding.ASCII.GetBytes(Query);
        foreach (IPEndPoint endpoint in GetDiscoveryEndpoints()) {
            try {
                await client.SendAsync(payload, payload.Length, endpoint).WaitAsync(cancellationToken).ConfigureAwait(false);
            } catch (SocketException) {
            } catch (ObjectDisposedException) {
                break;
            }
        }

        // [C#] `Dictionary<TKey,TValue>` evita duplicados por host:puerto.
        Dictionary<string, AcuratexNetworkDeviceInfo> devices = new(StringComparer.OrdinalIgnoreCase);

        while (!timeoutCts.IsCancellationRequested) {
            UdpReceiveResult received;

            try {
                received = await client.ReceiveAsync(timeoutCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                break;
            } catch (SocketException) {
                break;
            } catch (ObjectDisposedException) {
                break;
            }

            // [ACURATEX] Cada datagrama se interpreta como una línea ASCII de respuesta.
            string line = Encoding.ASCII.GetString(received.Buffer).Trim();
            AcuratexNetworkDeviceInfo? device = ParseResponse(line, received.RemoteEndPoint);
            if (device == null) {
                continue;
            }

            string key = $"{device.Host}:{device.TcpPort}";
            devices[key] = device;
        }

        return devices.Values
            .OrderBy(static x => x.Host, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para generar las direcciones broadcast donde conviene anunciarse.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `DiscoverAsync`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta antes de enviar el broadcast UDP.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una secuencia de endpoints de difusión.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene; solo enumera direcciones útiles.
    ///
    /// [FLUJO ACURATEX]
    /// `DiscoverAsync` -> `GetDiscoveryEndpoints()` -> broadcast UDP.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a calcular a qué puertos de red se les puede “gritar” el discovery.
    ///
    /// [SI NO EXISTIERA]
    /// El descubrimiento tendría menos rutas posibles para encontrar equipos.
    /// </summary>
    private static IEnumerable<IPEndPoint> GetDiscoveryEndpoints()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (IPAddress address in EnumerateBroadcastAddresses()) {
            string key = address.ToString();
            if (seen.Add(key)) {
                yield return new IPEndPoint(address, DiscoveryPort);
            }
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para recorrer interfaces activas y calcular broadcast por subred.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `GetDiscoveryEndpoints()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al construir la lista de destinos de discovery.
    ///
    /// [ENTRADAS]
    /// No recibe entradas.
    ///
    /// [SALIDAS]
    /// Devuelve una secuencia de direcciones IPv4 broadcast.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Consulta la configuración de red de Windows.
    ///
    /// [FLUJO ACURATEX]
    /// Interfaz de red -> broadcast -> discovery.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a enumerar nodos visibles en cada subred.
    ///
    /// [SI NO EXISTIERA]
    /// El discovery solo intentaría una difusión genérica.
    /// </summary>
    private static IEnumerable<IPAddress> EnumerateBroadcastAddresses()
    {
        yield return IPAddress.Broadcast;

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces()) {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback) {
                continue;
            }

            IPInterfaceProperties properties;
            try {
                properties = networkInterface.GetIPProperties();
            } catch (NetworkInformationException) {
                continue;
            }

            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses) {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                    unicast.IPv4Mask == null ||
                    IPAddress.IsLoopback(unicast.Address)) {
                    continue;
                }

                IPAddress? broadcast = TryGetBroadcastAddress(unicast.Address, unicast.IPv4Mask);
                if (broadcast != null) {
                    yield return broadcast;
                }
            }
        }
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para calcular el broadcast a partir de IP y máscara IPv4.
    ///
    /// [QUIÉN LA USA]
    /// La usa `EnumerateBroadcastAddresses()`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta por cada interfaz IPv4 válida.
    ///
    /// [ENTRADAS]
    /// Recibe una dirección local y una máscara de subred.
    ///
    /// [SALIDAS]
    /// Devuelve la dirección broadcast o `null` si no se puede calcular.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// IP + máscara -> broadcast -> endpoint UDP.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a derivar la dirección de difusión de una red local en firmware.
    ///
    /// [SI NO EXISTIERA]
    /// El discovery no podría construir broadcasts por subred.
    /// </summary>
    private static IPAddress? TryGetBroadcastAddress(IPAddress address, IPAddress mask)
    {
        byte[] addressBytes = address.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();

        if (addressBytes.Length != 4 || maskBytes.Length != 4) {
            return null;
        }

        byte[] broadcast = new byte[4];
        for (int i = 0; i < broadcast.Length; i++) {
            broadcast[i] = (byte)(addressBytes[i] | ~maskBytes[i]);
        }

        return new IPAddress(broadcast);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para convertir la respuesta UDP en un objeto de información útil.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `DiscoverAsync`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cada vez que llega un datagrama.
    ///
    /// [ENTRADAS]
    /// Recibe la línea recibida y el endpoint remoto.
    ///
    /// [SALIDAS]
    /// Devuelve un objeto o `null` si la respuesta no coincide.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No modifica estado global; solo interpreta texto.
    ///
    /// [FLUJO ACURATEX]
    /// UDP datagram -> ParseResponse -> AcuratexNetworkDeviceInfo.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a parsear una trama de discovery en un firmware.
    ///
    /// [SI NO EXISTIERA]
    /// La app recibiría texto plano sin estructura.
    /// </summary>
    private static AcuratexNetworkDeviceInfo? ParseResponse(string line, IPEndPoint remoteEndpoint)
    {
        // [ACURATEX] Cada respuesta se convierte en objeto solo si sigue el formato esperado.
        if (string.IsNullOrWhiteSpace(line)) {
            return null;
        }

        // [C#] `Split` con `StringSplitOptions` separa la trama en campos sin crear ruido por espacios.
        // [ACURATEX] El firmware anuncia claves como `ip=`, `tcp_port=` y `ssid=`.
        string[] parts = line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !string.Equals(parts[0], ResponsePrefix, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        // [C#] `Dictionary<string, string>` permite buscar cada campo por nombre, no por posición fija.
        // [ACURATEX] Esto hace más robusta la lectura del anuncio UDP.
        Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < parts.Length; i++) {
            int eq = parts[i].IndexOf('=');
            if (eq <= 0) {
                continue;
            }

            // [ACURATEX] Cada par `clave=valor` se guarda para convertir luego el anuncio en objeto.
            string key = parts[i][..eq].Trim();
            string value = parts[i][(eq + 1)..].Trim();
            fields[key] = value;
        }

        // [ACURATEX] Sin puerto TCP no hay forma de conectar aunque el equipo responda.
        if (!fields.TryGetValue("tcp_port", out string? portText) ||
            !int.TryParse(portText, out int tcpPort) ||
            tcpPort <= 0 ||
            tcpPort > 65535) {
            return null;
        }

        // [ACURATEX] Si el equipo no anuncia IP, se usa la dirección real desde la que respondió.
        string host = fields.TryGetValue("ip", out string? ip) && IPAddress.TryParse(ip, out _)
            ? ip
            : remoteEndpoint.Address.ToString();

        // [ACURATEX] Los campos opcionales se leen por nombre y se dejan vacíos si no aparecen.
        fields.TryGetValue("hostname", out string? hostname);
        fields.TryGetValue("name", out string? name);
        fields.TryGetValue("ssid", out string? ssid);

        return new AcuratexNetworkDeviceInfo(
            host,
            tcpPort,
            hostname ?? string.Empty,
            name ?? string.Empty,
            ssid ?? string.Empty,
            remoteEndpoint);
    }
}
