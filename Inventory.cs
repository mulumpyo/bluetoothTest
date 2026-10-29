using System.Diagnostics;
using System.Text.Json;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Foundation.Metadata;

namespace BluetoothDiagnostics;

public sealed class Inventory(DiagnosticLog log)
{
    public List<PnpDevice> Pnp { get; private set; } = [];
    public List<AdapterInfo> Adapters { get; private set; } = [];
    public async Task RefreshAsync()
    {
        Pnp = [];
        Adapters = [];
        try
        {
            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; & '" + Path.Combine(AppContext.BaseDirectory, "Inventory.ps1").Replace("'", "''") + "'");
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); throw; }
            if (process.ExitCode != 0) throw new InvalidOperationException(await error);
            var json = (await output).Trim();
            if (json.StartsWith('[')) Pnp = JsonSerializer.Deserialize<List<PnpDevice>>(json)!;
            else if (json.StartsWith('{')) Pnp.Add(JsonSerializer.Deserialize<PnpDevice>(json)!);
        }
        catch (Exception ex) { log.Error("PnP 정보 조회 (기능 검사는 계속 가능)", ex); }
        BluetoothAdapter? defaultAdapter = null;
        try { defaultAdapter = await BluetoothAdapter.GetDefaultAsync(); }
        catch (Exception ex) { log.Error("기본 어댑터 조회", ex); }
        try
        {
            var devices = await DeviceInformation.FindAllAsync(BluetoothAdapter.GetDeviceSelector(), ["System.Devices.DeviceInstanceId"]);
            foreach (var device in devices)
            {
                var instance = device.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var id) ? id as string : null;
                var info = new AdapterInfo { Name = device.Name, DeviceId = device.Id, InstanceId = instance, IsDefault = defaultAdapter?.DeviceId == device.Id };
                info.Pnp = Pnp.FirstOrDefault(p => string.Equals(p.InstanceId, instance, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(instance))
                {
                    var native = PnpNative.Check(device.Name, instance);
                    if (info.Pnp == null)
                    {
                        info.Pnp = native;
                        Pnp.Add(native);
                        log.Add("장치 존재 확인", $"{instance}: Present={native.Present?.ToString() ?? "미확인"}, 출처={native.PresenceSource}, {native.PresenceDetail}. 드라이버 상세 조회와 독립 확인.");
                    }
                    else if (native.Present.HasValue)
                    {
                        info.Pnp.Present = native.Present;
                        info.Pnp.PresenceSource = native.PresenceSource;
                        info.Pnp.PresenceDetail = native.PresenceDetail;
                        info.Pnp.Status = native.Status;
                    }
                }
                Adapters.Add(info);
                await ProbeAsync(info);
            }
        }
        catch (Exception ex) { log.Error("WinRT 어댑터 열거", ex); }
    }
    public async Task ProbeAsync(AdapterInfo info)
    {
        info.Capabilities.Clear();
        info.Error = null;
        info.BluetoothAddress = null;
        info.BluetoothAddressState = "미확인";
        info.BluetoothAddressError = null;
        try
        {
            var adapter = await BluetoothAdapter.FromIdAsync(info.DeviceId);
            if (adapter is null) { info.Error = "어댑터 미인식 (null)"; return; }
            try
            {
                info.BluetoothAddress = BluetoothAddressFormatter.Format(adapter.BluetoothAddress);
                info.BluetoothAddressState = info.BluetoothAddress != null ? "조회 성공" : "미확인";
                if (info.BluetoothAddress == null) info.BluetoothAddressError = "API 주소가 0이거나 유효한 48비트 주소가 아닙니다.";
            }
            catch (Exception ex)
            {
                info.BluetoothAddressState = "조회 실패";
                info.BluetoothAddressError = $"HRESULT=0x{ex.HResult:X8}: {ex.Message}";
            }
            Read("IsLowEnergySupported", () => adapter.IsLowEnergySupported);
            Read("IsCentralRoleSupported", () => adapter.IsCentralRoleSupported);
            Read("IsPeripheralRoleSupported", () => adapter.IsPeripheralRoleSupported);
            Read("IsExtendedAdvertisingSupported", () => adapter.IsExtendedAdvertisingSupported);
        }
        catch (Exception ex) { info.Error = $"0x{ex.HResult:X8}: {ex.Message}"; }
        void Read(string name, Func<bool> getter)
        {
            try
            {
                info.Capabilities[name] = ApiInformation.IsPropertyPresent("Windows.Devices.Bluetooth.BluetoothAdapter", name)
                    ? new("조회 성공", getter()) : new("API 미지원");
            }
            catch (Exception ex) { info.Capabilities[name] = new("조회 실패", null, $"0x{ex.HResult:X8}: {ex.Message}"); }
        }
    }
}
