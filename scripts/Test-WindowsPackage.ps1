[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '0.2.0',
    [string]$ArchivePath,
    [ValidateRange(10, 300)]
    [int]$SmokeTimeoutSeconds = 90,
    [switch]$SkipSmokeTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = (Get-Location).Path
}

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
$distRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'dist'))
$releaseName = "SimuladorIndustrial3D-$Version-windows-x86_64"
$expectedArchiveName = "$releaseName.zip"
if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $ArchivePath = Join-Path $distRoot $expectedArchiveName
}

$archiveFullPath = [System.IO.Path]::GetFullPath($ArchivePath)
if (-not (Test-Path -LiteralPath $archiveFullPath -PathType Leaf)) {
    throw "Pacote Windows não encontrado: $archiveFullPath"
}
if (-not ([System.IO.Path]::GetFileName($archiveFullPath)).Equals(
        $expectedArchiveName,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Nome de pacote inesperado. Esperado: $expectedArchiveName"
}

New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $distRoot ".package-validation-$([Guid]::NewGuid().ToString('N'))"))
$distPrefix = $distRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $stagingRoot.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not ([System.IO.Path]::GetFileName($stagingRoot)).StartsWith('.package-validation-', [System.StringComparison]::Ordinal)) {
    throw "Diretório temporário de validação recusado: $stagingRoot"
}

$requiredFiles = @(
    'app\bin\SimuladorIndustrial3D.exe',
    'Iniciar-SimuladorIndustrial3D.cmd',
    'LEIA-ME.txt',
    'SHA256SUMS.txt',
    'THIRD_PARTY_NOTICES.md',
    'GODOT_LICENSE.txt',
    'GODOT_COPYRIGHT.txt',
    'docs\MANUAL_OPERACAO.md',
    'sbom\simulador-industrial.spdx.json',
    'samples\celula-envase.scene.json'
)

