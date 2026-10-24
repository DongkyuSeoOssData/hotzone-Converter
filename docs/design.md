# Hotzone Converter (가칭) — 초기 설계

> Windows용 로컬 파일 변환 유틸리티. 탐색기에서 파일을 끌다가 **스페이스바를 한 번 탭**하고 **화면 가장자리 핫존**으로 가져가면 변환 패널이 펼쳐지고, 원하는 형식 위에 놓으면 원본 옆에 새 파일이 생긴다.

---

## 1. 목표 / 비목표

**목표**
- 앱을 열지 않고 드래그 한 번으로 변환을 끝낸다
- 모든 처리는 로컬에서 한다 (네트워크 전송 없음)
- 원본은 절대 수정하지 않는다 (항상 새 파일 생성)
- Windows 사용자에게 자연스러운 UX (트레이 상주, 토스트 알림)

**비목표 (MVP 기준)**
- 클라우드 변환, 계정, 동기화
- 고급 편집 (크롭, 워터마크 등은 Phase 3 이후)
- 관리자 권한 창과의 드래그 연동

---

## 2. 사용자 흐름

```
[탐색기에서 파일 드래그 시작]
        │
        ▼
[드래그 중 스페이스바 탭 → 무장(armed), 커서 옆에 표시]
        │
        ▼
[커서가 화면 가장자리 핫존에 진입]
        │  (OLE DragEnter/DragOver에서 무장 상태 확인)
        ▼
[드래그 중인 파일 확장자 분석 → 가능한 변환 형식만 표시]
        │
        ▼
[패널이 펼쳐짐: JPG / PNG / PDF / WebP ...]
        │
        ▼
[원하는 형식 타일 위에 드롭]
        │
        ▼
[백그라운드 변환 → 원본 폴더에 새 파일 생성 → 토스트 알림]
```

- 패널 밖으로 나가면 300ms 후 접힘
- 드래그를 Esc로 취소하면 즉시 접힘
- 여러 파일을 한 번에 끌어도 동작 (일괄 변환)
- 순서는 상관없음: 핫존에 먼저 들어간 뒤 스페이스를 탭해도 펼쳐짐
- 무장하지 않고 핫존을 지나가면 아무 일도 없음 (가장자리 창으로의 일반 드래그 보호)

---

## 3. 핵심 설계: 핫존 감지

### 3.1 아이디어
전역 키보드·마우스 훅으로 "드래그 중인지" 추측할 필요가 없다. 핫존 자체를 **OLE 드롭 타깃(IDropTarget)으로 등록한 얇은 창**으로 만들면, 파일을 끌고 들어오는 순간 OS가 `DragEnter`를 호출해 준다. 이게 곧 "파일 드래그가 핫존에 들어왔다"는 신호다.

### 3.2 핫존 창 스펙
| 항목 | 값 |
|---|---|
| 위치 | 기본: 오른쪽 가장자리 세로 중앙 (설정으로 변경) |
| 크기 | 두께 4px × 높이 화면의 40% (DPI 반영) |
| 스타일 | `WS_EX_TOPMOST`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `WS_EX_LAYERED` |
| 투명도 | 알파 1/255 (완전 투명이면 히트 테스트가 안 돼서 드롭을 못 받음) |
| 클릭 통과 | 평소엔 `WS_EX_TRANSPARENT` 켜서 클릭 통과 → 마우스 왼쪽 버튼 눌림 동안만 해제 |

### 3.3 클릭 통과 토글
4px 띠가 평소 클릭(스크롤바 등)을 막지 않도록:
- `WH_MOUSE_LL` 훅으로 왼쪽 버튼 down/up만 감시
- down → 핫존의 `WS_EX_TRANSPARENT` 해제 (드롭 받을 준비)
- up → 다시 설정
- 훅 콜백은 플래그만 바꾸고 즉시 리턴 (훅 타임아웃 방지)

### 3.4 스페이스 무장(Arming)
핫존만으로는 가장자리에 붙은 창으로 파일을 옮길 때 패널이 튀어나와 가리는 오작동이 생긴다. 스페이스바 탭을 조건으로 건다.

**누르고 유지가 아니라 "탭 한 번"으로 무장** — 드래그하면서 키를 계속 누르는 건 손이 불편하다.

