Write-Output 'START TEST'
Write-Output $env:TEMP
try { Write-Output ('TEMP TYPE: ' + ($env:TEMP.GetType().FullName)) } catch { Write-Output 'CANNOT GET TYPE' }
$tempFile = Join-Path $env:TEMP 'vc_redist.x64.exe'
Write-Output ('tempFile=' + $tempFile)
