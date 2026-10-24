# 오픈소스 라이선스 고지

Hotzone Converter 자체는 [MIT 라이선스](LICENSE)(© 2026 ADER)로 배포합니다. 이 앱은 아래 오픈소스 소프트웨어를 사용하며, 각 소프트웨어의 저작권은 원저작자에게 있고 각자의 라이선스를 따릅니다.
배포 파일(`HotzoneConverter-win-x64.zip`)에는 이 문서와 `licenses\` 폴더의 라이선스 전문이 함께 들어 있습니다.

> 이 문서는 법률 자문이 아닙니다. 유료 판매나 기업 배포 전에는 전문가 검토를 받으세요.

## 함께 배포하는 구성요소

| 구성요소 | 버전 | 라이선스 | 쓰는 방식 | 출처 |
|---|---|---|---|---|
| FFmpeg (BtbN LGPL 빌드) | n8.1 | **LGPL-2.1-or-later** | 별도 프로그램(`tools\ffmpeg\ffmpeg.exe`)으로 실행만 함. 코드에 링크하지 않음 | https://ffmpeg.org · 빌드: https://github.com/BtbN/FFmpeg-Builds |
| libvips + 동봉 라이브러리 (NetVips.Native) | 8.18.7 | **LGPL-3.0-or-later** (libvips, glib, libheif, librsvg, pango 등) 외 MIT·BSD·zlib 계열 | 별도 DLL로 동적 로드 | https://github.com/libvips/libvips · 목록: `licenses\libvips-THIRD-PARTY-NOTICES.md` |
| NetVips | 3.2.0 | MIT | .NET 바인딩 | https://github.com/kleisauke/net-vips |
| PDFium (PDFiumCore) | 157.0.8100 | Apache-2.0 (PDFium 본체는 BSD-3-Clause·Apache-2.0) | 별도 DLL로 동적 로드 | https://github.com/Dtronix/PDFiumCore · https://pdfium.googlesource.com/pdfium |
| PDFsharp | **6.2.4 (고정)** | MIT | 라이브러리 | https://github.com/empira/PDFsharp |
| .NET 런타임 (self-contained 배포 시) | 8.0 | MIT | 런타임 | https://github.com/dotnet/runtime |

빌드할 때만 쓰고 배포하지 않는 것: Microsoft.Windows.CsWin32 (MIT), xUnit (Apache-2.0).

## LGPL 구성요소에 대한 안내 (FFmpeg, libvips)

- Hotzone Converter는 FFmpeg를 **별도 실행 파일로 호출**하고, libvips 등은 **교체 가능한 별도 DLL**로 불러옵니다. 앱 코드와 정적으로 묶지 않습니다.
- 사용자는 `tools\ffmpeg\ffmpeg.exe`나 libvips DLL을 같은 인터페이스의 다른 빌드로 바꿔 쓸 수 있습니다. 이런 교체나 디버깅을 위한 리버스 엔지니어링을 금지하지 않습니다.
- 배포한 바로 그 버전의 소스:
  - FFmpeg: https://github.com/FFmpeg/FFmpeg/tree/release/8.1 (빌드 스크립트·설정: https://github.com/BtbN/FFmpeg-Builds)
  - libvips 패키지: https://github.com/kleisauke/libvips-packaging (v8.18.7)
- **서면 제안**: 위 주소에서 소스를 받을 수 없게 되면, 이 저장소의 Issues로 요청해 주세요. 배포일로부터 3년 동안 해당 버전의 소스 코드를 실비 이하로 제공합니다.

## 쓰지 않는 것 (라이선스·특허 문제 회피)

| 쓰지 않는 것 | 이유 | 대신 |
|---|---|---|
| FFmpeg GPL 빌드, x264, x265 | GPL이라 앱 전체를 GPL로 공개해야 함 | LGPL 빌드만 사용 |
| FFmpeg 안의 H.264·AAC 소프트웨어 인코더 (OpenH264 포함) | 특허 라이선스 문제. Cisco의 OpenH264 특허 보장은 Cisco가 배포한 바이너리에만 적용됨 | Windows 내장 Media Foundation 인코더(`h264_mf`, `aac_mf`). 특허 라이선스는 Windows에 포함 |
| Ghostscript | AGPL | PDF 렌더링은 PDFium |
| PDFsharp 6.3 이상 | AGPL로 바뀜 | 6.2.4(MIT)에 고정 |

MP3 인코더(libmp3lame, LGPL)는 MP3 특허가 2017년에 만료되어 문제가 없습니다. GIF, WAV도 마찬가지입니다.

## 상표·글꼴

- "FFmpeg"는 Fabrice Bellard의 상표입니다. 이 앱은 FFmpeg 프로젝트와 관계가 없고 그 이름을 앱 이름·로고에 쓰지 않습니다.
- 앱 화면의 아이콘은 Windows에 설치된 Segoe Fluent Icons / Segoe MDL2 Assets 글꼴을 그대로 표시할 뿐, 글꼴 파일을 배포하지 않습니다. 앱·트레이 아이콘(로고)은 글꼴을 쓰지 않고 직접 그렸습니다.
