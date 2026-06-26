using System.Globalization;
using System.Linq;
using System.Text;

namespace AcuratexControlApp.Services;

public sealed record CanRxFrame(
    DateTime ReceivedAt,
    string Bus,
    uint Id,
    int Dlc,
    byte[] Data,
    string RawLine);

public sealed record CanAlarmInfo(
    DateTime DetectedAt,
    string Bus,
    uint Id,
    int Dlc,
    byte[] Data,
    string AlarmType,
    string Title,
    string Message,
    string RawLine);

public sealed class CanAlarmDetector
{
    private static readonly IReadOnlyDictionary<uint, byte[]> MotorFirstBytes = new Dictionary<uint, byte[]>
    {
        [0x710] = [0xB0],
        [0x711] = [0xB1],
        [0x712] = [0xB2],
        [0x713] = [0xB3],
        [0x714] = [0xB4],
        [0x715] = [0xB5],
        [0x716] = [0xB6],
        [0x717] = [0xB7],
        [0x718] = [0xC1, 0xC3],
        [0x719] = [0xC1, 0xC4],
    };

    private static readonly IReadOnlyDictionary<byte, string> MotorCodeTitles = new Dictionary<byte, string>
    {
        [0x10] = "Feedback / Encoder Error",
        [0x20] = "Zero-Point Error",
    };

    public CanAlarmInfo? Detect(CanRxFrame frame)
    {
        if (frame.Data.Length == 0) {
            return null;
        }

        if (frame.Id >= 0x700 && frame.Id <= 0x70F) {
            return DetectHeartbeatAlarm(frame);
        }

        if (frame.Id >= 0x710 && frame.Id <= 0x719) {
            return DetectMotorAlarm(frame);
        }

        return null;
    }

    private static CanAlarmInfo? DetectHeartbeatAlarm(CanRxFrame frame)
    {
        if (frame.Data.Length < 4) {
            return null;
        }

        if (frame.Data[0] != 0xC1) {
            return null;
        }

        if (frame.Data[1] != 0x00) {
            return null;
        }

        if (frame.Data[3] != 0x00) {
            return null;
        }

        byte sub = frame.Data[2];
        if (sub != 0x00 && sub != 0x01) {
            return null;
        }

        int node = checked((int)(frame.Id - 0x700));
        string title = "ERROR STATE (C1)";
        string who = $"NODE {node} (sub={sub})";
        string message = BuildAlarmMessage(title, who, 0xC1, frame);

        return new CanAlarmInfo(
            frame.ReceivedAt,
            NormalizeBus(frame.Bus),
            frame.Id,
            frame.Dlc,
            frame.Data,
            "HB_C1",
            title,
            message,
            frame.RawLine);
    }

    private static CanAlarmInfo? DetectMotorAlarm(CanRxFrame frame)
    {
        if (frame.Data.Length < 4) {
            return null;
        }

        if (!MotorFirstBytes.TryGetValue(frame.Id, out byte[]? allowedFirstBytes)) {
            return null;
        }

        if (!allowedFirstBytes.Contains(frame.Data[0])) {
            return null;
        }

        if (frame.Data[1] != 0x00 || frame.Data[2] != 0x00) {
            return null;
        }

        byte code = frame.Data[3];
        if (!MotorCodeTitles.ContainsKey(code)) {
            return null;
        }

        int motor = checked((int)(frame.Id - 0x70F));
        string title = MotorCodeTitles[code];
        string who = $"MOTOR {motor}";
        string message = BuildAlarmMessage(title, who, code, frame);

        return new CanAlarmInfo(
            frame.ReceivedAt,
            NormalizeBus(frame.Bus),
            frame.Id,
            frame.Dlc,
            frame.Data,
            "MOTOR",
            title,
            message,
            frame.RawLine);
    }

    private static string BuildAlarmMessage(string title, string who, byte code, CanRxFrame frame)
    {
        string codeHex = code.ToString("X2", CultureInfo.InvariantCulture);
        string idHex = frame.Id.ToString("X3", CultureInfo.InvariantCulture);
        string data = FormatData(frame.Data);

        return string.Join(
            Environment.NewLine,
            $"{title} - {who}",
            $"CODE=0x{codeHex} ({code})",
            $"ID=0x{idHex}  DLC={frame.Dlc}  DATA={data}",
            string.Empty,
            "Action: STOP ALL (J/Yarn/Stitch) + DEN/SIC sequences aborted.",
            "TX locked (HALT) until you close this popup.");
    }

    private static string NormalizeBus(string bus)
    {
        return string.IsNullOrWhiteSpace(bus) ? "CAN1" : bus.Trim().ToUpperInvariant();
    }

