using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace BluetoothDiagnostics;

public sealed class GattServer(DiagnosticLog log, DiagnosticRun? diagnosticRun = null) : IDisposable
{
    public static readonly Guid ServiceUuid = Guid.Parse("7a310001-8d4e-4ef0-a4d7-95d1a468d001");
    public static readonly Guid CharacteristicUuid = Guid.Parse("7a310002-8d4e-4ef0-a4d7-95d1a468d001");
    private readonly object gate = new();
    private readonly Dictionary<string, GattSession> sessions = [];
    private readonly Dictionary<string, string> responses = [];
    public DiagnosticRun Run { get; } = diagnosticRun ?? new();
    private GattServiceProvider? provider;
    private GattLocalCharacteristic? characteristic;
    private bool disposed;
    public string Status { get; private set; } = "미시험";
    public bool IsCreated { get { lock (gate) return provider != null; } }
    public string? AdvertisementStatus { get; private set; }
    public string? AdvertisementError { get; private set; }
    public object Snapshot()
    {
        lock (gate)
        {
            var diagnostics = Run.Snapshot();
            return new { Status, IsCreated, AdvertisementStatus, AdvertisementError, ServiceUuid, CharacteristicUuid,
                diagnostics.PeakObservedSessions, diagnostics.PeakSuccessfulPhones, diagnostics.PhysicalConnectionCount,
                diagnostics.MaximumControllerConnections, Diagnostics = diagnostics,
                Connections = diagnostics.Sessions.Where(s => s.Active).Select(s => new { Id = s.DeviceId, s.ConnectionId,
                    s.PhoneAlias, s.FirstObserved, s.LastActivityAt, s.ReadCount, s.WriteCount, s.CommandSuccessCount,
                    s.CommandRejectedCount, s.ErrorCount, ObservedSeconds = (DateTimeOffset.Now - s.FirstObserved).TotalSeconds }).ToArray() };
        }
    }
    private void WriteLog(string kind, string detail) => log.Add(kind, detail, Run.Id);
    public async Task StartAsync(AdapterInfo selected, Inventory inventory)
    {
        if (disposed) throw new ObjectDisposedException(nameof(GattServer));
        if (IsCreated) throw new InvalidOperationException("서버가 이미 생성되어 있습니다. 먼저 종료하세요.");
        try
        {
            await inventory.ProbeAsync(selected);
            Run.Adapter(selected);
            if (selected.Capabilities.GetValueOrDefault("IsPeripheralRoleSupported")?.Value != true)
                throw new InvalidOperationException("Peripheral 지원 True가 확인되지 않았습니다. 기능 검사 결과를 확인하세요.");
            var currentDefault = await BluetoothAdapter.GetDefaultAsync();
            if (currentDefault?.DeviceId != selected.DeviceId || inventory.Adapters.Count != 1 || selected.Pnp?.Present != true)
                throw new InvalidOperationException("GattServiceProvider는 어댑터 직접 지정 인자가 없습니다. 다른 Bluetooth 라디오를 비활성화하고 새로 조회하세요. 선택 장치가 Present=True인 유일한 기본 라디오여야 합니다.");
            WriteLog("광고 형식", "Legacy 강제 설정 API 없음. Extended PHY 설정은 사용하지 않습니다. 실제 무선 패킷 형식은 확인되지 않음.");
            WriteLog("어댑터 주소", $"Windows BluetoothAdapter.BluetoothAddress={selected.BluetoothAddress ?? selected.BluetoothAddressState}. 실제 BLE 광고 주소와 일치 여부는 확인되지 않음.");
            var result = await GattServiceProvider.CreateAsync(ServiceUuid);
            if (result.Error != BluetoothError.Success) throw new InvalidOperationException($"GattServiceProvider.CreateAsync: {result.Error}");
            provider = result.ServiceProvider;
            provider.AdvertisementStatusChanged += AdvertisingChanged;
            var cr = await provider.Service.CreateCharacteristicAsync(CharacteristicUuid, new GattLocalCharacteristicParameters
            {
                CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Write,
                ReadProtectionLevel = GattProtectionLevel.Plain, WriteProtectionLevel = GattProtectionLevel.Plain,
                UserDescription = "OPEN_SIMULATION diagnostic"
            });
            if (cr.Error != BluetoothError.Success) throw new InvalidOperationException($"CreateCharacteristicAsync: {cr.Error}");
            characteristic = cr.Characteristic;
            characteristic.ReadRequested += ReadRequested;
            characteristic.WriteRequested += WriteRequested;
            Status = "광고 시작 요청 (성공 이벤트 대기)";
            Run.AdvertisingRequested();
            provider.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
            WriteLog("GATT", $"Service={ServiceUuid}, Characteristic={CharacteristicUuid}");
        }
        catch (Exception ex)
        {
            Run.AdvertisingFailure($"{ex.GetType().Name} HRESULT=0x{ex.HResult:X8}: {ex.Message}");
            try { Stop(); } catch (Exception cleanup) { WriteLog("오류", "실패 후 정리: " + cleanup.Message); }
            throw;
        }
    }
    private void AdvertisingChanged(GattServiceProvider sender, GattServiceProviderAdvertisementStatusChangedEventArgs args)
    {
        lock (gate)
        {
            if (provider != sender || disposed) return;
            AdvertisementStatus = args.Status.ToString();
            AdvertisementError = args.Error.ToString();
            Status = $"{args.Status}, Error={args.Error}";
            Run.Advertising(AdvertisementStatus, AdvertisementError);
            WriteLog("Advertising", Status);
        }
    }
    private bool Observe(GattSession session)
    {
        lock (gate)
        {
            if (disposed || provider == null || session.SessionStatus != GattSessionStatus.Active) return false;
            var id = session.DeviceId.Id;
            if (sessions.TryGetValue(id, out var previous))
            {
                if (previous.SessionStatus == GattSessionStatus.Active) return true;
                previous.SessionStatusChanged -= SessionChanged;
                Run.Close(id, "새 요청 전 이전 세션 Closed 확인");
            }
            sessions[id] = session;
            session.SessionStatusChanged += SessionChanged;
            var episode = Run.Open(id);
            WriteLog("연결 관측", $"{id}, ConnectionId={episode}. 첫 GATT 요청 기준 / 물리 연결 수 확인되지 않음.");
            return true;
        }
    }
    private void SessionChanged(GattSession sender, GattSessionStatusChangedEventArgs args)
    {
        lock (gate)
        {
            var id = sender.DeviceId.Id;
            if (!sessions.TryGetValue(id, out var current) || current != sender) return;
            WriteLog("세션", $"{id}: {args.Status}, Error={args.Error}");
            if (args.Status == GattSessionStatus.Closed)
            {
                sender.SessionStatusChanged -= SessionChanged;
                sessions.Remove(id);
                responses.Remove(id);
                Run.Close(id, $"SessionStatusChanged: Closed, Error={args.Error}");
                WriteLog("연결 종료 관측", id + " (첫 요청부터의 관측 이력 보존)");
            }
        }
    }
    private async void ReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        var id = args.Session.DeviceId.Id;
        try
        {
            var request = await args.GetRequestAsync();
            if (request == null) { WriteLog("읽기", "요청 취소/null"); return; }
            if (!Observe(args.Session)) { request.RespondWithProtocolError(0x0E); return; }
            byte[] bytes;
            lock (gate) bytes = Encoding.UTF8.GetBytes(responses.GetValueOrDefault(id, "READY"));
            if (request.Offset > bytes.Length)
            {
                request.RespondWithProtocolError(0x07);
                Run.Activity(id, "READ", false, "InvalidOffset 0x07");
                return;
            }
            using var writer = new DataWriter();
            writer.WriteBytes(bytes[(int)request.Offset..]);
            request.RespondWithValue(writer.DetachBuffer());
            Run.Activity(id, "READ", true, Encoding.UTF8.GetString(bytes));
            WriteLog("READ", $"{id}, Offset={request.Offset}");
        }
        catch (Exception ex)
        {
            Run.Activity(id, "READ", false, $"HRESULT=0x{ex.HResult:X8}: {ex.Message}");
            WriteLog("오류", $"GATT READ {id}: HRESULT=0x{ex.HResult:X8}: {ex.Message}");
        }
        finally { deferral.Complete(); }
    }
    private async void WriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
    {
        var deferral = args.GetDeferral();
        var id = args.Session.DeviceId.Id;
        try
        {
            var request = await args.GetRequestAsync();
            if (request == null) { WriteLog("쓰기", "요청 취소/null"); return; }
            if (!Observe(args.Session)) { if (request.Option == GattWriteOption.WriteWithResponse) request.RespondWithProtocolError(0x0E); return; }
            using var reader = DataReader.FromBuffer(request.Value);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            var command = Encoding.UTF8.GetString(bytes);
            var valid = request.Offset == 0 && command == "OPEN_SIMULATION";
            lock (gate) responses[id] = valid ? "OK:OPEN_SIMULATION" : "ERROR:COMMAND";
            if (request.Option == GattWriteOption.WriteWithResponse)
            {
                if (valid) request.Respond(); else request.RespondWithProtocolError(0x80);
            }
            Run.Activity(id, "WRITE", valid, valid ? "OK:OPEN_SIMULATION" : "ERROR:COMMAND Offset=" + request.Offset, valid);
            WriteLog("WRITE", $"{id}, UTF8={System.Text.Json.JsonSerializer.Serialize(command)}, 수락={valid}");
        }
        catch (Exception ex)
        {
            Run.Activity(id, "WRITE", false, $"HRESULT=0x{ex.HResult:X8}: {ex.Message}");
            WriteLog("오류", $"GATT WRITE {id}: HRESULT=0x{ex.HResult:X8}: {ex.Message}");
        }
        finally { deferral.Complete(); }
    }
    public void Stop()
    {
        lock (gate)
        {
            var old = provider;
            provider = null;
            if (old != null) old.AdvertisementStatusChanged -= AdvertisingChanged;
            if (characteristic != null) { characteristic.ReadRequested -= ReadRequested; characteristic.WriteRequested -= WriteRequested; }
            characteristic = null;
            foreach (var s in sessions.Values) s.SessionStatusChanged -= SessionChanged;
            sessions.Clear();
            responses.Clear();
            Run.EndAll("서버 추적 종료 (물리 링크 해제 확인 아님)");
            if (old == null) return;
            try { old.StopAdvertising(); }
            finally
            {
                AdvertisementStatus = "Stopped";
                Status = "광고 종료 / 서버 핸들러 해제";
                WriteLog("GATT", Status + " (물리 링크 강제 해제 API 없음)");
            }
        }
    }
    public void Dispose() { Stop(); disposed = true; }
}
