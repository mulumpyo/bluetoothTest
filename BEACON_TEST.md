# GATT를 제외한 독립 광고 송출 시험

모바일에서 GATT Service UUID를 찾지 못할 때, GATT 서버 광고와 일반 BLE 송출 경로를 비교하는 비연결형 비콘입니다. GATT 서버를 종료한 뒤 실행하세요. 원래 GATT 시험 결과와 합치지 않습니다.

다른 Windows x64 PC에서는 제공한 `BLE-Beacon-Probe-win-x64.zip`을 완전히 압축 해제하고 **Run-Beacon-Test.cmd**를 실행하세요. 런타임이 포함되어 .NET/SDK 설치가 필요 없습니다. 같은 BARROT 동글을 사용하고 내장 라디오가 있다면 별도 비활성화 후 단독 시험하세요. Manufacturer 표식은 실행할 때마다 바뀌므로 새로 출력된 값을 확인해야 합니다. 결과는 압축 해제 폴더의 results에 저장됩니다.

```powershell
cd D:\dev\bluetoothTest
dotnet build BluetoothDiagnostics.csproj -c Release -o artifacts\beacon-probe
dotnet artifacts\beacon-probe\BluetoothDiagnostics.dll --beacon-test
```

- 실제 현재 존재하는 유일한 기본 라디오가 BARROT 33FA:0012인지 확인합니다. 라디오를 임의 변경하지 않습니다.
- `BluetoothLEAdvertisementPublisher`에서 `UseExtendedAdvertisement=false`를 요청합니다.
- 일반 Publisher의 Service UUID/이름 필드는 Windows가 예약하므로 넣지 않습니다. 대신 Manufacturer Data에 시험용 Company ID 0xFFFF와 `BLAB-XXXXXXXX` ASCII 표식을 넣습니다. 프로그램이 표식과 HEX를 출력합니다.
- nRF Connect에서 이름/MAC/Service UUID/RSSI와 연결 가능 기기 전용 필터를 모두 해제하세요. 검색 항목의 Manufacturer Data 또는 RAW에 출력한 HEX가 있는지 확인합니다.
- 이 비콘에는 GATT Service UUID나 CONNECT 버튼이 없는 것이 정상입니다. 비콘 발견은 GATT 연결 성공의 증거가 아닙니다.
- 5분 뒤 자동 종료합니다. 먼저 종료하려면 `results/beacon-latest.json`의 `StopRequestFile` 경로에 빈 파일을 만드세요. API 이벤트는 시험별 report.json과 beacon-latest.json에 기록합니다.
- API Started는 송출 요청 상태입니다. 모바일 수신 여부와 실제 광고 주소를 자동 판정하지 않습니다. 폰에서 확인한 관측은 별도 사용자 근거로 남겨야 합니다.

비콘을 발견하면 일반 송출 경로가 적어도 그 시점에 동작한 근거입니다. 기존 GATT 광고와 구분해 조사하세요. 비콘도 발견되지 않으면 Windows/드라이버/동글/수신 조건을 다른 PC와 수신기로 비교해야 하며, 어느 하나의 결함으로 단정하지 않습니다.

공식 근거: [Publisher의 예약 필드와 광고 자원 제약](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementpublisher?view=winrt-26100), [UseExtendedAdvertisement](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.advertisement.bluetoothleadvertisementpublisher.useextendedadvertisement?view=winrt-26100).
