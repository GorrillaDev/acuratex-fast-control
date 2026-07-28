using AcuratexControlApp.Components;
using Xunit;

namespace AcuratexControlApp.Tests;

public sealed class Program2ResponseRouterTests
{
    [Fact]
    public void JErrorDoesNotSelectPendingDenOperation()
    {
        Program2PendingOperation[] pending =
        [
            new("uni_den_pos_1|325", 1),
            new("uni_j_run_1", 2),
        ];

        string? failed = CabezalDashboardUnificadoProgram2ResponseRouter
            .SelectFailedCommand("ERR|UNI|J_RUN", pending);

        Assert.Equal("uni_j_run_1", failed);
        Assert.NotEqual("uni_den_pos_1|325", failed);
    }

    [Fact]
    public void ErrorFromAnotherProtocolDoesNotSelectAnyProgram2Operation()
    {
        Program2PendingOperation[] pending = [new("uni_den_run_1", 1)];

        string? failed = CabezalDashboardUnificadoProgram2ResponseRouter
            .SelectFailedCommand("ERR FILE_BUSY", pending);

        Assert.Null(failed);
    }

    [Fact]
    public void GenericUniErrorSelectsOnlyOldestPendingOperation()
    {
        Program2PendingOperation[] pending =
        [
            new("uni_yarn_pin_1|2|1", 8),
            new("uni_den_run_1", 3),
        ];

        string? failed = CabezalDashboardUnificadoProgram2ResponseRouter
            .SelectFailedCommand("ERR|UNI|CAN_TX", pending);

        Assert.Equal("uni_den_run_1", failed);
    }
}
