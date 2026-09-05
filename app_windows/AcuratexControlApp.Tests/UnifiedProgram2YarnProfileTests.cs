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
        Assert.Equal("363 - 05 01 00 canal 00 estado", block.Subtitle);
    }

    [Fact]
    public void YarnCommandsExposeOnlyYarnOneWithConfiguredVisualPeriods()
    {
        CabezalOutputBlockUnificado block = Assert.Single(
            CabezalDashboardUnificadoProgram2Commands.Profile.CreateYarnBlocks());

        Assert.Equal("y1_run", block.RunCommand);
        Assert.Equal("y1_stop", block.StopCommand);
        Assert.Equal(80, CabezalDashboardUnificadoProgram2Commands.YarnVisualPeriodMs);
        Assert.Equal(120, CabezalDashboardUnificadoProgram2Commands.YarnAllVisualPeriodMs);
        Assert.DoesNotContain(CabezalDashboardUnificadoProgram2Commands.Profile.CreateYarnBlocks(),
            candidate => candidate.Key.Equals("yarn2", StringComparison.OrdinalIgnoreCase));
    }
}
