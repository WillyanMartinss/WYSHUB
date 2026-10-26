using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SystemWM.Services;

namespace SystemWM.Views
{
    public sealed class OfflineInstallerOption : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string DisplayName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public partial class SetupWizardWindow : Window
    {
        private readonly string _scriptPath = SetupWizardPathResolver.ResolveScriptPath();
        private readonly ObservableCollection<RequirementChecklistItem> _requisitos = new();
        private readonly ObservableCollection<OfflineInstallerOption> _offlineInstallers = new();
        private readonly StringBuilder _wizardLog = new();
        private string _wizardLogPath = string.Empty;
        private int _totalRequisitos;
        private int _requisitosConcluidos;

        public SetupWizardWindow()
        {
            InitializeComponent();
            LstOfflineInstallers.ItemsSource = _offlineInstallers;
            CarregarOfflineInstallers();
            CarregarChecklist();
            TxtStatus.Text = "Pronto para preparar o ambiente.";
            TxtDetalhes.Text = "O assistente varrerá primeiro a pasta dependencias_offline, mostrará os instaladores disponíveis e instalará o que ainda não estiver presente.";
            TxtWizardLog.Visibility = Visibility.Visible;
            TxtLogLabel.Visibility = Visibility.Visible;
            TxtConclusao.Visibility = Visibility.Collapsed;
            BtnFechar.Visibility = Visibility.Collapsed;
            TxtProgresso.Visibility = Visibility.Collapsed;
        }

        private void CarregarChecklist(bool preservarConclusao = false)
        {
            try
            {
                var estadosAnteriores = preservarConclusao
                    ? _requisitos.ToDictionary(item => item.Name, item => new { item.IsCompleted, item.CurrentState }, StringComparer.OrdinalIgnoreCase)
                    : null;

                _requisitos.Clear();
                var checklist = RequirementChecklistService.GetChecklist(AppDomain.CurrentDomain.BaseDirectory);
                foreach (var item in checklist)
                {
                    if (preservarConclusao && estadosAnteriores != null && estadosAnteriores.TryGetValue(item.Name, out var estadoAnterior))
                    {
                        item.IsCompleted = estadoAnterior.IsCompleted;
                        item.CurrentState = estadoAnterior.IsCompleted
                            ? (string.IsNullOrWhiteSpace(estadoAnterior.CurrentState) ? "Concluído." : estadoAnterior.CurrentState)
                            : "Pendente de verificação.";
                        if (!item.IsCompleted)
                        {
                            item.Status = RequirementStatus.Missing;
                            item.Detail = "Pendente - aguardando verificação e instalação.";
                        }
                    }
                    else
                    {
                        item.IsCompleted = false;
                        item.Status = RequirementStatus.Missing;
                        item.Detail = "Pendente - aguardando verificação e instalação.";
                        item.CurrentState = "Pendente de verificação.";
                    }

                    _requisitos.Add(item);
                }

                _totalRequisitos = _requisitos.Count;
                _requisitosConcluidos = _requisitos.Count(item => item.IsCompleted);
                AtualizarResumoDependencias();
                TxtWizardLog.Visibility = Visibility.Visible;
                TxtLogLabel.Visibility = Visibility.Visible;

                TxtDetalhes.Text = "O assistente prioriza os instaladores da pasta dependencias_offline, depois verifica os requisitos do arquivo de requisitos e instala o que estiver faltando.";
            }
            catch (Exception ex)
            {
                TxtWizardLog.Text = $"Falha ao ler o arquivo de requisitos: {ex.Message}";
                TxtDetalhes.Text = ex.Message;
            }
        }

        private void CarregarOfflineInstallers()
        {
            _offlineInstallers.Clear();
            try
            {
                var offlineFolder = RequirementChecklistService.ResolveOfflinePackagesPath(AppDomain.CurrentDomain.BaseDirectory);
                if (string.IsNullOrWhiteSpace(offlineFolder) || !Directory.Exists(offlineFolder))
                {
                    TxtOfflineSummary.Text = "Nenhum instalador local encontrado.";
                    return;
                }

                var installers = RequirementChecklistService.GetOfflineInstallerFiles(AppDomain.CurrentDomain.BaseDirectory)
                    .Select(path => new OfflineInstallerOption
                    {
                        DisplayName = Path.GetFileName(path),
                        FullPath = path,
                        StatusText = "Disponível para instalação",
                        IsSelected = true
                    });

                foreach (var installer in installers)
                {
                    _offlineInstallers.Add(installer);
                }

                TxtOfflineSummary.Text = _offlineInstallers.Count == 0
                    ? "Nenhum instalador local encontrado."
                    : $"{_offlineInstallers.Count} instalador(es) local(is) disponível(is).";
            }
            catch (Exception ex)
            {
                TxtOfflineSummary.Text = $"Não foi possível carregar a pasta offline: {ex.Message}";
            }
        }

