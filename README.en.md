# NTSC Video Studio · 0.0.4

## 0.0.4 export settings

This release is 0.0.4; the previous release was 0.0.3. The window title and executable version also identify 0.0.4.

The export button opens a settings dialog with output resolution (source/480p/720p/1080p/1440p/2160p), FPS (source/23.976/24/25/29.97/30/50/59.94/60/120), compression quality (best/high/balanced/small), and encoder (CPU H.264/NVIDIA NVENC). Cancel starts no encoding.

Resolution limits the long edge to 854/1280/1920/2560/3840 pixels, never enlarges the source, preserves aspect ratio, and rounds to even dimensions. Portrait output is supported. Check the actual dimensions in the dialog for non-16:9 sources. Output resolution is independent of the NTSC effect grid.

Source FPS is the default. An explicit FPS resamples frames by duplication/dropping while preserving playback speed and approximate duration. Every duplicated frame is filtered independently, so converting 30 to 60 FPS retains phase flicker. No motion interpolation is performed. Duration may round by one output frame. VFR sources are converted to constant average FPS when source FPS is selected; individual original frame timestamps are not preserved.

Quality values are 10/14/18/23 (high is the default). CPU uses CRF and NVENC uses VBR-CQ; identical values are not equivalent across encoders. Output is lossy.

The main renderer selector controls NTSC effect computation. The export encoder selector controls H.264 compression independently. CPU effects with NVENC and GPU effects with CPU encoding are supported. Earlier GPU rendering did not enable hardware video encoding. Version 0.0.4 explicitly uses h264_nvenc.

NVENC requires a supported NVIDIA GPU and compatible driver. An actual one-frame initialization check runs at the selected output dimensions/FPS before conversion. Failure reports NVIDIA's error; it never silently falls back to CPU. AMD/Intel users should choose CPU encoding; existing Direct3D effect rendering remains available. The effect renderer and NVENC may use different adapters on a multi-GPU system.

Files are named vid/title_NTSC.mp4, then title_NTSC_2.mp4 on collision. Existing _NISC files are not renamed or overwritten. The Korean capture-card preset is now 캡처카드 · 아날로그.

    "NTSC Video Studio.exe" --export "input.mp4" --fps=60 --max-edge=1920 --quality=high --encoder=nvenc
    "NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 512 --fps=source --quality=balanced --encoder=cpu

--fps accepts source, a positive integer/rational or 23.976/29.97/59.94. --max-edge accepts 0 (source) or 64..8192. --quality accepts best/high/balanced/small or 1..40. --renderer=cpu|gpu selects effects; --encoder=cpu|nvenc selects compression.

