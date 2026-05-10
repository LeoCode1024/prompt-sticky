$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Src = Join-Path $Root "src\PromptSticky.cs"
$IconResource = Join-Path $Root "assets\sticky-icon.png"
$Dist = Join-Path $Root "dist"
$Out = Join-Path $Dist "PromptSticky.exe"
$Framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$Csc = Join-Path $Framework "csc.exe"

$PresentationFramework = Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll"
$PresentationCore = Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll"
$WindowsBase = Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll"
$SystemXaml = Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089\System.Xaml.dll"

New-Item -ItemType Directory -Force -Path $Dist | Out-Null

& $Csc `
  /nologo `
  /target:winexe `
  /platform:x64 `
  /optimize+ `
  /out:$Out `
  /reference:"$Framework\System.dll" `
  /reference:"$Framework\System.Core.dll" `
  /reference:"$Framework\System.Xml.dll" `
  /reference:"$PresentationFramework" `
  /reference:"$PresentationCore" `
  /reference:"$WindowsBase" `
  /reference:"$SystemXaml" `
  /resource:"$IconResource",PromptStickyTool.Assets.StickyIcon.png `
  $Src

if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

Write-Host "Built $Out"