    private static string FormatData(byte[] data)
    {
        if (data.Length == 0) {
            return string.Empty;
        }

        return string.Join(" ", data.Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }
}

public sealed record HeadTestFrameTrace(
    int Sequence,
    DateTime ReceivedAt,
    string Bus,
    uint CanId,
    int Dlc,
    byte[] Data,
    byte? Code,
    string Interpretation,
    string RawLine);

public sealed record HeadTestResult(
    DateTime ReceivedAt,
    string Status,
    string Title,
    string Description,
    string Bus,
    uint CanId,
    int Dlc,
    byte[] Data,
    string RawLine,
    IReadOnlyList<string>? MissingA1Boards = null,
    IReadOnlyList<string>? MissingA2Boards = null,
    IReadOnlyList<string>? MissingExpansionBoards = null,
    IReadOnlyList<string>? PresentA1Boards = null,
    IReadOnlyList<string>? PresentA2Boards = null,
    IReadOnlyList<string>? PresentExpansionBoards = null,
    bool IsComplete = false,
    byte StatusCode = 0,
    IReadOnlyList<HeadTestFrameTrace>? Frames = null)
{
    public string GeneralStatus => Status;

    public byte[] RawData => Data;

    public IReadOnlyList<string> MissingA1BoardsValue => MissingA1Boards ?? Array.Empty<string>();

    public IReadOnlyList<string> MissingA2BoardsValue => MissingA2Boards ?? Array.Empty<string>();

    public IReadOnlyList<string> MissingExpansionBoardsValue => MissingExpansionBoards ?? Array.Empty<string>();

    public IReadOnlyList<string> PresentA1BoardsValue => PresentA1Boards ?? Array.Empty<string>();

    public IReadOnlyList<string> PresentA2BoardsValue => PresentA2Boards ?? Array.Empty<string>();

    public IReadOnlyList<string> PresentExpansionBoardsValue => PresentExpansionBoards ?? Array.Empty<string>();

    public IReadOnlyList<HeadTestFrameTrace> FramesValue => Frames ?? Array.Empty<HeadTestFrameTrace>();
}

public sealed record class CanAlarmHistoryItem
{
    public DateTime DetectedAt { get; init; }

    public DateTime? ClosedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public string AlarmType { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Bus { get; init; } = "CAN1";

    public uint CanId { get; init; }

    public int Dlc { get; init; }

    public byte[] Data { get; init; } = [];

    public string RawLine { get; init; } = string.Empty;
}

public sealed class HeadTestResultDetector
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(3000);

    private readonly object _gate = new();
    private readonly List<HeadTestFrameTrace> _frames = new();
    private DateTime _startedAt;
    private DateTime _deadlineAt;
    private bool _running;
    private bool _saw700;
    private bool _gotFirst;
    private byte _firstCode;
    private byte _lastCode;
    private int _a1Count;
    private int _a2Count;
    private int _sequence;
    private string _bus = "CAN1";
    private string _status = "IDLE";
    private string _title = "TESTEO";
    private string _description = "Listo.";

    public string Status
    {
        get { lock (_gate) { return _status; } }
    }

    public string Title
    {
        get { lock (_gate) { return _title; } }
    }

    public string Description
    {
        get { lock (_gate) { return _description; } }
    }

    public bool IsRunning
    {
        get { lock (_gate) { return _running; } }
    }

    public void Begin(string bus)
    {
        lock (_gate) {
            _startedAt = DateTime.Now;
            _deadlineAt = _startedAt.Add(DefaultTimeout);
            _running = true;
            _saw700 = false;
            _gotFirst = false;
            _firstCode = 0;
            _lastCode = 0;
            _a1Count = 0;
            _a2Count = 0;
            _sequence = 0;
            _frames.Clear();
            _bus = string.IsNullOrWhiteSpace(bus) ? "CAN1" : bus.Trim().ToUpperInvariant();
            _status = "CONSULTANDO";
            _title = "TESTEO";
            _description = "TESTEO: esperando respuesta CAN...";
        }
    }

    public void Cancel()
    {
        lock (_gate) {
            ResetLocked("IDLE", "Listo.");
        }
    }

