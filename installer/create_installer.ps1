$ErrorActionPreference = 'Stop'

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..\WYSHUB_executavel')
$publishDir = Join-Path $projectRoot 'bin\Release\net8.0-windows\win-x64\publish'
$exePath = Join-Path $publishDir 'WYSHUB.exe'
$installerDir = Join-Path $PSScriptRoot '..\instaladores'
$finalDir = Join-Path $PSScriptRoot '..\dist'
$iconPath = Join-Path $projectRoot 'icons\Light.ico'

Write-Host 'Publicando aplicação em Release win-x64...'
Push-Location $projectRoot
try {
    dotnet publish .\SystemWM.csproj -c Release -r win-x64 /p:PublishSingleFile=true /p:SelfContained=true /p:EnableCompressionInSingleFile=true | Write-Host
}
finally {
    Pop-Location
}

if ($env:SIGN_CERT_PATH) {
    if (-not (Test-Path $env:SIGN_CERT_PATH)) {
        throw "Certificado de assinatura não encontrado: $env:SIGN_CERT_PATH"
    }

    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        throw 'signtool.exe não encontrado no PATH. Instale o Windows SDK ou adicione signtool ao PATH para assinar o executável.'
    }

    Write-Host 'Assinando executável com certificado...'    
    & $signtool.Source sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $env:SIGN_CERT_PATH /p $env:SIGN_CERT_PASSWORD $exePath
}

if (-not (Test-Path $exePath)) {
    throw "Executável não encontrado: $exePath"
}

if (-not (Test-Path $iconPath)) {
    $iconPath = $exePath
}

New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
New-Item -ItemType Directory -Force -Path $finalDir | Out-Null

$packageDir = Join-Path $installerDir 'WYSHUB_Portavel'
Remove-Item -Recurse -Force $packageDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

Copy-Item $exePath -Destination (Join-Path $packageDir 'WYSHUB.exe')
Copy-Item $iconPath -Destination (Join-Path $packageDir 'WYSHUB.ico') -ErrorAction SilentlyContinue

$readme = @"
WYSHUB

Aplicativo desktop para suporte técnico e visitas de campo.

Instalação:
1. Extraia a pasta para o computador do cliente.
2. Execute WYSHUB.exe.
3. Se o Windows mostrar "App não é confiável", clique em Mais informações e depois em Executar mesmo assim.
4. O app pode solicitar permissão de Administrador para funções de firewall e sensores.

Observações:
- O programa foi publicado em modo self-contained para reduzir dependências.
- Para reduzir bloqueios do Defender, recomende instalar em C:\Program Files\WYSHUB.
"@
Set-Content -Path (Join-Path $packageDir 'Leia-me.txt') -Value $readme

$shortcutPath = Join-Path $packageDir 'WYSHUB.lnk'
$wsh = New-Object -ComObject WScript.Shell
$shortcut = $wsh.CreateShortcut($shortcutPath)
$shortcut.TargetPath = (Join-Path $packageDir 'WYSHUB.exe')
$shortcut.WorkingDirectory = $packageDir
$shortcut.IconLocation = (Join-Path $packageDir 'WYSHUB.ico')
$shortcut.Save()

$zipPath = Join-Path $installerDir 'WYSHUB_installer.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -Force

# Copy final portable package to root dist folder
$finalPortableDir = Join-Path $finalDir 'WYSHUB_Portavel_Final'
Remove-Item -Recurse -Force $finalPortableDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $finalPortableDir | Out-Null
Copy-Item -Path (Join-Path $packageDir '*') -Destination $finalPortableDir -Recurse

$finalPortableZip = Join-Path $finalDir 'WYSHUB_Portavel_Final.zip'
if (Test-Path $finalPortableZip) { Remove-Item $finalPortableZip -Force }
Compress-Archive -Path (Join-Path $finalPortableDir '*') -DestinationPath $finalPortableZip -Force

Write-Host "Instalador criado em: $zipPath"
Write-Host "Portável final copiado para: $finalPortableDir"
Write-Host "Zip final criado em: $finalPortableZip"

# Se houver certificado configurado, assina o executável dentro do pacote
if ($env:SIGN_CERT_PATH) {
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($signtool) {
        $appExeInPackage = Join-Path $packageDir 'WYSHUB.exe'
        if (Test-Path $appExeInPackage) {
            Write-Host "Assinando $appExeInPackage..."
            & $signtool.Source sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $env:SIGN_CERT_PATH /p $env:SIGN_CERT_PASSWORD $appExeInPackage
        }
    }
    else {
        Write-Warning 'signtool.exe não encontrado no PATH. Pulei assinatura dos binários.'
    }
}

# Tentar compilar o instalador Inno Setup se o compilador estiver disponível
$issPath = Join-Path $PSScriptRoot 'WYSHUB.iss'
$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if ($iscc -and (Test-Path $issPath)) {
    Write-Host 'Compilando instalador com Inno Setup (iscc.exe)...'
    Push-Location $PSScriptRoot
    try {
        & $iscc.Source $issPath | Write-Host
    }
    finally {
        Pop-Location
    }

    # O instalador compilado agora sai na pasta instaladores/
    $setupExe = Join-Path $installerDir 'WYSHUB_Setup.exe'
    if ((Test-Path $setupExe) -and $env:SIGN_CERT_PATH) {
        $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
        if ($signtool) {
            Write-Host "Assinando instalador $setupExe..."
            & $signtool.Source sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $env:SIGN_CERT_PATH /p $env:SIGN_CERT_PASSWORD $setupExe
        }
    }

    if (Test-Path $setupExe) {
        $finalSetupExe = Join-Path $finalDir 'WYSHUB_Setup_Final.exe'
        Copy-Item -Path $setupExe -Destination $finalSetupExe -Force
        Write-Host "Instalador final copiado para: $finalSetupExe"
    }
}
