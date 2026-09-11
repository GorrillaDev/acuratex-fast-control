using AcuratexControlApp.Services;
using Microsoft.AspNetCore.Components;

namespace AcuratexControlApp.Components;

public partial class CabezalDashboardUnificadoPrograma2
{
    protected static readonly int[] DenPositions = CabezalDashboardUnificadoProgram2Commands.DenPositionValues;

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
    protected List<Program2SicMotor> SicMotors { get; } =
        CabezalDashboardUnificadoProgram2Commands.SicPositionValues
            .Select((positions, index) => new Program2SicMotor(index + 1, positions))
            .ToList();
    protected List<Program2TransferMotor> TransferMotors { get; } = [new(1), new(2)];
    protected bool[] YarnStates { get; } = new bool[CabezalDashboardUnificadoProgram2Commands.YarnChannelCount];
    protected bool YarnRunning { get; set; }
    protected bool YarnPending => _yarnPendingCount > 0;
    protected string Status { get; set; } = "Programa 2 confirmado";
    protected string LastMessage { get; set; } = "Listo.";
    protected bool ActionsBlocked => InitBlocked || EmergencyStop.IsEmergencyStopActive || !Connection.IsConnected;

    private readonly Dictionary<string, PendingCommand> _pending = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _yarnAnimation;
    private readonly CancellationTokenSource?[] _sicAnimations = new CancellationTokenSource?[2];
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
    public Task StopAllAsync() => SendAsync(CabezalDashboardUnificadoProgram2Commands.StopAll, ApplyGlobalStopConfirmation);

    protected Task RunDenAsync(Program2DenMotor motor, bool alternate)
    {
        motor.Pending = true;
        string command = alternate
            ? CabezalDashboardUnificadoProgram2Commands.DenRun1(motor.PhysicalNumber)
            : CabezalDashboardUnificadoProgram2Commands.DenRun(motor.PhysicalNumber);
        return SendAsync(command, () =>
        {
            motor.Pending = false;
            StartDenAnimation(motor, alternate);
        }, () => motor.Pending = false);
    }

    protected Task StopDenAsync(Program2DenMotor motor, bool alternate)
    {
        if (InitBlocked) return Task.CompletedTask;
        motor.Pending = true;
        string command = alternate
            ? CabezalDashboardUnificadoProgram2Commands.DenStop1(motor.PhysicalNumber)
            : CabezalDashboardUnificadoProgram2Commands.DenStop(motor.PhysicalNumber);
        return SendAsync(command, () =>
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

    protected Task RunSicAsync(Program2SicMotor sic)
    {
        sic.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.SicRun(sic.Number), () =>
        {
            sic.Pending = false;
            StartSicAnimation(sic);
        }, () => sic.Pending = false);
    }

    protected Task StopSicAsync(Program2SicMotor sic)
    {
        if (InitBlocked) return Task.CompletedTask;
        sic.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.SicStop(sic.Number), () =>
        {
            sic.Pending = false;
            StopSicAnimation(sic);
        }, () => sic.Pending = false);
    }