NVENC reference: [NVIDIA's official FFmpeg guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/ffmpeg-with-nvidia-gpu/index.html). source/verify-export.py checks output dimensions, rate/count, quality differences, GPU effects with CPU encoding, and actual NVENC encoding or explicit failure. Hardware verification limitations are recorded in VALIDATION.txt.


A Windows application that applies the Blargg SNES NTSC filter from MesenCE to regular videos. It processes analog color bleeding, edge cross-talk, and phase-dependent artifacts, with a Windows Forms preview and MP4 encoding.

[한국어](README.md)

## Runtime requirements

- Windows 10/11, 64-bit
- .NET Framework 4.8
- `ffmpeg.exe`, `ffprobe.exe`, `ntsc.dll`, and `ntsc.hlsl` in `assets`

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

The `vid` directory does not initially exist. Opening a video or generating a preview does not create it. Starting an encode creates it beside the executable.

```text
NTSC-Video-Studio/vid/video_title_NTSC.mp4
```

The filename uses the input name without its extension, followed by `_NTSC.mp4`. Existing files are preserved: subsequent exports use `_NTSC_2.mp4`, `_NTSC_3.mp4`, and so on. `NISC` is the application's output suffix.

Both `assets` and `vid` are resolved relative to the executable, independent of the input location or current working directory. The native DLL is preloaded using its absolute path in `assets`.

## Usage

1. Extract the entire release ZIP and run `NTSC Video Studio.exe`.
2. Select an SDR video with **영상 열기** (Open video).
3. Adjust the style, effect resolution, whole-effect magnification, and sliders.
4. Select **미리보기** (Preview) to inspect phase changes at the selected position. Refresh the preview after changing settings.
5. Select **내보내기 설정 · vid에 저장** (Export settings · Save to vid), choose settings, then start encoding.

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

The renderer selector at the top right offers CPU and GPU modes; CPU is the default. GPU mode runs BGR555 conversion, Blargg lookup evaluation, phase processing, edge enhancement, and scanlines in a Direct3D 11 compute shader. Lookup initialization and frame transfers remain on the CPU. Final H.264 encoding uses the independently selected CPU libx264 or NVIDIA NVENC encoder. It alternates Blargg phases 0 and 1 rather than adding synthetic rainbow stripes. Additional edge enhancement amplifies the difference between the current phase value `c` and the opposite phase value `a`:

```text
c + 1.5 × edge strength × enhancement setting × (c - a)
```

RGB values are clamped to 0–255. This enhancement introduces no new pattern where the phases are identical. The default **캡처카드 · 아날로그** (Capture card · Analog) style takes visual inspiration from the supplied sample. It does not reconstruct a particular capture card's circuitry or decoder exactly.

### Whole-effect magnification

Effect resolution is specified by width, with a default of 512. **Automatic** magnification calculates the entire filter at approximately 512 pixels wide when the selected reference width exceeds 512, then enlarges the result. Color bleeding, edge artifacts, flicker, and scanlines scale together.

Manual magnification divides the reference width by the selected factor. The GUI offers factors from 1× through 32×. A factor of 1 uses the specified effect resolution without magnification. Internal widths are adjusted to `3n+1` for Blargg's processing chunks.

Output dimensions retain the original display size, but detail lost at a lower working resolution is not recovered. BGR555 quantization also limits color precision.

### Frame timing

- By default, preserve the source average frame rate and frame count, including 24/25/30 and 23.976/29.97 fps. No frames are repeated to increase the output to 60 fps.
- Below the 60/59.94 fps clock, alternate phase 0/1 on every source frame to prevent frozen patterns. A complete A→B→A cycle at 30 fps is 15 Hz.
- At or above that clock, retain the existing phase timing. At 120 fps, the phase changes every two frames.
- Variable-frame-rate inputs retain all decoded frames but are saved at a constant average frame rate. Individual original frame timestamps are not preserved, and total duration may be rounded to the output frame interval.

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
- Output: MP4, H.264 (CPU CRF 14 by default; quality/encoder selectable), yuv420p, with the first audio track re-encoded as AAC at 192 kbps.
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

## CPU / GPU selection

Select CPU or GPU · Direct3D 11 at the top right, then refresh the preview. This choice applies to preview and export. GPU mode requires a real hardware adapter supporting Direct3D feature level 11.0 or higher. It selects the first compatible adapter and shows its name. WARP software rendering is not substituted for hardware, and GPU processing failures are reported rather than silently switching to CPU. GPU speed depends on resolution, transfers, and hardware.

The assets/ntsc.hlsl shader is compiled at runtime using the Windows D3DCompiler. Small CPU/GPU color differences may result from floating-point arithmetic. This selector controls NTSC rendering; the export dialog independently selects CPU or NVIDIA NVENC encoding.

```powershell
& ".\NTSC Video Studio.exe" --export "input.mp4" --renderer=gpu
& ".\NTSC Video Studio.exe" --convert "input.mp4" "output.mp4" 512 --renderer=gpu
python .\source\verify-gpu.py
```

Use --renderer=cpu or omit the option for CPU mode. verify-gpu.py compares actual hardware GPU output against CPU output and checks preview, export, cancellation, and explicit shader errors. It reports unavailability when no compatible hardware is present. The build links d3d11.lib, dxgi.lib, and d3dcompiler.lib and copies native/ntsc.hlsl to assets. The new gpu.cpp/gpu.h backend is GPL-3.0-or-later; the shader evaluator adapts the supplied Blargg macros, with attribution retained.
