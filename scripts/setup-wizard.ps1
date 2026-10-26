param(
    [string]$SourcePath = '',
    [string[]]$OfflineInstallers = @()
)

$ErrorActionPreference = 'Stop'

$AppFolder = Join-Path $env:ProgramFiles 'WYSHUB'
$ExeName = 'WYSHUB.exe'
$SourceAppPath = if ($SourcePath) { $SourcePath } else { Split-Path $PSScriptRoot -Parent }
$SourceExePath = Join-Path $SourceAppPath $ExeName
$TargetExePath = Join-Path $AppFolder $ExeName
$RequirementsPath = Join-Path (Split-Path $SourceAppPath -Parent) 'requirements.txt'
$DocumentsFolder = [Environment]::GetFolderPath('MyDocuments')
$SetupWizardLogFolder = Join-Path $DocumentsFolder 'SystemWM\Logs'
$SetupWizardLogPath = Join-Path $SetupWizardLogFolder ('SetupWizard_{0:yyyyMMdd_HHmmss}.log' -f (Get-Date))

$ScriptFolder = Split-Path $MyInvocation.MyCommand.Path -Parent
$OfflinePackagesFolder = $null

function Write-SetupWizardLog {
    param(
        [string]$Message
    )

    Write-Host $Message
}

function Resolve-OfflinePackagesFolder {
    $searchRoots = @()

    if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
        $searchRoots += [System.IO.Path]::GetFullPath($SourcePath)
    }

    if (-not [string]::IsNullOrWhiteSpace($ScriptFolder)) {
        $searchRoots += [System.IO.Path]::GetFullPath($ScriptFolder)
    }

    $searchRoots += [System.IO.Path]::GetFullPath((Get-Location).Path)
    $searchRoots += [System.IO.Path]::GetFullPath($PSScriptRoot)
    $searchRoots += [System.IO.Path]::GetFullPath($SourceAppPath)

    $folderNames = @('dependencias_offline', 'dependencias')
    $visited = @()

    foreach ($root in ($searchRoots | Select-Object -Unique)) {
        $current = $root
        while (-not [string]::IsNullOrWhiteSpace($current)) {
            if ($visited -notcontains $current) {
                $visited += $current

                foreach ($folderName in $folderNames) {
                    $candidate = Join-Path $current $folderName
                    if (Test-Path $candidate -PathType Container) {
                        return [System.IO.Path]::GetFullPath($candidate)
                    }
                }
            }

            $parent = Split-Path $current -Parent
            if (-not $parent -or $parent -eq $current) {
                break
            }

            $current = $parent
        }
    }

    return $null
}

$OfflinePackagesFolder = Resolve-OfflinePackagesFolder

New-Item -ItemType Directory -Path $SetupWizardLogFolder -Force | Out-Null
Start-Transcript -Path $SetupWizardLogPath -Force | Out-Null
Write-SetupWizardLog "DEBUG: Offline packages folder: $OfflinePackagesFolder"

function Get-WingetPath {
    $possiblePaths = @(
        "$env:windir\sysnative\winget.exe",
        "$env:windir\System32\winget.exe"
    )

    foreach ($path in $possiblePaths) {
        if (Test-Path $path) {
            return $path
        }
    }

    return $null
}

function Get-OfflinePackageFile {
    param(
        [string]$Pattern
    )

    if (-not (Test-Path $OfflinePackagesFolder)) {
        return $null
    }

    $file = Get-ChildItem -Path $OfflinePackagesFolder -Filter $Pattern -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    return $file?.FullName
}

function Get-OfflineInstallerPackages {
    param(
        [string[]]$ExcludePatterns = @(),
        [string[]]$SelectedPackageNames = @()
    )

    if (-not (Test-Path $OfflinePackagesFolder)) {
        return @()
    }

    $allowedExtensions = @('.exe', '.msi', '.msu', '.bat', '.cmd', '.ps1')
    $selectedNames = @($SelectedPackageNames | ForEach-Object { $_.ToLowerInvariant() })
    $packages = Get-ChildItem -Path $OfflinePackagesFolder -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $name = $_.Name.ToLowerInvariant()
            $extension = $_.Extension.ToLowerInvariant()
            if ($allowedExtensions -notcontains $extension) {
                return $false
            }

            foreach ($pattern in $ExcludePatterns) {
                if ($name -like $pattern.ToLowerInvariant()) {
                    return $false
                }
            }

            if ($selectedNames.Count -gt 0) {
                return ($selectedNames -contains $name) -or ($selectedNames -contains $_.FullName.ToLowerInvariant())
            }

            return $true
        } |
        Sort-Object Name

    return @($packages)
}

