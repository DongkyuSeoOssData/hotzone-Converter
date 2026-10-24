# Hotzone Converter

Windows용 로컬 파일 변환 유틸리티. 탐색기에서 파일을 끌다가 **스페이스바를 한 번 탭**하고 **화면 오른쪽 가장자리 핫존**으로 가져가면 변환 패널이 펼쳐지고, 원하는 형식 위에 놓으면 원본 옆에 새 파일이 생긴다.

- 설계 문서: [docs/design.md](docs/design.md)
- 모든 처리는 로컬, 원본은 절대 수정하지 않음

## 빌드·실행

필요: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Visual Studio 2022 17.8+ 또는 Rider도 가능)

```powershell
cd D:\01_Dev\90_GitHub\hotzone-Converter
dotnet build
dotnet test
dotnet run --project src/Hotzone.App
```

트레이에 아이콘이 뜨면 동작 중. 탐색기에서 이미지·PDF를 끌면서 스페이스를 한 번 누르고(커서 옆에 주황 점), 화면 오른쪽 가장자리 세로 중앙으로 가져간다.

## 구조

```
src/
  Hotzone.App/          WPF 트레이 앱: 핫존 창, 패널, 무장 표시, 토스트
  Hotzone.Interop/      CsWin32: 저수준 마우스·키보드 훅(InputHook), 창 스타일
  Hotzone.Core/         형식 판별, [Converts] 간선 + 2단계 체인 레지스트리, OutputWriter, ConversionQueue
  Hotzone.Converters/   NetVips(이미지), PDFium(PDF→이미지), PDFsharp(이미지→PDF, 병합), Office COM, LibreOffice
tests/
  Hotzone.Tests/        코어 단위 테스트 + 실제 이미지/PDF 변환 테스트
```

변환기 추가는 클래스에 간선만 선언하면 된다. 타일 노출과 체인 경로는 레지스트리가 알아서 찾는다.

```csharp
[Converts("pdf -> jpeg, png", Cost = 2)]
public sealed class PdfRenderConverter : IConverter { ... }
```

## 라이선스 주의

- PDFsharp는 **6.2.4에 고정**. 6.3부터 AGPL로 바뀌었다 (설계상 AGPL 회피).
- Ghostscript(AGPL) 사용 금지, FFmpeg은 LGPL 빌드만.

## Phase 1 진행 상황

- [x] 형식 판별(시그니처) + 간선 선언 + 2단계 체인 레지스트리
- [x] 출력 규칙: 단일 파일 / 다중 페이지 폴더 + 0 채움 번호 / `(1)` 충돌 / 임시 폴더 → 이동
- [x] 이미지 ↔ 이미지/PDF, PDF → 이미지, 이미지·PDF 합치기 (테스트 통과)
- [x] Office COM → PDF, PPTX → 이미지, LibreOffice 대체 (Windows 실기 확인 필요)
- [x] 트레이, 핫존 창, 스페이스 무장 훅, 무장 표시, 패널, 토스트 (Windows 실기 확인 필요)
- [ ] 다중 모니터, 작업 표시줄 회피
- [ ] Office 타임아웃 시 해당 프로세스 종료
- [ ] 시작 프로그램 등록, 첫 실행 온보딩
