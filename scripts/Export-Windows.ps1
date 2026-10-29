[CmdletBinding()]
param(
    [string]$GodotPath,
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.2.0',
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = (Get-Location).Path
}

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$godotProject = Join-Path $projectRoot 'src\SimuladorIndustrial.Godot'
$solutionPath = Join-Path $projectRoot 'SimuladorIndustrial.sln'
$exportPresetPath = Join-Path $godotProject 'export_presets.cfg'
$sbomPath = Join-Path $projectRoot 'sbom\simulador-industrial.spdx.json'

$expectedProductVersion = "application/product_version=`"$Version.0`""
$exportPresetContent = Get-Content -LiteralPath $exportPresetPath -Raw
if (-not $exportPresetContent.Contains($expectedProductVersion, [System.StringComparison]::Ordinal)) {
    throw "A versão do export_presets.cfg não corresponde ao pacote $Version."
}

$sbom = Get-Content -LiteralPath $sbomPath -Raw | ConvertFrom-Json
$applicationPackage = @($sbom.packages) |
    Where-Object { $_.SPDXID -eq 'SPDXRef-Package-SimuladorIndustrial' } |
    Select-Object -First 1
if ($null -eq $applicationPackage -or $applicationPackage.versionInfo -ne $Version) {
    throw "A versão da SBOM não corresponde ao pacote $Version."
}

if ([string]::IsNullOrWhiteSpace($env:APPDATA)) {
    throw 'APPDATA não está definido; não foi possível localizar os templates do Godot.'
}
$templatePath = Join-Path $env:APPDATA 'Godot\export_templates\4.7.2.stable.mono\windows_release_x86_64.exe'
$godotLicenseDirectory = Join-Path $projectRoot '.tools\licenses\godot-4.7.2'
$godotLicenseHashes = @{
    'LICENSE.txt' = 'B0435E3B3E4E55238F05F4B306F30524A1B2E20147810D436EAA554FA6855C80'
    'COPYRIGHT.txt' = 'CB1980C88089573BCACD7221D777C689BB8BBD778799F24C27FCA0FE5F774D6D'
}

if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) {
    throw "Templates de exportação .NET ausentes. Execute '$scriptRoot\Setup-GodotExportTemplates.ps1' primeiro."
}

foreach ($requiredLicense in $godotLicenseHashes.GetEnumerator()) {
    $requiredLicensePath = Join-Path $godotLicenseDirectory $requiredLicense.Key
    if (-not (Test-Path -LiteralPath $requiredLicensePath -PathType Leaf)) {
        throw "Licença oficial Godot ausente: $requiredLicensePath. Execute o setup de templates novamente."
    }
    $actualLicenseHash = (Get-FileHash -LiteralPath $requiredLicensePath -Algorithm SHA256).Hash
    if (-not $actualLicenseHash.Equals($requiredLicense.Value, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 inválido para a licença oficial Godot: $requiredLicensePath."
    }
}

if ([string]::IsNullOrWhiteSpace($GodotPath)) {
    $godotDirectory = Join-Path $projectRoot '.tools\godot-4.7.2\Godot_v4.7.2-stable_mono_win64'
    $GodotPath = Get-ChildItem -LiteralPath $godotDirectory -Filter '*_console.exe' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if ([string]::IsNullOrWhiteSpace($GodotPath)) {
        $GodotPath = Get-ChildItem -LiteralPath $godotDirectory -Filter 'Godot*.exe' -File -ErrorAction SilentlyContinue |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

if ([string]::IsNullOrWhiteSpace($GodotPath) -or -not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw 'Godot 4.7.2 .NET não encontrado. Informe -GodotPath ou instale-o em .tools.'
}

Write-Host 'Compilando solução em Release...'
& dotnet build $solutionPath -c Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build falhou com código $LASTEXITCODE."
}

if (-not $SkipTests) {
    Write-Host 'Executando testes automatizados...'
    & dotnet test $solutionPath -c Release --no-build --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test falhou com código $LASTEXITCODE."
    }
}

$distRoot = Join-Path $projectRoot 'dist'
$stagingRoot = Join-Path $distRoot ".staging-$([Guid]::NewGuid().ToString('N'))"
$releaseName = "SimuladorIndustrial3D-$Version-windows-x86_64"
$stagingRelease = Join-Path $stagingRoot $releaseName
$stagingApp = Join-Path $stagingRelease 'app\bin'
$finalRelease = Join-Path $distRoot $releaseName
$archivePath = Join-Path $distRoot "$releaseName.zip"
$backupRoot = Join-Path $distRoot 'backups'

New-Item -ItemType Directory -Path $stagingApp -Force | Out-Null
$resolvedDistRoot = [System.IO.Path]::GetFullPath($distRoot)
$resolvedStagingRoot = [System.IO.Path]::GetFullPath($stagingRoot)
if (-not $resolvedStagingRoot.StartsWith($resolvedDistRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not ([System.IO.Path]::GetFileName($resolvedStagingRoot)).StartsWith('.staging-', [System.StringComparison]::Ordinal)) {
    throw "Diretório de staging inválido: $resolvedStagingRoot"
}

try {
    $outputExe = Join-Path $stagingApp 'SimuladorIndustrial3D.exe'
    Write-Host "Exportando preset 'Windows Desktop' para: $outputExe"
    & $GodotPath --headless --path $godotProject --export-release 'Windows Desktop' $outputExe
    if ($LASTEXITCODE -ne 0) {
        throw "Exportação Godot falhou com código $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $outputExe -PathType Leaf)) {
        throw "A exportação terminou sem gerar o executável esperado: $outputExe"
    }

    $sampleDestination = Join-Path $stagingRelease 'samples'
    New-Item -ItemType Directory -Path $sampleDestination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'samples\celula-envase.scene.json') -Destination $sampleDestination
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $stagingRelease
    Copy-Item -LiteralPath (Join-Path $godotLicenseDirectory 'LICENSE.txt') -Destination (Join-Path $stagingRelease 'GODOT_LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $godotLicenseDirectory 'COPYRIGHT.txt') -Destination (Join-Path $stagingRelease 'GODOT_COPYRIGHT.txt')

    $documentationDestination = Join-Path $stagingRelease 'docs'
    New-Item -ItemType Directory -Path $documentationDestination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\MANUAL_OPERACAO.md') -Destination $documentationDestination
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\clean-room') -Destination $documentationDestination -Recurse
    Copy-Item -LiteralPath (Join-Path $projectRoot 'tests\interop') -Destination $documentationDestination -Recurse

    $sbomDestination = Join-Path $stagingRelease 'sbom'
    New-Item -ItemType Directory -Path $sbomDestination -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'sbom\simulador-industrial.spdx.json') -Destination $sbomDestination

    $launcher = @'
@echo off
setlocal
cd /d "%~dp0"
start "Simulador Industrial 3D" "%~dp0app\bin\SimuladorIndustrial3D.exe"
'@
    Set-Content -LiteralPath (Join-Path $stagingRelease 'Iniciar-SimuladorIndustrial3D.cmd') -Value $launcher -Encoding Ascii

    $readme = @"
SIMULADOR INDUSTRIAL 3D $Version

1. Extraia todo o arquivo ZIP para uma pasta gravável.
2. Execute Iniciar-SimuladorIndustrial3D.cmd.
3. O pacote é x86_64 e requer Windows com OpenGL 3.3 ou Vulkan 1.0.

Este build não possui assinatura de código. O Windows pode exibir um aviso do SmartScreen.
Licenças e inventário: THIRD_PARTY_NOTICES.md e sbom\simulador-industrial.spdx.json.
Manual e roteiro OpenPLC: docs\MANUAL_OPERACAO.md e docs\interop\openplc-60min\README.md.
"@
    Set-Content -LiteralPath (Join-Path $stagingRelease 'LEIA-ME.txt') -Value $readme -Encoding UTF8

    $manifestLines = Get-ChildItem -LiteralPath $stagingRelease -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath($stagingRelease, $_.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relativePath"
        }
    Set-Content -LiteralPath (Join-Path $stagingRelease 'SHA256SUMS.txt') -Value $manifestLines -Encoding Ascii

    New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
    if ((Test-Path -LiteralPath $finalRelease) -or (Test-Path -LiteralPath $archivePath)) {
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        $backupStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        if (Test-Path -LiteralPath $finalRelease) {
            Move-Item -LiteralPath $finalRelease -Destination (Join-Path $backupRoot "$releaseName-$backupStamp")
        }
        if (Test-Path -LiteralPath $archivePath) {
            Move-Item -LiteralPath $archivePath -Destination (Join-Path $backupRoot "$releaseName-$backupStamp.zip")
        }
    }

    Move-Item -LiteralPath $stagingRelease -Destination $finalRelease
    Compress-Archive -LiteralPath $finalRelease -DestinationPath $archivePath -CompressionLevel Optimal

    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    Write-Host "Pacote criado: $archivePath"
    Write-Host "SHA-256: $archiveHash"
}
finally {
    if (Test-Path -LiteralPath $resolvedStagingRoot) {
        $checkedStaging = (Resolve-Path -LiteralPath $resolvedStagingRoot).Path
        if (-not $checkedStaging.StartsWith($resolvedDistRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not ([System.IO.Path]::GetFileName($checkedStaging)).StartsWith('.staging-', [System.StringComparison]::Ordinal)) {
            throw "Limpeza recusada para staging inesperado: $checkedStaging"
        }
        Remove-Item -LiteralPath $checkedStaging -Recurse -Force
    }
}
