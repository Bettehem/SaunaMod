param(
    [string]$ValheimPath = "C:\Games\Valheim",
    [string]$RepoPath = "C:\ValheimMods\GitHub"
)

$ErrorActionPreference = "Stop"

$Managed = Join-Path $ValheimPath "valheim_Data\Managed"
$Assembly = Join-Path $Managed "publicized_assemblies\assembly_valheim_publicized.dll"
$Output = Join-Path $RepoPath "ValheimDecompiled"

Write-Host "Valheim path: $ValheimPath"
Write-Host "Repository:   $RepoPath"
Write-Host "Assembly:     $Assembly"
Write-Host "Output:       $Output"
Write-Host ""

if (-not (Test-Path $RepoPath)) {
    throw "Repository path does not exist: $RepoPath"
}

if (-not (Test-Path $Assembly)) {
    throw "Valheim publicized assembly was not found: $Assembly"
}

$Ilspy = Get-Command ilspycmd -ErrorAction SilentlyContinue
if (-not $Ilspy) {
    Write-Host "ilspycmd was not found."
    Write-Host "Install it with:"
    Write-Host "  dotnet tool install --global ilspycmd"
    throw "ilspycmd is required."
}

if (Test-Path $Output) {
    Write-Host "Removing previous decompiled output..."
    Remove-Item $Output -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null

Write-Host "Decompiling Valheim..."
& ilspycmd `
    --nested-directories `
    -p `
    -o "$Output" `
    -r "$Managed" `
    -lv CSharp9_0 `
    "$Assembly"

if ($LASTEXITCODE -ne 0) {
    throw "ilspycmd failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Done."
Write-Host "Decompiled source is in:"
Write-Host "  $Output"
Write-Host ""
Write-Host "This folder should stay in .gitignore:"
Write-Host "  ValheimDecompiled/"
