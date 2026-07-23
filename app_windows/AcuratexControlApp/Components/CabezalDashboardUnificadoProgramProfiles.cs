namespace AcuratexControlApp.Components;

public enum CabezalDashboardUnificadoProgramId
{
    Program1 = 1,
    Program2 = 2,
    Program3 = 3,
}

public sealed record CabezalDashboardUnificadoProgramProfile(
    CabezalDashboardUnificadoProgramId ProgramId,
    string DisplayName,
    Func<IReadOnlyList<CabezalMotorUnificado>> CreateDenMotors,
    Func<IReadOnlyList<CabezalMotorUnificado>> CreateSicMotors,
    Func<IReadOnlyList<CabezalMotorUnificado>> CreateFeetMotors,
    Func<IReadOnlyList<CabezalJGroupUnificado>> CreateJGroups,
    Func<IReadOnlyList<CabezalOutputBlockUnificado>> CreateYarnBlocks,
    Func<IReadOnlyList<CabezalOutputBlockUnificado>> CreateStitchBlocks,
    IReadOnlyList<int> DenRunSequence,
    IReadOnlyList<int> DenRun1Sequence,
    IReadOnlyList<int> SicRunSequence,
    IReadOnlyList<int> FeetRunSequence,
    int SicRunPeriodMs,
    int FeetRunPeriodMs);

internal static class CabezalDashboardUnificadoProgramCommon
{
    public static IReadOnlyList<CabezalJGroupUnificado> CreateJGroups() =>
        Enumerable.Range(1, 8).Select(number => new CabezalJGroupUnificado(number)).ToArray();
}

public static class CabezalDashboardUnificadoProgramCatalog
{
    public static CabezalDashboardUnificadoProgramProfile Get(CabezalDashboardUnificadoProgramId program) =>
        program switch
        {
            CabezalDashboardUnificadoProgramId.Program1 => CabezalDashboardUnificadoProgram1Commands.Profile,
            CabezalDashboardUnificadoProgramId.Program2 => CabezalDashboardUnificadoProgram2Commands.Profile,
            CabezalDashboardUnificadoProgramId.Program3 => CabezalDashboardUnificadoProgram3Commands.Profile,
            _ => CabezalDashboardUnificadoProgram1Commands.Profile,
        };
}
