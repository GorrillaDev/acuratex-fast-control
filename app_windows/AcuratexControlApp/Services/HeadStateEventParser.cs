using System.Globalization;
using AcuratexControlApp.Models;

namespace AcuratexControlApp.Services;

public sealed class HeadStateEventParser : IHeadStateEventParser
{
    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para detectar y normalizar eventos de estado emitidos por el
    /// firmware como `427|...|420`.
    ///
    /// [QUIÉN LA LLAMA]
    /// La llama la UI del cabezal cuando recibe una línea entrante.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta al procesar cada línea recibida por la conexión.
    ///
    /// [ENTRADAS]
    /// Recibe la línea y un `out` para devolver el evento ya interpretado.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si la línea es un evento válido.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// Crea un objeto `HeadStateEvent` con nombre de instancia, tipo de estado, máscara,
    /// valor opcional y línea cruda.
    ///
    /// [FLUJO ACURATEX]
    /// Firmware -> línea `427|...|420` -> parser -> HeadStateEvent -> UI.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a una rutina de decodificación de una trama de estado de bus.
    ///
    /// [SI NO EXISTIERA]
    /// La UI no sabría convertir las líneas del firmware en estado utilizable.
    /// </summary>
    public bool TryParse(string line, out HeadStateEvent? stateEvent)
    {
        stateEvent = null;

        string cleanLine = (line ?? string.Empty).Trim();
        if (cleanLine.Length == 0
            || !cleanLine.StartsWith("427|", StringComparison.Ordinal)
            || !cleanLine.EndsWith("|420", StringComparison.Ordinal)) {
            return false;
        }

        string[] parts = cleanLine.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 5
            || !string.Equals(parts[0], "427", StringComparison.Ordinal)
            || !string.Equals(parts[^1], "420", StringComparison.Ordinal)) {
            return false;
        }

        string instanceName = NormalizeToken(parts[1]);
        string stateType = NormalizeToken(parts[2]);
        string rawMask = parts[3].Trim();
        if (instanceName.Length == 0 || stateType.Length == 0 || !TryParseMask(rawMask, out int mask)) {
            return false;
        }

        int? value = null;
        for (int index = 4; index < parts.Length - 1; index++) {
            string part = parts[index].Trim();
            if (!part.StartsWith("VALUE=", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (int.TryParse(part["VALUE=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue)) {
                value = parsedValue;
            }
        }

        stateEvent = new HeadStateEvent(instanceName, stateType, mask, rawMask, value, cleanLine);
        return true;
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para convertir la máscara textual del firmware en un entero usable.
    ///
    /// [QUIÉN LA USA]
    /// La usa `TryParse` dentro del parser de estado.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta para cada línea válida de estado antes de construir el evento.
    ///
    /// [ENTRADAS]
    /// Recibe la máscara cruda.
    ///
    /// [SALIDAS]
    /// Devuelve `true` si la máscara se pudo interpretar y entrega el valor por `out`.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Texto de máscara -> `TryParseMask()` -> entero del evento.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a leer un registro que puede venir en decimal o en binario.
    ///
    /// [SI NO EXISTIERA]
    /// El parser solo entendería un formato de máscara.
    /// </summary>
    private static bool TryParseMask(string rawMask, out int mask)
    {
        mask = 0;
        if (string.IsNullOrWhiteSpace(rawMask)) {
            return false;
        }

        string cleanMask = rawMask.Trim();
        if (cleanMask.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) {
            string bits = cleanMask[2..];
            if (bits.Length == 0) {
                return false;
            }

            int value = 0;
            foreach (char ch in bits) {
                if (ch != '0' && ch != '1') {
                    return false;
                }

                value = (value << 1) | (ch == '1' ? 1 : 0);
            }

            mask = value;
            return true;
        }

        return int.TryParse(cleanMask, NumberStyles.Integer, CultureInfo.InvariantCulture, out mask);
    }

    /// <summary>
    /// [POR QUÉ EXISTE]
    /// Esta función existe para comparar tokens del firmware sin depender de mayúsculas o
    /// espacios.
    ///
    /// [QUIÉN LA USA]
    /// La usa `TryParse()` al normalizar instancia y tipo de estado.
    ///
    /// [CUÁNDO SE EJECUTA]
    /// Se ejecuta antes de comparar nombres de campos del evento.
    ///
    /// [ENTRADAS]
    /// Recibe un texto.
    ///
    /// [SALIDAS]
    /// Devuelve el texto limpio y en mayúsculas.
    ///
    /// [EFECTOS SECUNDARIOS]
    /// No tiene.
    ///
    /// [FLUJO ACURATEX]
    /// Token textual -> `NormalizeToken()` -> comparación estable.
    ///
    /// [EQUIVALENCIA MCU]
    /// Se parece a normalizar un identificador antes de compararlo contra una tabla.
    ///
    /// [SI NO EXISTIERA]
    /// Las comparaciones serían sensibles al formato de texto recibido.
    /// </summary>
    private static string NormalizeToken(string value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}