| 이벤트 | 동작 |
|---|---|
| 왼쪽 버튼 down 상태에서 스페이스 keydown | `IsArmed = true`, 키 이벤트 삼킴(return 1), 커서 옆 무장 표시 |
| 왼쪽 버튼 up / Esc | `IsArmed = false`, 표시 제거 |
| 왼쪽 버튼 up 상태의 스페이스 | 관여하지 않음 (평소 타이핑 영향 없음) |
| 스페이스 keyup / 자동 반복 keydown | 무장 상태면 함께 삼킴 (뒤늦은 공백 입력 방지) |

- `WH_KEYBOARD_LL` 훅을 3.3의 `WH_MOUSE_LL`과 같은 `InputHook` 클래스에서 관리
- 무장 표시: 커서 옆 작은 주황 점 (최상단, 클릭 통과, 커서 따라 이동)
- 설정 **"패널 열기 조건"**: `스페이스 무장 시에만`(기본) / `항상`

### 3.5 DragEnter / DragOver에서 할 일
1. `IDataObject`에서 `CF_HDROP` 존재 확인 → 없으면 무시 (텍스트 드래그 등)
2. **무장 확인** → 무장 안 됐으면 `DROPEFFECT_NONE` 반환, 패널 접은 채 유지
3. 파일 경로 목록 추출 → 확장자로 입력 타입 판별
4. `ConverterRegistry`에 공통 출력 형식 질의
5. 패널 확장 애니메이션 + 형식 타일 렌더
6. `DROPEFFECT_COPY` 반환 (원본 이동 방지)

- `DragOver`는 커서가 핫존 안에 있는 동안 계속 호출되므로, 핫존 진입 후 스페이스를 탭해도 다음 `DragOver`에서 펼쳐진다
- 파일 경로·형식 판별은 `DragEnter`에서 한 번만 하고 캐시, `DragOver`에서는 무장 플래그만 확인

---

## 4. 아키텍처

```
┌────────────────────────── App (WPF, 트레이 상주) ──────────────────────────┐
│                                                                            │
│  HotzoneWindow ──DragEnter──▶ PanelController ──▶ PanelWindow (형식 타일)   │
│        ▲                                              │ Drop               │
│  InputHook (마우스·키보드 LL, 무장 상태)                ▼                    │
│                                               ConversionQueue              │
│                                                       │                    │
│                                               ConverterRegistry            │
│                                 ┌──────────┬──────────┼──────────┐         │
│                                 ▼          ▼          ▼          ▼         │
│                           ImageConv    PdfConv    MediaConv   DocConv      │
│                           (NetVips)   (PDFium)    (FFmpeg)  (Office COM /  │
│                                                              LibreOffice)  │
│                                                       │                    │
│                                               OutputWriter → ToastNotifier │
└────────────────────────────────────────────────────────────────────────────┘
```

### 4.1 컴포넌트 책임
| 컴포넌트 | 책임 |
|---|---|
| `HotzoneWindow` | 얇은 드롭 타깃 창, DragEnter/Leave 이벤트 발행 |
| `InputHook` | 왼쪽 버튼 상태로 클릭 통과 토글, 드래그 중 스페이스 탭으로 무장/해제, 무장 표시 |
| `PanelController` | 패널 열기/닫기 타이밍, 입력 타입 → 표시 형식 결정 |
| `PanelWindow` | 형식 타일 UI, 타일별 드롭 처리 |
| `ConversionQueue` | 작업 큐, 동시 실행 수 제한(기본 CPU 코어 수 / 2), 취소 |
| `ConverterRegistry` | `(입력 형식 → 출력 형식)` 매핑 → 변환기 선택 |
| `IConverter` 구현체 | 실제 변환 |
| `OutputWriter` | 출력 경로 결정, 이름 충돌 처리, 임시 파일 → 원자적 이동 |
| `ToastNotifier` | 완료/실패 알림, "폴더 열기" 버튼 |

### 4.2 변환기 인터페이스
```csharp
public interface IConverter
{
    string Id { get; }
    IReadOnlyCollection<string> InputFormats { get; }   // "pdf", "jpg" ...
    IReadOnlyCollection<string> OutputFormats { get; }
    Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<double>? progress,
        CancellationToken ct);
}

public record ConversionRequest(
    string SourcePath,
    string TargetFormat,
    string OutputPath,
    IReadOnlyDictionary<string, string> Options); // 품질, DPI 등
```

### 4.3 출력 규칙
변환기는 결과물(파일 1개 또는 페이지 N장)만 반환하고, 저장 방식은 `OutputWriter`가 일괄 결정한다.

**단일 결과**
- 경로: 원본과 같은 폴더
- 이름: `원본이름.새확장자`, 충돌 시 `원본이름 (1).새확장자`

