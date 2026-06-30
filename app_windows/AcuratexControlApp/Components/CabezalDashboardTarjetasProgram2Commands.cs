namespace AcuratexControlApp.Components;

public static class CabezalDashboardTarjetasProgram2Commands
{
    public static readonly CabezalDashboardTarjetasProgramProfile Profile = new(
        CabezalDashboardTarjetasProgramId.Program2,
        "Programa 2",
        CabezalDashboardTarjetasProtocol.CreateDenMotors,
        CabezalDashboardTarjetasProtocol.CreateSicMotors,
        CabezalDashboardTarjetasProtocol.CreateFeetMotors,
        CabezalDashboardTarjetasProgramCommon.CreateJGroups,
        CabezalDashboardTarjetasProtocol.CreateYarnBlocks,
        CabezalDashboardTarjetasProtocol.CreateStitchBlocks,
        CabezalDashboardTarjetasProtocol.DenRunSequence,
        CabezalDashboardTarjetasProtocol.DenRun1Sequence,
        CabezalDashboardTarjetasProtocol.SicRunSequence,
        new[] { 1, 2 },
        CabezalDashboardTarjetasProtocol.SicRunPeriodMs,
        CabezalDashboardTarjetasProtocol.SicRunPeriodMs);
}
