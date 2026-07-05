namespace AcuratexControlApp.Services.Profiles.Acx;

internal static class AcxCrc32
{
    private const uint Polynomial = 0xEDB88320U;
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFU;
        for (int i = 0; i < data.Length; i++) {
            crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < table.Length; i++) {
            uint crc = i;
            for (int bit = 0; bit < 8; bit++) {
                crc = (crc & 1U) != 0 ? (crc >> 1) ^ Polynomial : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }
}