**다중 페이지 결과** (PDF·PPTX·XLSX → 이미지)
| 페이지 수 | 저장 방식 |
|---|---|
| 1 | 원본 옆에 단일 파일 → `보고서.jpg` |
| 2 이상 | 원본 이름 폴더 생성 → `보고서/보고서_01.jpg`, `보고서_02.jpg` ... |

- 번호 자릿수는 총 페이지 수에 맞춰 0 채움 (10장 → `01`, 100장 → `001`) — 탐색기 정렬 보장
- 폴더 이름 충돌 시 `보고서 (1)/`
- 여러 파일을 한 번에 변환하면 **파일마다 각자 폴더** (한 폴더에 섞지 않음)
- 토스트의 "폴더 열기"는 생성된 폴더를 연다

**다중 페이지 옵션 타일** (패널의 "더보기")
- 첫 페이지만 → 단일 파일
- 세로로 이어붙인 이미지 1장 → `보고서_merged.jpg` (최대 높이 초과 시 분할)
- ZIP으로 묶기 → `보고서_jpg.zip`

**공통**
- 쓰기: `%TEMP%`에 생성 → 완료 후 `File.Move` (중간 실패 시 반쪽 파일 방지)
- 다중 페이지는 임시 폴더에 전부 생성한 뒤 폴더째 이동 (일부만 남는 상태 방지)

### 4.4 형식 매핑
확장자 → 확장자 표를 중앙에서 관리하지 않는다. **변환기가 간선(입력→출력)을 선언하고, 레지스트리가 길을 찾는다.**

**① 형식 판별: 확장자는 힌트, 시그니처로 확정**
- 별칭 정규화: `jpg`/`jpeg`/`jfif` → `jpeg`, `tif`/`tiff` → `tiff` 등
- 파일 앞부분 매직 넘버로 실제 형식 확인 (확장자만 `.jpg`이고 내용은 PNG/WebP인 경우 대응)
- `DragEnter`에서는 앞의 몇 개 파일만 판별하고, 나머지는 드롭 후 검증 (패널 지연 방지)

**② 간선 선언**
```csharp
[Converts("pdf", to: "jpeg", "png", Cost = 2)]
[Converts("jpeg", "png", "webp", "heic", to: "jpeg", "png", "webp", "pdf", Cost = 1)]
[Converts("pptx", "docx", "xlsx", to: "pdf", Cost = 2, Requires = "Office")]       // OfficeComConverter
[Converts("pptx", "docx", "xlsx", to: "pdf", Cost = 4, Requires = "LibreOffice")]  // LibreOfficeConverter
[Converts("pptx", to: "jpeg", "png", Cost = 2, Requires = "Office")]               // Slide.Export 직접 간선
```
- `Requires` 조건이 충족되지 않으면 간선 자체를 제외 → 타일 자동 숨김
- 같은 간선에 변환기가 여럿이면 `Cost`가 낮은 쪽 우선 (Office COM > LibreOffice)
- `Cost`는 속도 + 손실 정도를 반영한 상대값

**③ 체인 (자동 추론)**
- 직접 간선이 없으면 **최대 2단계**까지 최소 비용 경로 탐색
  - 예: XLSX → PNG = XLSX → PDF → PNG / HEIC → PDF = HEIC → JPEG → PDF
- 직접 간선이 있으면 체인보다 비용이 낮게 잡혀 자동 선택 (예: PPTX → JPG는 `Slide.Export` 직접 사용)
- 3단계 이상은 품질·속도 손해로 금지

**④ 여러 파일을 끌었을 때**
- **1:1 변환**: 모든 파일의 출력 가능 형식 **교집합**만 표시
- **N:1 동작**: 교집합과 별개로 "합치기" 타일 표시
  - 이미지 N장 → PDF 1개
  - PDF N개 → 병합 PDF 1개

**⑤ 타일 노출 규칙**
- 자기 자신으로의 변환은 숨김 (압축·리사이즈 같은 같은 형식 도구는 Phase 3 별도 그룹)
- 카테고리별 기본 우선순위 + 사용 빈도로 재정렬 → 상위 5~6개만 노출, 나머지 "더보기"
- 손실 변환(예: PDF → JPG는 텍스트 선택 불가)은 작은 경고 아이콘

**MVP 간선 목록**
| 입력 | 출력 | 변환기 |
|---|---|---|
| PPTX / DOCX / XLSX | PDF | Office COM → (없으면) LibreOffice |
| PPTX | JPG / PNG | Office COM `Slide.Export` |
| PDF | JPG / PNG | PDFium |
| JPG / PNG / HEIC / WebP | 서로 변환, PDF | NetVips + PdfSharp |
| 이미지 N장 | PDF 1개 (합치기) | PdfSharp |
| PDF N개 | PDF 1개 (병합) | PdfSharp |

