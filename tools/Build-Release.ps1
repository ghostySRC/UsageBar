param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$setup = Join-Path $root 'artifacts\installer'
$solution = Join-Path $root 'UsageBar.sln'
$installerScript = Join-Path $root 'installer\UsageBar.iss'

Push-Location $root
try {
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    dotnet test $solution --configuration Release --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }

    dotnet publish 'src\UsageBar.Windows\UsageBar.Windows.csproj' `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publish `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:Version=$Version
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $compilerCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $compiler) { throw 'Inno Setup 6 is required to compile UsageBar-Setup.exe.' }

    & $compiler "/DMyAppVersion=$Version" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }

    Write-Host "Release files are ready under $setup and $publish"
}
finally {
    Pop-Location
}
