[CmdletBinding()]
param(
    [switch]$Repair
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = (Get-Location).Path
}

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$godotVersion = '4.7.2'
$templateVersion = '4.7.2.stable.mono'
$archiveName = 'Godot_v4.7.2-stable_mono_export_templates.tpz'
$downloadUrl = "https://github.com/godotengine/godot/releases/download/4.7.2-stable/$archiveName"
$expectedSha256 = '92F8681E349EF1F90891B792DA95E3B2B0BD1ED610B78018C58FEB2D87E15A9D'
$expectedArchiveSize = 1202598411L

if ([string]::IsNullOrWhiteSpace($env:APPDATA)) {
    throw 'APPDATA não está definido; não foi possível localizar o diretório de templates do Godot.'
}

$templateParent = Join-Path $env:APPDATA 'Godot\export_templates'
$templateDestination = Join-Path $templateParent $templateVersion
$requiredTemplate = Join-Path $templateDestination 'windows_release_x86_64.exe'
$licenseDirectory = Join-Path $projectRoot '.tools\licenses\godot-4.7.2'
New-Item -ItemType Directory -Path $licenseDirectory -Force | Out-Null

$licenseFiles = @(
    [pscustomobject]@{
        Name = 'LICENSE.txt'
        Size = 1149L
        Sha256 = 'B0435E3B3E4E55238F05F4B306F30524A1B2E20147810D436EAA554FA6855C80'
    },
    [pscustomobject]@{
        Name = 'COPYRIGHT.txt'
        Size = 100108L
        Sha256 = 'CB1980C88089573BCACD7221D777C689BB8BBD778799F24C27FCA0FE5F774D6D'
    }
)

foreach ($licenseFile in $licenseFiles) {
    $licensePath = Join-Path $licenseDirectory $licenseFile.Name
    $licenseValid = (Test-Path -LiteralPath $licensePath -PathType Leaf) -and
        ((Get-Item -LiteralPath $licensePath).Length -eq $licenseFile.Size) -and
        ((Get-FileHash -LiteralPath $licensePath -Algorithm SHA256).Hash.Equals(
            $licenseFile.Sha256,
            [System.StringComparison]::OrdinalIgnoreCase))

    if ($licenseValid) {
        continue
    }

    if (Test-Path -LiteralPath $licensePath) {
        Move-Item -LiteralPath $licensePath -Destination "$licensePath.invalid-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    }

    $licenseUrl = "https://raw.githubusercontent.com/godotengine/godot/4.7.2-stable/$($licenseFile.Name)"
    Invoke-WebRequest -Uri $licenseUrl -OutFile $licensePath -Headers @{ 'User-Agent' = 'SimuladorIndustrial-TemplateSetup/1.0' }
    $downloadedLicense = Get-Item -LiteralPath $licensePath
    $downloadedHash = (Get-FileHash -LiteralPath $licensePath -Algorithm SHA256).Hash
    if ($downloadedLicense.Length -ne $licenseFile.Size -or
        -not $downloadedHash.Equals($licenseFile.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Falha ao verificar o arquivo oficial Godot $($licenseFile.Name)."
    }
}

if (Test-Path -LiteralPath $requiredTemplate -PathType Leaf) {
    Write-Host "Templates Godot $templateVersion já instalados em: $templateDestination"
    Write-Host "Licenças oficiais verificadas em: $licenseDirectory"
    return
}

if ((Test-Path -LiteralPath $templateDestination) -and -not $Repair) {
    throw "Existe uma instalação parcial em '$templateDestination'. Execute novamente com -Repair para preservá-la como backup antes de instalar."
}

$downloadDirectory = Join-Path $projectRoot '.tools\downloads'
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
$archivePath = Join-Path $downloadDirectory $archiveName
$driveRoot = [System.IO.Path]::GetPathRoot($projectRoot)
$freeBytes = ([System.IO.DriveInfo]::new($driveRoot)).AvailableFreeSpace
if ($freeBytes -lt 4GB) {
    throw 'São necessários pelo menos 4 GB livres para baixar e extrair os templates de exportação.'
}

if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
    $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if (-not $actualSha256.Equals($expectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        $invalidArchive = "$archivePath.invalid-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Move-Item -LiteralPath $archivePath -Destination $invalidArchive
        Write-Warning "Download existente tinha hash inválido e foi preservado em: $invalidArchive"
    }
}

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    Write-Host "Baixando templates oficiais Godot $godotVersion .NET (aprox. 1,2 GB)..."
    $previousProgressPreference = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -Headers @{ 'User-Agent' = 'SimuladorIndustrial-TemplateSetup/1.0' }
    }
    finally {
        $ProgressPreference = $previousProgressPreference
    }
}

$archive = Get-Item -LiteralPath $archivePath
if ($archive.Length -ne $expectedArchiveSize) {
    throw "Tamanho inesperado para '$archivePath': $($archive.Length) bytes; esperado: $expectedArchiveSize bytes."
}

$actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
if (-not $actualSha256.Equals($expectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "SHA-256 inválido para '$archivePath'. Esperado: $expectedSha256; obtido: $actualSha256."
}

$stagingPath = Join-Path ([System.IO.Path]::GetTempPath()) "simulador-industrial-templates-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $stagingPath | Out-Null
$resolvedStaging = (Resolve-Path -LiteralPath $stagingPath).Path
$resolvedTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
if (-not $resolvedStaging.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Diretório temporário fora da raiz esperada: $resolvedStaging"
}

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $resolvedStaging)

    $extractedTemplates = Join-Path $resolvedStaging 'templates'
    if (-not (Test-Path -LiteralPath $extractedTemplates -PathType Container)) {
        $candidate = Get-ChildItem -LiteralPath $resolvedStaging -Directory -Recurse |
            Where-Object Name -EQ 'templates' |
            Select-Object -First 1
        if ($null -eq $candidate) {
            throw 'O arquivo oficial não contém a pasta templates esperada.'
        }
        $extractedTemplates = $candidate.FullName
    }

    if (Test-Path -LiteralPath $templateDestination) {
        $backupDestination = "$templateDestination.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Move-Item -LiteralPath $templateDestination -Destination $backupDestination
        Write-Warning "Instalação parcial preservada em: $backupDestination"
    }

    New-Item -ItemType Directory -Path $templateDestination -Force | Out-Null
    Get-ChildItem -LiteralPath $extractedTemplates -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $templateDestination -Recurse
    }

    if (-not (Test-Path -LiteralPath $requiredTemplate -PathType Leaf)) {
        throw "O template obrigatório não foi instalado: $requiredTemplate"
    }

    Write-Host "Templates verificados e instalados em: $templateDestination"
}
finally {
    if (Test-Path -LiteralPath $resolvedStaging) {
        $checkedStaging = (Resolve-Path -LiteralPath $resolvedStaging).Path
        if (-not $checkedStaging.StartsWith($resolvedTempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not ([System.IO.Path]::GetFileName($checkedStaging)).StartsWith('simulador-industrial-templates-', [System.StringComparison]::Ordinal)) {
            throw "Limpeza recusada para diretório temporário inesperado: $checkedStaging"
        }
        Remove-Item -LiteralPath $checkedStaging -Recurse -Force
    }
}
