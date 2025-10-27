
param(
  [string]$Owner = "Jaxe-Dev",
  [string]$Repo  = "Bubbles"
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path   # …\RimTalk\Lib
$DestDll   = Join-Path $ScriptDir "Bubbles.dll"

Write-Host "[Bubbles] Target => $DestDll"

if (Test-Path $DestDll) {
  Write-Host "[Bubbles] Bubbles.dll already exists. Skip."
  exit 0
}

$Candidates = @(
  (Join-Path $ScriptDir "..\Bubbles\source\+\Legacy\1.5\Assemblies\Bubbles.dll"),
  (Join-Path $ScriptDir "..\Bubbles\bin\Release\net48\Bubbles.dll"),
  (Join-Path $ScriptDir "..\Bubbles\bin\Release\Bubbles.dll"),
  (Join-Path $ScriptDir "..\Bubbles.dll")
) | ForEach-Object { Resolve-Path -LiteralPath $_ -ErrorAction SilentlyContinue } | ForEach-Object { $_.Path }

foreach ($src in $Candidates) {
  if (Test-Path $src) {
    Write-Host "[Bubbles] Found local DLL: $src"
    Copy-Item $src $DestDll -Force
    Write-Host "[Bubbles] Copied to $DestDll"
    exit 0
  }
}

function GetJson($url) {
  Invoke-RestMethod -Uri $url -Headers @{ "User-Agent" = "RimTalk-Bubbles-Fetcher" }
}

$ApiBase = "https://api.github.com/repos/$Owner/$Repo"
Write-Host "[Bubbles] Try fetch latest release from $ApiBase ..."
$latest  = GetJson "$ApiBase/releases/latest"
$asset   = $latest.assets | Where-Object { $_.name -match '\.zip$' } | Select-Object -First 1
if (-not $asset) { throw "[Bubbles] No zip asset found in latest release." }

$tmpZip   = Join-Path $env:TEMP $asset.name
$tmpDir   = Join-Path $env:TEMP ("Bubbles_" + ([Guid]::NewGuid().ToString()))
New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null

Write-Host "[Bubbles] Downloading: $($asset.browser_download_url)"
Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tmpZip -Headers @{ "User-Agent" = "RimTalk-Bubbles-Fetcher" }

Write-Host "[Bubbles] Extracting to: $tmpDir"
Expand-Archive -LiteralPath $tmpZip -DestinationPath $tmpDir -Force

$extractedDll = Get-ChildItem -LiteralPath $tmpDir -Recurse -Filter "Bubbles.dll" | Where-Object { $_.FullName -match "Assemblies\\Bubbles\.dll$" } | Select-Object -First 1
if (-not $extractedDll) { throw "[Bubbles] Extracted zip but did not find Assemblies\Bubbles.dll." }

Copy-Item $extractedDll.FullName $DestDll -Force
Write-Host "[Bubbles] Placed at: $DestDll"

Remove-Item $tmpZip -Force
Remove-Item $tmpDir -Recurse -Force
