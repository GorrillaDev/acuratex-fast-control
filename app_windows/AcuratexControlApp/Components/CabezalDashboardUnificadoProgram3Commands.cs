namespace AcuratexControlApp.Components;

public static class CabezalDashboardUnificadoProgram3Commands
{
    public static readonly CabezalDashboardUnificadoProgramProfile Profile = new(
        CabezalDashboardUnificadoProgramId.Program3,
        "Programa 3",
        CabezalDashboardUnificadoProtocol.CreateDenMotors,
        CabezalDashboardUnificadoProtocol.CreateSicMotors,
        () => Array.Empty<CabezalMotorUnificado>(),
        CabezalDashboardUnificadoProgramCommon.CreateJGroups,
        CabezalDashboardUnificadoProtocol.CreateYarnBlocks,
        CabezalDashboardUnificadoProtocol.CreateStitchBlocks,
        CabezalDashboardUnificadoProtocol.DenRunSequence,
        CabezalDashboardUnificadoProtocol.DenRun1Sequence,
        CabezalDashboardUnificadoProtocol.SicRunSequence,
        Array.Empty<int>(),
        CabezalDashboardUnificadoProtocol.SicRunPeriodMs,
        CabezalDashboardUnificadoProtocol.SicRunPeriodMs);
}
