# NTSC Video Studio · 0.0.4

## 0.0.4 내보내기 설정

이전 버전은 0.0.3이며 이번 배포본은 0.0.4입니다. 실행 파일 제목과 파일 버전에도 표시됩니다.

메인 화면의 **내보내기 설정 · vid에 저장**을 누르면 다음 항목을 선택할 수 있습니다. 취소하면 인코딩을 시작하지 않습니다.

| 항목 | 선택 |
|---|---|
| 저장 해상도 | 원본 / 480p / 720p / 1080p / 1440p / 2160p |
| 저장 FPS | 원본 / 23.976 / 24 / 25 / 29.97 / 30 / 50 / 59.94 / 60 / 120 |
| 압축 품질 | 최고 / 높음(기본) / 균형 / 용량 절약 |
| 파일 인코딩 | CPU H.264 / GPU NVIDIA NVENC |

해상도는 각각 긴 변 854/1280/1920/2560/3840 이하로 맞추고 원본보다 확대하지 않습니다. 세로 영상도 비율을 유지하며 짝수 크기로 저장합니다. 16:9 이외의 영상은 p 이름과 실제 짧은 변이 다를 수 있으므로 설정 창의 실제 저장 크기를 확인하세요. 효과 해상도·전체 효과 확대와 저장 해상도는 별개입니다.

기본 FPS는 원본 유지입니다. 다른 FPS를 선택하면 속도와 길이를 유지하면서 시간에 맞춰 프레임을 반복하거나 생략합니다. 반복한 프레임도 NTSC 필터를 다시 적용하므로 30→60fps에서 경계가 교대합니다. 모션 보간은 하지 않습니다. 출력 끝은 최대 한 출력 프레임 정도 반올림될 수 있습니다. 가변 FPS는 모든 프레임을 평균 FPS의 고정 FPS로 저장하며, FPS를 직접 변경하면 선택한 FPS로 재샘플링합니다.

압축 품질 값은 최고 10, 높음 14, 균형 18, 용량 절약 23입니다. CPU는 CRF, NVIDIA는 VBR-CQ이며 두 인코더의 같은 숫자가 완전히 같은 화질·용량을 뜻하지는 않습니다. 무손실 저장은 아닙니다.

메인 화면 오른쪽 위 렌더링은 **NTSC 효과 계산**입니다. 내보내기 창의 파일 인코딩은 **H.264 압축**이며 독립적으로 선택합니다. CPU 효과 + NVIDIA 인코딩 또는 GPU 효과 + CPU 인코딩도 가능합니다. 이전 GPU 렌더링 선택은 실제 파일 인코딩을 NVENC로 바꾸지 않았습니다. 0.0.4는 h264_nvenc를 직접 사용합니다.

NVENC는 해당 기능을 지원하는 NVIDIA GPU와 호환 드라이버가 필요합니다. 선택한 해상도/FPS로 실제 초기화를 먼저 검사하며, 실패하면 NVIDIA 오류와 해결 안내를 표시합니다. CPU로 자동 전환하지 않습니다. AMD/Intel 장치는 CPU 파일 인코딩을 선택해 주세요. Direct3D 효과 렌더링은 기존 호환 장치에서 계속 사용할 수 있습니다. 여러 GPU에서는 효과용 어댑터와 NVENC 인코더가 다른 장치를 사용할 수도 있습니다.

저장 이름은 **vid/영상제목_NTSC.mp4**입니다. 중복 시 _NTSC_2.mp4 순으로 저장합니다. 예전 _NISC 파일을 자동으로 바꾸거나 덮어쓰지 않습니다. 스타일 이름은 **캡처카드 · 아날로그**로 변경했습니다.

명령행 예:

    "NTSC Video Studio.exe" --export "input.mp4" --fps=60 --max-edge=1920 --quality=high --encoder=nvenc
    "NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 512 --fps=source --quality=balanced --encoder=cpu

--fps는 source, 정수, 양의 분수 또는 23.976/29.97/59.94를 받습니다. --max-edge=0은 원본, 그 외 64~8192입니다. --quality는 best/high/balanced/small 또는 1~40 숫자입니다. --renderer=cpu|gpu는 효과 계산, --encoder=cpu|nvenc는 파일 압축입니다.

NVIDIA 설정 근거: [NVIDIA 공식 FFmpeg/NVENC 설명](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/ffmpeg-with-nvidia-gpu/index.html).

source/verify-export.py는 저장 크기, FPS, 프레임 수, 압축 품질 차이, GPU 효과와 CPU 인코딩 조합, NVENC 실행 또는 명시적 오류를 검사합니다. NVIDIA 하드웨어 검증이 불가능한 환경에서는 그 사실을 VALIDATION.txt에 기록합니다.


