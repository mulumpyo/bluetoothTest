namespace BluetoothDiagnostics;

public record StageEvidence(DateTimeOffset Time, string Stage, string Status, string Source, string Detail, string? ConnectionId = null, string? Phone = null);
public record StageResult(string Key, string Name, string Status, string Source, string Detail, DateTimeOffset? Time);
public record SessionRecord(string ConnectionId, string DeviceId, string? PhoneAlias, DateTimeOffset FirstObserved,
    DateTimeOffset LastActivityAt, DateTimeOffset? EndedAt, string? EndReason, bool Active,
    int ReadCount, int WriteCount, int CommandSuccessCount, int CommandRejectedCount, int ErrorCount);
public record ActivityRecord(DateTimeOffset Time, string ConnectionId, string DeviceId, string? PhoneAlias, string Operation, bool Success, string Detail);
public record TrialParticipant(string ConnectionId, string DeviceId, string PhoneAlias);
public record MultiTrial(string Id, DateTimeOffset StartedAt, DateTimeOffset Deadline, TrialParticipant[] Participants,
    int ActivityOffset, string Status, string Detail, DateTimeOffset? ConfirmedAt, string[] ReceivedFrom);
public record RunSnapshot(string RunId, DateTimeOffset StartedAt, string? DeviceId, string? InstanceId, StageResult[] Stages,
    StageEvidence[] Evidence, SessionRecord[] Sessions, ActivityRecord[] Activities, MultiTrial[] Trials,
    int ObservedActiveSessions, int PeakObservedSessions, int PeakSuccessfulPhones, string PhysicalConnectionCount,
    string MaximumControllerConnections, string AdvertisingMode, bool LegacyForced, string ActualAdvertisingPacketFormat);