function Install-OfflinePackageFile {
    param(
        [System.IO.FileInfo]$Package
    )

    if (-not $Package) {
        return $false
    }

    $extension = $Package.Extension.ToLowerInvariant()

    try {
        switch ($extension) {
            '.msi' {
                $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList '/i', $Package.FullName, '/qn', '/norestart' -Wait -NoNewWindow -PassThru
                return $process.ExitCode -eq 0
            }
            '.msu' {
                $process = Start-Process -FilePath 'wusa.exe' -ArgumentList $Package.FullName, '/quiet', '/norestart' -Wait -NoNewWindow -PassThru
                return $process.ExitCode -eq 0
            }
            '.bat' {
                $process = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $Package.FullName -Wait -NoNewWindow -PassThru
                return $process.ExitCode -eq 0
            }
            '.cmd' {
                $process = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $Package.FullName -Wait -NoNewWindow -PassThru
                return $process.ExitCode -eq 0
            }
            '.ps1' {
                $process = Start-Process -FilePath 'powershell.exe' -ArgumentList '-ExecutionPolicy', 'Bypass', '-File', $Package.FullName -Wait -NoNewWindow -PassThru
                return $process.ExitCode -eq 0
            }
            default {
                $process = Start-Process -FilePath $Package.FullName -Wait -NoNewWindow -PassThru
                if ($null -ne $process) {
                    return $true
                }

                return $false
            }
        }
    }
    catch {
        Write-SetupWizardLog ("WARN: não foi possível instalar o pacote offline {0}: {1}" -f $Package.Name, $_.Exception.Message)
        return $false
    }
}

function Test-OfflinePackageInstalled {
    param(
        [System.IO.FileInfo]$Package
    )

    if (-not $Package) {
        return $false
    }

    $name = $Package.Name.ToLowerInvariant()
    if ($name -match 'dotnet|windowsdesktop-runtime|runtime') {
        return Test-DotNet8Runtime
    }

    if ($name -match 'vc_redist|vcredist') {
        return Test-VisualCppRedist
    }

    return $false
}

function Install-OfflinePackageFiles {
    param(
        [string[]]$ExcludePatterns = @(),
        [string[]]$SelectedPackageNames = @()
    )

    if ($SelectedPackageNames.Count -gt 0) {
        Write-SetupWizardLog ("INFO: Instaladores offline selecionados: {0}" -f ($SelectedPackageNames -join ', '))
    }

    $packages = Get-OfflineInstallerPackages -ExcludePatterns $ExcludePatterns -SelectedPackageNames $SelectedPackageNames
    if ($packages.Count -eq 0) {
        return @()
    }

    $installedPackages = @()
    foreach ($package in $packages) {
        Write-SetupWizardLog ("OFFLINE_SCAN: {0}" -f $package.Name)
        if (Test-OfflinePackageInstalled -Package $package) {
            Write-SetupWizardLog ("OFFLINE_SKIP: {0}" -f $package.Name)
            continue
        }

        Write-SetupWizardLog ("OFFLINE_INSTALLING: {0}" -f $package.Name)
        $installed = Install-OfflinePackageFile -Package $package
        if ($installed) {
            Write-SetupWizardLog ("OFFLINE_INSTALLED: {0}" -f $package.Name)
            $installedPackages += $package.FullName
        }
        else {
            Write-SetupWizardLog ("WARN: Falha ao instalar pacote offline: {0}" -f $package.Name)
        }
    }

    return $installedPackages
}

if (-not $SourceExePath -or -not (Test-Path $SourceExePath)) {
    $SourceAppPath = (Get-Location).Path
    $SourceExePath = Join-Path $SourceAppPath $ExeName
}

