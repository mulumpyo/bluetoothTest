# Android nRF Connect 실측 절차 — v1.1

이 문서는 실제 스마트폰에서 실행하는 시험입니다. 자동 테스트의 가상 세션은 여기에 성공 증거로 사용하지 않습니다. 스마트폰 A/B/C를 준비하고 각각 구분할 수 있도록 모델/별칭을 적어 두세요.

## 시작 전

1. 기존 진단 콘솔/GUI에서 필요한 결과를 저장하고 서버를 종료합니다. 시험 서버는 한 개만 실행하세요.
2. PC에 BARROT 동글을 연결하고 새 GUI를 실행합니다. 다른 Bluetooth 라디오가 활성화돼 있으면 프로그램이 GATT 시작을 막습니다. 사용자가 장치 관리자에서 시험 라디오를 정리한 뒤 새로 조회하세요.
3. Peripheral=True, 현재 존재=True, 기본 라디오 여부와 Instance ID를 확인합니다.
4. GATT 서버를 시작하고 **실제 이벤트 Started / Success**를 기다립니다. Aborted는 Error=Success라도 실패입니다.

Extended Advertising=False를 광고 실패로 처리하지 않습니다. 이 프로그램은 Extended PHY 설정을 사용하지 않는 연결 가능한 GATT 광고를 요청합니다. `GattServiceProvider`에는 Legacy 형식 강제 설정이나 특정 라디오 지정 인자가 없습니다. 실제 무선 패킷 형식은 자동 판정하지 않고 '확인되지 않음'으로 남깁니다. nRF Connect의 광고 상세 또는 무선 캡처에서 확인한 내용은 '광고 형식' 실측 메모에 근거와 함께 적으세요.

## A 한 대 시험

1. A에서 **nRF Connect for Mobile → SCANNER → SCAN**을 실행합니다. 앱이 요구하는 Bluetooth/주변 기기 권한은 사용자가 직접 허용해야 합니다.
2. PC의 광고를 찾습니다. 장치 이름은 Windows가 결정하므로 이름만으로 식별하지 마세요. Service UUID는 다음과 같습니다.

   `7a310001-8d4e-4ef0-a4d7-95d1a468d001`

   v1.1.1에서는 PC 어댑터 카드의 Bluetooth MAC을 확인/복사할 수 있습니다. 이 값은 [BluetoothAdapter.BluetoothAddress](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothadapter.bluetoothaddress?view=winrt-26100)가 보고한 어댑터 주소입니다. 실제 광고에서 관측하는 주소와 동일하다고 보장하지 않습니다. BLE 프라이버시 주소 사용 등의 경우 다를 수 있습니다. nRF Connect는 MAC을 미리 입력할 필요 없이 SCANNER의 광고를 선택해 CONNECT할 수 있으며 Service UUID도 함께 확인하세요. 광고가 Aborted이면 MAC을 알아도 해당 서버에 연결할 수 없습니다.

   광고 공간에 UUID가 들어가지 않는 경우가 있으므로 Service UUID 필터만으로 검색되지 않는다고 단정하지 마세요. 주변 장치를 좁힌 뒤 CONNECT하여 서비스 검색 결과로 확인하고 그 근거를 메모하세요. 광고를 실제로 발견한 폰에서는 GUI의 '스마트폰 실측 기록'에 검색/성공을 기록합니다.
3. **CONNECT → CLIENT**에서 위 서비스와 다음 특성을 찾습니다.

   `7a310002-8d4e-4ef0-a4d7-95d1a468d001`

4. 특성의 Read 기능을 실행합니다. 최초 응답은 UTF-8 `READY`입니다.
5. 특성의 Write 기능에서 **Text/UTF-8**, **Request / Write with response**를 선택하고 `OPEN_SIMULATION`을 전송합니다. 줄바꿈/NUL 없이 15바이트입니다.
6. 다시 Read하여 `OK:OPEN_SIMULATION`을 확인합니다. 실제 잠금장치 동작은 없습니다. 응답은 세션별로 분리되어 다른 폰의 잘못된 명령이 이 폰의 응답을 바꾸지 않습니다.
7. PC에서 새 활성 세션의 ID와 최근 활동 시각을 보고 A임을 확인한 뒤 **폰 ↔ 세션 매핑 → 스마트폰 A**로 지정합니다. 폰의 MAC 주소/이름을 추측해 자동 매핑하지 않습니다.
8. GUI의 GATT 연결/명령 수신 결과와 시간·근거를 확인합니다. '연결' 성공은 활성 세션에서 실제 요청을 관측한 경우 또는 명시적인 사용자 실측으로 기록되며 두 근거는 구분됩니다.

