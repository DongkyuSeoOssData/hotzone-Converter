---
name: hotzone-convert
description: Hotzone Converter의 명령줄 도구 hzc로 로컬 파일을 변환한다. 이미지 형식 바꾸기(JPG·PNG·WebP·AVIF·TIFF·GIF·ICO·HEIC), 이미지·PDF를 PDF로 합치기, PDF를 페이지별로 나누거나 이미지·텍스트로 뽑기, 사진 용량 줄이기, 회전·반전, ZIP 압축·풀기, 영상 → MP4·GIF·MP3, 오디오 → MP3·WAV·M4A, Word·Excel·PowerPoint → PDF. "이거 jpg로 바꿔줘", "PDF 합쳐줘", "용량 줄여줘", "압축해줘", "mp3로 뽑아줘", "convert to png", "merge these PDFs"처럼 사용자의 파일을 다른 형식으로 만들어 달라는 요청에 쓴다. 파일은 네트워크로 보내지 않고 이 PC에서만 처리한다.
---

# Hotzone Converter로 파일 변환하기

`hzc`는 Hotzone Converter 앱 폴더에 `HotzoneConverter.exe`와 함께 들어 있는 명령줄 도구다. 드래그 메뉴와 같은 변환기를 쓰고, 결과도 같은 규칙으로 만든다.

- **원본은 바꾸지 않는다.** 결과는 항상 원본과 같은 폴더에 새 파일로 생긴다.
- 이름이 겹치면 `이름 (1).jpg`처럼 번호를 붙인다. 덮어쓰지 않는다.
- 결과가 여러 장이면(PDF → JPG, PDF 나누기) 원본 이름의 폴더 안에 `이름_01.jpg`, `이름_02.jpg` …로 만든다.

## 1. hzc 찾기

이 순서로 찾고, 처음 찾은 것을 이후 명령에 그대로 쓴다.

1. `PATH`에 있는 `hzc` (`where hzc` / `Get-Command hzc`)
2. 시작 프로그램에 등록된 앱 위치 옆의 `hzc.exe`:
   ```powershell
   $exe = (Get-ItemProperty HKCU:\Software\Microsoft\Windows\CurrentVersion\Run).HotzoneConverter -replace '"',''
   Join-Path (Split-Path $exe) hzc.exe
   ```
3. 흔한 설치 위치: `C:\Tools\HotzoneConverter\hzc.exe`
4. 이 저장소 안에서 작업 중이면: `dotnet run --project src/Hotzone.Cli --` (빌드에 시간이 걸리므로 여러 번 쓸 거면 `dotnet build src/Hotzone.Cli -c Release` 후 `src/Hotzone.Cli/bin/Release/net8.0/hzc` 사용)

