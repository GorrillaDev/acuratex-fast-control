using AcuratexControlApp.Services;
using Xunit;

namespace AcuratexControlApp.Tests;

public sealed class UnifiedMotorCommandProtocolTests
{
    [Fact]
    public void BuildsDocumentedCommands()
    {
        Assert.Equal("U3_INIT", UnifiedMotorCommandProtocol.Init());
        Assert.Equal("U3_STATUS", UnifiedMotorCommandProtocol.Status());
        Assert.Equal("U3_S1_CONFIG|P1=0|P2=500|P3=1000|SEQ=123|HZ=1",
            UnifiedMotorCommandProtocol.RackingConfig(0, 500, 1000, "123", 1m));
        Assert.Equal("U3_S1_STOP", UnifiedMotorCommandProtocol.RackingStop());
        Assert.Equal("U3_S2_DIR|RIGHT", UnifiedMotorCommandProtocol.MainDirection(true));
        Assert.Equal("U3_S2_DIR|LEFT", UnifiedMotorCommandProtocol.MainDirection(false));
        Assert.Equal("U3_S2_SPEED|LEVEL=10", UnifiedMotorCommandProtocol.MainSpeed(10));
        Assert.Equal("U3_S2_RUN", UnifiedMotorCommandProtocol.MainRun());
        Assert.Equal("U3_S2_STOP", UnifiedMotorCommandProtocol.MainStop());
        Assert.Equal("LD_STEP_DIR|0", UnifiedMotorCommandProtocol.RollerDirection(0));
        Assert.Equal("LD_STEP_DIR|1", UnifiedMotorCommandProtocol.RollerDirection(1));
        Assert.Equal("LD_STEP_FREQ|HZ=200", UnifiedMotorCommandProtocol.RollerFrequency(200));
        Assert.Equal("LD_STEP_RUN|ON", UnifiedMotorCommandProtocol.RollerRun(true));
        Assert.Equal("LD_STEP_RUN|OFF", UnifiedMotorCommandProtocol.RollerRun(false));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("13231")]
    public void AcceptsValidRackingSequences(string sequence)
    {
        string command = UnifiedMotorCommandProtocol.RackingConfig(0, 500, 1000, sequence, 1m);
        Assert.Contains($"SEQ={sequence}", command);
    }

    [Theory]
    [InlineData("124")]
    [InlineData("abc")]
    [InlineData("")]
    public void RejectsInvalidRackingSequences(string sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UnifiedMotorCommandProtocol.RackingConfig(0, 500, 1000, sequence, 1m));
    }

    [Fact]
    public void RejectsRackingSequenceLongerThan64()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UnifiedMotorCommandProtocol.RackingConfig(0, 500, 1000, new string('1', 65), 1m));
    }

    [Fact]
    public void RejectsOutOfRangeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UnifiedMotorCommandProtocol.RackingConfig(32768, 500, 1000, "123", 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnifiedMotorCommandProtocol.MainSpeed(31));
        Assert.Throws<ArgumentOutOfRangeException>(() => UnifiedMotorCommandProtocol.RollerDirection(2));
    }
}
