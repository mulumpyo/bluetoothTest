# Bluetooth Adapter Lab — Electron GUI

`ZT411_Electron`의 Electron + 순수 HTML/CSS/JavaScript 기술 구성과 밝은 회색/파란색 카드 디자인을 참고한 Windows 데스크톱 진단 앱입니다. 기존 C# 콘솔도 유지됩니다.

## 실행

가장 간단한 방법은 `dist/Bluetooth-Adapter-Lab-1.1.2-x64.exe`를 더블클릭하는 것입니다. 어댑터 카드에 Windows API의 Bluetooth MAC과 복사 버튼이 표시됩니다. v1.1.2는 PnP 상세 조회 실패 시 CfgMgr32로 현재 장치 존재를 독립 확인하고 시작 버튼의 차단 이유를 표시합니다. Portable 실행 파일에 .NET 엔진과 Runtime을 포함하므로 대상 PC에 Node.js/.NET SDK를 설치할 필요가 없습니다. 일반 사용자 권한으로 실행하세요.

개발 실행은 Node.js 24 LTS와 .NET 10 SDK가 필요합니다.

```powershell
cd D:\dev\bluetoothTest\gui
npm ci
npm start
```

`npm start`는 C# 엔진을 먼저 빌드하고 GUI를 실행합니다. 엔진이 이미 빌드되어 있다면 `npm run start:built`도 가능합니다. 개발 엔진은 `gui/backend-dev`, 런타임 포함 배포 엔진은 `gui/backend-package`로 분리합니다. 두 배포 방식을 같은 출력 폴더에 섞지 마세요.

배포 파일 생성:

```powershell
npm run build:win
```

현재 빌드 대상은 Windows x64입니다. Windows 10 build 19041 이상/Windows 11에서 사용하며, .NET 공식 지원 범위와 Windows 10 에디션별 수명 정책은 루트 README를 참고하세요.

## 화면에서 진행하는 시험

1. **어댑터 선택**: 시작 시 실제 장치를 조회합니다. 연결된 BARROT `33FA/0012`가 하나면 자동 선택합니다. 여러 동글이나 내장 라디오가 있으면 직접 고르세요. Instance ID, 기본 어댑터 여부, PnP, 드라이버가 표시됩니다. '전체 PnP 장치'를 펼치면 과거 Unknown 장치도 볼 수 있습니다. PnP 조회는 최대 45초 걸릴 수 있습니다.
2. **지원 기능 검사**: `Peripheral Role` 카드의 True/False를 확인합니다. BLE, Central, Extended Advertising도 함께 표시합니다. 미인식/조회 실패/API 미지원은 False로 바꾸지 않습니다. '지원 기능 다시 검사'로 재조회할 수 있습니다.
3. **GATT 서버 시작**: Peripheral=True이고 선택한 동글이 유일한 기본 라디오이며 Present=True일 때 활성화됩니다. 다른 라디오가 있으면 장치 관리자에서 비활성화한 뒤 '장치 새로 조회'를 누르세요. 시작 시 라디오 목록을 다시 확인합니다. 실제 이벤트 `Started / Success`를 받은 뒤에만 '광고 중'을 표시합니다.
4. **스마트폰 시험**: BLE Scanner 앱에서 Service UUID로 검색하고 연결합니다. UUID는 화면의 '복사' 버튼으로 복사할 수 있습니다. 표시된 Read/Write Characteristic에 Read하여 `READY`를 확인하고, UTF-8 `OPEN_SIMULATION`을 Write with response로 보냅니다. 줄바꿈/NUL은 추가하지 마세요. 다시 Read하여 `OK:OPEN_SIMULATION`을 확인하세요. PC의 요청 관측과 스마트폰에서 확인한 응답 성공은 구분합니다.
5. **다중 연결**: A 연결을 유지하고 B, C를 추가 연결해 각 스마트폰에서 Read/Write를 반복합니다. '현재 관측 세션'과 '이번 서버의 최고 관측 수'를 확인합니다. 첫 요청 이전 연결은 표시되지 않으며 시간은 첫 요청 이후 관측 시간입니다. 최고 관측값은 Controller의 최대 연결 수가 아닙니다.
6. **사용자 실측**: 스마트폰 별칭, 검색/연결/읽기/쓰기/동시 연결/연결 해제, 성공/실패/미확인 및 오류 코드를 입력해 '기록 추가'를 누르세요. API 결과와 별도로 저장합니다. 스마트폰이 PC에 요청을 보내기 전에 실패한 원인은 PC API에서 알 수 없습니다.
7. **저장**: 'JSON / CSV 저장'을 누르고 저장할 폴더를 선택합니다. 해당 폴더 안에 날짜별 하위 폴더가 생성됩니다. 저장 후 '저장 폴더 열기'로 확인하세요.
8. **종료**: 스마트폰 연결을 끊고 '서버 종료'를 누릅니다. 창을 닫아도 엔진에 종료 명령을 보내 광고를 정리합니다. API에는 특정 폰의 물리 연결을 강제로 끊는 기능이 없습니다.

