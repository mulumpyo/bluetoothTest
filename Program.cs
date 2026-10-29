using System.Text;
using System.Text.Json;
using BluetoothDiagnostics;

if (args.Contains("--beacon-test"))
{
    await BeaconProbe.RunAsync();
    return;
}

if (args.Contains("--ipc"))
{
    await GuiWorker.RunAsync();
    return;
}

Console.OutputEncoding = Encoding.UTF8;
var log = new DiagnosticLog();
var inventory = new Inventory(log);
var server = new GattServer(log);
var archivedRuns = new List<RunSnapshot>();
log.CurrentRunId = server.Run.Id;
AdapterInfo? selected = null;
try
{
await Refresh();
while (true)
{
    Console.WriteLine("\n=== Bluetooth USB Adapter Diagnostic Tool ===\n1. 어댑터 목록 새로 조회\n2. 어댑터 선택 (BARROT 포함)\n3. BLE 지원 기능 검사\n4. GATT 서버 시작 (새 시험)\n5. GATT 서버 종료\n6. 6단계 결과 / 연결 이력 / 로그\n7. JSON/CSV 저장\n8. 스마트폰 실측 기록\n9. 세션과 A/B/C 매핑\n10. 30초 다중 폰 시험 시작\n11. 폰 응답/연결 유지 확인 및 성공 확정\n12. 다중 폰 시험 취소\n0. 종료");
    Console.WriteLine($"선택: {selected?.Name ?? "없음"} / {selected?.InstanceId}");
    Console.Write("> ");
    var choice = Console.ReadLine();
    if (choice is null or "0") break;
    try
    {
        switch (choice)
        {
            case "1": server.Stop(); await Refresh(); break;
            case "2":
                ListAdapters();
                Console.Write("어댑터 번호: ");
                if (int.TryParse(Console.ReadLine(), out var n) && n >= 1 && n <= inventory.Adapters.Count)
                { selected = inventory.Adapters[n - 1]; NewRun(); ShowCapabilities(selected); }
                else Console.WriteLine("유효한 번호가 아닙니다.");
                break;
            case "3":
                if (selected is null) throw new InvalidOperationException("먼저 어댑터를 선택하세요.");
                await inventory.ProbeAsync(selected); server.Run.Adapter(selected); ShowCapabilities(selected); break;
            case "4":
                if (selected is null) throw new InvalidOperationException("먼저 어댑터를 선택하세요.");
                // Refresh immediately before advertising to avoid a stale multi-radio inventory.
                var oldId = selected.DeviceId;
                NewRun();
                await inventory.RefreshAsync();
                selected = inventory.Adapters.FirstOrDefault(a => a.DeviceId == oldId);
                if (selected is null) throw new InvalidOperationException("선택 어댑터가 사라졌습니다.");
                await server.StartAsync(selected, inventory); break;
            case "5": server.Stop(); break;
            case "6":
                Console.WriteLine(JsonSerializer.Serialize(server.Snapshot(), JsonOptions()));
                foreach (var entry in log.Entries) Console.WriteLine($"{entry.Time:O} {entry.Event}: {entry.Detail}");
                break;
            case "7": await Export(); break;
            case "8":
                Console.Write("스마트폰 별칭: "); var phone = Console.ReadLine() ?? "미지정";
                Console.Write("시험 항목 (검색/연결/읽기/쓰기/동시 연결/연결 해제/광고 형식): "); var stage = Console.ReadLine() ?? "기타";
                Console.Write("결과 (성공/실패/미확인): "); var outcome = Console.ReadLine() ?? "미확인";
                if (outcome is not ("성공" or "실패" or "미확인")) throw new ArgumentException("성공/실패/미확인 중 입력하세요.");
                Console.Write("상세 메모: "); var detail = Console.ReadLine() ?? "";
                server.Run.Manual(phone, stage, outcome, detail);
                log.Add("사용자 실측 (API 결과와 별도)", $"{phone} / {stage} / {outcome}: {detail}"); break;
            case "9":
                var sessions = server.Run.Snapshot().Sessions.Where(s => s.Active).ToArray();
                for (var i = 0; i < sessions.Length; i++) Console.WriteLine($"{i + 1}. {sessions[i].DeviceId}, 폰={sessions[i].PhoneAlias ?? "미매핑"}, 최근={sessions[i].LastActivityAt:O}");
                Console.Write("활성 세션 번호: ");
                if (!int.TryParse(Console.ReadLine(), out var index) || index < 1 || index > sessions.Length) throw new ArgumentException("활성 세션 번호가 필요합니다.");
                Console.Write("실제 폰 별칭 (A/B/C): "); server.Run.Assign(sessions[index - 1].ConnectionId, Console.ReadLine() ?? ""); break;
            case "10":
                if (!server.IsCreated) throw new InvalidOperationException("먼저 서버를 시작하세요.");
                Console.Write("시험할 폰 수 (2 또는 3): ");
                if (!int.TryParse(Console.ReadLine(), out var count)) throw new ArgumentException("2 또는 3을 입력하세요.");
                Console.Write("매핑된 A/B/C가 서로 다른 실제 폰이면 '확인' 입력: ");
                server.Run.BeginTrial(count, Console.ReadLine() == "확인");
                Console.WriteLine("30초 안에 각 폰에서 OPEN_SIMULATION 전송 후 응답을 읽고 메뉴 11을 실행하세요."); break;
            case "11":
                Console.Write("모든 대상 폰의 OK:OPEN_SIMULATION 응답 및 연결 유지를 확인했으면 '확인' 입력: ");
                server.Run.ConfirmTrial(Console.ReadLine() == "확인"); break;
            case "12": server.Run.CancelTrial(); break;
        }
    }
    catch (Exception ex) { log.Error("메뉴 " + choice, ex); }
}
}
finally { server.Dispose(); }

