[CmdletBinding()]
param(
    [switch]$Editor,
    [string]$GodotPath
)

$projectRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    (Get-Location).Path
}
else {
    $PSScriptRoot
}

$bundledGodotExecutable = Join-Path $projectRoot '.tools\godot-4.7.2\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe'
$godotProject = Join-Path $projectRoot 'src\SimuladorIndustrial.Godot'

if ([string]::IsNullOrWhiteSpace($GodotPath)) {
    $GodotPath = $bundledGodotExecutable
}

if (-not (Test-Path -LiteralPath $GodotPath -PathType Leaf)) {
    throw @"
Godot 4.7.2 .NET não encontrado.
Informe o executável com -GodotPath ou extraia a versão portátil em:
$bundledGodotExecutable
"@
}

if (-not (Test-Path -LiteralPath (Join-Path $godotProject 'project.godot') -PathType Leaf)) {
    throw "Projeto 3D não encontrado em: $godotProject"
}

$godotArguments = [System.Collections.Generic.List[string]]::new()
$godotArguments.Add('--path')
$godotArguments.Add('.')
if ($Editor) {
    $godotArguments.Add('--editor')
}

Start-Process `
    -FilePath $GodotPath `
    -ArgumentList $godotArguments `
    -WorkingDirectory $godotProject | Out-Null
