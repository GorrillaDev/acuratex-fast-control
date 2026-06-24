// [ACURATEX] Esta factoría decide qué implementación concreta usar según el modo de conexión.
// La UI no debería conocer las clases de transporte concretas.
namespace AcuratexControlApp;

// [C#] `sealed` evita herencia innecesaria en la factoría.
public sealed class ControllerTransportFactory
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para convertir el modo elegido en una instancia concreta de transporte.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama `ConnectionController`.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta cuando el usuario conecta o cambia de modo.
    ///
    /// [ENTRADAS]
    /// Recibe el modo, el dispositivo USB opcional, host, puerto TCP, puerto serial y baudrate.
    ///
    /// [SALIDAS]
    /// Devuelve un `IControllerTransport`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Valida parámetros y crea el objeto correcto.
    ///
    /// [FLUJO ACURATEX]
    /// ConnectionController -> ControllerTransportFactory.Create -> transporte concreto.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una tabla de selección que decide si se usa UART, USB o Ethernet.
    ///
    /// [SI NO EXISTIERA]
    /// El controlador tendría que contener lógica de construcción de cada transporte.
    /// </summary>
    public IControllerTransport Create(
        ConnectionMode mode,
        UsbVendorDeviceInfo? device,
        string host,
        int tcpPort,
        string serialPort,
        int baudRate)
    {
        // [ACURATEX] La factoría traduce la selección de la UI en una clase concreta de transporte.
        if (mode == ConnectionMode.Usb) {
            if (device == null) {
                throw new InvalidOperationException("Selecciona un dispositivo USB Acuratex.");
            }

            // [ACURATEX] WinUSB usa la ruta del dispositivo que devolvió el enumerador.
            return new WinUsbControllerTransport(device.DevicePath);
        }

        if (mode == ConnectionMode.Serial) {
            if (string.IsNullOrWhiteSpace(serialPort)) {
                throw new InvalidOperationException("Selecciona un puerto serial.");
            }

            if (baudRate <= 0) {
                throw new InvalidOperationException("Baudrate serial invalido.");
            }

            // [ACURATEX] Serial usa el nombre del puerto COM y la velocidad elegida.
            return new SerialControllerTransport(serialPort, baudRate);
        }

        if (string.IsNullOrWhiteSpace(host)) {
            throw new InvalidOperationException("Host invalido.");
        }

        if (tcpPort <= 0) {
            throw new InvalidOperationException("Puerto TCP invalido.");
        }

        // [ACURATEX] TCP usa la dirección y el puerto del dispositivo remoto.
        return new TcpControllerTransport(host, tcpPort);
    }
}