일반 영상에 MesenCE의 Blargg SNES NTSC 필터를 적용하는 Windows 프로그램입니다. 아날로그 색 번짐, 경계의 색 혼입, 위상에 따라 바뀌는 무늬를 처리하며, Windows Forms 미리보기와 MP4 인코딩을 제공합니다.

[English](README.en.md)

## 실행 환경

- Windows 10/11, 64비트
- .NET Framework 4.8
- `assets` 폴더의 `ffmpeg.exe`, `ffprobe.exe`, `ntsc.dll`, `ntsc.hlsl`

실행에는 Python이나 Visual Studio가 필요하지 않습니다. ROM이나 MesenCE 전체 프로그램도 필요하지 않습니다. 현재 화면과 안내 문구는 한국어입니다.

## 폴더 구조

```text
NTSC-Video-Studio/
├─ NTSC Video Studio.exe
├─ NTSC Video Studio.exe.config
├─ README.ko.md
├─ VALIDATION.txt
├─ assets/
│  ├─ ffmpeg.exe
│  ├─ ffprobe.exe
│  ├─ ntsc.dll
│  └─ ntsc.hlsl
├─ licenses/
└─ source/
   ├─ README.md
   ├─ README.en.md
   ├─ Studio.cs
   ├─ build.ps1
   ├─ verify.py
   ├─ verify-whole.py
   ├─ verify-distribution.py
   ├─ verify-gpu.py
   └─ native/
      ├─ bridge.cpp
      ├─ snes_ntsc.cpp
      ├─ snes_ntsc.h
      ├─ snes_ntsc_impl.h
      ├─ snes_ntsc_config.h
      ├─ gpu.cpp
      ├─ gpu.h
      └─ ntsc.hlsl
```

`vid` 폴더는 처음에는 없습니다. 영상 열기와 미리보기에서는 생성하지 않으며, 인코딩을 시작하면 실행 파일과 같은 폴더 아래에 자동 생성합니다.

```text
NTSC-Video-Studio/vid/영상제목_NTSC.mp4
```

파일명은 입력 영상의 확장자를 제외한 이름에 `_NTSC.mp4`를 붙입니다. 같은 이름이 존재하면 `_NTSC_2.mp4`, `_NTSC_3.mp4` 순으로 저장해 기존 파일을 보존합니다. `NISC`는 이 프로그램의 저장 접미사입니다.

`assets`와 `vid`의 기준은 실행 파일 위치입니다. 입력 영상 위치나 현재 작업 폴더에 영향을 받지 않습니다. DLL은 `assets`의 절대 경로로 미리 불러옵니다.

## 사용 방법

1. 배포 ZIP 전체를 압축 해제하고 `NTSC Video Studio.exe`를 실행합니다.
2. **영상 열기**에서 SDR 영상을 선택합니다.
3. 스타일, 효과 해상도, 전체 효과 확대와 슬라이더를 조절합니다.
4. **미리보기**를 눌러 선택한 장면의 위상 변화를 확인합니다. 설정을 변경하면 다시 눌러 갱신합니다.
5. **내보내기 설정 · vid에 저장**에서 설정을 고르고 인코딩 시작을 누르면 자동 저장합니다.

취소하거나 처리에 실패하면 미완성 임시 영상은 제거합니다. 생성된 `vid` 폴더와 이미 완료된 영상은 유지합니다.

## 소스 구성

| 파일 | 역할 |
|---|---|
| `Studio.cs` | Windows Forms, 영상 정보 확인, FFmpeg 입출력, 프레임 시계, 미리보기, 저장과 취소 |
| `native/bridge.cpp` | 네이티브 DLL 인터페이스, RGB→BGR555, 두 위상 처리, 경계 강조, 보간과 스캔라인 |
| `native/snes_ntsc*` | 첨부된 MesenCE의 Blargg 필터 구현. `snes_ntsc.cpp`의 Mesen 전용 `pch.h` include만 제거 |
| `build.ps1` | C++ DLL과 C# 실행 파일 빌드 |
| `verify*.py` | 합성 영상과 별도 배포 폴더를 이용한 검증 |

## 처리 방식

```text
FFmpeg 디코딩 → 작업 해상도 변환 → RGB24 → BGR555
→ Blargg 두 위상 계산 → 실제 위상 차이 강조
→ 행 복제·선택적 스캔라인 → 원본 표시 크기로 확대
→ H.264/AAC MP4
```

