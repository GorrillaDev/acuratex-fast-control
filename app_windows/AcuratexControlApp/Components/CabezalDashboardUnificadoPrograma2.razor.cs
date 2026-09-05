using AcuratexControlApp.Services;
using Microsoft.AspNetCore.Components;

namespace AcuratexControlApp.Components;

public partial class CabezalDashboardUnificadoPrograma2
{
    protected static readonly int[] DenPositions = CabezalDashboardUnificadoProgram2Commands.DenPositionValues;
    protected static readonly int[] StitchPositions = CabezalDashboardUnificadoProgram2Commands.StitchPositionValues;

    [Inject] protected ICabezalDashboardUnificadoCommandService CommandService { get; set; } = default!;
    [Inject] protected IConnectionController Connection { get; set; } = default!;
    [Inject] protected IEmergencyStopService EmergencyStop { get; set; } = default!;
    [Parameter] public bool InitBlocked { get; set; }

    protected List<Program2DenMotor> DenMotors { get; } =
        CabezalDashboardUnificadoProgram2Commands.DenModules
            .Select(module => new Program2DenMotor(module.DisplayNumber, module.PhysicalNumber, module.CanId, module.Selector))
            .ToList();
    protected List<Program2JGroup> JGroups { get; } =
        CabezalDashboardUnificadoProgram2Commands.JModules
            .Select(module => new Program2JGroup(module.DisplayNumber, module.PhysicalNumber))
            .ToList();
    protected List<Program2StitchMotor> StitchMotors { get; } = [new(1), new(2)];
    protected bool[] YarnStates { get; } = new bool[CabezalDashboardUnificadoProgram2Commands.YarnChannelCount];
    protected bool YarnRunning { get; set; }
    protected bool YarnPending => _yarnPendingCount > 0;
    protected string Status { get; set; } = "Programa 2 confirmado";
    protected string LastMessage { get; set; } = "Listo.";
    protected bool ActionsBlocked => InitBlocked || EmergencyStop.IsEmergencyStopActive || !Connection.IsConnected;

    private readonly Dictionary<string, PendingCommand> _pending = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _yarnAnimation;
    private bool[]? _yarnBeforeRun;
    private int _yarnPendingCount;
    private long _nextPendingSequence;
    private bool _disposed;

    protected override void OnInitialized()
    {
        EmergencyStop.StateChanged += OnEmergencyStateChanged;
    }

    public Task OffAllAsync() => SendAsync(CabezalDashboardUnificadoProgram2Commands.OffAll, () => ApplyAllOutputs(false));
    public Task OnAllAsync() => SendAsync(CabezalDashboardUnificadoProgram2Commands.OnAll, () => ApplyAllOutputs(true));
    public Task RunAllAsync() => SendAsync(CabezalDashboardUnificadoProgram2Commands.RunAll, null);
    public Task StopAllAsync() => SendAsync(CabezalDashboardUnificadoProgram2Commands.StopAll, ApplyGlobalStopConfirmation);

    protected Task TriggerFeetAsync(int feet) => SendAsync(CabezalDashboardUnificadoProgram2Commands.FeetTrigger(feet), null);