void NewRun()
{
    server.Dispose();
    var previous = server.Run.Snapshot();
    if (previous.Evidence.Length > 0) archivedRuns.Add(previous);
    server = new GattServer(log);
    log.CurrentRunId = server.Run.Id;
    if (selected != null) server.Run.Adapter(selected);
}

async Task Refresh()
{
    var previous = selected?.DeviceId;
    await inventory.RefreshAsync();
    var targets = inventory.Adapters.Where(a => a.IsBarrot && a.Pnp?.Present == true).ToArray();
    selected = targets.Length == 1 ? targets[0] : null;
    if (previous != null && previous != selected?.DeviceId) NewRun();
    server.Run.Adapter(selected);
    ListAdapters();
    if (selected != null) ShowCapabilities(selected);
    else Console.WriteLine("연결된 BARROT 동글을 유일하게 식별하지 못했습니다. 메뉴 2에서 직접 선택하세요.");
}
void ListAdapters()
{
    Console.WriteLine("\n[WinRT 어댑터: 기능 검사 가능]");
    for (int i = 0; i < inventory.Adapters.Count; i++)
    {
        var a = inventory.Adapters[i];
        Console.WriteLine($"{i + 1}. {a.Name}, BARROT={a.IsBarrot}, 기본={a.IsDefault}\n Device ID={a.DeviceId}\n Instance ID={a.InstanceId ?? "미확인"}");
        ShowCapabilities(a);
    }
    Console.WriteLine("[PnP 전체: 과거 장치 포함; Unknown만으로 연결 여부를 판단하지 않음]");
    foreach (var p in inventory.Pnp)
    {
        var match = System.Text.RegularExpressions.Regex.Match(p.InstanceId, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Console.WriteLine($"{p.Name}\n Instance ID={p.InstanceId}\n VID/PID={(match.Success ? match.Groups[1].Value + "/" + match.Groups[2].Value : "해당 없음/미확인")}, 현재 연결={p.Present?.ToString() ?? "미확인"}, PnP={p.Status}, 드라이버={p.Provider ?? "미확인"} {p.Version ?? "미확인"}");
    }
}
void ShowCapabilities(AdapterInfo a)
{
    Console.WriteLine($"\nBluetooth Adapter: {a.Name}\nInstance ID: {a.InstanceId}");
    Console.WriteLine($"Bluetooth MAC (Windows 어댑터 주소): {a.BluetoothAddress ?? a.BluetoothAddressState} {a.BluetoothAddressError}");
    Console.WriteLine("실제 BLE 광고 주소와 일치 여부: 확인되지 않음. nRF Connect의 스캔 결과 및 Service UUID로 식별하세요.");
    if (a.Error != null) Console.WriteLine("조회 실패/미인식: " + a.Error);
    foreach (var (name, value) in a.Capabilities)
        Console.WriteLine($"{name,-34}: {value.Value?.ToString() ?? value.State} {value.Detail}");
    Console.WriteLine("최대 동시 연결 수: 미확인 (실측 필요). 기능 True는 실제 광고/연결 성공을 보장하지 않습니다.");
}
async Task Export()
{
    var directory = Path.Combine(Environment.CurrentDirectory, "results", DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff"));
    Directory.CreateDirectory(directory);
    var report = new { GeneratedAt = DateTimeOffset.Now, OS = Environment.OSVersion.ToString(), SelectedDeviceId = selected?.DeviceId,
        Adapters = inventory.Adapters, PnpDevices = inventory.Pnp, Gatt = server.Snapshot(), ArchivedRuns = archivedRuns, Logs = log.Entries,
        Interpretation = "세션은 첫 GATT 요청에서 관측. Peak는 관측 하한이며 Controller 최대치가 아님. 연결 전 실패는 스마트폰 메모 필요." };
    await File.WriteAllTextAsync(Path.Combine(directory, "report.json"), JsonSerializer.Serialize(report, JsonOptions()), Encoding.UTF8);
    var rows = new List<string> { "Category,DeviceId,Name,Field,Value,State,Detail" };
    void Row(params string?[] cells) => rows.Add(string.Join(",", cells.Select(c => "\"" + (c ?? "").Replace("\"", "\"\"") + "\"")));
    foreach (var a in inventory.Adapters)
    {
        Row("Adapter", a.DeviceId, a.Name, "InstanceId", a.InstanceId, "", a.Error);
        Row("Adapter", a.DeviceId, a.Name, "BluetoothAddress", a.BluetoothAddress, a.BluetoothAddressState, a.BluetoothAddressError);
        foreach (var c in a.Capabilities) Row("Capability", a.DeviceId, a.Name, c.Key, c.Value.Value?.ToString(), c.Value.State, c.Value.Detail);
    }
    foreach (var p in inventory.Pnp) Row("PnP", p.InstanceId, p.Name, "Present", p.Present?.ToString(), p.Status, $"{p.Provider} {p.Version}");
    Row("Gatt", selected?.DeviceId, "", "Snapshot", JsonSerializer.Serialize(server.Snapshot()), "관측", "최대 연결 한계 미확인");
    foreach (var run in archivedRuns.Append(server.Run.Snapshot()))
    {
        foreach (var s in run.Stages) Row("Stage", run.DeviceId, s.Name, s.Key, s.Status, s.Source, $"{run.RunId}; {s.Time:O}; {s.Detail}");
        foreach (var s in run.Sessions) Row("Session", s.DeviceId, s.PhoneAlias, s.ConnectionId, JsonSerializer.Serialize(s), s.Active ? "관측 활성" : "추적 종료", run.RunId);
        foreach (var a in run.Activities) Row("Activity", a.DeviceId, a.PhoneAlias, a.Operation, a.Detail, a.Success.ToString(), $"{run.RunId}; {a.Time:O}; {a.ConnectionId}");
        foreach (var t in run.Trials) Row("MultiTrial", run.DeviceId, t.Id, t.Participants.Length.ToString(), JsonSerializer.Serialize(t), t.Status, run.RunId);
    }
    foreach (var e in log.Entries) Row("Log", "", "", e.Event, e.Detail, "", e.Time.ToString("O"));
    await File.WriteAllLinesAsync(Path.Combine(directory, "report.csv"), rows, Encoding.UTF8);
    Console.WriteLine("저장 완료: " + directory);
}
static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
