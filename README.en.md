# NTSC Video Studio

A Windows application that applies the Blargg SNES NTSC filter from MesenCE to regular videos. It processes analog color bleeding, edge cross-talk, and phase-dependent artifacts, with a Windows Forms preview and MP4 encoding.

[한국어](README.md)

## Runtime requirements

- Windows 10/11, 64-bit
- .NET Framework 4.8
- `ffmpeg.exe`, `ffprobe.exe`, and `ntsc.dll` in `assets`

Python and Visual Studio are not required to run the application. No ROM or full MesenCE installation is needed. The current interface and messages are in Korean.

## Directory layout

```text
NTSC-Video-Studio/
├─ NTSC Video Studio.exe
├─ NTSC Video Studio.exe.config
├─ README.ko.md
├─ VALIDATION.txt
├─ assets/
│  ├─ ffmpeg.exe
│  ├─ ffprobe.exe
│  └─ ntsc.dll
├─ licenses/
└─ source/
   ├─ README.md
   ├─ README.en.md
   ├─ Studio.cs
   ├─ build.ps1
   ├─ verify.py
   ├─ verify-whole.py
   ├─ verify-distribution.py
   └─ native/
      ├─ bridge.cpp
      ├─ snes_ntsc.cpp
      ├─ snes_ntsc.h
      ├─ snes_ntsc_impl.h
      └─ snes_ntsc_config.h
```

The `vid` directory does not initially exist. Opening a video or generating a preview does not create it. Starting an encode creates it beside the executable.

```text
NTSC-Video-Studio/vid/video_title_NISC.mp4
```

The filename uses the input name without its extension, followed by `_NISC.mp4`. Existing files are preserved: subsequent exports use `_NISC_2.mp4`, `_NISC_3.mp4`, and so on. `NISC` is the application's output suffix.

Both `assets` and `vid` are resolved relative to the executable, independent of the input location or current working directory. The native DLL is preloaded using its absolute path in `assets`.

## Usage

1. Extract the entire release ZIP and run `NTSC Video Studio.exe`.
2. Select an SDR video with **영상 열기** (Open video).
3. Adjust the style, effect resolution, whole-effect magnification, and sliders.
4. Select **미리보기** (Preview) to inspect phase changes at the selected position. Refresh the preview after changing settings.
5. Select **인코딩 시작 · vid에 저장** (Start encoding · Save to vid). Export starts without a save-location dialog.

Cancellation or failure removes the unfinished temporary video. The created `vid` directory and completed videos remain.

## Source structure

| File | Responsibility |
|---|---|
| `Studio.cs` | Windows Forms, video probing, FFmpeg I/O, frame timing, preview, export, and cancellation |
| `native/bridge.cpp` | Native DLL interface, RGB-to-BGR555 conversion, two-phase processing, edge enhancement, interpolation, and scanlines |
| `native/snes_ntsc*` | Blargg implementation from the supplied MesenCE source. Only the Mesen-specific `pch.h` include was removed from `snes_ntsc.cpp` |
| `build.ps1` | Builds the C++ DLL and C# executable |
| `verify*.py` | Validation using synthetic videos and a separate portable installation |

## Processing pipeline

```text
FFmpeg decoding → working resolution → RGB24 → BGR555
→ two Blargg phases → enhancement of actual phase differences
→ row duplication / optional scanlines → original display dimensions
→ H.264/AAC MP4
```

The filter runs on the CPU without a GPU shader. It alternates Blargg phases 0 and 1 rather than adding synthetic rainbow stripes. Additional edge enhancement amplifies the difference between the current phase value `c` and the opposite phase value `a`:

```text
c + 1.5 × edge strength × enhancement setting × (c - a)
```

RGB values are clamped to 0–255. This enhancement introduces no new pattern where the phases are identical. The default **캡처카드 · 예시 참고** (Capture card · Reference-inspired) style takes visual inspiration from the supplied sample. It does not reconstruct a particular capture card's circuitry or decoder exactly.

### Whole-effect magnification

Effect resolution is specified by width, with a default of 512. **Automatic** magnification calculates the entire filter at approximately 512 pixels wide when the selected reference width exceeds 512, then enlarges the result. Color bleeding, edge artifacts, flicker, and scanlines scale together.

