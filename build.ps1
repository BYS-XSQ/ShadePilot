$ErrorActionPreference = "Stop"
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = Join-Path $projectDir "bin"
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ `
  /out:"$outputDir\ShadePilot.exe" `
  /win32icon:"$projectDir\assets\shadepilot-icon.ico" `
  /win32manifest:"$projectDir\app.manifest" `
  /reference:System.dll `
  /reference:System.Core.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  /reference:System.Runtime.Serialization.dll `
  "$projectDir\DisplayPreset.cs"

if ($LASTEXITCODE -ne 0) { throw "编译失败，退出代码 $LASTEXITCODE" }
Write-Host "Built: $outputDir\ShadePilot.exe"
