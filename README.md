# Bluetooth USB Adapter Diagnostic Tool

**Electron GUI도 사용할 수 있습니다.** `Start-GUI.cmd`를 더블클릭하거나 `gui/dist/Bluetooth-Adapter-Lab-1.1.2-x64.exe`를 실행하세요. v1.1은 6단계 독립 판정과 A/B/C 매핑, 다중 폰 시험, 연결 이력을 제공합니다. v1.1.1은 MAC 표시/복사를, v1.1.2는 동글 교체 후 PnP 상세 조회가 실패해도 Native 장치 존재 확인으로 GATT 시작 조건을 검증하는 기능을 추가했습니다. [GUI 사용 방법](gui/README.md), [nRF Connect 실제 폰 시험](NRF_CONNECT_TEST.md)을 참고하세요. 아래는 기존 콘솔 실행 방법입니다.

Windows Bluetooth USB 동글의 **`BluetoothAdapter.IsPeripheralRoleSupported`** 를 우선 확인하는 C# 콘솔 프로그램입니다. 넥시 BARROT `VID_33FA / PID_0012`를 식별하고, 실제 Windows API 결과와 GATT 세션 관측을 저장합니다. Bluetooth 6.0 제품명이나 '최대 5개 연결' 문구로 기능을 추정하지 않습니다.

저장소에는 소스와 문서만 포함합니다. 실행 파일/ZIP, NuGet·npm 의존성, 실측 로그·장치별 감사 기록은 포함하지 않습니다. 처음 받은 경우 아래 방법으로 빌드하세요. 빌드·시험 범위는 [검증 안내](VALIDATION.md)를 참고하세요.

```powershell
git clone https://github.com/mulumpyo/bluetoothTest.git
cd bluetoothTest
dotnet build -c Release
cd gui
npm ci
npm start
```

## 환경 및 빌드

- .NET 10 SDK (안정 LTS), Windows 10 2004/build 19041 이상 또는 Windows 11.
- 현재 .NET 10의 Windows 10 공식 지원 범위는 LTSC/Enterprise 등 OS 수명 정책에 영향을 받습니다. 일반 Windows 10에서의 실행 가능성과 Microsoft의 공식 지원 여부는 별개입니다. 실제 사용 OS/드라이버 조합에서 확인하세요.
- Target Framework: `net10.0-windows10.0.19041.0`. WinRT Windows SDK 참조는 SDK가 NuGet에서 자동 복원합니다. 별도 `Microsoft.Windows.SDK.Contracts` 패키지를 추가하지 않습니다.
- 최초 복원에는 인터넷이 필요합니다. 관리자 권한 요청, 드라이버 설치/변경, 장치 비활성화는 프로그램이 수행하지 않습니다.

```powershell
cd D:\dev\bluetoothTest
dotnet restore
dotnet build -c Release
dotnet run -c Release --no-build
```

