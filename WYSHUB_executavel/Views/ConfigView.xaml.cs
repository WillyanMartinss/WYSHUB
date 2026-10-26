using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SystemWM.Services;

namespace SystemWM.Views
{
    public partial class ConfigView : UserControl
    {
        private bool _apiValida;

        public ConfigView()
        {
            InitializeComponent();
            var s = AppState.Settings.Carregar();
            TxtApiKey.Password = s.ResendApiKey;
            TxtApiKeyVisivel.Text = s.ResendApiKey;
            CarregarBackupConfig(s);
            AtualizarStatusApi(null, "Sem validação");
        }

        private void AtualizarStatusApi(bool? valido, string mensagem)
        {
            _apiValida = valido == true;

            if (valido == true)
            {
                StatusApiBadge.Background = new SolidColorBrush(Colors.LimeGreen);
                TxtStatusApi.Text = "Válida";
            }
            else if (valido == false)
            {
                StatusApiBadge.Background = new SolidColorBrush(Colors.IndianRed);
                TxtStatusApi.Text = "Inválida";
            }
            else
            {
                StatusApiBadge.Background = new SolidColorBrush(Colors.LightGray);
                TxtStatusApi.Text = "Sem validação";
            }

            TxtStatus.Text = mensagem;
        }

        private void CarregarBackupConfig(AppSettings settings)
        {
            TxtBackupPastaSelecionada.Text = string.IsNullOrWhiteSpace(settings.PastaBackupLimpeza)
                ? "Nenhuma pasta de backup selecionada"
                : settings.PastaBackupLimpeza;

            ChkLimparAutomaticamenteBackup.IsChecked = settings.LimparAutomaticamentePastaBackup;
            AtualizarEstadoRetencaoBackup();

            ChkUsarPastaBackup.IsChecked = settings.UsarBackupLimpeza;
            BtnSelecionarPastaBackupSettings.IsEnabled = settings.UsarBackupLimpeza;
            AtualizarBotaoBackupCorSettings();

            switch (settings.DiasRetencaoPastaBackup)
            {
                case 60:
                    RbRetencao60.IsChecked = true;
                    break;
                case 90:
                    RbRetencao90.IsChecked = true;
                    break;
                default:
                    RbRetencao30.IsChecked = true;
                    break;
            }
        }

        private void ChkLimparAutomaticamenteBackup_Checked(object sender, RoutedEventArgs e)
        {
            AtualizarEstadoRetencaoBackup();
        }

        private void AtualizarEstadoRetencaoBackup()
        {
            var habilitado = ChkLimparAutomaticamenteBackup.IsChecked == true;
            RbRetencao30.IsEnabled = habilitado;
            RbRetencao60.IsEnabled = habilitado;
            RbRetencao90.IsEnabled = habilitado;
        }

        private void AtualizarBotaoBackupCorSettings()
        {
            if (BtnSelecionarPastaBackupSettings == null)
                return;

            if (BtnSelecionarPastaBackupSettings.IsEnabled)
            {
                BtnSelecionarPastaBackupSettings.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                BtnSelecionarPastaBackupSettings.Foreground = Brushes.White;
            }
            else
            {
                BtnSelecionarPastaBackupSettings.Background = new SolidColorBrush(Color.FromRgb(75, 85, 99));
                BtnSelecionarPastaBackupSettings.Foreground = Brushes.White;
            }
        }

        private void ChkUsarPastaBackup_Changed(object sender, RoutedEventArgs e)
        {
            var habilitado = ChkUsarPastaBackup.IsChecked == true;
            BtnSelecionarPastaBackupSettings.IsEnabled = habilitado;
            AtualizarBotaoBackupCorSettings();

            try
            {
                var settings = AppState.Settings.Carregar();
                settings.UsarBackupLimpeza = habilitado;
                AppState.Settings.Salvar(settings);
            }
            catch { }
        }

