[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceDirectory,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$source = [System.IO.Path]::GetFullPath($SourceDirectory)
$output = [System.IO.Path]::GetFullPath($OutputPath)
$manifest = Join-Path $source 'component-package.json'

if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Diretório de origem não encontrado: $source"
}

if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "Manifesto obrigatório não encontrado: $manifest"
}

if ([System.IO.Path]::GetExtension($output) -ne '.silabcomponent') {
    throw 'O arquivo de saída deve usar a extensão .silabcomponent.'
}

$outputDirectory = Split-Path -Parent $output
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

if ((Test-Path -LiteralPath $output) -and -not $Force) {
    throw "O arquivo de saída já existe: $output"
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $source,
    $output,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Pacote criado: $output"
Write-Host "SHA-256: $hash"
