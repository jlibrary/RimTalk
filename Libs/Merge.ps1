# RimTalk Merge Script
# 
# This script triggers the build process which includes the ILRepack merging step.
# The merging logic is defined in the 'MergeDlls' target within RimTalk.csproj.
# This ensures that Scriban.dll is internalized into RimTalk.dll automatically.

Write-Host "Starting RimTalk Build and Merge Process..."

# Execute the build (Release configuration recommended for distribution)
dotnet build -c Release

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build and Merge completed successfully."
    Write-Host "Merged assembly can be found in the output directory (e.g., 1.6/Assemblies/RimTalk.dll)."
} else {
    Write-Host "Build failed. Please check the errors above." -ForegroundColor Red
    exit 1
}