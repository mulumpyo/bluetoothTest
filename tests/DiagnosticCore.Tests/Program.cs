using BluetoothDiagnostics;
using System.Text.Json;

var results = new List<object>();
var failed = 0;
void Check(bool value, string reason = "assertion failed") { if (!value) throw new Exception(reason); }
void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
void Test(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); results.Add(new { name, passed = true }); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); results.Add(new { name, passed = false, error = ex.Message }); }
}
string Stage(DiagnosticRun run, string key) => run.Snapshot().Stages.Single(s => s.Key == key).Status;
var time = DateTimeOffset.Parse("2026-10-29T16:00:00+09:00");
DiagnosticRun Setup(int count = 3)
{
    var run = new DiagnosticRun(() => time);
    foreach (var alias in new[] { "A", "B", "C" }.Take(count)) run.Assign(run.Open("test-device-" + alias), alias);
    return run;
}
void Write(DiagnosticRun run, string alias) => run.Activity("test-device-" + alias, "WRITE", true, "OK:OPEN_SIMULATION", true);

Test("six independent untested stages and unknown physical count", () => {
    var d = new DiagnosticRun().Snapshot(); Check(d.Stages.Length == 6 && d.Stages.All(s => s.Status == "미시험"));
    Check(d.PhysicalConnectionCount == "확인되지 않음" && !d.LegacyForced && d.ActualAdvertisingPacketFormat == "확인되지 않음");
});
Test("Peripheral true cannot pass advertising, phones or commands", () => {
    var r = new DiagnosticRun(); r.Adapter(new AdapterInfo { Capabilities = new() { ["IsPeripheralRoleSupported"] = new("조회 성공", true) } });
    Check(Stage(r, "adapterSupport") == "성공"); Check(r.Snapshot().Stages.Skip(1).All(s => s.Status == "미시험"));
});
Test("false support and failed query are distinct", () => {
    var r = new DiagnosticRun(); r.Adapter(new AdapterInfo { Capabilities = new() { ["IsPeripheralRoleSupported"] = new("조회 성공", false) } }); Check(Stage(r, "adapterSupport") == "실패");
    r.Adapter(new AdapterInfo { Error = "0x80070005" }); Check(Stage(r, "adapterSupport") == "확인되지 않음");
});
Test("request alone or partial advertisement cannot pass advertising", () => {
    var r = new DiagnosticRun(); r.AdvertisingRequested(); Check(Stage(r, "advertising") == "확인되지 않음");
    r.Advertising("StartedWithoutAllAdvertisementData", "Success"); Check(Stage(r, "advertising") == "확인되지 않음");
    r.Advertising("Started", "Success"); Check(Stage(r, "advertising") == "성공" && Stage(r, "phoneDiscovery") == "미시험");
    r.Advertising("Stopped", "Success"); Check(Stage(r, "advertising") == "성공");
});
Test("advertisement failure recorded independently", () => { var r = new DiagnosticRun(); r.AdvertisingFailure("RadioNotAvailable"); Check(Stage(r, "advertising") == "실패" && Stage(r, "gattConnection") == "미시험"); });
Test("manual discovery is explicit user evidence", () => { var r = new DiagnosticRun(); r.Manual("A", "검색", "성공", "nRF에서 UUID 확인"); Check(Stage(r, "phoneDiscovery") == "성공" && r.Snapshot().Stages.Single(s => s.Key == "phoneDiscovery").Source.Contains("사용자")); });
Test("manual successful write or multi claim cannot fabricate PC evidence", () => {
    var r = new DiagnosticRun(); r.Manual("A", "쓰기", "성공", "claim"); r.Manual("A/B", "동시 연결", "성공", "claim");
    Check(Stage(r, "commandReceived") == "확인되지 않음" && Stage(r, "multiPhone") == "확인되지 않음" && r.Snapshot().PeakSuccessfulPhones == 0);
});
Test("read, write, rejection counters and last activity", () => {
    var r = Setup(2); var initial = r.Snapshot().Sessions[0].LastActivityAt; time = time.AddSeconds(1);
    r.Activity("test-device-A", "READ", true, "READY"); Write(r, "A"); r.Activity("test-device-A", "WRITE", false, "BAD COMMAND");
    var s = r.Snapshot().Sessions[0]; Check(s.ReadCount == 1 && s.WriteCount == 2 && s.CommandSuccessCount == 1 && s.CommandRejectedCount == 1 && s.ErrorCount == 1 && s.LastActivityAt > initial);
});
Test("reconnect creates a distinct retained episode", () => {
    var r = Setup(2); var old = r.Snapshot().Sessions[0]; r.Close(old.DeviceId, "Closed"); var next = r.Open(old.DeviceId);
    Check(old.ConnectionId != next && r.Snapshot().Sessions.Length == 3 && r.Snapshot().Sessions[0].EndReason == "Closed");
    Check(r.Snapshot().Sessions[^1].PhoneAlias == null);
});
Test("repeated request observation is not another connection", () => {
    var r = Setup(2); var before = r.Snapshot(); r.Open("test-device-A"); Check(r.Snapshot().Sessions.Length == before.Sessions.Length && r.Snapshot().PeakObservedSessions == 2);
});
Test("duplicate aliases and invalid identity assignments rejected", () => { var r = Setup(2); Throws(() => r.Assign(r.Snapshot().Sessions[1].ConnectionId, "A")); Throws(() => r.Assign("missing", "C")); });
Test("two observed sessions alone never pass multi phone", () => { var r = Setup(2); Check(Stage(r, "multiPhone") == "미시험" && r.Snapshot().PeakSuccessfulPhones == 0); });
Test("trial requires physical phone identity confirmation and mapped participants", () => { var r = Setup(2); Throws(() => r.BeginTrial(2, false)); Throws(() => r.BeginTrial(3, true)); });
Test("commands before trial even at same timestamp are excluded", () => {
    var r = Setup(2); Write(r, "A"); Write(r, "B"); r.BeginTrial(2, true);
    Check(r.Snapshot().Trials[^1].ReceivedFrom.Length == 0); Throws(() => r.ConfirmTrial(true));
});
Test("one phone repeated or rejected commands cannot pass a two phone trial", () => {
    var r = Setup(2); r.BeginTrial(2, true); Write(r, "A"); Write(r, "A"); r.Activity("test-device-B", "WRITE", false, "BAD");
    Check(r.Snapshot().Trials[^1].ReceivedFrom.Length == 1); Throws(() => r.ConfirmTrial(true));
});
Test("all valid commands still require operator response confirmation", () => {
    var r = Setup(2); r.BeginTrial(2, true); Write(r, "A"); Write(r, "B");
    Check(r.Snapshot().Trials[^1].Status == "PC 요청 조건 충족" && Stage(r, "multiPhone") != "성공");
    Throws(() => r.ConfirmTrial(false)); r.ConfirmTrial(true); Check(Stage(r, "multiPhone") == "성공" && r.Snapshot().PeakSuccessfulPhones == 2);
});
Test("three phone test raises measured lower bound only after success", () => {
    var r = Setup(); r.BeginTrial(2, true); Write(r, "A"); Write(r, "B"); r.ConfirmTrial(true);
    r.BeginTrial(3, true); Write(r, "A"); Write(r, "B"); Write(r, "C"); r.ConfirmTrial(true);
    Check(r.Snapshot().PeakSuccessfulPhones == 3 && r.Snapshot().MaximumControllerConnections == "확인되지 않음");
});
Test("disconnect invalidates trial even after commands were received", () => {
    var r = Setup(2); r.BeginTrial(2, true); Write(r, "A"); Write(r, "B"); r.Close("test-device-A", "Closed");
    Throws(() => r.ConfirmTrial(true)); Check(Stage(r, "multiPhone") == "실패" && r.Snapshot().PeakSuccessfulPhones == 0);
});
Test("expired trial cannot be confirmed", () => {
    var r = Setup(2); r.BeginTrial(2, true); Write(r, "A"); Write(r, "B"); time = time.AddSeconds(31);
    Throws(() => r.ConfirmTrial(true)); Check(r.Snapshot().Trials[^1].Status == "실패");
});
Test("mapping changes are blocked during trial and cancellation preserves history", () => {
    var r = Setup(2); r.BeginTrial(2, true); Throws(() => r.Assign(r.Snapshot().Sessions[0].ConnectionId, "C"));
    r.CancelTrial(); r.Assign(r.Snapshot().Sessions[0].ConnectionId, "C"); Check(r.Snapshot().Trials[0].Status == "취소");
});
Test("concurrent request bookkeeping is atomic", () => {
    var r = Setup(2); Parallel.For(0, 500, _ => Write(r, "A")); Check(r.Snapshot().Sessions[0].CommandSuccessCount == 500);
});
Test("immutable snapshot remains unchanged when tracking ends", () => {
    var r = Setup(2); var saved = r.Snapshot(); r.EndAll("server stopped - physical unknown"); Check(saved.Sessions.All(s => s.Active) && r.Snapshot().Sessions.All(s => !s.Active));
});
var output = args.FirstOrDefault();
Test("empty Instance ID cannot accidentally confirm the root device", () => { Check(PnpNative.Check("test", "").Present == null); });
if (OperatingSystem.IsWindows()) Test("nonexistent PnP device cannot be considered present", () => {
    Check(PnpNative.Check("nonexistent test node", @"USB\VID_FFFF&PID_FFFF\BluetoothLab-Nonexistent-3fa379fa").Present != true);
});
Test("Bluetooth address is displayed in the conventional byte order with leading zeros", () => {
    Check(BluetoothAddressFormatter.Format(0x0123456789ABUL) == "01:23:45:67:89:AB");
    Check(BluetoothAddressFormatter.Format(0x000000000001UL) == "00:00:00:00:00:01");
    Check(BluetoothAddressFormatter.Format(0) == null && BluetoothAddressFormatter.Format(0x1000000000000UL) == null);
});
if (output != null) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); File.WriteAllText(output, JsonSerializer.Serialize(new { testType = "synthetic policy tests - NOT hardware evidence", failed, count = results.Count, results }, new JsonSerializerOptions { WriteIndented = true })); }
Console.WriteLine($"{results.Count - failed}/{results.Count} passed (synthetic logic tests only)");
return failed == 0 ? 0 : 1;