오른쪽 위에서 CPU/GPU 렌더링을 선택할 수 있으며 기본값은 CPU입니다. GPU는 Direct3D 11 컴퓨트 셰이더로 BGR555 변환, Blargg 룩업 평가, 두 위상 처리, 경계 강조와 스캔라인을 계산합니다. 룩업 초기화와 프레임 전송은 CPU를 사용하며, 최종 H.264 인코딩은 내보내기 설정에서 CPU libx264 또는 NVIDIA NVENC를 선택합니다. 임의의 무지개 띠를 더하지 않고, Blargg가 만드는 위상 0과 1을 교대합니다. 경계 강조는 현재 위상 값 `c`와 반대 위상 값 `a`의 차이를 다음과 같이 증폭하는 추가 기능입니다.

```text
c + 1.5 × 경계 강도 × 강조 설정 × (c - a)
```

RGB 결과는 0~255로 제한합니다. 위상 차이가 없는 부분에는 이 처리로 새로운 무늬를 만들지 않습니다. 기본 스타일은 **캡처카드 · 아날로그**이며, 제공된 영상의 느낌을 참고한 설정입니다. 특정 캡처카드의 회로나 디코더를 정확히 복원한 것은 아닙니다.

### 전체 효과 확대

효과 해상도는 가로 기준이며 기본값은 512입니다. **자동** 확대는 기준 폭이 512보다 크면 전체 필터를 가로 512 수준에서 계산한 뒤 결과를 확대합니다. 색 번짐, 경계 무늬, 깜박임과 스캔라인이 함께 커집니다.

수동 배율은 기준 폭을 배율로 나눈 크기에서 처리합니다. GUI는 1~32배의 선택 항목을 제공합니다. 1배는 확대 없이 지정한 효과 해상도를 사용합니다. Blargg 처리 묶음에 맞춰 내부 폭을 `3n+1`로 조정합니다.

저장 해상도는 원본 표시 크기를 유지하지만, 낮은 작업 해상도로 줄어든 디테일은 복원되지 않습니다. 입력의 BGR555 양자화도 색 정밀도에 영향을 줍니다.

### 프레임 속도

- 기본 원본 유지 설정에서는 원본 평균 FPS와 프레임 수를 유지합니다. 24/25/30fps는 그대로 저장하고, 23.976/29.97fps도 그대로 유지합니다. 60fps로 늘리기 위한 프레임 반복은 하지 않습니다.
- 60/59.94fps보다 낮은 입력에서는 원본 프레임마다 위상 0↔1을 교대합니다. 30fps 영상의 A→B→A 주기는 15Hz이며, 무늬가 멈추지 않도록 합니다.
- 60/59.94fps 이상에서는 기존 위상 시계를 유지합니다. 120fps에서는 두 프레임마다 위상이 바뀝니다.
- 가변 FPS 입력도 프레임을 추가하거나 삭제하지 않고 평균 FPS의 고정 FPS로 저장합니다. 각 프레임의 원래 표시 시간은 보존하지 않으며, 총 길이는 출력 프레임 단위로 반올림될 수 있습니다.

미리보기는 선택한 한 장면의 두 위상을 재생합니다. 원본 영상 전체를 재생하는 플레이어는 아닙니다.

## 빌드

Visual Studio 2022의 C++ 개발 도구와 C# 컴파일러가 필요합니다. 스크립트의 기본 경로는 Community 설치입니다. 다른 설치를 사용하면 `build.ps1`의 `$vsBase`를 수정하세요.

프로그램 최상위 폴더에서 PowerShell로 실행합니다.

```powershell
& .\source\build.ps1
```

빌드 결과는 최상위 폴더의 `NTSC Video Studio.exe`와 `assets\ntsc.dll`입니다. C++ 런타임은 정적으로 연결합니다. 빌드 스크립트는 FFmpeg를 다운로드하거나 빌드하지 않으므로 해당 실행 파일과 라이선스는 별도로 유지해야 합니다.

## 명령행

사용자용 자동 저장은 GUI와 같은 `vid` 규칙을 사용합니다.

```powershell
& ".\NTSC Video Studio.exe" --export "input.mp4"
```

개발·검증용 변환은 출력 위치와 처리 폭을 지정할 수 있습니다.

```powershell
& ".\NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 512
& ".\NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 1920 --effect-scale=4
& ".\NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 7680 --effect-scale=1
```

`--effect-scale=0`은 자동, `1`은 확대 없음, `2~32`는 수동 배율입니다. 옵션을 사용할 때는 예시처럼 폭을 먼저 지정하세요. `--convert`는 결과 정보 파일 `output.mp4.result.txt`도 생성하며, `--export`와 GUI는 영상만 저장합니다. 명령행 실패 로그는 실행 파일 폴더의 `last-error.txt`입니다.