    protected Task SelectSicPositionAsync(Program2SicMotor sic, int positionNumber)
    {
        if (ActionsBlocked) return Task.CompletedTask;
        sic.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.SicSelect(sic.Number, positionNumber), () =>
        {
            StopSicAnimation(sic);
            sic.Pending = false;
            sic.SelectedPosition = positionNumber;
            sic.Value = sic.Positions[positionNumber - 1];
            sic.PreviewValue = sic.Value;
        }, () => sic.Pending = false);
    }

    protected void PreviewSic(Program2SicMotor sic, ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString(), out int value)) sic.PreviewValue = Math.Clamp(value, 0, 750);
    }

    protected Task CommitSicAsync(Program2SicMotor sic, ChangeEventArgs args)
    {
        PreviewSic(sic, args);
        int requested = sic.PreviewValue;
        sic.Pending = true;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.SicPosition(sic.Number, requested), () =>
        {
            StopSicAnimation(sic);
            sic.Pending = false;
            sic.Value = requested;
            sic.PreviewValue = requested;
            sic.SelectedPosition = Array.IndexOf(sic.Positions, requested) + 1;
        }, () =>
        {
            sic.Pending = false;
            sic.PreviewValue = sic.Value;
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

    protected async Task SetAllTransferAsync(Program2TransferMotor transfer, bool on)
    {
        if (ActionsBlocked) return;
        StopTransferAnimation(transfer, false);
        for (int pin = 1; pin <= CabezalDashboardUnificadoProgram2Commands.TransferChannelCount; pin++)
        {
            int currentPin = pin;
            transfer.PendingCount++;
            await SendAsync(CabezalDashboardUnificadoProgram2Commands.TransferPin(transfer.Number, pin, on), () =>
            {
                transfer.States[currentPin - 1] = on;
                transfer.PendingCount = Math.Max(0, transfer.PendingCount - 1);
            }, () => transfer.PendingCount = Math.Max(0, transfer.PendingCount - 1));
        }
    }

    protected Task ToggleTransferAsync(Program2TransferMotor transfer, int pin)
    {
        if (ActionsBlocked) return Task.CompletedTask;
        bool requested = !transfer.States[pin - 1];
        transfer.PendingCount++;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.TransferPin(transfer.Number, pin, requested), () =>
        {
            StopTransferAnimation(transfer, false);
            transfer.States[pin - 1] = requested;
            transfer.PendingCount = Math.Max(0, transfer.PendingCount - 1);
        }, () => transfer.PendingCount = Math.Max(0, transfer.PendingCount - 1));
    }

    protected Task RunTransferAsync(Program2TransferMotor transfer)
    {
        transfer.PendingCount++;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.TransferRun(transfer.Number), () =>
        {
            transfer.PendingCount = Math.Max(0, transfer.PendingCount - 1);
            StartTransferAnimation(transfer);
        }, () => transfer.PendingCount = Math.Max(0, transfer.PendingCount - 1));
    }

    protected Task StopTransferAsync(Program2TransferMotor transfer)
    {
        if (InitBlocked) return Task.CompletedTask;
        transfer.PendingCount++;
        return SendAsync(CabezalDashboardUnificadoProgram2Commands.TransferStop(transfer.Number), () =>
        {
            transfer.PendingCount = Math.Max(0, transfer.PendingCount - 1);
            StopTransferAnimation(transfer, true);
        }, () => transfer.PendingCount = Math.Max(0, transfer.PendingCount - 1));
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
        else if (command.Equals("uni_s_run_all", StringComparison.OrdinalIgnoreCase)) foreach (Program2TransferMotor transfer in TransferMotors) StartTransferAnimation(transfer);
        else if (command.Equals("uni_s_stop_all", StringComparison.OrdinalIgnoreCase)) foreach (Program2TransferMotor transfer in TransferMotors) StopTransferAnimation(transfer, true);
        else if (command.Equals("uni_off_all", StringComparison.OrdinalIgnoreCase)) ApplyAllOutputs(false);
        else if (command.Equals("uni_on_all", StringComparison.OrdinalIgnoreCase)) ApplyAllOutputs(true);
        else if (command.Equals("uni_stop", StringComparison.OrdinalIgnoreCase)
                 || command.Equals("uni_emergency_stop", StringComparison.OrdinalIgnoreCase)) ApplyGlobalStopConfirmation();
    }

    private void ApplyAllOutputs(bool on)
    {
        foreach (Program2JGroup group in JGroups) StopJAnimation(group, false);
        StopYarnAnimation(false);
        foreach (Program2TransferMotor transfer in TransferMotors) StopTransferAnimation(transfer, false);
        foreach (Program2JGroup group in JGroups) group.Register = on ? (byte)0x00 : (byte)0xFF;
        Array.Fill(YarnStates, on);
        foreach (Program2TransferMotor transfer in TransferMotors) Array.Fill(transfer.States, on);
    }

    private void ApplyGlobalStopConfirmation()
    {
        StopAllAnimations(false);
        Status = "STOP aplicado: secuencias detenidas sin forzar OFF físico";
    }

    private void StartDenAnimation(Program2DenMotor motor, bool alternate)
    {
        StopDenAnimation(motor);
        motor.Running = true;
        motor.RunMode = alternate ? "RUN1" : "RUN";
        motor.Animation = new CancellationTokenSource();
        int[] sequence = alternate
            ? CabezalDashboardUnificadoProgram2Commands.DenRun1Sequence
            : CabezalDashboardUnificadoProgram2Commands.DenRunSequence;
        int period = alternate
            ? CabezalDashboardUnificadoProgram2Commands.DenRun1VisualPeriodMs
            : CabezalDashboardUnificadoProgram2Commands.DenVisualPeriodMs;
        _ = AnimateAsync(motor.Animation.Token, period, step =>
        {
            int position = sequence[step % sequence.Length];
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
        motor.RunMode = string.Empty;
    }

    private void StartSicAnimation(Program2SicMotor sic)
    {
        StopSicAnimation(sic);
        sic.Running = true;
        CancellationTokenSource animation = new();
        _sicAnimations[sic.Number - 1] = animation;
        _ = AnimateAsync(animation.Token, CabezalDashboardUnificadoProgram2Commands.SicVisualPeriodMs, step =>
        {
            int position = CabezalDashboardUnificadoProgram2Commands.SicRunSequence[step % CabezalDashboardUnificadoProgram2Commands.SicRunSequence.Length];
            sic.SelectedPosition = position;
            sic.Value = sic.Positions[position - 1];
            sic.PreviewValue = sic.Value;
        });
    }

    private void StopSicAnimation(Program2SicMotor sic)
    {
        int index = sic.Number - 1;
        _sicAnimations[index]?.Cancel();
        _sicAnimations[index]?.Dispose();
        _sicAnimations[index] = null;
        sic.Running = false;
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

    private void StartTransferAnimation(Program2TransferMotor transfer)
    {
        StopTransferAnimation(transfer, false);
        transfer.BeforeRun = [.. transfer.States];
        transfer.Running = true;
        transfer.Animation = new CancellationTokenSource();
        _ = AnimateAsync(transfer.Animation.Token, CabezalDashboardUnificadoProgram2Commands.TransferVisualPeriodMs, step =>
        {
            Array.Fill(transfer.States, false);
            transfer.States[step % CabezalDashboardUnificadoProgram2Commands.TransferChannelCount] = true;
        });
    }

    private static void StopTransferAnimation(Program2TransferMotor transfer, bool restore)
    {
        bool wasRunning = transfer.Running;
        transfer.Animation?.Cancel();
        transfer.Animation?.Dispose();
        transfer.Animation = null;
        transfer.Running = false;
        if (restore && wasRunning) Array.Copy(transfer.BeforeRun, transfer.States, transfer.States.Length);
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
        foreach (Program2SicMotor sic in SicMotors) StopSicAnimation(sic);
        foreach (Program2JGroup group in JGroups) StopJAnimation(group, restore);
        StopYarnAnimation(restore);
        foreach (Program2TransferMotor transfer in TransferMotors) StopTransferAnimation(transfer, restore);
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
        public int Value { get; set; } = 18;
        public int PreviewValue { get; set; } = 18;
        public int SelectedPosition { get; set; } = 5;
        public bool Running { get; set; }
        public string RunMode { get; set; } = string.Empty;
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

    protected sealed class Program2SicMotor(int number, int[] positions)
    {
        public int Number { get; } = number;
        public int[] Positions { get; } = positions;
        public int Value { get; set; } = positions[0];
        public int PreviewValue { get; set; } = positions[0];
        public int SelectedPosition { get; set; } = 1;
        public bool Running { get; set; }
        public bool Pending { get; set; }
    }

    protected sealed class Program2TransferMotor(int number)
    {
        public int Number { get; } = number;
        public bool[] States { get; } = new bool[CabezalDashboardUnificadoProgram2Commands.TransferChannelCount];
        public bool[] BeforeRun { get; set; } = new bool[CabezalDashboardUnificadoProgram2Commands.TransferChannelCount];
        public bool Running { get; set; }
        public int PendingCount { get; set; }
        public bool Pending => PendingCount > 0;
        public CancellationTokenSource? Animation { get; set; }
    }
}