**오피스 변환 기본 옵션**
- Excel: "시트를 한 페이지 너비에 맞춤" 기본 적용 (인쇄 영역 미설정 시 잘림·축소 방지)
- PPTX → 이미지: 기본 폭 1920px, 설정에서 변경
- PDF → 이미지: 기본 150 DPI, 설정에서 변경

---

## 5. 기술 스택

| 영역 | 선택 | 비고 |
|---|---|---|
| 런타임 | .NET 8 | 자체 포함(self-contained) 배포 |
| UI | WPF | 투명 창·애니메이션 다루기 쉬움 |
| Win32 interop | CsWin32 | P/Invoke 자동 생성 |
| 이미지 | NetVips (libvips) | 빠르고 메모리 효율적, HEIC/WebP/AVIF 지원 |
| PDF 렌더 | PDFium (PDFiumCore 등) | BSD 라이선스 |
| 이미지→PDF | PdfSharp 6.2.4 고정 | MIT (6.3부터 AGPL로 바뀌어 업그레이드 금지) |
| 영상·오디오 | FFmpeg (exe 번들, 프로세스 호출) | LGPL 빌드 사용 |
| 오피스 문서 (1순위) | MS Office COM (`ExportAsFixedFormat`, `Slide.Export`) | 설치 감지 시 사용, 폰트·레이아웃 원본 그대로 |
| 오피스 문서 (대체) | LibreOffice headless | Office 없고 LibreOffice 설치 시에만 활성화 (번들 X) |
| 알림 | Windows App SDK 토스트 | |
| 설치 | MSIX 또는 Inno Setup | 시작 프로그램 등록 포함 |

**라이선스 주의**: Ghostscript(AGPL)는 피한다. FFmpeg은 GPL 컴포넌트 없는 LGPL 빌드로.

---

## 6. 프로젝트 구조

```
src/
  Hotzone.App/            # WPF, 트레이, 핫존·패널 창, 설정 UI
  Hotzone.Interop/        # 훅, OLE 드롭 타깃, 창 스타일 (CsWin32)
  Hotzone.Core/           # IConverter, Registry, Queue, OutputWriter
  Hotzone.Converters/     # Image / Pdf / Media / Doc 구현체
tools/
  ffmpeg/                 # 번들 바이너리
tests/
  Hotzone.Core.Tests/
  Hotzone.Converters.Tests/   # 샘플 파일 기반 골든 테스트
```

---

## 7. 단계별 범위

### Phase 1 — MVP
- [ ] 트레이 상주 + 시작 프로그램 등록
- [ ] 오른쪽 가장자리 핫존 1개
- [ ] 드래그 중 스페이스 탭 무장 + 무장 표시
- [ ] DragEnter/DragOver(무장 확인) → 패널 확장 → 타일 드롭 → 변환
- [ ] 형식 판별(시그니처) + 간선 선언 + 2단계 체인 레지스트리
- [ ] 변환: 4.4 MVP 간선 목록 전체 (오피스 → PDF 포함)
- [ ] 다중 페이지 출력 규칙 + 토스트 알림
- [ ] 다중 모니터, Per-Monitor DPI v2

### Phase 2
- [ ] 영상·오디오 (MP4/MOV/GIF, MP3/WAV/M4A) — FFmpeg
- [ ] 진행률 표시 (패널 또는 토스트)
- [ ] 핫존 위치·크기·개수 설정 (모서리별 다른 프리셋)
- [ ] 변환 옵션 (JPG 품질, PDF 렌더 DPI, 슬라이드 이미지 폭)
- [ ] 다중 페이지 옵션 타일 (첫 페이지만 / 이어붙이기 / ZIP)
- [ ] 사용 빈도 기반 타일 재정렬

### Phase 3
- [ ] 파일 도구: 압축, 메타데이터 제거, 리사이즈
- [ ] 오른쪽 버튼 드래그 셸 확장 (DragDropHandler) 보조 진입점

---

## 8. 리스크와 대응