Manual magnification divides the reference width by the selected factor. The GUI offers factors from 1× through 32×. A factor of 1 uses the specified effect resolution without magnification. Internal widths are adjusted to `3n+1` for Blargg's processing chunks.

Output dimensions retain the original display size, but detail lost at a lower working resolution is not recovered. BGR555 quantization also limits color precision.

### Frame timing

- Phases alternate on a 60 or 60000/1001 fps clock.
- 30 fps input produces 60 fps output; 29.97 fps input produces 59.94 fps output.
- 24/25 fps input produces 60 fps output; 23.976 fps input produces 59.94 fps output.
- Faster inputs retain their average frame rate, with phase changes scheduled by time. At 120 fps, the phase changes every two frames.
- Duplicated source frames are filtered independently for each output frame.
- An NTSC-family nominal frame rate selects the 59.94 clock even when dropped frames lower the measured average rate.

The preview animates the two phases of one selected scene. It is not a full video player.

## Building

Visual Studio 2022 C++ development tools and the C# compiler are required. The script defaults to the Community installation path. Change `$vsBase` in `build.ps1` for another installation.

Run PowerShell from the application root:

```powershell
& .\source\build.ps1
```

The build produces `NTSC Video Studio.exe` in the application root and `assets\ntsc.dll`. The C++ runtime is statically linked. The script does not download or build FFmpeg; its binaries and license files must be supplied separately.

## Command line

Automatic export follows the same `vid` rules as the GUI:

```powershell
& ".\NTSC Video Studio.exe" --export "input.mp4"
```

Developer/test conversion supports an explicit output path and working width:

```powershell
& ".\NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 512
& ".\NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 1920 --effect-scale=4
& ".\NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 7680 --effect-scale=1
```

`--effect-scale=0` selects automatic magnification, `1` disables magnification, and `2–32` selects a manual factor. Specify the width before options, as shown. `--convert` also creates `output.mp4.result.txt`; `--export` and the GUI save only the video. Command-line failures are logged to `last-error.txt` beside the executable.

## Validation

The scripts use only the Python 3 standard library. Run them from the application root:

```powershell
python .\source\verify.py
python .\source\verify-whole.py
python .\source\verify-distribution.py
```

- `verify.py`: conversion, audio, aspect ratio and rotation, phase timing, preview, cancellation, file preservation, and high-resolution export.
- `verify-whole.py`: compares low-resolution and HD results at a common display size and checks color variation and flicker.
- `verify-distribution.py`: checks an unrelated working directory, Korean paths, asset loading, delayed `vid` creation, automatic naming, and duplicate handling.

Results are recorded in `VALIDATION.txt`. If the full-grid 8K check fails because available memory is insufficient, the script reports that limitation and tests 8K output using automatic whole-effect magnification instead.

## Supported scope

- SDR video only; HDR is unsupported.
- Output: MP4, H.264 CRF 14, yuv420p, with the first audio track re-encoded as AAC at 192 kbps.
- Pixel aspect ratio, rotation, and audio start offsets are handled. Odd output dimensions are rounded up to even values.
- Input display dimensions are limited to 8192 on either axis. Working grids are limited to 8192 per axis and 33,554,432 total pixels.
- Large intermediate frames omit row duplication and interpolate horizontally before transfer. Outputs above 16,777,216 pixels use encoder settings that reduce memory usage.
- Full-grid 4K/8K processing requires considerable time and memory.
- Variable frame rates become constant frame rates. Additional audio tracks, subtitles, chapters, and interlaced-field preservation are unsupported. Deinterlace interlaced inputs first.

## Credits and licenses

- Application and `bridge.cpp`: GPL-3.0-or-later.
- Blargg `snes_ntsc 0.2.2`: Shay Green, LGPL-2.1-or-later, obtained from the supplied MesenCE source.
- Bundled FFmpeg: Gyan's FFmpeg 9.0.2 essentials build. See `licenses` for build information and license files.

Source code, license files, and the original filter's copyright notices are included in the distribution package.