$smokeProcess = $null
try {
    $expandedBytes = 0L
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archiveFullPath)
    try {
        if ($archive.Entries.Count -gt 5000) {
            throw "O pacote contém entradas demais: $($archive.Entries.Count)."
        }

        foreach ($entry in $archive.Entries) {
            $entryPath = $entry.FullName.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            if ([string]::IsNullOrWhiteSpace($entryPath)) {
                continue
            }

            $segments = $entryPath.Split(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.StringSplitOptions]::RemoveEmptyEntries)
            if ([System.IO.Path]::IsPathRooted($entryPath) -or $segments -contains '..') {
                throw "Entrada insegura no ZIP: $($entry.FullName)"
            }

            $expandedPath = [System.IO.Path]::GetFullPath((Join-Path $stagingRoot $entryPath))
            if (-not $expandedPath.StartsWith($stagingRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Entrada fora do staging no ZIP: $($entry.FullName)"
            }
            if ($segments.Count -eq 0 -or -not $segments[0].Equals($releaseName, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Raiz inesperada no ZIP: $($entry.FullName)"
            }

            $expandedBytes += $entry.Length
            if ($expandedBytes -gt 1GB) {
                throw 'O conteúdo descompactado excede o limite de segurança de 1 GiB.'
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    New-Item -ItemType Directory -Path $stagingRoot | Out-Null
    Expand-Archive -LiteralPath $archiveFullPath -DestinationPath $stagingRoot

    $releaseRoot = [System.IO.Path]::GetFullPath((Join-Path $stagingRoot $releaseName))
    if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
        throw "Diretório raiz esperado ausente no pacote: $releaseName"
    }

    foreach ($relativePath in $requiredFiles) {
        $requiredPath = Join-Path $releaseRoot $relativePath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Arquivo obrigatório ausente no pacote: $relativePath"
        }
    }

    $manifestPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
    $manifestEntries = 0
    foreach ($line in Get-Content -LiteralPath $manifestPath) {
        if ($line -notmatch '^([0-9a-fA-F]{64})  (.+)$') {
            throw "Linha inválida no SHA256SUMS.txt: $line"
        }

        $expectedHash = $Matches[1]
        $relativePath = $Matches[2].Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $segments = $relativePath.Split(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.StringSplitOptions]::RemoveEmptyEntries)
        if ([System.IO.Path]::IsPathRooted($relativePath) -or $segments -contains '..') {
            throw "Caminho inseguro no manifesto: $relativePath"
        }

        $filePath = [System.IO.Path]::GetFullPath((Join-Path $releaseRoot $relativePath))
        if (-not $filePath.StartsWith($releaseRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "Arquivo do manifesto ausente ou fora do pacote: $relativePath"
        }

        $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash
        if (-not $actualHash.Equals($expectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Hash divergente no pacote: $relativePath"
        }
        $manifestEntries++
    }
    if ($manifestEntries -eq 0) {
        throw 'O manifesto SHA256SUMS.txt está vazio.'
    }

    $executablePath = Join-Path $releaseRoot 'app\bin\SimuladorIndustrial3D.exe'
    $versionInfo = (Get-Item -LiteralPath $executablePath).VersionInfo
    $expectedExecutableVersion = "$Version.0"
    if ($versionInfo.FileVersion -ne $expectedExecutableVersion -or
        $versionInfo.ProductVersion -ne $expectedExecutableVersion) {
        throw "Metadados do executável divergentes. Esperado: $expectedExecutableVersion."
    }

    $sbomPath = Join-Path $releaseRoot 'sbom\simulador-industrial.spdx.json'
    $sbom = Get-Content -LiteralPath $sbomPath -Raw | ConvertFrom-Json
    $applicationPackage = @($sbom.packages) |
        Where-Object { $_.SPDXID -eq 'SPDXRef-Package-SimuladorIndustrial' } |
        Select-Object -First 1
    if ($null -eq $applicationPackage -or $applicationPackage.versionInfo -ne $Version) {
        throw "A versão da SBOM não corresponde ao pacote $Version."
    }

    $captureHash = $null
    if (-not $SkipSmokeTest) {
        $smokeProcess = Start-Process `
            -FilePath $executablePath `
            -ArgumentList @('--', '--capture-section=connections') `
            -WindowStyle Hidden `
            -PassThru
        if (-not $smokeProcess.WaitForExit($SmokeTimeoutSeconds * 1000)) {
            $smokeProcess.Kill($true)
            $smokeProcess.WaitForExit()
            throw "O smoke test excedeu $SmokeTimeoutSeconds segundos."
        }
        if ($smokeProcess.ExitCode -ne 0) {
            throw "O executável encerrou o smoke test com código $($smokeProcess.ExitCode)."
        }

        $capture = Get-ChildItem -LiteralPath $releaseRoot -Filter 'connections-check.png' -File -Recurse |
            Select-Object -First 1
        if ($null -eq $capture -or $capture.Length -eq 0) {
            throw 'O executável não produziu a captura esperada no smoke test.'
        }
        $captureHash = (Get-FileHash -LiteralPath $capture.FullName -Algorithm SHA256).Hash
    }

    $archiveHash = (Get-FileHash -LiteralPath $archiveFullPath -Algorithm SHA256).Hash
    Write-Host "Pacote aprovado: $archiveFullPath"
    Write-Host "Arquivos conferidos pelo manifesto: $manifestEntries"
    Write-Host "Versão do executável: $expectedExecutableVersion"
    if ($null -ne $captureHash) {
        Write-Host "Smoke test: aprovado (captura SHA-256 $captureHash)"
    }
    else {
        Write-Host 'Smoke test: ignorado por opção explícita.'
    }
    Write-Host "SHA-256 do ZIP: $archiveHash"
}
finally {
    if ($null -ne $smokeProcess -and -not $smokeProcess.HasExited) {
        $smokeProcess.Kill($true)
        $smokeProcess.WaitForExit()
    }

    if (Test-Path -LiteralPath $stagingRoot) {
        $resolvedStaging = (Resolve-Path -LiteralPath $stagingRoot).Path
        if (-not $resolvedStaging.Equals($stagingRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not $resolvedStaging.StartsWith($distPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not ([System.IO.Path]::GetFileName($resolvedStaging)).StartsWith('.package-validation-', [System.StringComparison]::Ordinal)) {
            throw "Limpeza recusada para staging inesperado: $resolvedStaging"
        }
        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}
