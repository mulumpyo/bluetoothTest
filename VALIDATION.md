# 검증 범위

Windows 10/11용 C# WinRT 진단 엔진과 Electron GUI입니다. 빌드·소프트웨어 테스트와 실제 무선 동작 판정을 구분합니다.

## 빌드 및 자동 테스트

루트에서 C# 빌드 및 정책/존재 확인 테스트를 실행합니다.

```powershell
dotnet build -c Release
dotnet run --project tests/DiagnosticCore.Tests -c Release
```

GUI 폴더에서 엔진을 빌드한 뒤 Node/실제 C# stdio 연동 테스트를 실행합니다.

```powershell
cd gui
npm ci
npm run build:backend
npm test
npm run test:ui
```

- C# 테스트: 25개. 단계별 판정, 시험 구간, 세션별 활동/이력, 동시 처리, 주소 형식, 존재 확인의 잘못된 양성 방지.
- Node 테스트: 7개. 한글 IPC, 오류 복구, 지원 미확인 구분, 광고 상태, 시작 차단 이유, 다중 연결 판정 표시.
- GUI 시험은 실제 로컬 어댑터가 있으면 조회 결과와 화면을 대조합니다. 스마트폰 통신 성공을 가짜로 만들지 않습니다.
- 합성 세션/명령 테스트 결과는 실제 하드웨어 성공 근거가 아닙니다.

## 실제 무선 시험

- 일부 시험 환경에서 Peripheral=True 및 Windows 광고 Started/Success가 보고됐지만, 모바일에서 해당 GATT 광고의 수신/연결/명령 전송 성공은 확인되지 않았습니다.
- GATT와 독립된 일반 광고 비교 시험에서도 Windows Started는 보고됐으나 사용자가 시험 비콘을 찾지 못했습니다. 이는 송출 부재의 확정 증거가 아니며 수신기에서 광고를 식별하기 어려웠을 가능성도 남아 있습니다.
- 따라서 특정 동글의 GATT 또는 다중 스마트폰 연결 성공을 주장하지 않습니다. 실제 물리 연결 수와 Controller 최대 연결 수는 확인되지 않음으로 표시합니다.
- 실측 로그, MAC 주소 및 장치별 감사 기록은 Git에 포함하지 않습니다.

[nRF Connect 시험 절차](NRF_CONNECT_TEST.md), [독립 비콘 비교 시험](BEACON_TEST.md)을 따라 대상 PC/동글/스마트폰 조합에서 확인하세요.

`npm run test:ui -- --smoke-gatt`는 실제 연결 가능한 광고를 잠시 시작하고 종료합니다. 다른 GATT 서버를 먼저 종료하세요. 일반 UI 테스트는 광고를 시작하지 않습니다.