v1.1은 6단계 독립 판정, 폰 A/B/C 매핑, 30초 다중 폰 시험, 연결별 활동/종료 이력을 제공합니다. 상세 스마트폰 절차는 [nRF Connect 실측 설명서](../NRF_CONNECT_TEST.md)를 참고하세요. 새 어댑터 선택/서버 시작 시 이전 시험을 보존하고 새 Run ID를 만듭니다. 전체 로그도 Run ID와 함께 유지합니다. '표시 지우기'는 화면만 지우며 내보낼 로그는 유지합니다. 새로 조회는 광고를 종료합니다.

GATT 시험 시에는 콘솔과 GUI 중 하나에서만 서버를 시작하세요. 같은 서비스 UUID로 여러 진단 서버를 동시에 생성하면 충돌할 수 있습니다.

동글을 교체한 경우 '장치 새로 조회'로 이전 GATT 객체를 정리하고 새 동글의 Instance ID/MAC을 확인하세요. PnP 상세 조회가 시간 초과되어도 현재 WinRT 어댑터의 정확한 Instance ID를 [CM_Locate_DevNodeW(NORMAL)](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_locate_devnodew)로 확인합니다. 과거 phantom 장치를 포함하지 않으며 조회 실패를 True로 추정하지 않습니다. 드라이버 공급자/버전은 별도 상세 조회가 실패하면 미확인입니다.

## 구성

```text
Electron renderer (HTML/CSS/JavaScript)
  → contextBridge / 제한된 IPC
Electron main
  → 자식 프로세스 stdio JSON Lines (로컬 네트워크 포트 없음)
C# .NET 10 / Windows WinRT
  → BluetoothAdapter, GattServiceProvider, PnP
```

- `src/main.js`: 창·저장 다이얼로그·C# 프로세스 관리
- `src/preload.js`: 화면에 허용한 명령만 노출
- `src/backend.js`: 요청 ID, 응답, 실시간 이벤트, 종료 관리
- `src/renderer.js`, `index.html`, `styles.css`: 화면
- `src/view-model.js`: 지원 미확인/False와 광고 성공의 구분
- 루트 `GuiWorker.cs`: 기존 C# 진단 기능의 GUI 명령 처리 및 JSON/CSV 내보내기

Node integration은 꺼져 있고 context isolation/sandbox가 켜져 있습니다. 외부 콘텐츠를 로드하지 않으며, 엔진 이벤트를 HTML로 삽입하지 않고 textContent로 표시합니다. Electron IPC는 [공식 contextBridge 문서](https://www.electronjs.org/docs/latest/api/context-bridge) 및 [ipcMain 문서](https://www.electronjs.org/docs/latest/api/ipc-main)를 기준으로 구성했습니다.

## 검증

```powershell
npm test
npm run test:core
npm run build:backend
npm run test:ui
# 실제 동글에서 광고 시작/종료까지 시험
npm run test:ui -- --smoke-gatt
# 실제 폰 시험: PC 광고를 시작하고 test-results/field-test/live-state.json에 상태 저장
npm run test:field
```

UI 검증은 실제 C# 엔진을 사용해 화면 표시, 검사 버튼, 미확인 실측 메모 입력, JSON/CSV 저장, 화면 폭 및 작은 창 크기를 확인하고 `test-results`에 스크린샷을 남깁니다. 스마트폰 통신 성공을 가짜로 생성하지 않습니다. `--smoke-gatt`는 실제 연결 가능한 광고를 잠시 시작했다 종료하므로 다른 BLE 진단 서버를 종료한 상태에서 실행하세요.

문제가 생기면 상단 오류와 실시간 로그를 확인하세요. 엔진 실행 파일이 없으면 `npm run build:backend`를 다시 실행합니다. 실제 테스트 UUID와 Bluetooth API 제약, 폰별 상세 절차는 [기존 진단 README](../README.md)를 참고하세요.
