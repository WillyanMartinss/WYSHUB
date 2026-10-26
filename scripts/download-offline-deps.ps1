$out = 'c:/Users/willy/Documents/WYSHUB/dependencias_offline'
if (-not (Test-Path $out)) {
    New-Item -ItemType Directory -Path $out | Out-Null
}

Write-Host 'Downloading VC++ redistributable...'
Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile "$out/vc_redist.x64.exe" -UseBasicParsing

Write-Host 'Downloading .NET Desktop Runtime x64...'
Invoke-WebRequest -Uri 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/8.0.29/windowsdesktop-runtime-8.0.29-win-x64.exe' -OutFile "$out/windowsdesktop-runtime-8.0.29-win-x64.exe" -UseBasicParsing

Write-Host 'Download completed.'
