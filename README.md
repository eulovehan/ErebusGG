# EternalGG

C# / .NET 10 / WPF 기반 이터널리턴 전적 도우미 MVP.

## 실행

`artifacts/win-x64/EternalGG.exe`를 실행한다. 런타임을 포함한 폴더 배포본이므로 DLL/x64/tessdata 폴더를 함께 유지한다. Windows 10/11 x64를 대상으로 개발하며, 현재 실제 실행 검증은 Windows 11에서만 수행했다.

.NET 런타임은 포함되어 있지만 Tesseract 네이티브 DLL에는 Visual C++ 2019 호환 x64 런타임이 필요하다. 개발 도구 없는 PC의 해당 전제 조건은 별도로 검증해야 한다. [OCR 바인딩 공식 의존성 안내](https://github.com/charlesw/tesseract#dependencies).

1. 앱을 먼저 실행한다.
2. 이터널리턴에서 매칭을 시작한다.
3. 캐릭터 선택/루트 선택 화면을 전경으로 유지한다.
4. 팀원 이름이 두 번 일치하면 카드에 표시한다. AI 대전은 본인 1명만 확인한다.
5. 조회 서비스를 사용할 수 없으면 이름을 유지하고 이용 불가 안내를 표시한다. 가짜 전적을 채우지 않는다.

게임 중 다시 읽기는 앱에서 `Ctrl+R`을 누른 뒤 게임으로 돌아가 `Tab`의 게임 현황 화면을 연다. 앱은 게임 입력을 자동 조작하지 않는다.

- `Ctrl+R`: 40초 동안 이름 다시 읽기
- `Ctrl+Enter`: 전적 조회
- `Ctrl+,`: 설정 열기/닫기
- 전적 검색: 현재 팀과 분리된 검색 화면
- Esc: 현재 매칭으로 돌아가기

일반 설정은 본인 닉네임, 자동 이름 확인, 일반 / 랭크 조회 모드, 게임 연결 / 새 매칭 확인 시 창 앞으로 표시다. 창 표시는 각 이벤트에 한 번이며 기본값은 켜짐이다. API 키, 시즌 ID, 로그 경로와 진단은 설정의 고급 설정에서 개발용 설정 표시를 선택한 경우에만 노출한다. API 키는 파일에 저장하지 않으며 개발 모드 해제 시 입력창과 조회 상태에서 지운다. 기본 키는 비어 있고 공개 중계 서비스는 아직 구현하지 않았다.

## 빌드

SDK는 `global.json`의 버전으로 고정한다. 프로젝트 전용 SDK는 `.tools/dotnet`에 설치할 수 있다.

```powershell
.\bootstrap.ps1
.\build.ps1 -Test -Publish
```

Test는 Core 계약 / 알림 정책 테스트와 WPF 오프스크린 상태 / 배치 검증을 실행한다. 오프스크린 검증은 실제 사용자 조작이나 실게임 검증을 대신하지 않는다.

코드 또는 중앙 패키지 버전을 바꿔 의존성 잠금 갱신이 필요할 때만 `dotnet restore --force-evaluate`를 사용하고 잠금 파일 변경을 검토한다. 일반 빌드는 `--locked-mode`를 사용한다.

VS Code는 모든 창을 닫은 뒤 프로젝트의 `open-vscode.cmd`를 실행한다. 프로젝트 전용 SDK 환경으로 편집기를 열며, 터미널에서는 `.\open-vscode.ps1`도 사용할 수 있다. 다른 경로로 프로젝트를 옮기거나 새로 받은 경우 먼저 `.\bootstrap.ps1`을 실행한다. 제공된 Build/Test/Publish 작업을 사용할 수 있다. Visual Studio에서는 `EternalGG.sln`을 연다.

## 검증 범위와 제한

- Windows 11의 실행 중인 실제 게임에서 AI 상대 대전 보통만 사용했다.
- 일반/랭크 3인 로스터 인식 경로는 구현했지만 실제 PvP 테스트는 수행하지 않았다.
- API 키가 없어 라이브 전적 응답은 검증하지 않았다. HTTP 고정 응답을 이용한 계약/오류 처리 테스트와 빈 키 무요청 테스트는 별도로 제공한다.
- OCR 기본 좌표는 한국어 UI/1920×1080/테두리 없는 창모드에서 검증한다. 다른 비율/UI 변경/긴 닉네임은 추가 보정이 필요할 수 있다.
- 일반 OS 화면 캡처를 사용한다. 전경 게임만 읽으며 캡처 원본을 저장/업로드하지 않는다. 다른 앱이 게임 위를 가리면 인식이 실패할 수 있다.
- 실제 광고가 없어 광고 영역을 표시하지 않는다. 광고 SDK/실제 광고/인앱 결제/게임 주입 오버레이는 구현하지 않았다.
- 개발용 API 직접 호출 클라이언트다. 공용 키를 넣어 배포하기 전에는 중계 서버로 이전한다.
- 서명/설치 프로그램/자동 업데이트는 후속 배포 작업이다.

설정 파일: `%LOCALAPPDATA%/EternalGG/settings.json`. `SelectionRegions`와 `GameRegions`의 좌표는 게임 클라이언트 영역 대비 0~1 비율이다. 파일을 변경한 뒤 앱을 재실행한다.

최신 실행 파일은 artifacts/win-x64/EternalGG.exe다. win-x64-neutral과 win-x64-chrome은 이전 검증 빌드다. 이번 화면 변경과 실제 조작 검증은 [UX/UI 검수 기록](docs/ux-ui-review.md)을 참고한다.

설계/출처는 `docs/architecture.md`, 검증 기록은 `docs/validation.md`, 라이선스는 `THIRD-PARTY-NOTICES.md`를 참고한다.

## 개발 지침

[지침 세트](docs/guidelines/README.md)를 먼저 확인한다. [개발과 주석](docs/guidelines/development.md), [UX](docs/guidelines/ux.md), [심미성 중심 UI](docs/guidelines/ui.md), [기획](docs/guidelines/planning.md), [25개 원문 조사](docs/guidelines/research.md)를 보관한다.

앱 자체 상단 바는 최신 기본 배포본에도 포함된다. [상단 바 검증 기록](docs/custom-titlebar.md)을 참고한다.

게임 없이 직접 입력과 창 알림을 검수할 때는 프로젝트 SDK로 dotnet run --project tests/EternalGG.UiTests -c Release -- --attention-preview를 실행한다. 검수 창은 실제 MainWindow를 사용하지만 게임 감시를 시작하지 않는다. 검수용 설정 입력은 저장하지 않는다.
