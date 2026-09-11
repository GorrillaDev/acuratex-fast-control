using AcuratexControlApp.Components;
using Xunit;

namespace AcuratexControlApp.Tests;

public sealed class UnifiedProgram2YarnProfileTests
{
    [Fact]
    public void YarnProfileHasEightChannels()
    {
        CabezalOutputBlockUnificado block = Assert.Single(
            CabezalDashboardUnificadoProgram2Commands.Profile.CreateYarnBlocks());

        Assert.Equal(8, CabezalDashboardUnificadoProgram2Commands.YarnChannelCount);
        Assert.Equal(CabezalDashboardUnificadoProgram2Commands.YarnChannelCount, block.PinCount);
        Assert.Equal(Enumerable.Range(1, CabezalDashboardUnificadoProgram2Commands.YarnChannelCount), block.Addresses);
        Assert.Equal(CabezalDashboardUnificadoProgram2Commands.YarnChannelCount, block.States.Length);
        Assert.Equal("320 1E 18..1F · 00 ON / 01 OFF", block.Subtitle);
    }

    [Fact]
    public void YarnCommandsExposeOnlyYarnOneWithConfiguredVisualPeriods()
    {
        CabezalOutputBlockUnificado block = Assert.Single(
            CabezalDashboardUnificadoProgram2Commands.Profile.CreateYarnBlocks());

        Assert.Equal("y1_run", block.RunCommand);
        Assert.Equal("y1_stop", block.StopCommand);
        Assert.Equal(120, CabezalDashboardUnificadoProgram2Commands.YarnVisualPeriodMs);
        Assert.Equal(120, CabezalDashboardUnificadoProgram2Commands.YarnAllVisualPeriodMs);
        Assert.DoesNotContain(CabezalDashboardUnificadoProgram2Commands.Profile.CreateYarnBlocks(),
            candidate => candidate.Key.Equals("yarn2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PresentationProfileExposesOnlyPhysicalDemoModules()
    {
        CabezalDashboardUnificadoProgramProfile profile =
            CabezalDashboardUnificadoProgram2Commands.Profile;

        Assert.Equal(4, profile.CreateDenMotors().Count);
        Assert.Equal(new[] { "M1 Density", "M2 Density", "M3 Density", "M4 Density" },
            profile.CreateDenMotors().Select(motor => motor.Title));
        Assert.Equal(new[] { 650, 487, 325, 162, 18 },
            profile.CreateDenMotors()[0].Positions.Select(position => position.Value));
        Assert.Equal(new[] { 5, 3, 1, 4, 2 }, profile.DenRunSequence);
        Assert.Equal(new[] { 5, 3, 1 }, profile.DenRun1Sequence);

        Assert.Equal(2, profile.CreateSicMotors().Count);
        Assert.Equal(0, profile.CreateSicMotors()[0].Positions[0].Value);
        Assert.Equal(60, profile.CreateSicMotors()[1].Positions[0].Value);
        Assert.Equal(new[] { 1, 2, 3 }, profile.SicRunSequence);

        Assert.Empty(profile.CreateFeetMotors());
        Assert.Equal(4, profile.CreateJGroups().Count);
        Assert.Single(profile.CreateYarnBlocks());
        Assert.Equal(new[] { "M Transfer Back", "M Transfer Front" },
            profile.CreateStitchBlocks().Select(block => block.Title));
    }
}
