using System.Text;
using System.Text.Json;

namespace BluetoothDiagnostics;

/// <summary>One JSON message per line over redirected stdio. Hardware operations stay in WinRT/C#.</summary>
public static class GuiWorker
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        var output = Console.Out;
        Console.SetOut(TextWriter.Null);
        var writeGate = new object();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        void Send(object message)
        {
            lock (writeGate) { output.WriteLine(JsonSerializer.Serialize(message, options)); output.Flush(); }
        }
        var log = new DiagnosticLog();
        log.Added += entry => Send(new { type = "log", entry });
        var inventory = new Inventory(log);
        var server = new GattServer(log);
        log.CurrentRunId = server.Run.Id;
        AdapterInfo? selected = null;
        var observations = new List<ManualObservation>();
        var archives = new List<RunArchive>();
        void NewRun(string reason)
        {
            server.Dispose();
            var previous = server.Run.Snapshot();
            if (previous.Evidence.Length > 0 || observations.Count > 0) archives.Add(new(previous, observations.ToArray(), reason, DateTimeOffset.Now));
            server = new GattServer(log);
            log.CurrentRunId = server.Run.Id;
            observations.Clear();
        }
        object Snapshot() => new { runId = server.Run.Id, selectedDeviceId = selected?.DeviceId, adapters = inventory.Adapters,
            pnpDevices = inventory.Pnp, gatt = server.Snapshot(), logs = log.Entries, observations, archivedRuns = archives };
        using var cancel = new CancellationTokenSource();
        var tick = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                while (await timer.WaitForNextTickAsync(cancel.Token)) Send(new { type = "gatt", gatt = server.Snapshot() });
            }
            catch (OperationCanceledException) { }
        });
        Send(new { type = "ready", protocol = 1 });
        try
        {
            while (await Console.In.ReadLineAsync() is { } line)
            {
                string? id = null;
                string? command = null;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    id = root.GetProperty("id").GetString();
                    command = root.GetProperty("command").GetString();
                    var data = root.TryGetProperty("data", out var d) ? d : default;
                    string Field(string key) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
                    bool Flag(string key) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
                    object? result = null;
                    switch (command)
                    {
                        case "refresh":
                            server.Stop();
                            var previous = selected?.DeviceId;
                            await inventory.RefreshAsync();
                            selected = inventory.Adapters.FirstOrDefault(a => a.DeviceId == previous);
                            if (selected == null)
                            {
                                var candidates = inventory.Adapters.Where(a => a.IsBarrot && a.Pnp?.Present == true).ToArray();
                                selected = candidates.Length == 1 ? candidates[0] : null;
                            }
                            if (previous != selected?.DeviceId && previous != null) NewRun("목록 갱신 중 어댑터 변경/제거");
                            server.Run.Adapter(selected);
                            break;
                        case "select":
                            var target = inventory.Adapters.FirstOrDefault(a => a.DeviceId == Field("deviceId"))
                                ?? throw new InvalidOperationException("목록에 없는 어댑터입니다. 새로 조회하세요.");
                            NewRun("사용자 어댑터 선택");
                            selected = target;
                            server.Run.Adapter(selected);
                            log.Add("어댑터 선택", target.InstanceId ?? target.DeviceId);
                            break;
                        case "probe":
                            if (selected == null) throw new InvalidOperationException("먼저 어댑터를 선택하세요.");
                            await inventory.ProbeAsync(selected);
                            server.Run.Adapter(selected);
                            log.Add("기능 검사", selected.Error ?? $"Peripheral={selected.Capabilities.GetValueOrDefault("IsPeripheralRoleSupported")?.Value?.ToString() ?? "미확인"}");
                            break;
                        case "start":
                            if (selected == null) throw new InvalidOperationException("먼저 어댑터를 선택하세요.");
                            if (server.IsCreated) throw new InvalidOperationException("이미 서버가 생성되어 있습니다. 먼저 종료하세요.");
                            var selectedId = selected.DeviceId;
                            NewRun("새 GATT 서버 시험");
                            await inventory.RefreshAsync();
                            selected = inventory.Adapters.FirstOrDefault(a => a.DeviceId == selectedId)
                                ?? throw new InvalidOperationException("선택 어댑터가 사라졌습니다.");
                            await server.StartAsync(selected, inventory);
                            break;
                        case "stop": server.Stop(); break;
                        case "map":
                            server.Run.Assign(Field("connectionId"), Field("alias"));
                            log.Add("폰 매핑", $"{Field("connectionId")} -> {Field("alias")} (사용자 실제 폰 식별)");
                            break;
                        case "trial-start":
                            if (!server.IsCreated) throw new InvalidOperationException("먼저 GATT 서버를 시작하세요.");
                            server.Run.BeginTrial(data.GetProperty("count").GetInt32(), Flag("identitiesConfirmed"));
                            log.Add("다중 폰 시험", "시험 시작 / 30초 이내 전원 정상 명령 및 폰 응답 확인 필요");
                            break;
                        case "trial-confirm":
                            server.Run.ConfirmTrial(Flag("responsesConfirmed"));
                            log.Add("다중 폰 시험", "PC 명령 수신 + 사용자 폰 응답/연결 유지 확인으로 성공 확정");
                            break;
                        case "trial-cancel": server.Run.CancelTrial(); break;
                        case "note":
                            var phone = Field("phone");
                            var stage = Field("stage");
                            var outcome = Field("outcome");
                            var detail = Field("detail");
                            if (phone.Length is < 1 or > 80 || stage.Length is < 1 or > 80 || outcome.Length is < 1 or > 80 || detail.Length > 2000)
                                throw new ArgumentException("실측 항목을 확인하세요. 메모는 2000자 이내입니다.");
                            if (stage is not ("검색" or "연결" or "읽기" or "쓰기" or "동시 연결" or "연결 해제" or "광고 형식") || outcome is not ("성공" or "실패" or "미확인"))
                                throw new ArgumentException("지원하지 않는 실측 항목/결과입니다.");
                            var note = new ManualObservation(DateTimeOffset.Now, server.Run.Id, selected?.DeviceId, phone, stage, outcome, detail);
                            observations.Add(note);
                            server.Run.Manual(phone, stage, outcome, detail);
                            log.Add("사용자 실측 (API 검증 아님)", $"{phone} / {stage} / {outcome}: {detail}");
                            break;
                        case "export":
                            var directory = Field("directory");
                            if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("절대 저장 경로가 필요합니다.");
                            result = await GuiReport.SaveAsync(directory, Snapshot(), inventory, selected, server.Snapshot(), log.Entries, observations, server.Run.Snapshot(), archives.ToArray(), options);
                            log.Add("결과 저장", (string)result);
                            break;
                        case "snapshot": break;
                        case "quit":
                            server.Stop();
                            Send(new { type = "response", id, ok = true });
                            return;
                        default: throw new ArgumentException("지원하지 않는 명령입니다.");
                    }
                    Send(new { type = "response", id, ok = true, result, state = Snapshot() });
                }
                catch (Exception ex)
                {
                    if (command == "start" && !server.IsCreated) server.Run.AdvertisingFailure($"HRESULT=0x{ex.HResult:X8}: {ex.Message}");
                    log.Error("GUI 명령", ex);
                    Send(new { type = "response", id, ok = false, error = $"0x{ex.HResult:X8}: {ex.Message}", state = Snapshot() });
                }
            }
        }
        finally
        {
            cancel.Cancel();
            try { await tick; }
            finally { server.Dispose(); }
        }
    }
}

