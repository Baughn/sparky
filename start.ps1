
# Construct Vintage Story path from AppData
$appDataPath = [System.Environment]::GetEnvironmentVariable("APPDATA")
$vintageStoryPath = Join-Path $appDataPath "Vintagestory\Vintagestory.exe"

# Set the environment variable for Vintage Story in current session and user scope
$vintageStoryDir = Split-Path $vintageStoryPath
$env:VINTAGE_STORY = $vintageStoryDir
[System.Environment]::SetEnvironmentVariable("VINTAGE_STORY", $vintageStoryDir, "User")

Write-Host "VINTAGE_STORY path set to: $vintageStoryDir" -ForegroundColor Green

# Build the project in Release configuration
dotnet build -c Release

# Check if build was successful before proceeding
if ($LASTEXITCODE -eq 0) {
    # Path to the mod directory relative to the current script directory
    $modPath = Join-Path $PSScriptRoot "src\mod\bin\"

    # Run Vintage Story with the mod path
    & $vintageStoryPath --addModPath $modPath -o auto
} else {
    Write-Host "Build failed. Skipping Vintage Story launch." -ForegroundColor Red
    exit 1
}
