[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$publishDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot "publish\MicToggle-win-x64"))
$archivePath = [System.IO.Path]::GetFullPath(
    (Join-Path $artifactsRoot "MicToggle-win-x64.zip"))
$hashPath = "$archivePath.sha256"

$repositoryPrefix = $repositoryRoot.TrimEnd('\') + '\'
$artifactsPrefix = $artifactsRoot.TrimEnd('\') + '\'
if (-not $artifactsRoot.StartsWith(
        $repositoryPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Artifacts directory must be inside the repository."
}
if (-not $publishDirectory.StartsWith(
        $artifactsPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish directory must be inside the artifacts directory."
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
if (Test-Path -LiteralPath $hashPath) {
    Remove-Item -LiteralPath $hashPath -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

$solutionPath = Join-Path $repositoryRoot "MicToggle.sln"
$projectPath = Join-Path $repositoryRoot "src\MicToggle\MicToggle.csproj"

& dotnet restore $solutionPath --locked-mode
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE."
}

& dotnet publish $projectPath `
    -c $Configuration `
    --self-contained false `
    --no-restore `
    -o $publishDirectory `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Get-ChildItem -LiteralPath $publishDirectory -Recurse -File |
    Where-Object { $_.Extension -in ".pdb", ".xml" } |
    Remove-Item -Force

foreach ($fileName in @("LICENSE", "PRIVACY.md", "THIRD-PARTY-NOTICES.md")) {
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot $fileName) `
        -Destination $publishDirectory
}
Copy-Item `
    -LiteralPath (Join-Path $repositoryRoot "third-party") `
    -Destination (Join-Path $publishDirectory "third-party") `
    -Recurse

Compress-Archive `
    -Path (Join-Path $publishDirectory "*") `
    -DestinationPath $archivePath `
    -CompressionLevel Optimal

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  MicToggle-win-x64.zip" |
    Set-Content -LiteralPath $hashPath -Encoding ascii

Write-Host "Created $archivePath"
Write-Host "Created $hashPath"