    public HeadTestResult? Detect(CanRxFrame frame)
    {
        if (frame.Data.Length == 0) {
            return null;
        }

        lock (_gate) {
            if (!_running) {
                return null;
            }

            if (frame.Id == 0x702
                && frame.Dlc >= 2
                && frame.Data[0] == 0x3F
                && frame.Data[1] == 0x00) {
                RecordFrameLocked(frame, "REARM 702 3F 00", null);
                _startedAt = DateTime.Now;
                _deadlineAt = _startedAt.Add(DefaultTimeout);
                _saw700 = false;
                _gotFirst = false;
                _firstCode = 0;
                _lastCode = 0;
                _a1Count = 0;
                _a2Count = 0;
                _status = "IDLE";
                _title = "TESTEO";
                _description = "TESTEO rearmado por 702 3F 00.";
                return null;
            }

            if (frame.Id != 0x700 || frame.Dlc < 1) {
                return null;
            }

            _saw700 = true;
            _lastCode = frame.Data[0];
            if (!_gotFirst) {
                _gotFirst = true;
                _firstCode = _lastCode;
            }

            if (_lastCode == 0xA2) {
                _a2Count++;
            }

            if (_lastCode == 0xA1) {
                _a1Count++;
            }

            if (_lastCode == 0xCB) {
                return FinishLocked(frame, "COMPLETO", "TESTEO COMPLETO", "OK: placas del cabezal presentes (CB)", true, 0xCB);
            }

            if (_lastCode == 0xBC) {
                return FinishLocked(frame, "INCOMPLETO", "TESTEO INCOMPLETO", "FALTA: placa 3 de expansion (BC)", false, 0xBC);
            }

            if (_lastCode == 0xBF) {
                string description = BuildBfDescriptionLocked();
                return FinishLocked(frame, "INCOMPLETO", "TESTEO INCOMPLETO", description, false, 0xBF);
            }

            string interpretation = BuildObservationDescriptionLocked(_lastCode);
            RecordFrameLocked(frame, interpretation, _lastCode);
            _status = "CONSULTANDO";
            _title = "TESTEO";
            _description = interpretation;
            return null;
        }
    }

    public bool TryGetTimeoutResult(out HeadTestResult result)
    {
        lock (_gate) {
            if (!_running || DateTime.Now < _deadlineAt) {
                result = null!;
                return false;
            }

            result = BuildTimeoutResultLocked();
            return true;
        }
    }

    public HeadTestResult BuildTimeoutResult()
    {
        lock (_gate) {
            return BuildTimeoutResultLocked();
        }
    }

    public string BuildStatusSummary()
    {
        lock (_gate) {
            return $"{_status} - {_description}";
        }
    }

    private HeadTestResult FinishLocked(CanRxFrame frame,
                                        string status,
                                        string title,
                                        string description,
                                        bool isComplete,
                                        byte statusCode)
    {
        _running = false;
        _status = status;
        _title = title;
        _description = description;

        string interpretation = description;
        RecordFrameLocked(frame, interpretation, statusCode);
        return BuildResultLocked(frame,
                                 status,
                                 title,
                                 description,
                                 isComplete,
                                 statusCode,
                                 _frames.ToArray());
    }

    private HeadTestResult BuildTimeoutResultLocked()
    {
        _running = false;

        string status;
        string title;
        string description;

        if (!_saw700) {
            status = "SIN RESPUESTA";
            title = "TESTEO SIN RESPUESTA";
            description = "La consulta fue transmitida, pero no se recibio una respuesta valida del cabezal.";
        } else if (_a1Count > 0 && _a2Count > 0) {
            status = "INCOMPLETO";
            title = "TESTEO INCOMPLETO";
            description = "INCONCLUSO: A1/A2 sin BC/BF.";
        } else if (_a2Count > 0 && _a1Count == 0) {
            status = "INCOMPLETO";
            title = "TESTEO INCOMPLETO";
            description = "INCONCLUSO: A2 repetido sin BF/BC.";
        } else {
            status = "ERROR";
            title = "TESTEO ERROR";
            description = "INCONCLUSO: sin patron valido.";
        }

        _status = status;
        _title = title;
        _description = description;

        HeadTestFrameTrace[] frames = _frames.ToArray();
        byte statusCode = _frames.Count > 0 ? _frames[^1].Code ?? _frames[^1].Data.FirstOrDefault() : (byte)0;
        CanRxFrame? lastFrame = CreateSyntheticLastFrame();
        return BuildResultLocked(lastFrame,
                                 status,
                                 title,
                                 description,
                                 false,
                                 statusCode,
                                 frames);
    }