## B/C 추가 연결 및 매핑

1. A의 연결을 끊지 않은 상태에서 B로 검색/연결/Read/Write를 실행합니다. 새 세션을 B에 매핑합니다.
2. A에서 다시 Read/Write해 연결이 유지되는지 확인합니다.
3. C도 같은 방식으로 추가하고 C에 매핑합니다. 각 폰의 모델과 스캔/연결 오류는 사용자 실측에 남기세요.
4. 같은 별칭을 두 활성 세션에 지정할 수 없습니다. 폰이 재연결되면 새 Connection ID가 생기며 다시 매핑해야 합니다.

## 30초 동시 통신 시험

1. 대상 **A+B / 2대** 또는 **A+B+C / 3대**를 선택합니다.
2. '서로 다른 실제 폰임을 확인'을 체크하고 **30초 시험 시작**을 누릅니다. 필요한 활성 세션이 매핑되지 않으면 시작할 수 없습니다.
3. 시험 시작 이후 30초 안에 모든 대상 폰에서 `OPEN_SIMULATION`을 다시 Write하고 Read하여 `OK:OPEN_SIMULATION`을 확인합니다. 각 폰의 연결을 계속 유지하세요.
4. PC에 대상별 정상 명령이 도착하면 **PC 요청 조건 충족**으로 바뀝니다. 이 시점에도 다중 연결 성공은 아닙니다.
5. 폰별 OK 응답 및 연결 유지를 직접 확인한 뒤 해당 확인란을 체크하고 **통신 유지 확인 · 성공 확정**을 누릅니다. 사용자 확인도 30초 구간 안에 완료해야 합니다.
6. 전원 명령, 사용자 확인, 활성 세션 유지 조건이 충족된 경우만 다중 폰 성공을 기록합니다. 시험 전 명령, 한 폰의 반복 명령, 잘못된 명령, 종료/재연결된 세션은 대체 증거로 쓰지 않습니다.
7. 30초 초과/대상 세션 추적 종료는 해당 시험의 실패로 기록합니다. 이 결과만으로 동글의 최대 연결 한계 또는 실패 원인을 단정하지 않습니다. 필요하면 시험을 취소/다시 시작하세요.

성공 폰 수는 '이 조건에서 최소 N대 실측 성공'이라는 하한입니다. **실제 물리 연결 수와 Controller 최대 연결 수는 계속 '확인되지 않음'**입니다. 세션 종료 이벤트 지연 가능성 때문에 PC 관측만으로 물리 링크를 정확히 세지 않습니다.

## 독립 결과와 보존

6단계는 어댑터 Peripheral 지원 / 광고 시작 / 스마트폰 광고 발견 / GATT 연결 / GATT 명령 수신 / 다중 스마트폰 연결입니다. 각 항목에 상태, 근거 출처, 시각, 상세가 있습니다. 광고 발견은 폰에서 확인 후 수동 기록하며, 단순한 '쓰기 성공' 또는 '동시 연결 성공' 메모만으로 PC 검증 결과를 성공으로 바꾸지 않습니다.

- '현재 세션'은 활성 관측값입니다. 종료된 세션과 Read/Write/정상 명령/오류 횟수, 마지막 활동 및 종료 이유는 이력에 남습니다.
- 서버 새 시작/어댑터 변경은 새 Run ID를 만듭니다. 이전 판정, 세션/활동/다중 시험, 사용자 실측은 별도로 보존하고 내보내기에 포함합니다.
- 내보내기는 JSON의 `state.gatt.diagnostics`, `state.archivedRuns`와 CSV의 Stage/Evidence/Session/Activity/MultiTrial 행을 확인하세요.
- 종료 시 스마트폰에서 연결을 끊고 서버를 종료하세요. 서버 추적 종료는 물리 링크 해제 확인이 아닙니다.

## 공식 근거

- [Microsoft GATT 광고 설정](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattserviceprovideradvertisingparameters?view=winrt-26100)
- [Microsoft GATT 요청 Session](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.genericattributeprofile.gattreadrequestedeventargs.session?view=winrt-26100)
- [Nordic nRF Connect Android 설명](https://github.com/nordicsemi/Android-nRF-Connect/blob/main/documentation/README.md)
