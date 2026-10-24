# Hotzone Converter

Windows용 로컬 파일 변환 유틸리티. 탐색기에서 파일을 끌다가 **스페이스바를 한 번** 누르면 커서 주변에 원형 메뉴가 펼쳐지고, 원하는 형식 위에 놓으면 원본 옆에 새 파일이 생깁니다.

- 모든 처리는 내 PC 안에서만 합니다 (네트워크 전송 없음)
- 원본은 절대 수정하지 않습니다 (항상 새 파일)
- 설계 문서: [docs/design.md](docs/design.md)

## 설치 · 업데이트 · 제거

요구 사항: Windows 10/11 64비트. .NET을 따로 설치할 필요가 없습니다.

### 설치

1. [Releases](https://github.com/DongkyuSeoOssData/hotzone-Converter/releases)에서 `HotzoneConverter-win-x64.zip`을 받습니다. ([최신 버전 바로 받기](https://github.com/DongkyuSeoOssData/hotzone-Converter/releases/latest/download/HotzoneConverter-win-x64.zip))
2. `C:\Tools\HotzoneConverter`처럼 **계속 둘 폴더**에 압축을 풉니다. (다운로드 폴더나 바탕 화면처럼 정리하다 지울 수 있는 곳은 피하세요)
3. 그 안의 `HotzoneConverter.exe`를 실행합니다. 트레이에 파란 ⇄ 아이콘이 뜨면 동작 중입니다.

- 처음 실행할 때 "Windows의 PC 보호"(SmartScreen) 창이 뜨면 **추가 정보 → 실행**을 누릅니다. 코드 서명을 하지 않은 앱이라 뜨는 경고입니다.
- 처음 실행하면 **Windows 시작 시 자동 실행**에 등록됩니다. 환경설정 → 일반에서 끌 수 있습니다. 폴더를 옮기면 옮긴 곳에서 한 번 실행할 때 등록 경로가 자동으로 바뀝니다.
- 영상 MP4·오디오 M4A 변환은 Windows 미디어 기능을 씁니다. Windows N 에디션은 [미디어 기능 팩](https://support.microsoft.com/ko-kr/topic/media-feature-pack-list-for-windows-n-editions-c1c6fffa-d052-8338-7a79-a4bb980a700a)을 설치하세요.

> **exe만 따로 꺼내면 동작하지 않습니다.** 옆의 DLL들, `tools\ffmpeg\`(영상·오디오 변환), `licenses\`가 같은 폴더에 있어야 합니다. 바탕 화면에서 실행하고 싶으면 exe를 우클릭해 **바로 가기**를 만드세요.
>
> 파이썬 앱처럼 exe 하나로 묶지 않은 이유: 이미지 처리(libvips)와 FFmpeg가 LGPL 라이선스라서, 사용자가 그 부분을 다른 빌드로 바꿔 끼울 수 있게 폴더로 배포합니다.

### 업데이트

1. 트레이 아이콘 우클릭 → **종료**
2. 새 zip을 **같은 폴더에 덮어** 풉니다
3. `HotzoneConverter.exe`를 다시 실행합니다

설정과 작업 기록은 앱 폴더 밖(`%APPDATA%`, `%LOCALAPPDATA%`)에 있어 그대로 유지됩니다. 지금 버전은 환경설정 왼쪽 아래에서 볼 수 있습니다.

> 앱을 **두 개 동시에** 켜면 스페이스를 누를 때 패널이 두 번 뜹니다. 새 버전을 켜기 전에 이전 것을 꼭 종료하세요.

### 제거

1. 환경설정 → 일반 → **Windows 시작 시 자동 실행**을 끕니다 (시작 프로그램 등록이 지워짐)
2. 트레이 아이콘 우클릭 → **종료**
3. 앱 폴더(예: `C:\Tools\HotzoneConverter`)를 지웁니다
4. 설정·기록까지 지우려면 아래 두 폴더도 지웁니다
   - `%APPDATA%\HotzoneConverter` (설정)
   - `%LOCALAPPDATA%\HotzoneConverter` (작업 기록, 로그, 개발용 FFmpeg)

자동 실행을 끄지 않고 폴더부터 지웠다면, 작업 관리자 → 시작 앱에서 "HotzoneConverter"를 사용 안 함으로 바꾸면 됩니다.

## 사용법

1. 탐색기나 바탕 화면에서 파일(여러 개·폴더도 가능)을 끌기 시작합니다.
2. 끄는 중에 **스페이스바를 한 번** 누르면 커서 주변에 원형 메뉴가 펼쳐집니다. 한 번 더 누르면 닫힙니다.
3. 원하는 버블 위에 놓으면 변환이 시작되고, 화면 구석의 알림 창에서 진행률을 볼 수 있습니다.
4. 끝나면 알림 창의 **파일 열기 / 폴더 열기**로 결과를 바로 엽니다.

결과 위치: 원본과 같은 폴더. 같은 이름이 있으면 `이름 (1).jpg`처럼 번호를 붙입니다. 여러 페이지 결과(PDF → JPG 등)는 원본 이름 폴더 안에 `이름_01.jpg`, `이름_02.jpg` …로 만듭니다.

## 지원 변환

| 끌어온 파일 | 할 수 있는 것 |
|---|---|
| 아무 파일·폴더 | ZIP 압축 (1개는 `이름.zip`, 여러 개는 `첫이름 외 N개.zip`) |
| ZIP | 압축 풀기 |
| 이미지 (JPG, PNG, WebP, AVIF, TIFF, GIF, BMP, SVG, HEIC*) | JPG, PNG, WebP, AVIF, TIFF, GIF, ICO, PDF, 용량 줄이기 |
| 이미지 여러 장, PDF 여러 개 | PDF로 합치기 |
| PDF | JPG, PNG (페이지별), TXT (텍스트 추출), 페이지별 PDF로 나누기 |
| Word·Excel·PowerPoint | PDF (Office 또는 LibreOffice가 설치된 경우), PPTX → JPG·PNG |
| 영상 (MP4, MOV, MKV, WebM, AVI) | MP4, GIF, MP3 |
| 오디오 (WAV, M4A, FLAC, OGG, MP3) | MP3, WAV, M4A |

\* HEIC는 디코더가 없는 환경에서 변환이 실패할 수 있습니다.

## 환경설정

트레이 아이콘을 우클릭 → **환경설정…** (또는 아이콘 더블클릭)

| 메뉴 | 설정 |
|---|---|
| 일반 | 열기 방식 (커서 위치에 바로 / 핫존으로 가져가서), Windows 시작 시 자동 실행 |
| 모양 | 테마 (글라스 라이트 / 글라스 다크) |
| 핫존 | 핫존 모드일 때 모니터와 위치 (상하좌우·네 구석 8곳) |
| 알림 | 알림 창 보기, 완료 후 사라지기 (1·3·5·10초 / 안 사라짐), 모니터와 네 구석 위치 |
| 작업 기록 | 진행 중인 작업과 최근 100개 기록, 결과 열기 |

설정은 `%APPDATA%\HotzoneConverter\settings.json`, 작업 기록과 로그는 `%LOCALAPPDATA%\HotzoneConverter\`에 있습니다.

## 문제가 생기면

| 증상 | 확인할 것 |
|---|---|
| 스페이스를 눌러도 메뉴가 안 뜸 | 탐색기·바탕 화면에서 **끄는 중에** 눌렀는지 (다른 앱 위에서는 일부러 반응하지 않음). 관리자 권한으로 연 탐색기에서 끌면 동작하지 않습니다 |
| 메뉴가 두 번 뜸 | 앱이 두 개 켜져 있음. 작업 관리자에서 HotzoneConverter를 하나만 남기세요 |
| 영상·오디오 버블이 없음 | 앱 폴더에 `tools\ffmpeg\ffmpeg.exe`가 있는지 |
| MP4·M4A 변환 실패 | Windows N 에디션이면 미디어 기능 팩 설치 |
| 변환이 실패함 | 알림 창이나 환경설정 → 작업 기록의 **로그 보기** |

로그는 트레이 우클릭 → **로그 폴더 열기**(`%LOCALAPPDATA%\HotzoneConverter\hotzone.log`)에서 볼 수 있습니다. 문제를 알려 주실 때 이 파일을 함께 보내 주세요.

## 라이선스

- 함께 쓰는 오픈소스와 라이선스 전문: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- **FFmpeg는 LGPL 빌드만** 쓰고, 별도 프로그램으로 실행만 합니다 (교체 가능).
- **H.264·AAC 인코딩은 Windows 내장 인코더만** 씁니다 (특허 라이선스가 Windows에 포함). x264·OpenH264 등은 쓰지 않습니다.
- PDFsharp는 6.2.4(MIT)에 고정합니다 (6.3부터 AGPL). Ghostscript(AGPL)는 쓰지 않습니다.
- Hotzone Converter 자체: [MIT 라이선스](LICENSE) © 2026 ADER. 함께 배포하는 FFmpeg·libvips 등은 각자의 라이선스를 따릅니다.

## 개발

필요: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
dotnet build
dotnet test
dotnet run --project src/Hotzone.App
```

영상·오디오 변환을 개발 중에 쓰려면 FFmpeg LGPL 빌드의 `ffmpeg.exe`를 `%LOCALAPPDATA%\HotzoneConverter\ffmpeg\`에 둡니다. 배포 파일에는 `tools\ffmpeg\`에 함께 들어갑니다 ([scripts/publish.ps1](scripts/publish.ps1)).

```
src/
  Hotzone.App/          WPF 트레이 앱: 원형 패널, 핫존, 알림 창, 환경설정, 작업 기록
  Hotzone.Interop/      CsWin32: 저수준 마우스·키보드 훅(InputHook), 창 위치·스타일
  Hotzone.Core/         형식 판별, [Converts] 간선 + 2단계 체인 레지스트리, OutputWriter, ConversionQueue
  Hotzone.Converters/   NetVips(이미지), PDFium(PDF), PDFsharp(PDF 만들기), FFmpeg(영상·오디오), ZIP, Office
tests/
  Hotzone.Tests/        단위 테스트 + 실제 이미지·PDF·영상 변환 테스트
```

변환기 추가는 클래스에 간선만 선언하면 됩니다. 버블 노출과 체인 경로는 레지스트리가 찾습니다.

```csharp
[Converts("pdf -> jpeg, png", Cost = 2)]
public sealed class PdfRenderConverter : IConverter { ... }
```

개발용 명령: `HotzoneConverter.exe --render-preview <폴더>` (테마별 패널·알림·환경설정 화면을 PNG로 저장), `--make-icon <경로>` (앱 아이콘 다시 만들기)

## 📜 변경 기록

버전마다 바뀐 내용은 [CHANGELOG.md](CHANGELOG.md)에 있습니다.

- [0.1.0에서 달라진 점](CHANGELOG.md#-010에서-달라진-점)
