using System.Runtime.InteropServices;

namespace BluetoothDiagnostics;

/// <summary>Read-only presence check. NORMAL never includes phantom nodes or cancels removal.</summary>
public static class PnpNative
{
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);

    public static PnpDevice Check(string name, string instanceId)
    {
        var device = new PnpDevice { Name = name, InstanceId = instanceId, Status = "미확인", PresenceSource = "CfgMgr32 CM_Locate_DevNodeW(NORMAL)" };
        if (string.IsNullOrWhiteSpace(instanceId)) { device.PresenceDetail = "Instance ID 없음"; return device; }
        try
        {
            var result = CM_Locate_DevNodeW(out var node, instanceId, 0);
            device.PresenceDetail = $"CM_Locate_DevNodeW CONFIGRET=0x{result:X8}";
            if (result == 0x0D) { device.Present = false; device.Status = "현재 장치 트리에 없음"; return device; }
            if (result != 0) return device;
            device.Present = true;
            var statusResult = CM_Get_DevNode_Status(out var flags, out var problem, node, 0);
            device.PresenceDetail += $"; CM_Get_DevNode_Status=0x{statusResult:X8}, DN=0x{flags:X8}, Problem={problem}";
            if (statusResult == 0) device.Status = (flags & 0x400) != 0 ? $"문제 코드 {problem}" : (flags & 0x08) != 0 ? "OK" : "시작되지 않음";
        }
        catch (Exception ex) { device.PresenceDetail = $"0x{ex.HResult:X8}: {ex.Message}"; }
        return device;
    }
}
