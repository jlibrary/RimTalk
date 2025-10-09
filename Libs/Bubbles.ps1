$Owner = "Jaxe-Dev"
$Repo = "Bubbles"
$DLLName = "Bubbles.dll"

$ApiBaseUrl = "https://api.github.com/repos/$Owner/$Repo"

$ReadmeData = Invoke-RestMethod -Uri "$ApiBaseUrl/readme"

$ReadmeContent = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($ReadmeData.content))

Write-Host $ReadmeContent
Write-Host "==============================================="
Write-Host "Interaction Bubbles by $Owner."
Write-Host "You can check project at 'https://github.com/$Owner/$Repo'."
Write-Host "==============================================="

try {
  $LatestRelease = Invoke-RestMethod -Uri "$ApiBaseUrl/releases/latest"

  $ReleaseTagName = $LatestRelease.tag_name
  Write-Host "Latest release version: $ReleaseTagName"

  # First asset
  $Asset = $LatestRelease.assets | Select-Object -First 1
    
  if (-not $Asset) {
    Write-Host "Error: There's no github release asset!"
    return
  }

  $DownloadUrl = $Asset.browser_download_url
  $FileName = $Asset.name
  $OutputFile = Join-Path -Path $PSScriptRoot -ChildPath $FileName

  Invoke-WebRequest -Uri $DownloadUrl -OutFile $OutputFile -UseBasicParsing

  Write-Host "Fetch complete!"
  Write-Host "==============================================="
  Write-Host "Extracting..."
  
  $ExtractPath = Join-Path -Path $PSScriptRoot -ChildPath "Bubbles"
  Expand-Archive $OutputFile -DestinationPath $ExtractPath
  
  $DLLPath = Join-Path -Path $ExtractPath -ChildPath "Assemblies/$DLLName"
  Copy-Item -Path $DLLPath -Destination "$PSScriptRoot/$DLLName" -Force

  Remove-Item -Path $OutputFile
  Remove-Item -Path $ExtractPath -Recurse

  Write-Host "Done!"

} catch {
  Write-Host "Error: There's an error while fetching github release!"
  $_.Exception.Message
}