못 찾으면 사용자에게 [Releases](https://github.com/DongkyuSeoOssData/hotzone-Converter/releases/latest)에서 `hzc.exe`가 들어 있는 버전(0.2.0 다음 릴리스부터)을 받아 설치하라고 안내하고 멈춘다.

## 2. 작업 순서

1. **경로를 확정한다.** 사용자가 말한 파일을 실제 경로로 찾는다. 와일드카드(`*.png`)는 셸에서 먼저 펼쳐 파일 목록으로 넘긴다 (hzc는 와일드카드를 펼치지 않는다).
2. **할 수 있는지 먼저 본다.** 형식이 확실하지 않거나 처음 보는 조합이면:
   ```
   hzc actions <파일...> --json
   ```
   `convertTo`(convert `--to`에 줄 수 있는 형식), `tools`(merge·split·shrink·rotate·flip·zip·unzip), `lossy`(텍스트 선택이 안 되는 결과)가 나온다. 여러 파일이면 모두에 공통으로 되는 것만 나온다. 원하는 것이 없으면 실행하지 말고 이유를 설명한다.
3. **실행한다.** 항상 `--json`을 붙여 결과를 정확히 읽는다.
4. **보고한다.** 만들어진 파일 경로를 알려 준다. 실패한 파일은 `error` 메시지를 그대로 전하고 아래 "실패했을 때"를 참고해 원인을 설명한다.

`lossy`에 있는 변환(PDF·오피스 → 이미지)은 글자를 선택·검색할 수 없게 된다는 점을 한 줄로 알려 주고 진행한다.

## 3. 명령

| 하고 싶은 것 | 명령 |
|---|---|
| 형식 변환 (파일마다 하나씩) | `hzc convert <파일...> --to <형식> --json` |
| 이미지·PDF 여러 개를 PDF 하나로 (주어진 순서대로) | `hzc merge <파일...> --json` → `첫이름_merged.pdf` |
| PDF를 페이지별 PDF로 | `hzc split <PDF...> --json` |
| PDF → 페이지별 이미지 | `hzc convert <PDF> --to jpg --json` (또는 png) |
| PDF → 텍스트 | `hzc convert <PDF> --to txt --json` |
| 사진 용량 줄이기 (긴 변 2560px, 같은 형식) | `hzc shrink <이미지...> --json` → `이름_small.jpg` |
| 회전 | `hzc rotate <이미지...> --by right\|left\|180 --json` → `이름_rotated.jpg` |
| 반전 | `hzc flip <이미지...> --axis horizontal\|vertical --json` → `이름_flipped.jpg` |
| ZIP 압축 (파일·폴더 섞어도 됨) | `hzc zip <경로...> --json` → `이름.zip` / `첫이름 외 N개.zip` |
| 압축 풀기 | `hzc unzip <ZIP...> --json` → 원본 이름 폴더 |
| 설치된 도구 확인 | `hzc check --json` |

`--to`에 쓰는 형식: `jpg`, `png`, `webp`, `avif`, `tiff`, `gif`, `ico`, `pdf`, `txt`, `mp4`, `mp3`, `wav`, `m4a`. (`jpeg`, `tif`도 됨)

용도별 주의:
- 회전·반전은 JPG·PNG·WebP·AVIF·TIFF만. 움직이는 GIF는 안 된다.
- 용량 줄이기는 JPG·PNG·WebP·AVIF만.
- 합치기는 이미지·PDF 2개 이상. 순서가 중요하면 사용자가 원하는 순서대로 인수를 넘긴다.
- 폴더는 `zip`에만 넘길 수 있다. 폴더 안 파일들을 변환하려면 파일 목록을 펼쳐서 넘긴다.
- 파일 이름이 `-`로 시작하면 파일 앞에 `--`를 둔다: `hzc convert --to png -- -a.jpg`

## 4. 결과 읽기

```json
{
  "ok": false,
  "results": [
    { "source": "C:\\사진\\a.png", "output": "C:\\사진\\a.jpg" },
    { "source": "C:\\사진\\b.png", "error": "b.png → mp3 변환 경로가 없습니다" }
  ]
}
```

- 결과 순서는 입력 순서와 같다. 하나가 실패해도 나머지는 계속 처리한다.
- `merge`·`zip`은 결과가 하나이고 `sources`에 입력 목록이 들어간다.
- 종료 코드: `0` 전부 성공, `1` 하나 이상 실패, `2` 잘못된 사용법(없는 파일, 모르는 옵션 등 — 아무것도 만들지 않음).

## 5. 실패했을 때

| 메시지·상황 | 원인과 안내 |
|---|---|
| `변환 경로가 없습니다` | 지원하지 않는 조합. `hzc actions`로 가능한 형식을 보여 준다 |
| 영상·오디오 형식이 `actions`에 없음, `FFmpeg를 찾을 수 없습니다` | 앱 폴더의 `tools\ffmpeg\ffmpeg.exe`가 없음. 앱 zip을 다시 풀도록 안내 |
| 문서(docx·xlsx·pptx) → PDF가 `actions`에 없음 | Microsoft Office나 LibreOffice가 없음 (`hzc check`로 확인) |
| MP4·M4A 변환 실패 | Windows N 에디션이면 미디어 기능 팩 필요 |
| HEIC 변환 실패 | 이 PC에 HEIC 디코더가 없음. 사용자에게 알리고 다른 방법을 제안 |
| 종료 코드 2 | 경로 오타·없는 파일. 경로를 다시 확인한다 |

hzc는 Windows용으로 배포된다. 영상·오디오·오피스 변환은 Windows에서만 되고, 이미지·PDF·ZIP은 저장소에서 `dotnet run`으로 다른 OS에서도 동작한다.
