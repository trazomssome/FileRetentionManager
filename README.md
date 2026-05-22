# FileRetentionManager

현대적인 WPF MVVM 아키텍처로 구현한 조건부 파일 자동 삭제 시스템입니다. 사용자가 지정한 폴더를 주기적으로 스캔하고, 활성화된 삭제 조건에 맞는 파일을 찾아 삭제한 뒤 주기별 Markdown 리포트를 생성합니다.

## 주요 기능

- WPF 기반 데스크톱 UI
- Windows 11 시스템 테마를 따르는 Microsoft Fluent Theme 적용
- 시퀀스 시작 시 삭제 대상 파일 1회 미리보기 및 시작 여부 확인
- 시퀀스 시작 후 다음 주기부터는 추가 확인 없이 같은 조건으로 자동 삭제
- 여러 대상 폴더 관리
- 하위 폴더 포함 여부 선택
- 삭제 조건별 사용 여부 선택
  - 최대 보관 기간
  - 최소 파일 크기(KB)
  - 파일 이름 패턴
- 전체 삭제 조건에 대한 `And` / `Or` 조합 모드 선택
- 삭제 결과 및 실패 사유 표시
- 각 실행 주기별 Markdown 리포트 생성
- Serilog 기반 파일/콘솔 로깅

## 삭제 시퀀스 동작

1. 사용자가 대상 폴더와 삭제 조건을 설정합니다.
2. `Enable` 또는 `Run now`를 실행합니다.
3. 앱이 현재 조건으로 삭제 대상 파일을 스캔합니다.
4. 시퀀스 시작 확인 창에서 첫 실행 대상 파일 목록을 보여줍니다.
5. 사용자가 `Start sequence`를 선택하면 첫 삭제가 실행됩니다.
6. `Enable`로 시작한 시퀀스는 이후 주기부터 사용자에게 다시 묻지 않고 승인 당시 조건으로 삭제를 실행합니다.
7. 사용자가 `Disable`을 누르면 시퀀스가 중지됩니다.

## 조건 설정

### Condition Mode

활성화된 삭제 조건을 어떻게 조합할지 선택합니다.

- `And`: 켜져 있는 모든 조건을 만족하는 파일만 삭제 대상입니다.
- `Or`: 켜져 있는 조건 중 하나라도 만족하는 파일이 삭제 대상입니다.

### 삭제 옵션

각 삭제 옵션 앞의 체크박스를 통해 해당 조건의 사용 여부를 결정합니다.

- `Maximum age (days)`: 마지막 수정 시간이 지정 일수보다 오래된 파일을 대상으로 합니다.
- `Minimum size (KB)`: 지정 KB 이상 크기의 파일을 대상으로 합니다.
- `Name patterns`: 와일드카드 패턴과 일치하는 파일명을 대상으로 합니다. 예: `*.tmp;*.log`

## 스케줄 설정

스케줄 주기는 시/분/초로 입력합니다.

- `h`: 시간
- `m`: 분
- `s`: 초

입력한 주기는 0보다 커야 합니다.

## Target Paths

`Add folder` 버튼으로 Windows 폴더 선택 다이얼로그를 열어 대상 폴더를 추가합니다. 추가된 폴더는 목록에서 확인할 수 있으며, 선택 후 `Remove`로 삭제할 수 있습니다.

`Include subdirectories`를 켜면 대상 폴더의 하위 폴더까지 스캔합니다.

## 프로젝트 구조

```text
FileRetentionManager.sln
├── FileRetentionManager.Domain
├── FileRetentionManager.Infrastructure.WPF
├── FileRetentionManager.App
└── FileRetentionManager.Tests
```

### FileRetentionManager.Domain

순수 도메인 모델, 삭제 조건 규칙, 서비스 인터페이스를 포함합니다. WPF나 파일 시스템 구현에 의존하지 않습니다.

주요 구성:

- `RetentionCriteria`
- `CompositeRetentionRule`
- `IFileSystemService`
- `IUserDecisionService`
- `IReportGenerator`
- `ITargetPathPickerService`

### FileRetentionManager.Infrastructure.WPF

WPF 기반 인프라 구현을 포함합니다.

주요 구성:

- 물리 파일 시스템 구현
- Markdown 리포트 생성기
- 폴더 선택 다이얼로그 서비스
- 시퀀스 시작 확인 CustomControl

### FileRetentionManager.App

WPF 애플리케이션, MVVM ViewModel, DI/Hosting/Logging 구성을 포함합니다.

### FileRetentionManager.Tests

xUnit과 Moq 기반 단위 테스트를 포함합니다.

## 기술 스택

- .NET 10.0 WPF
- CommunityToolkit.MVVM
- Microsoft.Extensions.Hosting
- Microsoft Fluent Theme
- Serilog
- FluentValidation
- xUnit
- Moq

## 실행 방법

```powershell
dotnet build FileRetentionManager.sln
dotnet run --project FileRetentionManager.App\FileRetentionManager.App.csproj
```

## 테스트

```powershell
dotnet test FileRetentionManager.sln
```

## 생성 파일

앱 실행 디렉터리 기준으로 다음 파일이 생성됩니다.

- `logs/file-retention-.log`: Serilog 로그 파일
- `reports/retention-report-*.md`: 주기별 Markdown 리포트

## 설계 원칙

- ViewModel은 UI 타입을 직접 참조하지 않습니다.
- 파일 I/O는 `IFileSystemService`를 통해 수행합니다.
- 사용자 결정은 `IUserDecisionService.AskAsync()`로 추상화합니다.
- 삭제 조건 판단은 순수 규칙 객체에서 수행합니다.
- 입력 검증은 FluentValidation으로 처리합니다.
- ViewModel 속성과 명령은 CommunityToolkit.MVVM 소스 제너레이터를 사용합니다.
