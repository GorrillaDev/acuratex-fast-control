namespace AcuratexControlApp.Components;

public enum CabezalDashboardTarjetasProgramId
{
    Program1 = 1,
    Program2 = 2,
    Program3 = 3,
}

public sealed record CabezalDashboardTarjetasProgramProfile(
    CabezalDashboardTarjetasProgramId ProgramId,
    string DisplayName,
    Func<IReadOnlyList<CabezalMotorTarjetas>> CreateDenMotors,
    Func<IReadOnlyList<CabezalMotorTarjetas>> CreateSicMotors,
    Func<IReadOnlyList<CabezalMotorTarjetas>> CreateFeetMotors,
    Func<IReadOnlyList<CabezalJGroupTarjetas>> CreateJGroups,
    Func<IReadOnlyList<CabezalOutputBlockTarjetas>> CreateYarnBlocks,
    Func<IReadOnlyList<CabezalOutputBlockTarjetas>> CreateStitchBlocks,
    IReadOnlyList<int> DenRunSequence,
    IReadOnlyList<int> DenRun1Sequence,
    IReadOnlyList<int> SicRunSequence,
    IReadOnlyList<int> FeetRunSequence,
    int SicRunPeriodMs,
    int FeetRunPeriodMs);

internal static class CabezalDashboardTarjetasProgramCommon
{
    public static IReadOnlyList<CabezalJGroupTarjetas> CreateJGroups()
    {
        return Enumerable.Range(1, 8)
            .Select(number => new CabezalJGroupTarjetas(number))
            .ToArray();
    }
}

public static class CabezalDashboardTarjetasProgramCatalog
{
    public static CabezalDashboardTarjetasProgramProfile Get(CabezalDashboardTarjetasProgramId program)
    {
        return program switch
        {
            CabezalDashboardTarjetasProgramId.Program1 => CabezalDashboardTarjetasProgram1Commands.Profile,
            CabezalDashboardTarjetasProgramId.Program2 => CabezalDashboardTarjetasProgram2Commands.Profile,
            CabezalDashboardTarjetasProgramId.Program3 => CabezalDashboardTarjetasProgram3Commands.Profile,
            _ => CabezalDashboardTarjetasProgram1Commands.Profile,
        };
    }
}