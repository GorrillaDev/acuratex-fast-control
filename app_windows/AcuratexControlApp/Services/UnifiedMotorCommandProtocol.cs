using System.Globalization;

namespace AcuratexControlApp.Services;

/// <summary>
/// Unica autoridad de formato y validacion del protocolo textual del dashboard
/// de motores principales del Sistema Unificado.
/// </summary>
public static class UnifiedMotorCommandProtocol
{
    public const int MinPosition = short.MinValue;
    public const int MaxPosition = short.MaxValue;
    public const int MaxSequenceLength = 64;
    public const int MinSpeedLevel = 1;
    public const int MaxSpeedLevel = 30;
    public const int MinRollerFrequencyHz = 1;
    public const int MaxRollerFrequencyHz = 200_000;

    public static string Init() => "U3_INIT";
    public static string Status() => "U3_STATUS";

    public static string RackingConfig(int p1, int p2, int p3, string sequence, decimal frequencyHz)
    {
        ValidatePosition(p1, nameof(p1));
        ValidatePosition(p2, nameof(p2));
        ValidatePosition(p3, nameof(p3));
        string cleanSequence = (sequence ?? string.Empty).Trim();
        if (cleanSequence.Length is < 1 or > MaxSequenceLength
            || cleanSequence.Any(static value => value is < '1' or > '3')) {
            throw new ArgumentOutOfRangeException(nameof(sequence), "La secuencia admite solamente 1, 2 y 3, con longitud 1..64.");
        }
        if (frequencyHz <= 0m || frequencyHz > 1000m) {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"U3_S1_CONFIG|P1={p1}|P2={p2}|P3={p3}|SEQ={cleanSequence}|HZ={frequencyHz:0.###}");
    }

    public static string RackingStop() => "U3_S1_STOP";
    public static string MainDirection(bool right) => right ? "U3_S2_DIR|RIGHT" : "U3_S2_DIR|LEFT";

    public static string MainSpeed(int level)
    {
        if (level is < MinSpeedLevel or > MaxSpeedLevel) throw new ArgumentOutOfRangeException(nameof(level));
        return $"U3_S2_SPEED|LEVEL={level}";
    }

    public static string MainRun() => "U3_S2_RUN";
    public static string MainStop() => "U3_S2_STOP";

    public static string RollerDirection(int direction)
    {
        if (direction is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        return $"LD_STEP_DIR|{direction}";
    }

    public static string RollerFrequency(int frequencyHz)
    {
        if (frequencyHz is < MinRollerFrequencyHz or > MaxRollerFrequencyHz) {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        }
        return $"LD_STEP_FREQ|HZ={frequencyHz}";
    }

    public static string RollerRun(bool running) => $"LD_STEP_RUN|{(running ? "ON" : "OFF")}";

    private static void ValidatePosition(int position, string parameterName)
    {
        if (position is < MinPosition or > MaxPosition) throw new ArgumentOutOfRangeException(parameterName);
    }
}
