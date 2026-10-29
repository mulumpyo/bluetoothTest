using System.Text;
using System.Text.Json;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace BluetoothDiagnostics;

/// <summary>Independent non-connectable Legacy publisher test. Never changes GATT test results.</summary>
public static class BeaconProbe
{
    public static async Task RunAsync()
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        var directory = Path.Combine(Environment.CurrentDirectory, "results", "beacon-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(directory);
        var stopFile = Path.Combine(directory, "stop.request");
        var events = new List<object>();
        var gate = new object();
        var marker = "BLAB-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var bytes = Encoding.ASCII.GetBytes(marker);
        var status = "미시험";
        string? instance = null, mac = null, deviceId = null;
        void Record(string kind, string detail)
        {
            lock (gate)
            {
                var time = DateTimeOffset.Now;
                events.Add(new { Time = time, Kind = kind, Detail = detail });
                Console.WriteLine($"[{time:HH:mm:ss}] {kind}: {detail}");
                var data = new { Test = "Independent Legacy non-connectable manufacturer beacon", Marker = marker,
                    CompanyId = "0xFFFF (diagnostic only)", PayloadHex = Convert.ToHexString(bytes),
                    AdapterInstanceId = instance, AdapterDeviceId = deviceId, WindowsAdapterAddress = mac,
                    ActualAdvertisingAddress = "확인되지 않음", MobileReception = "확인되지 않음", Status = status,
                    IsGattServer = false, GattConnectionSuccess = "판정 대상 아님", UseExtendedAdvertisement = false,
                    StopRequestFile = stopFile, Events = events.ToArray() };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                File.WriteAllText(Path.Combine(directory, "report.json"), json, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "results", "beacon-latest.json"), json, new UTF8Encoding(false));
            }
        }
        BluetoothLEAdvertisementPublisher? publisher = null;
        Windows.Foundation.TypedEventHandler<BluetoothLEAdvertisementPublisher, BluetoothLEAdvertisementPublisherStatusChangedEventArgs>? handler = null;
        try
        {
            var devices = await DeviceInformation.FindAllAsync(BluetoothAdapter.GetDeviceSelector(), ["System.Devices.DeviceInstanceId"]);
            if (devices.Count != 1) throw new InvalidOperationException($"WinRT 라디오 {devices.Count}개. 다른 라디오가 비활성화된 단독 상태가 필요합니다.");
            var device = devices[0];
            instance = device.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var raw) ? raw as string : null;
            if (instance?.Contains("VID_33FA&PID_0012", StringComparison.OrdinalIgnoreCase) != true) throw new InvalidOperationException("유일한 라디오가 BARROT 33FA:0012로 확인되지 않았습니다.");
            var adapter = await BluetoothAdapter.FromIdAsync(device.Id) ?? throw new InvalidOperationException("어댑터 null");
            deviceId = device.Id;
            var defaultAdapter = await BluetoothAdapter.GetDefaultAsync();
            if (defaultAdapter?.DeviceId != deviceId || PnpNative.Check(device.Name, instance).Present != true)
                throw new InvalidOperationException("BARROT 현재 존재 및 기본 라디오 확인 실패");
            if (!adapter.IsLowEnergySupported) throw new InvalidOperationException("BLE 미지원");
            var radio = await adapter.GetRadioAsync();
            if (radio == null || radio.State != RadioState.On) throw new InvalidOperationException($"라디오 상태={radio?.State.ToString() ?? "미확인"}");
            mac = BluetoothAddressFormatter.Format(adapter.BluetoothAddress);
            publisher = new BluetoothLEAdvertisementPublisher { UseExtendedAdvertisement = false };
            using (var writer = new DataWriter())
            {
                writer.WriteBytes(bytes);
                publisher.Advertisement.ManufacturerData.Add(new BluetoothLEManufacturerData(0xFFFF, writer.DetachBuffer()));
            }
            handler = (_, args) => { status = args.Status.ToString(); Record("Publisher", $"{args.Status}, Error={args.Error}"); };
            publisher.StatusChanged += handler;
            status = "시작 요청";
            Record("식별 표식", $"{marker} / HEX={Convert.ToHexString(bytes)}, Windows MAC={mac}. 연결용 광고 아님.");
            Record("결과 저장", Path.Combine(directory, "report.json"));
            publisher.Start();
            var end = DateTimeOffset.Now.AddMinutes(5);
            while (DateTimeOffset.Now < end && !File.Exists(stopFile)) await Task.Delay(500);
            Record("시험 종료 요청", File.Exists(stopFile) ? "사용자/진단자 종료 요청" : "5분 자동 종료");
        }
        catch (Exception ex)
        {
            status = "실패";
            Record("오류", $"{ex.GetType().Name}, HRESULT=0x{ex.HResult:X8}: {ex.Message}");
            Environment.ExitCode = 1;
        }
        finally
        {
            if (publisher != null)
            {
                try { publisher.Stop(); await Task.Delay(1000); }
                finally { if (handler != null) publisher.StatusChanged -= handler; }
            }
            Record("종료", "이 비콘 시험은 스마트폰 수신 또는 GATT 연결 성공을 자동 판정하지 않습니다.");
        }
    }
}