if (-not (Test-Path $SourceExePath)) {
    throw "Executável não encontrado para instalação: $SourceExePath"
}

function Test-DotNet8Runtime {
    try {
        $runtimeRoots = @(
            $env:ProgramFiles,
            ${env:ProgramFiles(x86)},
            $env:LOCALAPPDATA
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

        foreach ($root in $runtimeRoots) {
            $candidateDir = Join-Path $root 'dotnet\shared\Microsoft.NETCore.App'
            if (Test-Path $candidateDir) {
                $versions = Get-ChildItem -Path $candidateDir -Directory -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like '8.*' }

                if ($versions) {
                    return $true
                }
            }
        }

        $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1
        if ($dotnetCommand) {
            $output = & $dotnetCommand --list-runtimes 2>$null
            return $output -match 'Microsoft.NETCore.App 8'
        }

        return $false
    }
    catch {
        return $false
    }
}

function Test-VisualCppRedist {
    try {
        $keys = @(
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*'
        )

        foreach ($k in $keys) {
            $items = Get-ItemProperty $k -ErrorAction SilentlyContinue
            if ($items) {
                foreach ($it in $items) {
                    $dn = $it.DisplayName
                    if ($dn -and $dn -match 'Visual C\+\+' -and $dn -match 'Redistributable') {
                        return $true
                    }
                }
            }
        }

        return $false
    }
    catch {
        return $false
    }
}

function Test-WmiService {
    try {
        $status = sc.exe query winmgmt 2>$null
        return ($status -match 'RUNNING') -or ($status -match 'START_PENDING')
    }
    catch {
        return $false
    }
}

function Install-RequirementItem {
    param(
        [string]$ItemName,
        [scriptblock]$CheckScript,
        [scriptblock]$InstallScript
    )

    Write-SetupWizardLog "Verificando: $ItemName"
    if (& $CheckScript) {
        Write-SetupWizardLog "PASS: $ItemName já está presente no sistema. Tentando instalação/atualização de qualquer forma..."
    }
    else {
        Write-SetupWizardLog "FAIL: $ItemName não está presente. Iniciando instalação..."
    }

    try {
        Write-SetupWizardLog "DEBUG: Executando script de instalação para: $ItemName"
        & $InstallScript

        for ($attempt = 1; $attempt -le 3; $attempt++) {
            if (& $CheckScript) {
                Write-SetupWizardLog "INSTALLED: $ItemName instalado com sucesso."
                return $true
            }

            if ($attempt -lt 3) {
                Write-SetupWizardLog "WARN: verificação de instalação ainda não confirmou $ItemName. Tentando novamente em 3 segundos..."
                Start-Sleep -Seconds 3
            }
        }

        Write-SetupWizardLog "ERROR: A instalação de $ItemName não foi confirmada após a execução do instalador."
        return $false
    }
    catch {
        $ex = $_.Exception
        Write-SetupWizardLog ("ERROR: Falha ao instalar {0}: {1}\nStackTrace:\n{2}\nInvocationInfo:\n{3}" -f $ItemName, $ex.Message, $ex.StackTrace, $_.InvocationInfo | Out-String)
        return $false
    }
}

try {
    Write-SetupWizardLog '== Assistente de instalação do WYSHUB =='
    Write-SetupWizardLog "Log do assistente: $SetupWizardLogPath"
    Write-SetupWizardLog '1) Procurando instaladores offline na pasta de dependências...'

    Write-SetupWizardLog '2) Instalando apenas os pacotes offline encontrados na pasta de dependências...'
    $additionalOfflinePackages = Install-OfflinePackageFiles -ExcludePatterns @() -SelectedPackageNames $OfflineInstallers
    if ($additionalOfflinePackages.Count -gt 0) {
        Write-SetupWizardLog ("INFO: Pacotes offline processados: {0}" -f ($additionalOfflinePackages -join ', '))
    }
    else {
        Write-SetupWizardLog 'INFO: Nenhum pacote offline encontrado para instalar.'
    }

    Write-SetupWizardLog 'Assistente concluído.'
}
finally {
    Stop-Transcript | Out-Null
}
