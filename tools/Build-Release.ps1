param(
    [string]$Version = "1.0.1"
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$setup = Join-Path $root 'artifacts\installer'
$portableZip = Join-Path $setup 'UsageBar-win-x64.zip'
$solution = Join-Path $root 'UsageBar.sln'
$installerScript = Join-Path $root 'installer\UsageBar.iss'
$fileVersion = "$($Version).0"
$assemblyVersion = "$($Version).0"

Push-Location $root
try {
    $expectedPublishPath = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts\publish'))
    if ([System.IO.Path]::GetFullPath($publish) -ne $expectedPublishPath) {
        throw 'Publish output path is outside the expected artifacts directory.'
    }
    if (Test-Path -LiteralPath $publish) {
        Remove-Item -LiteralPath $publish -Recurse -Force
    }

    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

    dotnet test $solution --configuration Release --no-restore --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE" }

    dotnet publish 'src\UsageBar.Windows\UsageBar.Windows.csproj' `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publish `
        -p:PublishSingleFile=false `
        -p:DebugType=None `
        -p:Version=$Version `
        "-p:FileVersion=$fileVersion" `
        "-p:AssemblyVersion=$assemblyVersion"
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

    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $portableZip -CompressionLevel Optimal -Force

    Write-Host "Installer: $(Join-Path $setup 'UsageBar-Setup.exe')"
    Write-Host "Portable package: $portableZip"
}
finally {
    Pop-Location
}
