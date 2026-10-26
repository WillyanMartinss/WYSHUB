using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace SystemWM.Services
{
    public enum RequirementStatus
    {
        Installed,
        Missing,
        Warning,
        Optional
    }

    public sealed class RequirementChecklistItem : INotifyPropertyChanged
    {
        private RequirementStatus _status;
        private string _detail = string.Empty;
        private string _currentState = string.Empty;
        private bool _isCompleted;
        private string _downloadUrl = string.Empty;

        public string Name { get; set; } = string.Empty;
        public bool IsOptional { get; set; }
        public RequirementStatus Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsSatisfied));
                OnPropertyChanged(nameof(HasDownloadLink));
            }
        }
        public string Detail
        {
            get => _detail;
            set
            {
                if (_detail == value) return;
                _detail = value;
                OnPropertyChanged(nameof(Detail));
            }
        }
        public string InstallHint { get; set; } = string.Empty;
        public string DownloadUrl
        {
            get => _downloadUrl;
            set
            {
                if (_downloadUrl == value) return;
                _downloadUrl = value;
                OnPropertyChanged(nameof(DownloadUrl));
                OnPropertyChanged(nameof(HasDownloadLink));
            }
        }
        public bool IsSatisfied => Status == RequirementStatus.Installed;
        public bool HasDownloadLink => !string.IsNullOrWhiteSpace(DownloadUrl) && !IsSatisfied;
        public string CurrentState
        {
            get => _currentState;
            set
            {
                if (_currentState == value) return;
                _currentState = value;
                OnPropertyChanged(nameof(CurrentState));
            }
        }
        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted == value) return;
                _isCompleted = value;
                OnPropertyChanged(nameof(IsCompleted));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void UpdateState(RequirementStatus status, string currentState)
        {
            Status = status;
            CurrentState = currentState;
        }

        public void MarkCompleted()
        {
            IsCompleted = true;
        }

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public static class RequirementChecklistService
    {
        public static List<RequirementChecklistItem> GetChecklist(string? baseDirectory = null)
        {
            var items = new List<RequirementChecklistItem>();
            items.AddRange(GetOfflineInstallerChecklistItems(baseDirectory));

            var requirementsPath = ResolveRequirementsPath(baseDirectory);
            var requirementsItems = ParseRequirementsFile(requirementsPath);

            foreach (var item in requirementsItems)
            {
                item.Status = EvalStatus(item.Name);
                item.Detail = BuildDetail(item.Name, item.Status);
                items.Add(item);
            }

            return items;
        }

        public static string? ResolveOfflinePackagesPath(string? baseDirectory = null)
        {
            var folderNames = new[] { "dependencias_offline", "dependencias" };
            var searchRoots = new List<string>();

            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                searchRoots.Add(Path.GetFullPath(baseDirectory));
            }

            searchRoots.Add(Path.GetFullPath(Directory.GetCurrentDirectory()));
            searchRoots.Add(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory));

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var searchRoot in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var current = searchRoot;

                while (!string.IsNullOrWhiteSpace(current))
                {
                    if (visited.Add(current))
                    {
                        foreach (var folderName in folderNames)
                        {
                            var candidate = Path.Combine(current, folderName);
                            if (Directory.Exists(candidate))
                            {
                                return candidate;
                            }
                        }
                    }

                    current = Directory.GetParent(current)?.FullName;
                }
            }

            return null;
        }

        public static List<string> GetOfflineInstallerFiles(string? baseDirectory = null)
        {
            var offlineFolder = ResolveOfflinePackagesPath(baseDirectory);
            if (string.IsNullOrWhiteSpace(offlineFolder) || !Directory.Exists(offlineFolder))
            {
                return new List<string>();
            }

            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".exe",
                ".msi",
                ".msu",
                ".bat",
                ".cmd",
                ".ps1"
            };

            return Directory.GetFiles(offlineFolder, "*", SearchOption.AllDirectories)
                .Where(path => allowedExtensions.Contains(Path.GetExtension(path)))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<RequirementChecklistItem> GetOfflineInstallerChecklistItems(string? baseDirectory = null)
        {
            return GetOfflineInstallerFiles(baseDirectory)
                .Select(path => new RequirementChecklistItem
                {
                    Name = $"Instalador local: {Path.GetFileName(path)}",
                    Status = RequirementStatus.Missing,
                    Detail = "Instalador encontrado na pasta de dependências. O assistente verificará e instalará se ainda não estiver presente.",
                    CurrentState = "Pendente de verificação.",
                    InstallHint = "Executar o instalador localizado na pasta local.",
                    IsOptional = false
                })
                .ToList();
        }

        public static string ResolveRequirementsPath(string? baseDirectory = null)
        {
            var searchRoot = string.IsNullOrWhiteSpace(baseDirectory)
                ? AppDomain.CurrentDomain.BaseDirectory
                : baseDirectory;

            var candidates = new[]
            {
                Path.Combine(searchRoot, "requirements.txt"),
                Path.Combine(searchRoot, "..", "..", "..", "requirements.txt"),
                Path.Combine(Directory.GetCurrentDirectory(), "requirements.txt"),
                Path.Combine(Path.GetDirectoryName(searchRoot) ?? searchRoot, "requirements.txt")
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException("Arquivo requirements.txt não encontrado no diretório do projeto.");
        }

        public static List<RequirementChecklistItem> ParseRequirementsFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Arquivo requirements.txt não encontrado.", filePath);

            var items = new List<RequirementChecklistItem>();
            var inOptionalSection = false;
            var lines = File.ReadAllLines(filePath);

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith("Opcional", StringComparison.OrdinalIgnoreCase))
                {
                    inOptionalSection = true;
                    continue;
                }

                if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
                {
                    var name = line.Substring(2).Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (name.StartsWith("pasta do programa", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("exclusão do Defender", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("assinatura do executável", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    items.Add(new RequirementChecklistItem
                    {
                        Name = name,
                        IsOptional = inOptionalSection,
                        Status = RequirementStatus.Warning,
                        InstallHint = BuildInstallHint(name),
                        DownloadUrl = BuildDownloadUrl(name)
                    });
                    continue;
                }

                if (!line.StartsWith("- ", StringComparison.Ordinal) && !line.StartsWith("* ", StringComparison.Ordinal))
                {
                    if (line.Contains("x64") || line.Contains("runtime") || line.Contains("PowerShell") || line.Contains("WMI") || line.Contains("Visual C++") || line.Contains("firewall"))
                    {
                        items.Add(new RequirementChecklistItem
                        {
                            Name = line,
                            IsOptional = inOptionalSection,
                            Status = RequirementStatus.Warning,
                            InstallHint = BuildInstallHint(line),
                            DownloadUrl = BuildDownloadUrl(line)
                        });
                    }
                }
            }

            return items;
        }

        private static RequirementStatus EvalStatus(string name)
        {
            var normalized = name.ToLowerInvariant();

            if (normalized.Contains("windows 10/11 x64") || normalized.Contains("windows 10") || normalized.Contains("windows 11"))
                return IsWindowsSupported() ? RequirementStatus.Installed : RequirementStatus.Missing;

            if (normalized.Contains(".net 8") || normalized.Contains("runtime .net 8") || normalized.Contains("desktop runtime x64"))
                return HasDotNetRuntime8() ? RequirementStatus.Installed : RequirementStatus.Missing;

            if (normalized.Contains("visual c++ redistributable"))
                return HasVisualCppRedist() ? RequirementStatus.Installed : RequirementStatus.Missing;

            if (normalized.Contains("powershell 5.1") || normalized.Contains("windows powershell"))
                return RequirementStatus.Installed;

            if (normalized.Contains("wmi") || normalized.Contains("windows management instrumentation"))
                return IsWmiEnabled() ? RequirementStatus.Installed : RequirementStatus.Missing;

            if (normalized.Contains("firewall"))
                return IsWindowsFirewallEnabled() ? RequirementStatus.Installed : RequirementStatus.Missing;

            if (normalized.Contains("acesso de administrador") || normalized.Contains("administrador"))
                return IsAdministrator() ? RequirementStatus.Installed : RequirementStatus.Warning;

            if (normalized.Contains("defender"))
                return RequirementStatus.Optional;

            return RequirementStatus.Warning;
        }

        private static string BuildDetail(string name, RequirementStatus status)
        {
            return status switch
            {
                RequirementStatus.Installed => $"OK - {name} já está disponível no sistema.",
                RequirementStatus.Missing => $"Pendente - {name} precisa ser baixado e instalado.",
                RequirementStatus.Warning => $"Verificação manual - {name} requer atenção para confirmar o estado.",
                RequirementStatus.Optional => $"Opcional - {name} pode ser aplicado quando necessário.",
                _ => $"Estado indefinido - {name}"
            };
        }

        private static string BuildInstallHint(string name)
        {
            if (name.Contains(".NET 8", StringComparison.OrdinalIgnoreCase))
                return "Baixar e instalar o runtime .NET 8 Desktop da Microsoft (winget ou instalador oficial).";

            if (name.Contains("Visual C++", StringComparison.OrdinalIgnoreCase))
                return "Baixar e instalar o redistribuível Microsoft Visual C++ 2015-2022 (x64).";

            if (name.Contains("WMI", StringComparison.OrdinalIgnoreCase))
                return "Ativar e iniciar o serviço Winmgmt com permissões de administrador.";

            if (name.Contains("Firewall", StringComparison.OrdinalIgnoreCase))
                return "Habilitar o Firewall do Windows para os perfis necessário.";

            return "Usar o instalador oficial ou o pacote recomendado pela Microsoft.";
        }

        private static string BuildDownloadUrl(string name)
        {
            if (name.Contains(".NET 8", StringComparison.OrdinalIgnoreCase))
                return "https://dotnet.microsoft.com/pt-br/download/dotnet/thank-you/runtime-desktop-8.0-windows-x64-installer";

            if (name.Contains("Visual C++", StringComparison.OrdinalIgnoreCase))
                return "https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist?view=msvc-170";

            return string.Empty;
        }

        private static bool IsWindowsSupported()
        {
            return Environment.OSVersion.Version.Major >= 10;
        }

        private static bool HasDotNetRuntime8()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "--list-runtimes",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                    return false;

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return output.Contains("Microsoft.NETCore.App 8", StringComparison.OrdinalIgnoreCase)
                    || error.Contains("Microsoft.NETCore.App 8", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool HasVisualCppRedist()
        {
            var uninstallNames = new[]
            {
                "Microsoft Visual C++ 2015-2022 Redistributable (x64)",
                "Microsoft Visual C++ 2015-2022 Redistributable (x86)",
                "Microsoft Visual C++ 2015-2022 Redistributable"
            };

            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (key == null)
                return false;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(subKeyName);
                var displayName = subKey?.GetValue("DisplayName") as string;
                if (uninstallNames.Any(name => string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        private static bool IsWmiEnabled()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "query winmgmt",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return false;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("START_PENDING", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWindowsFirewallEnabled()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall show allprofiles",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                    return false;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output.Contains("Firewall is ON", StringComparison.OrdinalIgnoreCase)
                    || output.Contains("Firewall is OFF", StringComparison.OrdinalIgnoreCase) == false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAdministrator()
        {
            return Environment.UserName.Equals("Administrator", StringComparison.OrdinalIgnoreCase)
                || Environment.GetEnvironmentVariable("USERNAME")?.Contains("Administrator", StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}