        private void BtnSelecionarPastaBackupSettings_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecionar pasta de backup para arquivos limpos",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            try
            {
                var settings = AppState.Settings.Carregar();
                settings.PastaBackupLimpeza = dialog.SelectedPath;
                settings.UsarBackupLimpeza = true;
                AppState.Settings.Salvar(settings);
                ChkUsarPastaBackup.IsChecked = true;
                BtnSelecionarPastaBackupSettings.IsEnabled = true;
                TxtBackupPastaSelecionada.Text = dialog.SelectedPath;
                AtualizarBotaoBackupCorSettings();
            }
            catch
            {
                MessageBox.Show("Não foi possível salvar a pasta de backup.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnRestaurarBackup_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppState.Settings.Carregar();
            if (string.IsNullOrWhiteSpace(settings.PastaBackupLimpeza) || !Directory.Exists(settings.PastaBackupLimpeza))
            {
                MessageBox.Show("Nenhuma pasta de backup válida foi encontrada para restaurar.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new RestoreConfirmDialog { Owner = Window.GetWindow(this) };
            var resultado = dlg.ShowDialog();
            if (resultado != true)
                return;

            try
            {
                var backupDir = settings.PastaBackupLimpeza;
                var arquivos = Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories);
                foreach (var arquivo in arquivos)
                {
                    var destino = arquivo.Replace(backupDir, AppDomain.CurrentDomain.BaseDirectory);
                    var destinoDir = Path.GetDirectoryName(destino);
                    if (!string.IsNullOrWhiteSpace(destinoDir))
                        Directory.CreateDirectory(destinoDir);

                    File.Copy(arquivo, destino, true);
                }

                MessageBox.Show("Backup restaurado com sucesso.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao restaurar backup: {ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static bool ValorEhInvalido(string? valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return true;

            var texto = valor.Trim();
            return texto.Contains("Adrian Gurvitz", StringComparison.OrdinalIgnoreCase)
                || texto.Contains("Classic", StringComparison.OrdinalIgnoreCase)
                || texto.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
                || texto.Contains("example", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> ValidarApiAsync(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey) || ValorEhInvalido(apiKey))
                return false;

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

                var payload = new
                {
                    from = "SystemWM <onboarding@resend.dev>",
                    to = new[] { "delivered@resend.dev" },
                    subject = "Teste de API",
                    text = "Teste de API do SystemWM"
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.resend.com/emails", content);
                var resposta = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void BtnMostrarApi_Click(object sender, RoutedEventArgs e)
        {
            if (TxtApiKeyVisivel.Visibility == Visibility.Visible)
                return;

            if (TxtApiKey.Password == "")
            {
                TxtApiKey.Password = AppState.Settings.ObterApiKeyVigente();
            }

            SenhaApiContainer.Visibility = Visibility.Visible;
            TxtSenhaApi.Clear();
            TxtSenhaApiErro.Visibility = Visibility.Collapsed;
            TxtSenhaApi.Focus();
        }

        private void BtnConfirmarSenhaApi_Click(object sender, RoutedEventArgs e)
        {
            if (TxtSenhaApi.Password == "2020")
            {
                var apiAtual = AppState.Settings.ObterApiKeyVigente();

                if (string.IsNullOrWhiteSpace(apiAtual))
                {
                    TxtSenhaApiErro.Text = "Nenhuma API salva.";
                    TxtSenhaApiErro.Visibility = Visibility.Visible;
                    return;
                }

                TxtApiKey.Password = "";
                TxtApiKey.Visibility = Visibility.Collapsed;
                TxtApiKeyVisivel.Text = apiAtual;
                TxtApiKeyVisivel.Visibility = Visibility.Visible;
                SenhaApiContainer.Visibility = Visibility.Collapsed;
                return;
            }

            TxtSenhaApiErro.Text = "Senha incorreta.";
            TxtSenhaApiErro.Visibility = Visibility.Visible;
            TxtSenhaApi.Clear();
            TxtSenhaApi.Focus();
        }

        private async void BtnSalvarApiArquivo_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = TxtApiKey.Password?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey) || ValorEhInvalido(apiKey))
            {
                AtualizarStatusApi(false, "Chave inválida ou vazia. Faça o teste antes de salvar.");
                return;
            }

            if (!await ValidarApiAsync(apiKey))
            {
                AtualizarStatusApi(false, "A chave precisa passar no teste antes de ser salva.");
                return;
            }

            AppState.Settings.SalvarApiKey(apiKey);
            TxtApiKey.Password = AppState.Settings.ObterApiKeyVigente();
            AtualizarStatusApi(true, $"API salva no arquivo: {AppState.Settings.ObterCaminhoArquivoApi()}");
        }

        private async void BtnSalvarConfig_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = TxtApiKey.Password?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey) || ValorEhInvalido(apiKey))
            {
                AtualizarStatusApi(false, "Chave inválida ou vazia. Faça o teste antes de salvar.");
                return;
            }

            if (!await ValidarApiAsync(apiKey))
            {
                AtualizarStatusApi(false, "A chave precisa passar no teste antes de ser salva na configuração.");
                return;
            }

            var s = AppState.Settings.Carregar();
            s.ResendApiKey = apiKey;
            AppState.Settings.Salvar(s);
            TxtApiKey.Password = AppState.Settings.ObterApiKeyVigente();
            AtualizarStatusApi(true, "API salva nas configurações e pronta para o envio.");
        }

        private async void BtnTestarApi_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = TxtApiKey.Password?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                AtualizarStatusApi(false, "Informe a API key antes de testar.");
                return;
            }

            if (ValorEhInvalido(apiKey))
            {
                AtualizarStatusApi(false, "A chave parece ser inválida ou antiga. Insira uma chave válida do Resend.");
                return;
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var payload = new
                {
                    from = "SystemWM <onboarding@resend.dev>",
                    to = new[] { "delivered@resend.dev" },
                    subject = "Teste de API",
                    text = "Teste de API do SystemWM"
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://api.resend.com/emails", content);
                var resposta = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    AtualizarStatusApi(true, "API válida e pronta para envio.");
                    return;
                }

                AtualizarStatusApi(false, $"API inválida: {(int)response.StatusCode} - {resposta}");
            }
            catch (Exception ex)
            {
                AtualizarStatusApi(false, $"Erro ao testar API: {ex.Message}");
            }
        }

        private void BtnAbrirAssistenteInstalacao_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
                var wizard = new SetupWizardWindow();

                if (owner != null)
                {
                    wizard.Owner = owner;
                    wizard.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    wizard.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                wizard.Topmost = true;
                wizard.Loaded += (_, _) =>
                {
                    wizard.Topmost = false;
                    wizard.Activate();
                };

                wizard.Show();
                AtualizarStatusApi(_apiValida, "Assistente de instalação aberto.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir o assistente de instalação:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