    private HeadTestResult BuildResultLocked(CanRxFrame? frame,
                                             string status,
                                             string title,
                                             string description,
                                             bool isComplete,
                                             byte statusCode,
                                             IReadOnlyList<HeadTestFrameTrace> frames)
    {
        IReadOnlyList<string> missingA1 = Array.Empty<string>();
        IReadOnlyList<string> missingA2 = Array.Empty<string>();
        IReadOnlyList<string> missingExpansion = Array.Empty<string>();
        IReadOnlyList<string> presentA1 = Array.Empty<string>();
        IReadOnlyList<string> presentA2 = Array.Empty<string>();
        IReadOnlyList<string> presentExpansion = Array.Empty<string>();

        if (statusCode == 0xCB) {
            presentA1 = new[] { "placa 1" };
            presentA2 = new[] { "placa 2" };
            presentExpansion = new[] { "placa 3 de expansion" };
        } else if (statusCode == 0xBC) {
            missingExpansion = new[] { "placa 3 de expansion" };
        } else if (statusCode == 0xBF) {
            if (_firstCode == 0xA2) {
                if (_a2Count <= 1) {
                    missingA2 = new[] { "placa 1", "placa 2" };
                } else {
                    missingA2 = new[] { "placa 2" };
                    presentA2 = new[] { "placa 1" };
                }
            } else if (_firstCode == 0xA1) {
                if (_a1Count <= 1) {
                    missingA1 = new[] { "placa 1", "placa 2" };
                } else {
                    missingA1 = new[] { "placa 1" };
                    presentA1 = new[] { "placa 2" };
                }
            }
        }

        DateTime receivedAt = frame?.ReceivedAt ?? DateTime.Now;
        string bus = frame is not null ? NormalizeBus(frame.Bus) : _bus;
        uint canId = frame?.Id ?? 0x700;
        int dlc = frame?.Dlc ?? 0;
        byte[] data = frame is not null ? (byte[])frame.Data.Clone() : (_frames.Count > 0 ? (byte[])_frames[^1].Data.Clone() : []);
        string rawLine = frame?.RawLine ?? (_frames.Count > 0 ? _frames[^1].RawLine : string.Empty);

        return new HeadTestResult(receivedAt,
                                  status,
                                  title,
                                  description,
                                  bus,
                                  canId,
                                  dlc,
                                  data,
                                  rawLine,
                                  missingA1,
                                  missingA2,
                                  missingExpansion,
                                  presentA1,
                                  presentA2,
                                  presentExpansion,
                                  isComplete,
                                  statusCode,
                                  frames);
    }

    private void RecordFrameLocked(CanRxFrame frame, string interpretation, byte? code)
    {
        _frames.Add(new HeadTestFrameTrace(
            ++_sequence,
            frame.ReceivedAt,
            NormalizeBus(frame.Bus),
            frame.Id,
            frame.Dlc,
            (byte[])frame.Data.Clone(),
            code,
            interpretation,
            frame.RawLine));
    }

    private string BuildObservationDescriptionLocked(byte code)
    {
        return code switch
        {
            0xA1 => $"A1 detectado (conteo A1={_a1Count}, A2={_a2Count})",
            0xA2 => $"A2 detectado (conteo A1={_a1Count}, A2={_a2Count})",
            _ => $"TESTEO: recibido 0x{code:X2}, esperando confirmacion...",
        };
    }

    private string BuildBfDescriptionLocked()
    {
        if (_firstCode == 0xA2) {
            if (_a2Count <= 1) {
                return "FALTA: ambas placas de fuerza (A2->BF)";
            }

            return "FALTA: placa 2 (A2 repetido -> BF)";
        }

        if (_firstCode == 0xA1) {
            if (_a1Count <= 1) {
                return "FALTA: ambas placas de fuerza (A1->BF)";
            }

            return "FALTA: placa 1 (A1 repetido -> BF)";
        }

        return "BF: faltan placas (patron no clasificado)";
    }

    private CanRxFrame? CreateSyntheticLastFrame()
    {
        if (_frames.Count == 0) {
            return null;
        }

        HeadTestFrameTrace last = _frames[^1];
        return new CanRxFrame(
            last.ReceivedAt,
            last.Bus,
            last.CanId,
            last.Dlc,
            (byte[])last.Data.Clone(),
            last.RawLine);
    }

    private void ResetLocked(string status, string description)
    {
        _startedAt = DateTime.MinValue;
        _deadlineAt = DateTime.MinValue;
        _running = false;
        _saw700 = false;
        _gotFirst = false;
        _firstCode = 0;
        _lastCode = 0;
        _a1Count = 0;
        _a2Count = 0;
        _sequence = 0;
        _frames.Clear();
        _status = status;
        _title = "TESTEO";
        _description = description;
    }

    private static string NormalizeBus(string bus)
    {
        return string.IsNullOrWhiteSpace(bus) ? "CAN1" : bus.Trim().ToUpperInvariant();
    }
}
