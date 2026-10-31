param([string]$Configuration = "Release", [string]$OutputDirectory = "")
$ErrorActionPreference = "Stop"
$dotnet = Join-Path $PSScriptRoot "..\.tools\dotnet\dotnet.exe"
$project = Join-Path $PSScriptRoot "..\src\ResourceManager.App\ResourceManager.App.csproj"
$artifactRoot = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $PSScriptRoot "..\artifacts\portable" }
foreach ($runtime in @("win-x64", "win-arm64")) {
    $output = Join-Path $artifactRoot $runtime
    & $dotnet publish $project -c $Configuration -r $runtime --self-contained true --no-restore -m:1 -o $output `
        -p:UseSharedCompilation=false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime" }
    $data = Join-Path $output "Data"
    New-Item -ItemType Directory -Force -Path $data | Out-Null
    Set-Content -Path (Join-Path $data "README.txt") -Value "Portable data, settings, and local history are stored here."
    Compress-Archive -Path (Join-Path $output "*") -DestinationPath (Join-Path $artifactRoot "ResourceManager-$runtime.zip") -Force
}