일반 터미널에서 실행하세요. 실행 파일은 `bin\Release\net10.0-windows10.0.19041.0\BluetoothDiagnostics.exe`입니다. 해당 폴더의 DLL, runtimeconfig, `Inventory.ps1` 등을 함께 유지해야 합니다. 다른 PC에는 .NET 10 Runtime을 설치하거나 아래처럼 독립 배포하세요.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o publish
.\publish\BluetoothDiagnostics.exe
```

ARM64 PC는 `win-arm64`를 사용하세요. Windows SDK 개발 참조와 실제 PC Bluetooth 드라이버는 서로 다른 구성 요소입니다.

## 파일 구조

| 파일 | 역할 |
| --- | --- |
| BluetoothDiagnostics.csproj | .NET/Windows SDK 설정 |
| Program.cs | 메뉴, 선택, JSON/CSV 저장 |
| Inventory.cs / Inventory.ps1 | WinRT 열거/기능 조회, PnP/드라이버 정보 |
| GattServer.cs | 광고, GATT 읽기·쓰기, 세션 관측 |
| Models.cs | 결과 모델 및 오류 로그 |

## 1. 동글 식별 및 기능 판단

시작 또는 메뉴 1에서 현재 WinRT 어댑터와 과거 장치를 포함한 PnP 장치가 출력됩니다. `System.Devices.DeviceInstanceId`로 두 목록을 정확히 매칭합니다. 매칭되지 않으면 연결 여부와 BARROT 식별을 미확인으로 두며 장치명으로 추정하지 않습니다. USB 장치에 VID/PID를 출력하고 Bluetooth 열거자처럼 USB 식별자가 없는 장치는 해당 없음으로 표시합니다.

`DEVPKEY_Device_IsPresent=True`는 현재 존재를 의미하며, PnP `Status=OK`와는 별도 값입니다. `Present=False`는 과거/현재 부재 장치이고, `Unknown`만으로 현재 연결 여부를 판단하지 않습니다. 드라이버 속성 접근이 거부되면 미확인입니다.

연결된 BARROT가 정확히 하나로 매칭된 경우 자동 선택합니다. 내장 어댑터로 자동 대체하지 않습니다. 메뉴 2에서는 다른 어댑터도 명시적으로 선택할 수 있습니다. 제공된 Instance ID `USB\VID_33FA&PID_0012\5&201B4142&0&1`는 현재 PC 출력과 비교하세요. USB 포트나 PC가 바뀌면 Instance ID가 달라질 수 있어 상수로 고정하지 않습니다.

메뉴 3은 선택 어댑터의 `FromIdAsync`를 다시 호출합니다.

| 결과 | 판단 |
| --- | --- |
| IsPeripheralRoleSupported=True | Windows가 이 어댑터의 Peripheral 역할 지원을 보고함. 다음 단계에서 실제 GATT 시험 |
| IsPeripheralRoleSupported=False | 현재 Windows/드라이버 조합에서 Peripheral 역할 미지원으로 보고됨 |
| API 미지원 / 조회 실패 / null 미인식 | 지원 여부 결론 불가. False가 아님 |

BLE, Central, Extended Advertising도 각각 독립 조회하므로 Peripheral=False라도 확인할 수 있습니다. Extended Advertising=False가 일반 연결 가능 GATT 광고 실패를 의미하지 않습니다. Central=True 역시 실제 원격 장치 연결 실험 결과는 아닙니다.

## 2. GATT 광고 및 스마트폰 한 대 시험

`GattServiceProvider.CreateAsync(Guid)`는 특정 어댑터를 지정하는 인자가 없습니다. 프로그램은 메뉴 4 직전에 목록을 다시 조회하고 **선택한 장치가 현재 기본 어댑터이고, WinRT 어댑터가 하나이며, PnP Present=True**인 경우에만 서버를 생성합니다. 복수 라디오 PC에서는 사용자가 장치 관리자에서 다른 라디오를 비활성화한 뒤 메뉴 1/2를 실행하세요. 이 검사는 잘못된 라디오로 시험하는 위험을 줄이지만 시험 도중 USB/기본 라디오 변경까지 고정하는 API는 아닙니다. 시험 중 구성 변경을 피하세요.

1. 메뉴 3에서 Peripheral=True를 확인합니다.
2. 메뉴 4를 선택하고 `Advertising: Started, Error=Success` 등 실제 이벤트를 확인합니다. 시작 요청 메시지만으로 성공으로 판단하지 않습니다. 이벤트가 없으면 미확인입니다. `StartedWithoutAllAdvertisementData` 등은 원문 상태 그대로 판단하세요.
3. 스마트폰의 BLE Scanner 앱에서 검색합니다. 장치 이름 대신 아래 Service UUID로 식별하세요. 광고 공간/Windows 동작에 따라 스캔 응답 또는 연결 후 서비스 검색이 필요할 수 있습니다. 검색 성공 여부는 PC가 알 수 없으므로 메뉴 8에 기록합니다.
4. 스마트폰에서 Connect 후 서비스/특성을 찾습니다.
5. 특성을 Read하면 UTF-8 `READY`를 받습니다.
6. **Write with response**, UTF-8 문자열 **`OPEN_SIMULATION`** 을 전송합니다. 줄바꿈/NUL 없이 15바이트로 보내세요.
7. 다시 Read하면 `OK:OPEN_SIMULATION`을 받습니다. 실제 문, 장치 또는 액추에이터 제어는 수행하지 않습니다.
8. 스마트폰에서 Disconnect하고 메뉴 6에서 세션 종료 이벤트를 확인합니다.

| 항목 | UUID |
| --- | --- |
| Service | `7a310001-8d4e-4ef0-a4d7-95d1a468d001` |
| Read/Write Characteristic | `7a310002-8d4e-4ef0-a4d7-95d1a468d001` |

읽기/쓰기는 암호화를 요구하지 않는 진단용 Plain 보호 수준입니다. 잘못된 명령/부분 쓰기는 application protocol error `0x80`을 반환합니다. 잘못된 읽기 offset은 `0x07`입니다. 서버 응답 문자열은 모든 클라이언트가 공유하는 진단 상태입니다.

메뉴 5는 광고 및 요청 핸들러를 종료합니다. Windows는 이 API로 특정 스마트폰의 물리 연결을 강제로 끊는 기능을 제공하지 않습니다. 재시험 시 스마트폰에서 먼저 연결을 끊으세요.

## 3. 여러 스마트폰 동시 연결 실측

연결 시도 시작/무선 연결 완료 이벤트 전체를 GATT 서버 API로 얻을 수는 없습니다. 프로그램은 읽기/쓰기 요청의 `args.Session`을 처음 관측할 때 활성 세션을 등록하고 `SessionStatusChanged`로 종료를 추적합니다. 첫 GATT 요청 전의 연결은 표시되지 않습니다. 시간은 **첫 요청부터 종료 관측까지**이며 Bluetooth 연결 협상 시간과 다릅니다. 접속 시도부터 첫 응답까지의 시간은 스마트폰 앱 또는 별도 스톱워치로 측정해 메뉴 8에 적으세요.

1. A를 연결해 Read/Write를 반복하고 연결을 유지합니다.
2. B를 추가 연결해 Read/Write합니다. A도 계속 Read/Write하여 두 장치가 살아 있음을 확인합니다.
3. C도 같은 절차를 반복합니다. 가능하면 A/B/C에서 동시에 요청을 반복합니다. 핸들러는 비동기로 요청을 처리하고 공유 상태는 lock으로 보호합니다.
4. 메뉴 6의 세션 ID/현재 수와 `PeakObservedSessions`를 확인합니다. 최고 관측값 2 이상은 복수 세션 관측 증거입니다. 물리 장치와 세션 ID 대응은 한 대씩 연결하며 로그와 비교하세요. 엄격한 동시 통신 증명에는 각 스마트폰의 지속적인 응답 성공도 함께 기록하세요.
5. B/C 실패 시 폰 별칭, 모델/OS, 시각, 연결 유지 중인 폰, Scanner 오류 코드, 재시도 결과를 메뉴 8에 기록합니다. PC에 요청이 도착하지 않은 연결 실패의 HCI 원인은 이 API에서 알 수 없어 **원인 미확인**입니다.
6. 연결을 끊으며 종료 로그를 확인하고 메뉴 7로 저장합니다. 세션 종료 이벤트 누락/지연이나 라디오 변경이 있으면 최고값만으로 결론 내리지 말고 재시험합니다.

v1.1의 `PeakObservedSessions`는 현재 Run의 관측 최고 세션 수입니다. 서버를 새로 시작하면 이전 시험을 보존하고 새 Run을 만듭니다. `PeakSuccessfulPhones`는 폰별 매핑/구간 내 명령/사용자 응답 확인이 모두 충족된 시험의 성공 폰 수 하한입니다. 실제 물리 연결 수와 최대 Controller 연결 수는 '확인되지 않음'으로 표시합니다. 3대 성공은 '최소 3대 실측 성공'이며 5대 지원의 증거가 아닙니다. API에는 최대 5개 BLE Peripheral 연결 가능 여부를 직접 반환하는 기능이 없습니다.

콘솔 v1.1은 메뉴 9에서 세션↔A/B/C를 매핑하고, 메뉴 10에서 30초 시험을 시작합니다. 전원 명령 전송 후 폰별 응답을 확인해 메뉴 11로 성공을 확정하세요. 메뉴 12는 진행 중 시험을 취소합니다. 상세 조건은 [nRF Connect 실측 절차](NRF_CONNECT_TEST.md)를 참고하세요.

Windows PC 3대 × 동글 3개의 조합을 각각 시험하고, 같은 스마트폰/거리/드라이버 버전을 유지해 비교하세요. 스마트폰만으로 PC의 다중 **Central** 연결을 검증하는 기능은 이 프로젝트에 포함하지 않습니다.

## 결과 저장 및 문제 해결

메뉴 7은 현재 작업 폴더 `results/<로컬시각>/report.json`, `report.csv`에 실제 조회값, 선택 Device ID, PnP 목록, GATT 상태, 최고 관측 수, 로그/실측 메모를 저장합니다. CSV는 UTF-8 BOM/인용부호 escaping을 사용합니다. 연결 상태는 세션 스냅샷으로 포함됩니다. Device ID 등 식별 정보가 들어 있으므로 공유 범위를 고려하세요.

- **어댑터 없음**: USB 연결, 장치 관리자, Bluetooth 켜짐을 확인하고 메뉴 1을 실행합니다. PnP 실패와 WinRT 실패는 별개로 기록됩니다.
- **PnP 조회 실패**: Windows PowerShell/PnpDevice 모듈, 기업 실행 정책을 확인합니다. 실행 정책은 자동 변경하지 않습니다. WinRT 기능 검사는 계속 사용할 수 있습니다.
- **Peripheral=False**: 다른 PC/드라이버 조합에서도 비교하세요. Bluetooth 6.0/EDR 표기만으로 Windows Peripheral 지원을 보장하지 않습니다.
- **AccessDenied / 0x80070005**: Bluetooth 접근, 보안 정책, 실행 환경을 확인하세요. 무조건 관리자 실행으로 해결된다고 가정하지 않습니다. 패키지 앱으로 이식할 경우 manifest의 `bluetooth` capability가 필요합니다.
- **RadioNotAvailable / Disabled / NotSupported / 기타 오류**: 실제 BluetoothError, HRESULT와 광고 상태를 저장하세요. 라디오 전원, 다른 서버/광고 앱, 드라이버를 확인하고 재시험하세요. 오류 코드만으로 칩셋 결함이라고 단정하지 않습니다.
- **광고는 시작했지만 검색되지 않음**: 스마트폰 Bluetooth/앱 권한, 스캔 필터, 거리, 캐시, 다른 광고 앱을 확인합니다. Extended Advertising 지원 여부와 일반 GATT 광고를 구분하세요.
- **실행 중 라디오 제거**: 현재 시험 결과를 저장하고 서버 종료/새로 조회 후 다시 시험하세요.
- **빌드 NuGet 오류**: `api.nuget.org` 연결/프록시를 확인하고 restore/build를 다시 실행하세요.

## 공식 API 근거

- [BluetoothAdapter: GetDeviceSelector / FromIdAsync / 기능 속성](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothadapter?view=winrt-26100): 주요 어댑터 API는 Windows 10 1703/build 15063, Extended Advertising 속성은 2004/build 19041부터입니다. 런타임 `ApiInformation.IsPropertyPresent` 검사도 수행합니다.
- [GattServiceProvider](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovider?view=winrt-26100): Windows 10 1703/build 15063부터. CreateAsync, 광고 상태 및 연결 해제 API 제약을 참고했습니다.
- [Microsoft 공식 GATT 서버 예제](https://github.com/microsoft/Windows-universal-samples/blob/main/Samples/BluetoothLE/cs/Scenario3_ServerForeground.xaml.cs): 특성 생성, 요청 deferral 및 응답 패턴.
- [GattSession.SessionStatusChanged](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattsession.sessionstatuschanged?view=winrt-26100): 세션 상태 관측.
- [.NET 지원 정책](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [Windows 설치/지원 범위](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

실제 장치 지원 판단은 **기능 API 조회 → 광고 시작 이벤트 → 스마트폰 검색 → Read/Write → 여러 폰 유지 시험** 순서로 진행하세요. 소스와 빌드 성공만으로 BARROT의 무선 시험 성공을 주장하지 않습니다.