/// <summary>Evidence and trial policy shared by WinRT, GUI, console and hardware-independent tests.</summary>
public sealed class DiagnosticRun(Func<DateTimeOffset>? clock = null)
{
    private readonly object gate = new();
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.Now);
    private readonly List<StageEvidence> evidence = [];
    private readonly List<SessionRecord> sessions = [];
    private readonly List<ActivityRecord> activities = [];
    private readonly List<MultiTrial> trials = [];
    private string? deviceId, instanceId;
    private int peakSessions, peakPhones;
    public string Id { get; } = Guid.NewGuid().ToString();
    private DateTimeOffset? startedAt;
    private static readonly (string Key, string Name)[] Definitions = [
        ("adapterSupport", "어댑터 Peripheral 지원"), ("advertising", "광고 시작"),
        ("phoneDiscovery", "스마트폰 광고 발견"), ("gattConnection", "GATT 연결"),
        ("commandReceived", "GATT 명령 수신"), ("multiPhone", "다중 스마트폰 연결")];

    private void Add(string stage, string status, string source, string detail, string? connectionId = null, string? phone = null)
    {
        startedAt ??= now();
        evidence.Add(new(now(), stage, status, source, detail, connectionId, phone));
    }
    public void Adapter(AdapterInfo? adapter)
    {
        lock (gate)
        {
            deviceId = adapter?.DeviceId;
            instanceId = adapter?.InstanceId;
            var c = adapter?.Capabilities.GetValueOrDefault("IsPeripheralRoleSupported");
            Add("adapterSupport", c?.Value == true ? "성공" : c?.Value == false ? "실패" : "확인되지 않음", "Windows BluetoothAdapter API",
                c?.Value is bool value ? $"IsPeripheralRoleSupported={value}" : adapter?.Error ?? c?.Detail ?? c?.State ?? "선택된 어댑터 없음");
        }
    }
    public void AdvertisingRequested() { lock (gate) Add("advertising", "확인되지 않음", "PC 시작 요청", "연결 가능/검색 가능 GATT 광고 요청. 성공 이벤트 대기."); }
    public void Advertising(string status, string error)
    {
        lock (gate)
        {
            if (status == "Stopped") return; // Ending a test does not erase its start result.
            var result = status == "Started" && error == "Success" ? "성공" : error != "Success" || status == "Aborted" ? "실패" : "확인되지 않음";
            Add("advertising", result, "AdvertisementStatusChanged", $"{status}, Error={error}. 무선 패킷 형식 별도 확인 필요.");
        }
    }
    public void AdvertisingFailure(string detail) { lock (gate) Add("advertising", "실패", "Windows GATT API / 사전 검사", detail); }
    public void Manual(string phone, string stage, string outcome, string detail)
    {
        lock (gate)
        {
            var key = stage switch { "검색" => "phoneDiscovery", "연결" => "gattConnection", "쓰기" => "commandReceived", "동시 연결" => "multiPhone", _ => "manual" };
            var status = outcome == "미확인" ? "확인되지 않음" : outcome;
            // A claimed write/multi success cannot replace missing PC-side evidence.
            if (key is "commandReceived" or "multiPhone" && status == "성공")
            {
                status = "확인되지 않음";
                detail = "사용자 성공 기록만 수신. PC 요청 및 세션 상관 검증 필요. " + detail;
                if (evidence.Any(e => e.Stage == key && e.Status == "성공")) key = "manual";
            }
            Add(key, status, "사용자 실측 / nRF Connect", detail, phone: phone);
        }
    }
    public string Open(string id)
    {
        lock (gate)
        {
            var existing = sessions.LastOrDefault(s => s.Active && s.DeviceId == id);
            if (existing != null) return existing.ConnectionId;
            var time = now();
            var session = new SessionRecord(Guid.NewGuid().ToString(), id, null, time, time, null, null, true, 0, 0, 0, 0, 0);
            sessions.Add(session);
            peakSessions = Math.Max(peakSessions, sessions.Count(s => s.Active));
            Add("gattConnection", "성공", "활성 GattSession의 실제 요청", "첫 GATT 요청에서 활성 세션 관측. 물리 링크 전체 개수와 연결 협상 시각은 확인되지 않음.", session.ConnectionId);
            return session.ConnectionId;
        }
    }
    public void Activity(string id, string operation, bool success, string detail, bool acceptedCommand = false)
    {
        lock (gate)
        {
            var i = sessions.FindLastIndex(s => s.Active && s.DeviceId == id);
            if (i < 0) return;
            var s = sessions[i];
            sessions[i] = s with { LastActivityAt = now(), ReadCount = s.ReadCount + (operation == "READ" ? 1 : 0),
                WriteCount = s.WriteCount + (operation == "WRITE" ? 1 : 0), CommandSuccessCount = s.CommandSuccessCount + (acceptedCommand && success ? 1 : 0),
                CommandRejectedCount = s.CommandRejectedCount + (operation == "WRITE" && !acceptedCommand ? 1 : 0), ErrorCount = s.ErrorCount + (!success ? 1 : 0) };
            activities.Add(new(now(), s.ConnectionId, id, s.PhoneAlias, operation, success, detail));
            if (operation == "WRITE") Add("commandReceived", acceptedCommand && success ? "성공" : "실패", "PC GATT Write 요청 처리", detail, s.ConnectionId, s.PhoneAlias);
            UpdateTrial();
        }
    }
    public void Close(string id, string reason)
    {
        lock (gate)
        {
            var i = sessions.FindLastIndex(s => s.Active && s.DeviceId == id);
            if (i < 0) return;
            var s = sessions[i];
            sessions[i] = s with { Active = false, EndedAt = now(), EndReason = reason };
            activities.Add(new(now(), s.ConnectionId, id, s.PhoneAlias, "CLOSE", true, reason));
            UpdateTrial();
        }
    }
    public void EndAll(string reason) { lock (gate) foreach (var s in sessions.Where(s => s.Active).ToArray()) Close(s.DeviceId, reason); }
    public void Assign(string connectionId, string alias)
    {
        lock (gate)
        {
            if (alias is not ("A" or "B" or "C")) throw new ArgumentException("폰 별칭은 A/B/C 중 하나여야 합니다.");
            UpdateTrial();
            if (trials.LastOrDefault()?.Status is "진행 중" or "PC 요청 조건 충족") throw new InvalidOperationException("시험 중 매핑을 바꿀 수 없습니다. 시험 취소 후 변경하세요.");
            var i = sessions.FindLastIndex(s => s.ConnectionId == connectionId && s.Active);
            if (i < 0) throw new InvalidOperationException("활성 세션을 선택하세요.");
            if (sessions.Any(s => s.Active && s.PhoneAlias == alias && s.ConnectionId != connectionId)) throw new InvalidOperationException($"스마트폰 {alias}는 다른 활성 세션에 매핑되어 있습니다.");
            sessions[i] = sessions[i] with { PhoneAlias = alias };
            activities.Add(new(now(), connectionId, sessions[i].DeviceId, alias, "MAP", true, "사용자가 물리 스마트폰과 세션을 매핑"));
        }
    }
    public void BeginTrial(int count, bool identitiesConfirmed)
    {
        lock (gate)
        {
            UpdateTrial();
            if (!identitiesConfirmed) throw new ArgumentException("A/B/C가 서로 다른 실제 폰임을 확인해야 합니다.");
            if (count is < 2 or > 3) throw new ArgumentException("시험 대상은 2대 또는 3대입니다.");
            if (trials.LastOrDefault()?.Status is "진행 중" or "PC 요청 조건 충족") throw new InvalidOperationException("진행 중인 시험을 먼저 종료하세요.");
            var aliases = new[] { "A", "B", "C" }.Take(count).ToArray();
            var participants = aliases.Select(a => sessions.SingleOrDefault(s => s.Active && s.PhoneAlias == a)
                ?? throw new InvalidOperationException($"스마트폰 {a}의 활성 세션을 매핑하세요.")).Select(s => new TrialParticipant(s.ConnectionId, s.DeviceId, s.PhoneAlias!)).ToArray();
            trials.Add(new(Guid.NewGuid().ToString(), now(), now().AddSeconds(30), participants, activities.Count, "진행 중",
                "30초 이내 모든 폰에서 OPEN_SIMULATION 전송 후 응답/연결 유지 확인 필요.", null, []));
            Add("multiPhone", "확인되지 않음", "다중 폰 시험 시작", $"{count}대, 30초 구간. 사용자 실기기 식별 확인 + PC 요청 검증.");
        }
    }
    private void UpdateTrial()
    {
        var trial = trials.LastOrDefault();
        if (trial == null || trial.Status is not ("진행 중" or "PC 요청 조건 충족")) return;
        var missing = trial.Participants.Any(p => !sessions.Any(s => s.ConnectionId == p.ConnectionId && s.Active));
        if (missing || now() > trial.Deadline)
        {
            var detail = missing ? "시험 도중 대상 세션 종료/추적 종료. 물리적 연결 실패 원인은 별도 확인 필요." : "30초 구간 내 전원 명령 및 사용자 응답 확인 미완료. 하드웨어 최대치의 근거가 아님.";
            trials[^1] = trial with { Status = "실패", Detail = detail };
            Add("multiPhone", "실패", "PC 시험 구간 판정", detail);
            return;
        }
        var received = trial.Participants.Where(p => activities.Skip(trial.ActivityOffset).Any(a => a.ConnectionId == p.ConnectionId && a.Time >= trial.StartedAt && a.Time <= trial.Deadline && a.Operation == "WRITE" && a.Success && a.Detail == "OK:OPEN_SIMULATION")).Select(p => p.PhoneAlias).ToArray();
        trials[^1] = trial with { ReceivedFrom = received, Status = received.Length == trial.Participants.Length ? "PC 요청 조건 충족" : "진행 중",
            Detail = received.Length == trial.Participants.Length ? "전원 정상 명령 수신. 폰별 OK 응답과 연결 유지 확인 후 확정하세요." : $"수신 폰 {received.Length}/{trial.Participants.Length}. 각 폰에서 시험 시작 이후 명령 전송 필요." };
    }
    public void ConfirmTrial(bool responsesConfirmed)
    {
        lock (gate)
        {
            UpdateTrial();
            var trial = trials.LastOrDefault();
            if (!responsesConfirmed || trial?.Status != "PC 요청 조건 충족") throw new InvalidOperationException("모든 폰의 구간 내 정상 명령 수신과 스마트폰 응답/연결 유지 확인이 필요합니다.");
            trials[^1] = trial with { Status = "성공", ConfirmedAt = now(), Detail = "구간 내 모든 대상의 PC 명령 수신 + 사용자 폰 응답/연결 유지 확인. 물리 링크 수 직접 계측은 아님." };
            peakPhones = Math.Max(peakPhones, trial.Participants.Length);
            Add("multiPhone", "성공", "PC 요청 검증 + 사용자 실기기 확인", $"최소 {trial.Participants.Length}대 시험 성공. Trial={trial.Id}");
        }
    }
    public void CancelTrial()
    {
        lock (gate)
        {
            if (trials.LastOrDefault() is { Status: "진행 중" or "PC 요청 조건 충족" } trial)
            {
                trials[^1] = trial with { Status = "취소", Detail = "사용자가 시험 취소" };
                Add("multiPhone", "확인되지 않음", "사용자 취소", "진행 중 다중 폰 시험 취소");
            }
        }
    }
    public RunSnapshot Snapshot()
    {
        lock (gate)
        {
            UpdateTrial();
            startedAt ??= now();
            var stages = Definitions.Select(d => {
                var e = evidence.LastOrDefault(e => e.Stage == d.Key);
                return new StageResult(d.Key, d.Name, e?.Status ?? "미시험", e?.Source ?? "없음", e?.Detail ?? "실제 시험 결과 없음", e?.Time);
            }).ToArray();
            return new(Id, startedAt.Value, deviceId, instanceId, stages, evidence.ToArray(), sessions.ToArray(), activities.ToArray(), trials.ToArray(),
                sessions.Count(s => s.Active), peakSessions, peakPhones, "확인되지 않음", "확인되지 않음",
                "연결 가능한 기본 GATT 광고 / Extended PHY 설정 사용 안 함", false, "확인되지 않음");
        }
    }
}