public record ManualObservation(DateTimeOffset Time, string RunId, string? DeviceId, string Phone, string Stage, string Outcome, string Detail);
public record RunArchive(RunSnapshot Diagnostics, ManualObservation[] Observations, string Reason, DateTimeOffset ArchivedAt);

public static class GuiReport
{
    public static async Task<string> SaveAsync(string parent, object state, Inventory inventory, AdapterInfo? selected,
        object gatt, LogEntry[] logs, List<ManualObservation> observations, RunSnapshot current, RunArchive[] archives, JsonSerializerOptions options)
    {
        var directory = Path.Combine(parent, "Bluetooth-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(directory);
        var report = new { generatedAt = DateTimeOffset.Now, os = Environment.OSVersion.ToString(), state,
            interpretation = "관측 최고 세션 수는 하한입니다. Controller 최대 수 미확인. 사용자 실측은 API 결과와 별도입니다." };
        var pretty = new JsonSerializerOptions(options) { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(report, pretty), new UTF8Encoding(true));
        var rows = new List<string> { "Category,DeviceId,Name,Field,Value,State,Detail" };
        void Row(params string?[] cells) => rows.Add(string.Join(",", cells.Select(c => "\"" + (c ?? "").Replace("\"", "\"\"") + "\"")));
        foreach (var a in inventory.Adapters)
        {
            Row("Adapter", a.DeviceId, a.Name, "InstanceId", a.InstanceId, a.IsDefault ? "Default" : "", a.Error);
            Row("Adapter", a.DeviceId, a.Name, "BluetoothAddress", a.BluetoothAddress, a.BluetoothAddressState, a.BluetoothAddressError);
            foreach (var c in a.Capabilities) Row("Capability", a.DeviceId, a.Name, c.Key, c.Value.Value?.ToString(), c.Value.State, c.Value.Detail);
        }
        foreach (var p in inventory.Pnp) Row("PnP", p.InstanceId, p.Name, "Present", p.Present?.ToString(), p.Status, $"{p.Provider} {p.Version}");
        Row("Gatt", selected?.DeviceId, "", "Snapshot", JsonSerializer.Serialize(gatt, options), "관측", "Controller 최대 연결 수 미확인");
        void RunRows(RunSnapshot run)
        {
            foreach (var stage in run.Stages) Row("Stage", run.DeviceId, stage.Name, stage.Key, stage.Status, stage.Source, $"Run={run.RunId}; {stage.Time:O}; {stage.Detail}");
            foreach (var e in run.Evidence) Row("Evidence", run.DeviceId, e.Phone, e.Stage, e.Status, e.Source, $"Run={run.RunId}; {e.Time:O}; Connection={e.ConnectionId}; {e.Detail}");
            foreach (var s in run.Sessions) Row("Session", s.DeviceId, s.PhoneAlias, s.ConnectionId, JsonSerializer.Serialize(s, options), s.Active ? "관측 활성" : "추적 종료", "Run=" + run.RunId);
            foreach (var a in run.Activities) Row("Activity", a.DeviceId, a.PhoneAlias, a.Operation, a.Detail, a.Success.ToString(), $"Run={run.RunId}; {a.Time:O}; {a.ConnectionId}");
            foreach (var t in run.Trials) Row("MultiTrial", run.DeviceId, t.Id, t.Participants.Length.ToString(), JsonSerializer.Serialize(t, options), t.Status, "Run=" + run.RunId);
        }
        RunRows(current);
        foreach (var archive in archives)
        {
            RunRows(archive.Diagnostics);
            foreach (var n in archive.Observations) Row("ArchivedManual", n.DeviceId, n.Phone, n.Stage, n.Outcome, "사용자 실측", $"{n.Time:O}; {n.RunId}; {n.Detail}");
        }
        foreach (var note in observations) Row("ManualObservation", note.DeviceId, note.Phone, note.Stage, note.Outcome, "사용자 실측", $"{note.Time:O} / {note.RunId} / {note.Detail}");
        foreach (var e in logs) Row("Log", "", e.RunId, e.Event, e.Detail, "", e.Time.ToString("O"));
        await File.WriteAllLinesAsync(Path.Combine(directory, "report.csv"), rows, new UTF8Encoding(true));
        return directory;
    }
}
