namespace AcuratexControlApp.Components;

public static class CabezalDashboardTarjetasProgram1Commands
{
    public static readonly CabezalDashboardTarjetasProgramProfile Profile = new(
        CabezalDashboardTarjetasProgramId.Program1,
        "Programa 1",
        CabezalDashboardTarjetasProtocol.CreateDenMotors,
        CabezalDashboardTarjetasProtocol.CreateSicMotors,
        () => Array.Empty<CabezalMotorTarjetas>(),
        CabezalDashboardTarjetasProgramCommon.CreateJGroups,
        CabezalDashboardTarjetasProtocol.CreateYarnBlocks,
        CabezalDashboardTarjetasProtocol.CreateStitchBlocks,
        CabezalDashboardTarjetasProtocol.DenRunSequence,
        CabezalDashboardTarjetasProtocol.DenRun1Sequence,
        CabezalDashboardTarjetasProtocol.SicRunSequence,
        Array.Empty<int>(),
        CabezalDashboardTarjetasProtocol.SicRunPeriodMs,
        CabezalDashboardTarjetasProtocol.SicRunPeriodMs);
}
