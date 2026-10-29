$ErrorActionPreference = 'Stop'
Get-PnpDevice -Class Bluetooth | ForEach-Object {
    $d = $_
    $properties = @{}
    Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName @('DEVPKEY_Device_IsPresent','DEVPKEY_Device_DriverProvider','DEVPKEY_Device_DriverVersion','DEVPKEY_Device_Parent') -ErrorAction SilentlyContinue | ForEach-Object {
        $properties[$_.KeyName] = $_.Data
    }
    [pscustomobject]@{
        Name = $d.FriendlyName
        InstanceId = $d.InstanceId
        Status = [string]$d.Status
        Present = $properties['DEVPKEY_Device_IsPresent']
        Provider = $properties['DEVPKEY_Device_DriverProvider']
        Version = $properties['DEVPKEY_Device_DriverVersion']
        Parent = $properties['DEVPKEY_Device_Parent']
    }
} | ConvertTo-Json -Depth 4 -Compress
