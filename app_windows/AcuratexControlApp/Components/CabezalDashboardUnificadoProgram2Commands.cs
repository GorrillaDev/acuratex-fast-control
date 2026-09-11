namespace AcuratexControlApp.Components;

public static class CabezalDashboardUnificadoProgram2Commands
{
    public readonly record struct ModuleMap(int DisplayNumber, int PhysicalNumber, string CanId, string Selector);

    public const int YarnChannelCount = 8;
    public const int TransferChannelCount = 4;

    public static readonly int[] DenPositionValues = [650, 487, 325, 162, 18];
    public static readonly int[] DenRunSequence = [5, 3, 1, 4, 2];
    public static readonly int[] DenRun1Sequence = [5, 3, 1];
    public static readonly int[][] SicPositionValues = [[0, 374, 750], [60, 374, 750]];
    public static readonly int[] SicRunSequence = [1, 2, 3];

    public static readonly ModuleMap[] DenModules =
    [
        new(1, 1, "320", "00"),
        new(2, 2, "320", "01"),
        new(3, 3, "320", "02"),
        new(4, 4, "320", "03"),
    ];

    public static readonly ModuleMap[] JModules =
    [
        new(1, 1, "320", "00"),
        new(2, 2, "320", "01"),
        new(3, 3, "320", "02"),
        new(4, 4, "320", "03"),
    ];

    public const int DenVisualPeriodMs = 80;
    public const int DenRun1VisualPeriodMs = 300;
    public const int SicVisualPeriodMs = 300;
    public const int JVisualPeriodMs = 120;
    public const int YarnVisualPeriodMs = 120;
    public const int YarnAllVisualPeriodMs = 120;
    public const int TransferVisualPeriodMs = 120;

    public const string OffAll = "off_all";
    public const string OnAll = "on_all";
    public const string StopAll = "stop";
    public static string DenRun(int instance) => $"den_run_{instance}";
    public static string DenStop(int instance) => $"den_stop_{instance}";
    public static string DenRun1(int instance) => $"den_run1_{instance}";
    public static string DenStop1(int instance) => $"den_stop1_{instance}";
    public static string DenSelect(int instance, int position) => $"den_select_{instance}|{position}";
    public static string DenPosition(int instance, int value) => $"den_pos_{instance}|{value}";
    public static string SicRun(int instance) => $"sic_run_{instance}";
    public static string SicStop(int instance) => $"sic_stop_{instance}";
    public static string SicSelect(int instance, int position) => $"sic_select_{instance}|{position}";
    public static string SicPosition(int instance, int value) => $"sic_pos_{instance}|{value}";
    public static string JSet(int instance, byte value) => $"j_set_{instance}|{value}";
    public static string JChannel(int instance, int pin) => $"j_ch_{instance}_{pin}";
    public static string JRun(int instance) => $"j_run_{instance}";
    public static string JStop(int instance) => $"j_stop_{instance}";
    public static string YarnPin(int pin, bool on) => $"yarn_pin_1|{pin}|{(on ? 1 : 0)}";
    public const string YarnRun = "y1_run";
    public const string YarnStop = "y1_stop";
    public static string TransferPin(int instance, int pin, bool on) => $"stitch_pin_{instance}|{pin}|{(on ? 1 : 0)}";
    public static string TransferRun(int instance) => $"s_run_{instance}";
    public static string TransferStop(int instance) => $"s_stop_{instance}";

    public static readonly CabezalDashboardUnificadoProgramProfile Profile = new(
        CabezalDashboardUnificadoProgramId.Program2,
        "Cabezal presentación",
        CreateDenMotors,
        CreateSicMotors,
        () => Array.Empty<CabezalMotorUnificado>(),
        CreateJGroups,
        CreateYarnBlocks,
        CreateTransferBlocks,
        DenRunSequence,
        DenRun1Sequence,
        SicRunSequence,
        Array.Empty<int>(),
        300,
        0);

    private static IReadOnlyList<CabezalMotorUnificado> CreateDenMotors() =>
        DenModules.Select(module => new CabezalMotorUnificado
        {
            LogicalIndex = module.PhysicalNumber - 1,
            MotorIndex = module.PhysicalNumber - 1,
            Title = $"M{module.DisplayNumber} Density",
            Positions = DenPositionValues.Select((value, index) => new CabezalPositionUnificado(index + 1, value)).ToArray(),
            MaxPosition = 650,
            HasRun1 = true,
        }).ToArray();

    private static IReadOnlyList<CabezalMotorUnificado> CreateSicMotors() =>
        SicPositionValues.Select((positions, index) => new CabezalMotorUnificado
        {
            LogicalIndex = index,
            MotorIndex = 0x08 + index,
            Title = $"SIC{index + 1}",
            Positions = positions.Select((value, position) => new CabezalPositionUnificado(position + 1, value)).ToArray(),
            MaxPosition = 750,
        }).ToArray();

    private static IReadOnlyList<CabezalJGroupUnificado> CreateJGroups() =>
        JModules.Select(module => new CabezalJGroupUnificado(module.PhysicalNumber)).ToArray();

    private static IReadOnlyList<CabezalOutputBlockUnificado> CreateYarnBlocks() =>
    [
        new("yarn1", "Guía Hilos", "320 1E 18..1F · 00 ON / 01 OFF", Enumerable.Range(1, YarnChannelCount).ToArray(), YarnRun, YarnStop),
    ];

    private static IReadOnlyList<CabezalOutputBlockUnificado> CreateTransferBlocks() =>
    [
        new("stitch1", "M Transfer Back", "320 1E 00,01,02,05", [0x00, 0x01, 0x02, 0x05], TransferRun(1), TransferStop(1)),
        new("stitch2", "M Transfer Front", "320 1E 06,07,08,0B", [0x06, 0x07, 0x08, 0x0B], TransferRun(2), TransferStop(2)),
    ];
}
