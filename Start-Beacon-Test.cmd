@echo off
cd /d "%~dp0"
echo Close the GATT server before running this non-connectable beacon test.
dotnet build BluetoothDiagnostics.csproj -c Release -o artifacts\beacon-probe
if errorlevel 1 goto failed
dotnet artifacts\beacon-probe\BluetoothDiagnostics.dll --beacon-test
pause
exit /b
:failed
echo Build failed. Close any previous beacon test and verify .NET 10 SDK.
pause