| 리스크 | 내용 | 대응 |
|---|---|---|
| UIPI | 관리자 권한 탐색기 → 일반 권한 앱 드롭 차단 | 지원 범위 밖으로 명시, 감지 시 안내 |
| 전체 화면 앱 | 게임·영상 위에 핫존이 걸림 | `SHQueryUserNotificationState`로 전체 화면 시 비활성 |
| 작업 표시줄 충돌 | 작업 표시줄이 같은 가장자리에 있을 때 | 작업 표시줄 위치 감지 후 핫존 자동 회피 |
| 자동 숨김 작업 표시줄 | 가장자리 hover 시 함께 튀어나옴 | 같은 가장자리면 핫존을 반대편으로 기본 배치 |
| 훅 안정성 | LL 훅 콜백 지연 시 OS가 훅 해제 | 콜백에서 플래그만 변경, 주기적 재등록 확인 |
| 스페이스 가로채기 부작용 | 마우스를 누른 채 스페이스를 쓰는 앱(게임, 디자인 툴의 손 도구 등)에서 키가 먹힘 | 왼쪽 버튼 down이어도 포그라운드가 탐색기·바탕화면일 때만 가로채기, 예외 앱 목록 설정 |
| 무장 상태 고착 | 드래그가 비정상 종료돼 up 이벤트를 놓침 | 무장 후 일정 시간(예: 10초) 지나면 자동 해제, 버튼 상태를 `GetAsyncKeyState`로 재확인 |
| Office COM 불안정 | 백그라운드 Office 프로세스가 대화상자(보호된 보기, 매크로 경고)에 걸려 멈춤, 좀비 프로세스 | `DisplayAlerts = false`, 읽기 전용으로 열기, 작업별 타임아웃 후 프로세스 종료, 동시 실행 1개로 제한 |
| 사용자 Office 작업 간섭 | 사용자가 열어둔 PowerPoint 인스턴스에 붙어버림 | 변환 전용 새 인스턴스 생성, 사용자 창 건드리지 않기 |
| 대용량 다중 페이지 | 수백 장 PDF → 이미지로 디스크·시간 폭증 | 50장 초과 시 확인 토스트, 임시 폴더 용량 체크 |
| 발견성 | 사용자가 스페이스 조건을 모름 | 첫 실행 온보딩, 무장 없이 핫존 진입 시 "스페이스를 눌러 열기" 힌트 1회 표시 |
| 대용량 파일 | 영상·수백 페이지 PDF | 큐 + 취소 + 진행률, 임시 폴더 용량 체크 |
| 백신 오탐 | 전역 훅 + 미서명 exe | 코드 서명 인증서, MSIX 배포 |

---

## 9. 검증 체크리스트 (MVP 완료 기준)

- [ ] 탐색기·바탕화면에서 끌어온 파일 모두 핫존 반응
- [ ] 텍스트·브라우저 이미지 드래그에는 반응하지 않음
- [ ] 평소 핫존 영역 클릭·스크롤이 막히지 않음
- [ ] 무장 없이 핫존을 지나가면 패널이 뜨지 않고, 가장자리 창으로의 드롭이 정상 동작
- [ ] 스페이스 탭 → 핫존 진입 / 핫존 진입 → 스페이스 탭, 두 순서 모두 패널이 펼쳐짐
- [ ] 드래그 중 스페이스가 다른 창에 공백으로 입력되지 않음
- [ ] 마우스를 누르지 않은 상태의 스페이스 입력은 그대로 동작
- [ ] 원본 파일 해시가 변환 전후 동일
- [ ] 이름 충돌 시 `(1)` 붙여 생성
- [ ] 확장자가 틀린 파일(PNG인데 `.jpg`)도 정상 변환
- [ ] 1페이지는 단일 파일, 2페이지 이상은 폴더 + 0 채운 번호로 생성
- [ ] PPT 3개 동시 변환 시 폴더 3개로 분리
- [ ] Office 설치 PC에서 PPTX → PDF 결과가 원본과 폰트·레이아웃 일치
- [ ] Office·LibreOffice 둘 다 없으면 오피스 타일이 표시되지 않음
- [ ] 이미지 + PDF 섞어 끌면 교집합 타일과 합치기 타일만 표시
- [ ] 125% / 150% DPI, 모니터 2대에서 위치 정확
- [ ] 50개 파일 일괄 변환 중 UI 멈춤 없음
- [ ] 네트워크 차단 상태에서 모든 MVP 변환 동작

---

## 10. 열린 질문

- 패널 형태: 세로 리스트 vs 반원 팬 — 핫존이 가장자리라 반원이 공간 활용에 유리
- 무료/유료 모델 여부 (무료 횟수 제한 등)
- 사내용 도구로 쓸지, 공개 배포할지 → 코드 서명·배포 채널 결정에 영향
