using AcuratexControlApp.Components;
using Xunit;

namespace AcuratexControlApp.Tests;

public sealed class UnifiedProgram2YarnProfileTests
{
    [Fact]
    public void YarnProfileHasOnlySixPhysicalChannels()
    {
        CabezalOutputBlockUnificado block = Assert.Single(
            CabezalDashboardUnificadoProgram2Commands.Profile.CreateYarnBlocks());

        Assert.Equal(CabezalDashboardUnificadoProgram2Commands.YarnChannelCount, block.PinCount);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, block.Addresses);
        Assert.Equal(6, block.States.Length);
        Assert.Contains("ID 0x363", block.Subtitle);
        Assert.Contains("00..05", block.Subtitle);
    }

    [Fact]
    public void YarnCommandsExposeOnlyYarnOneAtEightyMilliseconds()
    {
        CabezalOutputBlockUnificado block = Assert.Single(
            CabezalDashboardUnificadoProgram2Commands.Profile.CreateYarnBlocks());

        Assert.Equal("y1_run", block.RunCommand);
        Assert.Equal("y1_stop", block.StopCommand);
        Assert.Equal(80, CabezalDashboardUnificadoProgram2Commands.YarnVisualPeriodMs);
        Assert.Equal(80, CabezalDashboardUnificadoProgram2Commands.YarnAllVisualPeriodMs);
        Assert.DoesNotContain(CabezalDashboardUnificadoProgram2Commands.Profile.CreateYarnBlocks(),
            candidate => candidate.Key.Equals("yarn2", StringComparison.OrdinalIgnoreCase));
    }
}