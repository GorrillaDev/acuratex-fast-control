namespace AcuratexControlApp.Components;

public static class CabezalDashboardUnificadoProgram2Commands
{
    public static readonly CabezalDashboardUnificadoProgramProfile Profile = new(
        CabezalDashboardUnificadoProgramId.Program2,
        "Programa 2",
        CabezalDashboardUnificadoProtocol.CreateDenMotors,
        CabezalDashboardUnificadoProtocol.CreateSicMotors,
        CreateFeetMotors,
        CabezalDashboardUnificadoProgramCommon.CreateJGroups,
        CabezalDashboardUnificadoProtocol.CreateYarnBlocks,
        CabezalDashboardUnificadoProtocol.CreateStitchBlocks,
        CabezalDashboardUnificadoProtocol.DenRunSequence,
        CabezalDashboardUnificadoProtocol.DenRun1Sequence,
        CabezalDashboardUnificadoProtocol.SicRunSequence,
        new[] { 1, 2 },
        CabezalDashboardUnificadoProtocol.SicRunPeriodMs,
        CabezalDashboardUnificadoProtocol.SicRunPeriodMs);

    private static IReadOnlyList<CabezalMotorUnificado> CreateFeetMotors()
    {
        IReadOnlyList<CabezalPositionUnificado> positions = new[]
        {
            new CabezalPositionUnificado(1, 0x0000),
            new CabezalPositionUnificado(2, 0x0176),
            new CabezalPositionUnificado(3, 0x02EE),
        };

        return Enumerable.Range(0, 2)
            .Select(index => new CabezalMotorUnificado
            {
                LogicalIndex = index,
                MotorIndex = new[] { 0x08, 0x09 }[index],
                Title = $"Feet{index + 1}",
                Positions = positions,
                MaxPosition = 0x02EE,
            })
            .ToArray();
    }
}