        private static bool IsOfflineInstaller(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".msi", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".msu", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase);
        }

        private void ProcessPowerShellOutputLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            TxtWizardLog.Text += line + Environment.NewLine;
            TxtWizardLog.Visibility = Visibility.Visible;
            TxtLogLabel.Visibility = Visibility.Visible;

            if (line.StartsWith("Verificando:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = line[("Verificando:".Length)..].Trim();
                UpdateCurrentItem(itemName, RequirementStatus.Warning, "Verificando...");
                TxtStatus.Text = $"Verificando: {itemName}";
                TxtDetalhes.Text = "Aguarde enquanto cada requisito é examinado.";
                AtualizarProgresso("Verificando dependência", itemName);
                return;
            }

            if (line.StartsWith("OFFLINE_SCAN:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = line[("OFFLINE_SCAN:".Length)..].Trim();
                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    EnsureOfflineItem(itemName, RequirementStatus.Warning, "Instalador encontrado na pasta offline.");
                    TxtStatus.Text = $"Varredura offline: {itemName}";
                    TxtDetalhes.Text = "Varredura inicial da pasta offline em andamento.";
                }
                return;
            }

            if (line.StartsWith("OFFLINE_INSTALLING:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = line[("OFFLINE_INSTALLING:".Length)..].Trim();
                UpdateCurrentItem(itemName, RequirementStatus.Missing, "Instalando pacote offline...");
                TxtStatus.Text = $"Instalando offline: {itemName}";
                TxtDetalhes.Text = "O assistente está instalando os programas encontrados na pasta offline.";
                AtualizarProgresso("Instalação offline", itemName);
                return;
            }

            if (line.StartsWith("OFFLINE_SKIP:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = line[("OFFLINE_SKIP:".Length)..].Trim();
                CompletarItem(itemName, RequirementStatus.Installed, "Já presente no computador.", "Já presente no computador", $"Já presente: {itemName}");
                return;
            }

            if (line.StartsWith("OFFLINE_INSTALLED:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = line[("OFFLINE_INSTALLED:".Length)..].Trim();
                CompletarItem(itemName, RequirementStatus.Installed, "Instalado com sucesso.", "Instalação concluída", $"Instalação concluída: {itemName}");
                return;
            }

            if (line.StartsWith("PASS:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = ExtractItemName(line, "PASS:", " já está presente");
                UpdateCurrentItem(itemName, RequirementStatus.Warning, "Já presente; confirmando instalação...");
                TxtStatus.Text = $"Verificando: {itemName}";
                return;
            }

            if (line.StartsWith("FAIL:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = ExtractItemName(line, "FAIL:", " não está presente");
                CompletarItem(itemName, RequirementStatus.Missing, "Faltando - iniciando instalação...", "Instalação necessária", $"Instalando: {itemName}");
                TxtStatus.Text = $"Instalando: {itemName}";
                return;
            }

            if (line.StartsWith("DEBUG: Executando script de instalação para:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = line[("DEBUG: Executando script de instalação para:".Length)..].Trim();
                UpdateCurrentItem(itemName, RequirementStatus.Missing, "Executando instalador...");
                TxtDetalhes.Text = $"Instalação em andamento: {itemName}";
                AtualizarProgresso("Instalação em andamento", itemName);
                return;
            }

            if (line.StartsWith("INFO: Baixando", StringComparison.OrdinalIgnoreCase))
            {
                TxtDetalhes.Text = "Download em andamento...";
                AtualizarProgresso("Download em andamento");
                return;
            }

            if (line.StartsWith("INFO: Executando instalador", StringComparison.OrdinalIgnoreCase))
            {
                TxtDetalhes.Text = "Instalação em andamento...";
                AtualizarProgresso("Instalação em andamento");
                return;
            }

            if (line.StartsWith("INSTALLED:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = ExtractItemName(line, "INSTALLED:", " instalado com sucesso");
                CompletarItem(itemName, RequirementStatus.Installed, "Instalado com sucesso.", "Instalação concluída", $"Instalação concluída: {itemName}");
                TxtStatus.Text = $"Instalação concluída: {itemName}";
                return;
            }

            if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                var itemName = ExtractItemName(line, "ERROR: A instalação de ", " não foi confirmada");
                if (itemName != null)
                {
                    CompletarItem(itemName, RequirementStatus.Warning, "Falha ao confirmar instalação.", "Falha na instalação", $"Falha na instalação: {itemName}");
                    TxtStatus.Text = $"Erro na instalação: {itemName}";
                    return;
                }

                itemName = ExtractItemName(line, "ERROR: Falha ao instalar ", ":");
                if (itemName != null)
                {
                    CompletarItem(itemName, RequirementStatus.Warning, "Falha ao instalar.", "Falha na instalação", $"Falha na instalação: {itemName}");
                    TxtStatus.Text = $"Erro na instalação: {itemName}";
                }
            }
        }

        private void UpdateCurrentItem(string itemName, RequirementStatus status, string state)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            var item = FindRequirementItem(itemName);
            if (item == null)
                return;

            item.UpdateState(status, state);
            AtualizarResumoDependencias();
        }

        private void CompletarItem(string? itemName, RequirementStatus status, string state, string progressDetail, string statusDetail)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            var item = FindRequirementItem(itemName);
            if (item == null)
                return;

            item.UpdateState(status, state);
            if (!item.IsCompleted)
            {
                item.MarkCompleted();
                _requisitosConcluidos = Math.Min(_totalRequisitos, _requisitosConcluidos + 1);
            }

            TxtDetalhes.Text = statusDetail;
            AtualizarProgresso(progressDetail, itemName);
            AtualizarResumoDependencias();
        }

        private void AtualizarResumoDependencias()
        {
            if (_requisitos.Count == 0)
            {
                TxtWizardLog.Text = "Nenhuma dependência carregada ainda.";
                return;
            }

            var linhas = new List<string> { "Resumo das dependências:" };
            foreach (var item in _requisitos)
            {
                var simbolo = item.IsCompleted ? "✓" : "•";
                var estado = item.IsCompleted ? "concluída" : "pendente";
                linhas.Add($"{simbolo} {item.Name} — {estado}");
            }

            TxtWizardLog.Text = string.Join(Environment.NewLine, linhas);
            TxtWizardLog.Visibility = Visibility.Visible;
            TxtLogLabel.Visibility = Visibility.Visible;
        }

        private void AtualizarProgresso(string detalhe, string? itemName = null)
        {
            if (_totalRequisitos <= 0)
            {
                ProgressBarInstall.Value = 0;
                TxtProgresso.Text = "0% concluído";
                TxtProgresso.Visibility = Visibility.Visible;
                return;
            }

            var percentual = _requisitosConcluidos * 100 / _totalRequisitos;
            ProgressBarInstall.Value = Math.Min(100, Math.Max(0, percentual));
            var textoItem = string.IsNullOrWhiteSpace(itemName) ? string.Empty : $" • {itemName}";
            TxtProgresso.Text = $"{percentual}% concluído{textoItem}";
            TxtProgresso.Visibility = Visibility.Visible;
        }

        private RequirementChecklistItem? FindRequirementItem(string? itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return null;

            var normalizedItemName = itemName.Trim();
            return _requisitos.FirstOrDefault(x =>
                RequirementNameMatcher.AreEquivalent(x.Name, normalizedItemName) ||
                x.Name.Contains(normalizedItemName, StringComparison.OrdinalIgnoreCase) ||
                normalizedItemName.Contains(x.Name, StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureOfflineItem(string? itemName, RequirementStatus status, string state)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            var existing = FindRequirementItem(itemName);
            if (existing != null)
            {
                existing.UpdateState(status, state);
                AtualizarResumoDependencias();
                return;
            }

            var newItem = new RequirementChecklistItem
            {
                Name = $"Instalador local: {itemName}",
                Status = status,
                Detail = "Instalador encontrado na pasta dependencias_offline.",
                CurrentState = state,
                IsCompleted = false
            };

            _requisitos.Insert(0, newItem);
            _totalRequisitos = _requisitos.Count;
            _requisitosConcluidos = _requisitos.Count(item => item.IsCompleted);
            AtualizarResumoDependencias();
        }

        private static string? ExtractItemName(string line, string prefix, string suffix)
        {
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;

            var candidate = line.Substring(prefix.Length).Trim();
            var endIndex = candidate.IndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (endIndex > 0)
                return candidate.Substring(0, endIndex).Trim();

            return candidate;
        }

        private void LoadLatestLogToUi()
        {
            try
            {
                var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemWM", "Logs");
                if (!Directory.Exists(pasta))
                {
                    TxtWizardLog.Text = string.Empty;
                    return;
                }

                var file = Directory.GetFiles(pasta, "SetupWizard_*.log")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();

                if (file == null)
                {
                    TxtWizardLog.Text = string.Empty;
                    return;
                }

                TxtWizardLog.Text = File.ReadAllText(file);
            }
            catch
            {
                TxtWizardLog.Text = string.Empty;
            }
        }

        private void RegistrarLog(string mensagem)
        {
            var linha = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {mensagem}";
            _wizardLog.AppendLine(linha);
        }

        private void SalvarLogArquivo()
        {
            try
            {
                var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemWM", "Logs");
                Directory.CreateDirectory(pasta);
                _wizardLogPath = Path.Combine(pasta, $"SetupWizard_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.WriteAllText(_wizardLogPath, _wizardLog.ToString());
            }
            catch
            {
                _wizardLogPath = string.Empty;
            }
        }

        private async void BtnInstalar_Click(object sender, RoutedEventArgs e)
        {
            await ExecutarAssistenteAsync();
        }

        private void BtnSelecionarTodos_Click(object sender, RoutedEventArgs e)
        {
            foreach (var installer in _offlineInstallers)
            {
                installer.IsSelected = true;
            }
        }

        private void BtnLimparSelecao_Click(object sender, RoutedEventArgs e)
        {
            foreach (var installer in _offlineInstallers)
            {
                installer.IsSelected = false;
            }
        }

        private async void BtnInstalarSelecionados_Click(object sender, RoutedEventArgs e)
        {
            var selectedNames = _offlineInstallers.Where(x => x.IsSelected).Select(x => x.DisplayName).ToArray();
            if (selectedNames.Length == 0)
            {
                MessageBox.Show("Selecione pelo menos um instalador local antes de continuar.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await ExecutarAssistenteAsync(selectedNames);
        }

        private void BtnPular_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnFechar_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnAbrirDownload_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            var item = button.Tag as RequirementChecklistItem;
            if (item == null || string.IsNullOrWhiteSpace(item.DownloadUrl))
                return;

            try
            {
                if (Uri.TryCreate(item.DownloadUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("Não foi possível abrir o link de download.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir o link de download:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnElevate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? "WYSHUB.exe";
                var startInfo = new ProcessStartInfo
                {
                    FileName = currentExe,
                    Verb = "runas",
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível reabrir como administrador:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private async Task ExecutarAssistenteAsync(IReadOnlyCollection<string>? selectedOfflineInstallers = null)
        {
            _wizardLog.Clear();
            RegistrarLog("Iniciando assistente de instalação do WYSHUB.");
            RegistrarLog($"Script de instalação localizado em: {_scriptPath}");
            RegistrarLog($"Diretório de base da aplicação: {AppDomain.CurrentDomain.BaseDirectory}");

            BtnInstalar.Visibility = Visibility.Collapsed;
            BtnInstalar.IsEnabled = false;
            BtnPular.IsEnabled = true;
            BtnFechar.Visibility = Visibility.Collapsed;
            ProgressBarInstall.Visibility = Visibility.Visible;
            ProgressBarInstall.Value = 0;
            TxtProgresso.Visibility = Visibility.Visible;
            TxtConclusao.Visibility = Visibility.Collapsed;
            TxtStatus.Text = "Verificando e instalando requisitos...";
            TxtDetalhes.Text = "Aguarde. O assistente varrerá a pasta dependencias_offline, instalará os programas disponíveis e só então seguirá para os requisitos principais.";
            AtualizarProgresso("Iniciando verificação");

            try
            {
                if (!File.Exists(_scriptPath))
                {
                    throw new FileNotFoundException("Script de instalação não encontrado", _scriptPath);
                }

                RegistrarLog("Validação do script concluída.");

                if (!IsAdministrator())
                {
                    RegistrarLog("Aplicação sem privilégios de administrador. Reabrindo com UAC.");
                    var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? "WYSHUB.exe";
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = currentExe,
                        Verb = "runas",
                        UseShellExecute = true
                    };

                    Process.Start(startInfo);
                    Environment.Exit(0);
                    return;
                }

                RegistrarLog("Privilégios de administrador confirmados.");

                var appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                // Sanitize paths to avoid trailing-backslash escaping closing quote on command line
                var scriptPathArg = _scriptPath?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? _scriptPath;
                var baseDirArg = appBaseDirectory?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? appBaseDirectory;

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                psi.ArgumentList.Add("-ExecutionPolicy");
                psi.ArgumentList.Add("Bypass");
                psi.ArgumentList.Add("-File");
                psi.ArgumentList.Add(scriptPathArg);
                psi.ArgumentList.Add("-SourcePath");
                psi.ArgumentList.Add(baseDirArg);

                if (selectedOfflineInstallers != null && selectedOfflineInstallers.Count > 0)
                {
                    psi.ArgumentList.Add("-OfflineInstallers");
                    foreach (var installer in selectedOfflineInstallers)
                    {
                        psi.ArgumentList.Add(installer);
                    }

                    RegistrarLog($"Instaladores locais selecionados: {string.Join(", ", selectedOfflineInstallers)}");
                }

                RegistrarLog($"Executando PowerShell com script: {scriptPathArg} e SourcePath: {baseDirArg}");
                using var process = Process.Start(psi);
                if (process == null)
                    throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");

                var outputBuilder = new StringBuilder();
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data == null)
                        return;

                    Dispatcher.Invoke(() => ProcessPowerShellOutputLine(args.Data));
                    outputBuilder.AppendLine(args.Data);
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data == null)
                        return;

                    Dispatcher.Invoke(() => ProcessPowerShellOutputLine(args.Data));
                    outputBuilder.AppendLine(args.Data);
                };

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                var output = outputBuilder.ToString();
                RegistrarLog($"Saída do PowerShell:\n{output}");
                RegistrarLog($"ExitCode retornado: {process.ExitCode}");

                // Atualiza a checklist e carrega o log gerado pelo script para a UI
                CarregarOfflineInstallers();
                CarregarChecklist(preservarConclusao: true);
                LoadLatestLogToUi();

                if (process.ExitCode == 0)
                {
                    TxtStatus.Text = "Ambiente preparado com sucesso.";
                    TxtDetalhes.Text = "O programa pode ser aberto agora.";
                    TxtConclusao.Text = "Assistente concluído com sucesso. O ambiente está pronto para uso.";
                    TxtConclusao.Visibility = Visibility.Visible;
                    BtnFechar.Visibility = Visibility.Visible;
                    BtnPular.Visibility = Visibility.Collapsed;
                    RegistrarLog("Assistente concluído com sucesso.");
                    AtualizarResumoDependencias();
                    AtualizarProgresso("Assistente concluído", string.Empty);
                    MessageBox.Show("Assistente concluído com sucesso.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    TxtStatus.Text = "A instalação foi concluída com avisos ou falhas.";
                    TxtDetalhes.Text = "Verifique o log abaixo e, se necessário, execute o assistente novamente.";
                    TxtConclusao.Text = "O assistente terminou com avisos. Revise o log antes de continuar.";
                    TxtConclusao.Visibility = Visibility.Visible;
                    BtnFechar.Visibility = Visibility.Visible;
                    BtnPular.Visibility = Visibility.Collapsed;
                    RegistrarLog("Assistente retornou falha ou aviso no processo PowerShell.");
                    SalvarLogArquivo();
                    AtualizarResumoDependencias();
                    AtualizarProgresso("Execução finalizada com avisos");
                    MessageBox.Show($"A instalação retornou um erro.\nLog do assistente salvo em:\n{_wizardLogPath}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                RegistrarLog($"Exceção capturada no assistente: {ex}");
                SalvarLogArquivo();
                TxtStatus.Text = "Falha no assistente.";
                TxtDetalhes.Text = ex.Message;
                TxtConclusao.Text = "Não foi possível concluir a instalação. Confira o log e tente novamente.";
                TxtConclusao.Visibility = Visibility.Visible;
                BtnFechar.Visibility = Visibility.Visible;
                BtnPular.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Erro ao executar o assistente:\n{ex.Message}\n\nLog detalhado salvo em:\n{_wizardLogPath}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnInstalar.IsEnabled = false;
                BtnPular.IsEnabled = true;
                ProgressBarInstall.Visibility = Visibility.Visible;
                BtnInstalar.Visibility = Visibility.Collapsed;
            }
        }
    }
}
