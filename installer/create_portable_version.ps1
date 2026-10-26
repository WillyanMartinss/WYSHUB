$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptRoot '..\WYSHUB_executavel')
$publishDir = Join-Path $projectRoot 'bin\Release\net8.0-windows\win-x64\publish'
$versionRoot = Resolve-Path (Join-Path $scriptRoot '..\releases')
$stagingDir = Join-Path $scriptRoot 'portable_staging'

Write-Host 'Criando versão portátil versionada...'
Write-Host "Projeto: $projectRoot"
Write-Host "Publicação de saída: $publishDir"
Write-Host "Destino de versões: $versionRoot"

if (-not (Test-Path $projectRoot)) {
    throw "Pasta do projeto não encontrada: $projectRoot"
}

if (-not (Test-Path $versionRoot)) {
    New-Item -ItemType Directory -Force -Path $versionRoot | Out-Null
}

Push-Location $projectRoot
try {
    Write-Host 'Publicando aplicação em Release win-x64 (self-contained)...'
    dotnet publish .\SystemWM.csproj -c Release -r win-x64 /p:PublishSingleFile=false /p:SelfContained=true | Write-Host
}
finally {
    Pop-Location
}

$exePath = Join-Path $publishDir 'WYSHUB.exe'
if (-not (Test-Path $exePath)) {
    throw "Executável não encontrado após publicação: $exePath"
}

Write-Host 'Limpando área de staging...'
Remove-Item -Recurse -Force $stagingDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stagingDir | Out-Null

Write-Host 'Copiando arquivos publicados para staging...'
Copy-Item -Path (Join-Path $publishDir '*') -Destination $stagingDir -Recurse -Force

$readme = @"
WYSHUB - Versão portátil

Esta versão portátil pode ser usada sem instalação. Basta extrair a pasta e executar WYSHUB.exe.

Observações:
- Se o Windows pedir elevação, aceite para funções de firewall e diagnóstico.
- O aplicativo já foi publicado como self-contained para funcionar sem .NET instalado.
- Essa versão foi gerada para testes rápidos.
"@
Set-Content -Path (Join-Path $stagingDir 'Leia-me.txt') -Value $readme -Encoding UTF8

$shortcutPath = Join-Path $stagingDir 'WYSHUB.lnk'
Write-Host 'Criando atalho WYSHUB.lnk para facilitar testes...'
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = (Join-Path $stagingDir 'WYSHUB.exe')
$shortcut.WorkingDirectory = $stagingDir
$shortcut.IconLocation = (Join-Path $stagingDir 'WYSHUB.exe')
$shortcut.Save()

Write-Host 'Limpando versões portáteis antigas não versionadas...'
Get-ChildItem -Path $versionRoot -Filter 'WYSHUB_Portavel*' -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $versionRoot -Filter 'WYSHUB_Portavel_*.zip' -Force -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

$existingVersions = Get-ChildItem -Path $versionRoot -Directory -Filter 'versao_*' -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^versao_(\d{3})$' } |
    ForEach-Object { [int]$matches[1] }

$nextVersion = if ($existingVersions) { ($existingVersions | Measure-Object -Maximum).Maximum + 1 } else { 1 }
$versionFolderName = ('versao_{0:D3}' -f $nextVersion)
$versionFolder = Join-Path $versionRoot $versionFolderName

Write-Host "Nova versão portátil: $versionFolderName"
New-Item -ItemType Directory -Force -Path $versionFolder | Out-Null
Copy-Item -Path (Join-Path $stagingDir '*') -Destination $versionFolder -Recurse -Force

$zipPath = Join-Path $versionRoot "$versionFolderName.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Write-Host "Criando arquivo ZIP: $zipPath"
Compress-Archive -Path (Join-Path $versionFolder '*') -DestinationPath $zipPath -Force

Write-Host 'Versão portátil criada com sucesso.'
Write-Host "Pasta: $versionFolder"
Write-Host "Zip: $zipPath"

Write-Host 'Removendo staging temporário...'
Remove-Item -Recurse -Force $stagingDir
