$ErrorActionPreference='Stop'
$appRoot=Split-Path $PSScriptRoot -Parent
$assetRoot=Join-Path $appRoot 'assets'
New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'native\ntsc.hlsl') -Destination (Join-Path $assetRoot 'ntsc.hlsl') -Force
$vsBase='C:\Program Files\Microsoft Visual Studio\2022\Community'
$vcvars=Join-Path $vsBase 'VC\Auxiliary\Build\vcvars64.bat'
$csc=Join-Path $vsBase 'MSBuild\Current\Bin\Roslyn\csc.exe'
if(!(Test-Path -LiteralPath $vcvars)) { throw 'Visual Studio C++ build tools are required to rebuild.' }
Push-Location $PSScriptRoot
try {
 $buildCmd='call "'+$vcvars+'" >nul && cl /nologo /O2 /EHsc /MT /LD native\bridge.cpp native\snes_ntsc.cpp native\gpu.cpp /Fe:"'+$assetRoot+'\ntsc.dll" /link /INCREMENTAL:NO d3d11.lib dxgi.lib d3dcompiler.lib'
 & cmd.exe /d /c $buildCmd
 if($LASTEXITCODE -ne 0){throw 'Native build failed'}
 & $csc /nologo /target:winexe /platform:x64 /optimize+ /out:"$appRoot\NTSC Video Studio.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll Studio.cs
 if($LASTEXITCODE -ne 0){throw 'Application build failed'}
} finally { Pop-Location }