    protected Task RunDenAsync(Program2DenMotor motor)
    {
        motor.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.DenRun(motor.PhysicalNumber), () =>
        {
            motor.Pending = false;
            motor.Running = true;
            StartDenAnimation(motor);
        }, () => motor.Pending = false);
    }

    protected Task StopDenAsync(Program2DenMotor motor)
    {
        if (InitBlocked) return Task.CompletedTask;
        motor.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.DenStop(motor.PhysicalNumber), () =>
        {
            motor.Pending = false;
            StopDenAnimation(motor);
        }, () => motor.Pending = false);
    }

    protected Task SelectDenPositionAsync(Program2DenMotor motor, int positionNumber)
    {
        if (ActionsBlocked) return Task.CompletedTask;
        motor.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.DenSelect(motor.PhysicalNumber, positionNumber), () =>
        {
            StopDenAnimation(motor);
            motor.Pending = false;
            motor.SelectedPosition = positionNumber;
            motor.Value = DenPositions[positionNumber - 1];
            motor.PreviewValue = motor.Value;
        }, () => motor.Pending = false);
    }

    protected void PreviewDen(Program2DenMotor motor, ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out int value)) motor.PreviewValue = Math.Clamp(value, 0, 650);
    }

    protected Task CommitDenAsync(Program2DenMotor motor, ChangeEventArgs args)
    {
        PreviewDen(motor, args);
        int requested = motor.PreviewValue;
        motor.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.DenPosition(motor.PhysicalNumber, requested), () =>
        {
            StopDenAnimation(motor);
            motor.Pending = false;
            motor.Value = requested;
            motor.PreviewValue = requested;
            motor.SelectedPosition = Array.IndexOf(DenPositions, requested) + 1;
        }, () =>
        {
            motor.Pending = false;
            motor.PreviewValue = motor.Value;
        });
    }

    protected Task SetJAsync(Program2JGroup group, bool on)
    {
        group.Pending = true;
        byte value = on ? (byte)0x00 : (byte)0xFF;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.JSet(group.PhysicalNumber, value), () =>
        {
            StopJAnimation(group, false);
            group.Pending = false;
            group.Register = value;
        }, () => group.Pending = false);
    }

    protected Task ToggleJAsync(Program2JGroup group, int pin)
    {
        if (ActionsBlocked) return Task.CompletedTask;
        group.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.JChannel(group.PhysicalNumber, pin), () =>
        {
            StopJAnimation(group, false);
            group.Pending = false;
            group.Register ^= (byte)(1 << (pin - 1));
        }, () => group.Pending = false);
    }

    protected Task RunJAsync(Program2JGroup group)
    {
        group.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.JRun(group.PhysicalNumber), () =>
        {
            group.Pending = false;
            group.Register = 0xFF;
            StartJAnimation(group);
        }, () => group.Pending = false);
    }

    protected Task StopJAsync(Program2JGroup group)
    {
        if (InitBlocked) return Task.CompletedTask;
        group.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.JStop(group.PhysicalNumber), () =>
        {
            group.Pending = false;
            StopJAnimation(group, true);
        }, () => group.Pending = false);
    }

    protected async Task SetAllYarnAsync(bool on)
    {
        if (ActionsBlocked) return;
        StopYarnAnimation(false);
        for (int pin = 1; pin <= CabezalDashboardUnificadoProgram2Commands.YarnChannelCount; pin++)
        {
            int currentPin = pin;
            _yarnPendingCount++;
            await SendAsync(CabezalDashboardUnificadoProgram2Commands.YarnPin(pin, on), () =>
            {
                YarnStates[currentPin - 1] = on;
                _yarnPendingCount = Math.Max(0, _yarnPendingCount - 1);
            }, () => _yarnPendingCount = Math.Max(0, _yarnPendingCount - 1));
        }
    }

    protected Task ToggleYarnAsync(int pin)
    {
        if (ActionsBlocked) return Task.CompletedTask;
        bool requested = !YarnStates[pin - 1];
        _yarnPendingCount++;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.YarnPin(pin, requested), () =>
        {
            StopYarnAnimation(false);
            YarnStates[pin - 1] = requested;
            _yarnPendingCount = Math.Max(0, _yarnPendingCount - 1);
        }, () => _yarnPendingCount = Math.Max(0, _yarnPendingCount - 1));
    }

    protected Task RunYarnAsync()
    {
        _yarnPendingCount++;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.YarnRun, () =>
        {
            _yarnPendingCount = Math.Max(0, _yarnPendingCount - 1);
            StartYarnAnimation();
        }, () => _yarnPendingCount = Math.Max(0, _yarnPendingCount - 1));
    }

    protected Task StopYarnAsync()
    {
        if (InitBlocked) return Task.CompletedTask;
        _yarnPendingCount++;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.YarnStop, () =>
        {
            _yarnPendingCount = Math.Max(0, _yarnPendingCount - 1);
            StopYarnAnimation(true);
        }, () => _yarnPendingCount = Math.Max(0, _yarnPendingCount - 1));
    }

    protected Task ResetStitchAsync(Program2StitchMotor stitch)
    {
        stitch.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.StitchReset(stitch.Number), () =>
        {
            StopStitchAnimation(stitch, false);
            stitch.Pending = false;
            stitch.SelectedPosition = 0;
        }, () => stitch.Pending = false);
    }

    protected Task SelectStitchPositionAsync(Program2StitchMotor stitch, int position)
    {
        if (ActionsBlocked) return Task.CompletedTask;
        stitch.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.StitchPosition(stitch.Number, position), () =>
        {
            StopStitchAnimation(stitch, false);
            stitch.Pending = false;
            stitch.SelectedPosition = position;
        }, () => stitch.Pending = false);
    }

    protected Task RunStitchAsync(Program2StitchMotor stitch)
    {
        stitch.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.StitchRun(stitch.Number), () =>
        {
            stitch.Pending = false;
            StartStitchAnimation(stitch);
        }, () => stitch.Pending = false);
    }

    protected Task StopStitchAsync(Program2StitchMotor stitch)
    {
        if (InitBlocked) return Task.CompletedTask;
        stitch.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.StitchStop(stitch.Number), () =>
        {
            stitch.Pending = false;
            StopStitchAnimation(stitch, false);
        }, () => stitch.Pending = false);
    }

    private async Task SendAsync(string command, Action? confirmed, Action? failed = null)
    {
        if (_disposed || !Connection.IsConnected
            || (EmergencyStop.IsEmergencyStopActive && !command.Contains("stop", StringComparison.OrdinalIgnoreCase))) return;
        string fullCommand = command.StartsWith("uni_", StringComparison.OrdinalIgnoreCase) ? command : $"uni_{command}";
        PendingCommand pending = new(confirmed, failed, ++_nextPendingSequence);
        _pending[fullCommand] = pending;
        Status = "Comando enviado; esperando firmware";
        LastMessage = fullCommand;
        try
        {
            await CommandService.SendDoCommandAsync(fullCommand);
            _ = ExpirePendingAsync(fullCommand, pending);
        }
        catch (Exception ex)
        {
            _pending.Remove(fullCommand);
            failed?.Invoke();
            Status = "Error de envío";
            LastMessage = ex.Message;
        }
    }

    private async Task ExpirePendingAsync(string command, PendingCommand expected)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (_disposed) return;
        await InvokeAsync(() =>
        {
            if (_pending.TryGetValue(command, out PendingCommand? current) && ReferenceEquals(current, expected))
            {
                _pending.Remove(command);
                current.Failed?.Invoke();
                Status = "Sin confirmación del firmware";
                LastMessage = $"Timeout: {command}";
                StateHasChanged();
            }
        });
    }

    public void ProcessConnectionLine(string line)
    {
        if (!_disposed) ProcessLine(line);
    }

    private void ProcessLine(string rawLine)
    {
        string line = (rawLine ?? string.Empty).Trim();
        if (line.StartsWith("OK uni_", StringComparison.OrdinalIgnoreCase))
        {
            string command = line[3..].Trim();
            if (_pending.Remove(command, out PendingCommand? pending)) pending.Confirmed?.Invoke();
            else ApplyUntrackedConfirmation(command);
            if (!command.Equals("uni_stop", StringComparison.OrdinalIgnoreCase)
                && !command.Equals("uni_emergency_stop", StringComparison.OrdinalIgnoreCase))
            {
                Status = "Confirmado por firmware";
            }
            LastMessage = line;
        }
        else if (line.StartsWith("ERR", StringComparison.OrdinalIgnoreCase))
        {
            string? failedCommand = CabezalDashboardUnificadoProgram2ResponseRouter
                .SelectFailedCommand(
                    line,
                    _pending.Select(item =>
                        new Program2PendingOperation(item.Key, item.Value.Sequence)));
            if (failedCommand is not null
                && _pending.Remove(failedCommand, out PendingCommand? failed))
            {
                failed.Failed?.Invoke();
                if (failedCommand.Equals("uni_stop", StringComparison.OrdinalIgnoreCase)
                    || failedCommand.Equals("uni_emergency_stop", StringComparison.OrdinalIgnoreCase))
                {
                    StopAllAnimations(false);
                    Status = "Parada solicitada; falló una trama OFF y el estado físico no está confirmado";
                }
                else
                {
                    Status = "Firmware rechazó la operación correlacionada";
                }
                LastMessage = $"{line} ({failedCommand})";
            }
            else if (line.Contains("|STOP_TX", StringComparison.OrdinalIgnoreCase))
            {
                StopAllAnimations(false);
                Status = "Parada solicitada; falló una trama OFF y el estado físico no está confirmado";
                LastMessage = line;
            }
            else if (line.StartsWith("ERR|UNI|", StringComparison.OrdinalIgnoreCase))
            {
                Status = "Error UNI sin operación pendiente correlacionable";
                LastMessage = line;
            }
        }
        StateHasChanged();
    }

    private void ApplyUntrackedConfirmation(string command)
    {
        if (command.Equals("uni_j_run_all", StringComparison.OrdinalIgnoreCase)) foreach (Program2JGroup group in JGroups) StartJAnimation(group);
        else if (command.Equals("uni_j_stop_all", StringComparison.OrdinalIgnoreCase)) foreach (Program2JGroup group in JGroups) StopJAnimation(group, true);
        else if (command.Equals("uni_y_run_all", StringComparison.OrdinalIgnoreCase)) StartYarnAnimation(CabezalDashboardUnificadoProgram2Commands.YarnAllVisualPeriodMs);
        else if (command.Equals("uni_y_stop_all", StringComparison.OrdinalIgnoreCase)) StopYarnAnimation(true);
        else if (command.Equals("uni_s_run_all", StringComparison.OrdinalIgnoreCase)) foreach (Program2StitchMotor stitch in StitchMotors) StartStitchAnimation(stitch);
        else if (command.Equals("uni_s_stop_all", StringComparison.OrdinalIgnoreCase)) foreach (Program2StitchMotor stitch in StitchMotors) StopStitchAnimation(stitch, false);
        else if (command.Equals("uni_off_all", StringComparison.OrdinalIgnoreCase)) ApplyAllOutputs(false);
        else if (command.Equals("uni_on_all", StringComparison.OrdinalIgnoreCase)) ApplyAllOutputs(true);
        else if (command.Equals("uni_stop", StringComparison.OrdinalIgnoreCase)
                 || command.Equals("uni_emergency_stop", StringComparison.OrdinalIgnoreCase)) ApplyGlobalStopConfirmation();
    }

    private void ApplyAllOutputs(bool on)
    {
        foreach (Program2JGroup group in JGroups) StopJAnimation(group, false);
        StopYarnAnimation(false);
        foreach (Program2StitchMotor stitch in StitchMotors) StopStitchAnimation(stitch, false);
        foreach (Program2JGroup group in JGroups) group.Register = on ? (byte)0x00 : (byte)0xFF;
        Array.Fill(YarnStates, on);
    }

    private void ApplyGlobalStopConfirmation()
    {
        StopAllAnimations(false);
        foreach (Program2JGroup group in JGroups) group.Register = 0xFF;
        Array.Fill(YarnStates, false);
        Status = "Parada aplicada; J/Yarn apagados, estado físico DEN/SIC/Feet/Stitch no confirmado";
    }

    private void StartDenAnimation(Program2DenMotor motor)
    {
        StopDenAnimation(motor);
        motor.Running = true;
        motor.Animation = new CancellationTokenSource();
        _ = AnimateAsync(motor.Animation.Token, CabezalDashboardUnificadoProgram2Commands.DenVisualPeriodMs, step =>
        {
            int position = step % 5 + 1;
            motor.SelectedPosition = position;
            motor.Value = DenPositions[position - 1];
            motor.PreviewValue = motor.Value;
        });
    }

    private static void StopDenAnimation(Program2DenMotor motor)
    {
        motor.Animation?.Cancel();
        motor.Animation?.Dispose();
        motor.Animation = null;
        motor.Running = false;
    }

    private void StartJAnimation(Program2JGroup group)
    {
        StopJAnimation(group, false);
        group.BeforeRun = group.Register;
        group.Running = true;
        group.Animation = new CancellationTokenSource();
        _ = AnimateAsync(group.Animation.Token, CabezalDashboardUnificadoProgram2Commands.JVisualPeriodMs, step => group.Register = (byte)~(1 << (step % 8)));
    }

    private static void StopJAnimation(Program2JGroup group, bool restore)
    {
        bool wasRunning = group.Running;
        group.Animation?.Cancel();
        group.Animation?.Dispose();
        group.Animation = null;
        group.Running = false;
        if (restore && wasRunning) group.Register = group.BeforeRun;
    }

    private void StartYarnAnimation(int periodMs = CabezalDashboardUnificadoProgram2Commands.YarnVisualPeriodMs)
    {
        StopYarnAnimation(false);
        _yarnBeforeRun = [.. YarnStates];
        YarnRunning = true;
        _yarnAnimation = new CancellationTokenSource();
        _ = AnimateAsync(_yarnAnimation.Token, periodMs, step =>
        {
            Array.Fill(YarnStates, false);
            YarnStates[step % CabezalDashboardUnificadoProgram2Commands.YarnChannelCount] = true;
        });
    }

    private void StopYarnAnimation(bool restore)
    {
        bool wasRunning = YarnRunning;
        _yarnAnimation?.Cancel();
        _yarnAnimation?.Dispose();
        _yarnAnimation = null;
        YarnRunning = false;
        if (restore && wasRunning && _yarnBeforeRun is not null) Array.Copy(_yarnBeforeRun, YarnStates, CabezalDashboardUnificadoProgram2Commands.YarnChannelCount);
    }

    private void StartStitchAnimation(Program2StitchMotor stitch)
    {
        StopStitchAnimation(stitch, false);
        stitch.BeforeRun = stitch.SelectedPosition;
        stitch.Running = true;
        stitch.Animation = new CancellationTokenSource();
        _ = AnimateAsync(stitch.Animation.Token, CabezalDashboardUnificadoProgram2Commands.StitchVisualPeriodMs, step => stitch.SelectedPosition = step % 6);
    }

    private static void StopStitchAnimation(Program2StitchMotor stitch, bool restore)
    {
        bool wasRunning = stitch.Running;
        stitch.Animation?.Cancel();
        stitch.Animation?.Dispose();
        stitch.Animation = null;
        stitch.Running = false;
        if (restore && wasRunning) stitch.SelectedPosition = stitch.BeforeRun;
    }

    private async Task AnimateAsync(CancellationToken token, int periodMs, Action<int> update)
    {
        int step = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await InvokeAsync(() => { update(step++); StateHasChanged(); });
                await Task.Delay(periodMs, token);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void StopAllAnimations(bool restore)
    {
        foreach (Program2DenMotor motor in DenMotors) StopDenAnimation(motor);
        foreach (Program2JGroup group in JGroups) StopJAnimation(group, restore);
        StopYarnAnimation(restore);
        foreach (Program2StitchMotor stitch in StitchMotors) StopStitchAnimation(stitch, restore);
    }

    private void OnEmergencyStateChanged()
    {
        if (EmergencyStop.IsEmergencyStopActive)
        {
            StopAllAnimations(false);
            Status = "Parada de emergencia solicitada; estado físico aún no confirmado";
        }
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EmergencyStop.StateChanged -= OnEmergencyStateChanged;
        StopAllAnimations(false);
        _pending.Clear();
    }

    private sealed record PendingCommand(Action? Confirmed, Action? Failed, long Sequence);

    protected sealed class Program2DenMotor(int displayNumber, int physicalNumber, string canId, string selector)
    {
        public int DisplayNumber { get; } = displayNumber;
        public int PhysicalNumber { get; } = physicalNumber;
        public string CanId { get; } = canId;
        public string Selector { get; } = selector;
        public int Value { get; set; }
        public int PreviewValue { get; set; }
        public int SelectedPosition { get; set; } = 1;
        public bool Running { get; set; }
        public bool Pending { get; set; }
        public CancellationTokenSource? Animation { get; set; }
    }

    protected sealed class Program2JGroup(int displayNumber, int physicalNumber)
    {
        public int DisplayNumber { get; } = displayNumber;
        public int PhysicalNumber { get; } = physicalNumber;
        public byte Register { get; set; } = 0xFF;
        public byte BeforeRun { get; set; } = 0xFF;
        public bool Running { get; set; }
        public bool Pending { get; set; }
        public CancellationTokenSource? Animation { get; set; }
        public bool IsOn(int pin) => (Register & (1 << (pin - 1))) == 0;
    }

    protected sealed class Program2StitchMotor(int number)
    {
        public int Number { get; } = number;
        public int SelectedPosition { get; set; }
        public int BeforeRun { get; set; }
        public bool Running { get; set; }
        public bool Pending { get; set; }
        public CancellationTokenSource? Animation { get; set; }
    }
}
