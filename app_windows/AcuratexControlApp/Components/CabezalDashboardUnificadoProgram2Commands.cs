namespace AcuratexControlApp.Components;

public static class CabezalDashboardUnificadoProgram2Commands
{
    public readonly record struct ModuleMap(int DisplayNumber, int PhysicalNumber, string CanId, string Selector);

    public static readonly int[] DenPositionValues = [0, 162, 325, 487, 650];
    public static readonly int[] StitchPositionValues = [0, 320, 480, 160, 640];

    public static readonly ModuleMap[] DenModules =
    [
        new(1, 1, "363", "00"),
        new(2, 2, "363", "01"),
        new(3, 5, "363", "03"),
        new(4, 6, "363", "02"),
    ];

    public static readonly ModuleMap[] JModules =
    [
        new(1, 1, "363", "00"),
        new(2, 2, "363", "01"),
        new(3, 5, "363", "02"),
        new(4, 6, "363", "03"),
    ];

    public const int DenVisualPeriodMs = 300;
    public const int JVisualPeriodMs = 120;
    public const int YarnVisualPeriodMs = 80;
    public const int YarnAllVisualPeriodMs = 120;
    public const int StitchVisualPeriodMs = 120;

    public const string OffAll = "off_all";
    public const string OnAll = "on_all";
    public const string RunAll = "run_all";
    public const string StopAll = "stop";
    public static string FeetTrigger(int instance) => $"feet_trigger_{instance}";
    public static string DenRun(int physical) => $"den_run_{physical}";
    public static string DenStop(int physical) => $"den_stop_{physical}";
    public static string DenSelect(int physical, int position) => $"den_select_{physical}|{position}";
    public static string DenPosition(int physical, int value) => $"den_pos_{physical}|{value}";
    public static string JSet(int physical, byte value) => $"j_set_{physical}|{value}";
    public static string JChannel(int physical, int pin) => $"j_ch_{physical}_{pin}";
    public static string JRun(int physical) => $"j_run_{physical}";
    public static string JStop(int physical) => $"j_stop_{physical}";
    public static string YarnPin(int pin, bool on) => $"yarn_pin_1|{pin}|{(on ? 1 : 0)}";
    public const string YarnRun = "y1_run";
    public const string YarnStop = "y1_stop";
    public static string StitchReset(int instance) => $"stitch_reset_{instance}";
    public static string StitchPosition(int instance, int position) => $"stitch_pos_{instance}|{position}";
    public static string StitchRun(int instance) => $"s_run_{instance}";
    public static string StitchStop(int instance) => $"s_stop_{instance}";

    public static readonly CabezalDashboardUnificadoProgramProfile Profile = new(
        CabezalDashboardUnificadoProgramId.Program2,
        "Programa 2 final",
        CreateDenMotors,
        () => Array.Empty<CabezalMotorUnificado>(),
        () => Array.Empty<CabezalMotorUnificado>(),
        CreateJGroups,
        CreateYarnBlocks,
        CreateStitchBlocks,
        [1, 2, 3, 4, 5],
        Array.Empty<int>(),
        Array.Empty<int>(),
        Array.Empty<int>(),
        300,
        300);

    private static IReadOnlyList<CabezalMotorUnificado> CreateDenMotors() =>
        DenModules.Select(module => new CabezalMotorUnificado
        {
            LogicalIndex = module.PhysicalNumber - 1,
            MotorIndex = module.PhysicalNumber,
            Title = $"DEN {module.DisplayNumber}",
            Positions = DenPositionValues.Select((value, index) => new CabezalPositionUnificado(index + 1, value)).ToArray(),
            MaxPosition = 650,
            HasRun1 = false,
        }).ToArray();

    private static IReadOnlyList<CabezalJGroupUnificado> CreateJGroups() =>
        JModules.Select(module => new CabezalJGroupUnificado(module.PhysicalNumber)).ToArray();

    private static IReadOnlyList<CabezalOutputBlockUnificado> CreateYarnBlocks() =>
    [
        new("yarn1", "Yarn 1", "363 - 05 01 00 canal 00 estado", Enumerable.Range(1, 8).ToArray(), YarnRun, YarnStop),
    ];

    private static IReadOnlyList<CabezalOutputBlockUnificado> CreateStitchBlocks() =>
    [
        new("stitch1", "Stitch 1", "363 - selector 00", StitchPositionValues, StitchRun(1), StitchStop(1)),
        new("stitch2", "Stitch 2", "363 - selector 01", StitchPositionValues, StitchRun(2), StitchStop(2)),
    ];
}