## 검증

Python 3 표준 라이브러리만 사용합니다. 프로그램 최상위 폴더에서 실행하세요.

```powershell
python .\source\verify.py
python .\source\verify-whole.py
python .\source\verify-distribution.py
```

- `verify.py`: 변환, 오디오, 화면 비율과 회전, 위상 시계, 미리보기, 취소, 파일 보호, 고해상도 저장.
- `verify-whole.py`: 저해상도 결과와 HD 결과를 같은 표시 크기로 비교하고 색 변화·깜박임 검사.
- `verify-distribution.py`: 다른 작업 폴더와 한글 경로에서 실행, `assets` 로딩, `vid` 지연 생성, 자동 파일명과 중복 처리 검사.

결과는 `VALIDATION.txt`에 기록합니다. 8K 전체 격자 검사가 가용 메모리 부족으로 실패하면 해당 검증 스크립트는 이를 표시하고 자동 확대 방식의 8K 출력을 검사합니다.

## 지원 범위

- SDR 영상용이며 HDR은 지원하지 않습니다.
- 출력: MP4, H.264 (기본 CPU CRF 14; 품질·인코더 선택 가능), yuv420p, 첫 오디오 트랙의 AAC 192kbps 재인코딩.
- 픽셀 종횡비, 회전과 오디오 시작 시각 차이를 반영합니다. 홀수 출력 크기는 짝수로 올립니다.
- 입력 표시 크기는 가로·세로 각각 최대 8192입니다. 작업 격자는 각 변 8192, 전체 33,554,432픽셀까지 제한합니다.
- 큰 중간 프레임은 행 복제를 생략하고 가로 보간을 먼저 수행해 전송 크기를 줄입니다. 16,777,216픽셀보다 큰 CPU 출력은 메모리 사용을 줄이는 인코딩 설정을 사용합니다.
- 4K/8K의 전체 격자 처리는 시간과 메모리를 많이 사용합니다.
- 가변 fps는 고정 fps로 변환합니다. 추가 오디오, 자막, 챕터와 인터레이스 필드 보존은 지원하지 않습니다. 인터레이스 입력은 먼저 디인터레이스하세요.

## 출처와 라이선스

- 프로그램 및 `bridge.cpp`: GPL-3.0-or-later.
- Blargg `snes_ntsc 0.2.2`: Shay Green, LGPL-2.1-or-later. MesenCE 첨부 소스에서 가져왔습니다.
- 포함 FFmpeg: Gyan의 FFmpeg 9.0.2 essentials 빌드. 빌드 정보와 라이선스는 `licenses` 폴더를 참고하세요.

원본 필터의 저작권 고지와 함께 소스 및 라이선스를 배포 패키지에 포함합니다.

## CPU / GPU 선택

오른쪽 위에서 CPU 또는 GPU · Direct3D 11을 선택한 뒤 미리보기를 갱신합니다. 미리보기와 저장에 함께 적용됩니다. GPU는 실제 하드웨어 어댑터와 Direct3D feature level 11.0 이상을 요구합니다. 처음 사용 가능한 호환 어댑터를 선택해 이름을 표시합니다. 소프트웨어 WARP 렌더링으로 GPU를 대신하지 않으며 GPU 처리 실패도 안내합니다. 성능은 해상도·전송·장치에 따라 달라집니다.

assets/ntsc.hlsl은 Windows D3DCompiler로 실행 시 컴파일합니다. 부동소수점 계산 차이로 CPU와 GPU의 색 값에 작은 차이가 날 수 있습니다. 이 선택은 NTSC 필터 렌더링을 바꾸며, H.264 저장 인코더는 내보내기 설정에서 별도로 선택합니다.

```powershell
& ".\NTSC Video Studio.exe" --export "input.mp4" --renderer=gpu
& ".\NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 512 --renderer=gpu
python .\source\verify-gpu.py
```

--renderer=cpu 또는 옵션 생략 시 CPU를 사용합니다. verify-gpu.py는 실제 GPU와 CPU 결과를 비교하고 미리보기·저장·취소·셰이더 오류를 검사합니다. 호환 장치가 없으면 검사 불가 이유를 출력합니다. 빌드는 d3d11.lib, dxgi.lib, d3dcompiler.lib를 링크하고 native/ntsc.hlsl을 assets로 복사합니다. gpu.cpp/gpu.h는 GPL-3.0-or-later이며, 셰이더의 룩업 평가는 원본 Blargg 매크로를 각색하고 출처를 표시했습니다.
