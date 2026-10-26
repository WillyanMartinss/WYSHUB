using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SystemWM.Models;

namespace SystemWM.Views
{
    public partial class MaintenanceView : UserControl
    {
        private List<ItemLimpeza> _itensLimpeza = new();
        private readonly ObservableCollection<string> _logLimpeza = new();

        public MaintenanceView()
        {
            InitializeComponent();
            CarregarConfiguracao();
            _ = CarregarAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception.Flatten();
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Erro ao carregar Manutenção:\n{ex.InnerException ?? ex}", "Erro - Manutenção", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        private async Task CarregarAsync()
        {
            _itensLimpeza = await Task.Run(() => AppState.Cleanup.ListarItensDisponiveis());
            ListaLimpeza.ItemsSource = _itensLimpeza;
        }

        private void CarregarConfiguracao()
        {
            try
            {
                var settings = AppState.Settings.Carregar();
                ChkHabilitarBackup.IsChecked = settings.UsarBackupLimpeza;
                BtnSelecionarPastaBackup.IsEnabled = settings.UsarBackupLimpeza;
                TxtPastaBackupSelecionada.Text = string.IsNullOrWhiteSpace(settings.PastaBackupLimpeza)
                    ? "Nenhuma pasta de backup selecionada"
                    : settings.PastaBackupLimpeza;
                AtualizarBotaoBackupCor();
            }
            catch
            {
                BtnSelecionarPastaBackup.IsEnabled = false;
                TxtPastaBackupSelecionada.Text = "Nenhuma pasta de backup selecionada";
                AtualizarBotaoBackupCor();
            }
        }

        private void AtualizarBotaoBackupCor()
        {
            if (BtnSelecionarPastaBackup == null)
                return;

            if (BtnSelecionarPastaBackup.IsEnabled)
            {
                BtnSelecionarPastaBackup.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                BtnSelecionarPastaBackup.Foreground = Brushes.White;
            }
            else
            {
                BtnSelecionarPastaBackup.Background = new SolidColorBrush(Color.FromRgb(75, 85, 99));
                BtnSelecionarPastaBackup.Foreground = Brushes.White;
            }
        }

        private void ChkHabilitarBackup_Changed(object sender, RoutedEventArgs e)
        {
            var habilitado = ChkHabilitarBackup.IsChecked == true;
            BtnSelecionarPastaBackup.IsEnabled = habilitado;
            AtualizarBotaoBackupCor();

            try
            {
                var settings = AppState.Settings.Carregar();
                settings.UsarBackupLimpeza = habilitado;
                AppState.Settings.Salvar(settings);
            }
            catch
            {
            }
        }

        private void BtnSelecionarPastaBackup_Click(object sender, RoutedEventArgs e)
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
                ChkHabilitarBackup.IsChecked = true;
                BtnSelecionarPastaBackup.IsEnabled = true;
                TxtPastaBackupSelecionada.Text = dialog.SelectedPath;
                AtualizarBotaoBackupCor();
            }
            catch
            {
                MessageBox.Show("Não foi possível salvar a pasta de backup.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CardLimpeza_Click(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is CheckBox or Button)
                return;

            if (sender is Border border && border.DataContext is ItemLimpeza item)
                item.Selecionado = !item.Selecionado;
        }

        private void BtnEscolherPasta_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ItemLimpeza item)
            {
                using var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Selecionar pasta para limpar",
                    ShowNewFolderButton = true
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    item.CaminhoPersonalizado = dialog.SelectedPath;
                    item.Descricao = $"Limpeza da pasta escolhida: {dialog.SelectedPath}";
                    item.NivelAlerta = NivelAlertaLimpeza.Amarelo;
                    item.Selecionado = true;
                    // Persiste pasta nas configurações para aparecer sempre na lista
                    try
                    {
                        var settings = AppState.Settings.Carregar();
                        var existing = new System.Collections.Generic.List<string>(settings.PastasPersonalizadasParaLimpeza ?? System.Array.Empty<string>());
                        if (!existing.Contains(dialog.SelectedPath, System.StringComparer.OrdinalIgnoreCase))
                        {
                            existing.Add(dialog.SelectedPath);
                            settings.PastasPersonalizadasParaLimpeza = existing.ToArray();
                            AppState.Settings.Salvar(settings);
                        }
                    }
                    catch { }
                }
            }
        }

        private void BtnVerificarDefender_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var executavel = string.Empty;
                var candidatos = new[]
                {
                    @"C:\Program Files\Windows Defender\MpCmdRun.exe",
                    @"C:\Program Files\Microsoft Defender\MpCmdRun.exe"
                };

                executavel = candidatos.FirstOrDefault(File.Exists) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(executavel))
                {
                    MessageBox.Show("O Windows Security/Defender não foi encontrado neste computador.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                TxtStatusLimpeza.Text = "Executando verificação rápida do Windows Security...";
                TxtItemAtual.Text = "Aguarde enquanto o Defender realiza a varredura rápida.";
                _logLimpeza.Add("Iniciando verificação rápida do Defender...");
                TxtLogLimpeza.Text = string.Join(Environment.NewLine, _logLimpeza.TakeLast(8));

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = executavel,
                    Arguments = "-Scan -ScanType 1",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
                };

                using var processo = System.Diagnostics.Process.Start(psi);
                if (processo != null)
                {
                    processo.WaitForExit();
                }

                TxtStatusLimpeza.Text = "Verificação rápida concluída.";
                TxtItemAtual.Text = "A verificação do Defender foi finalizada.";
                _logLimpeza.Add("Verificação rápida do Defender concluída.");
                TxtLogLimpeza.Text = string.Join(Environment.NewLine, _logLimpeza.TakeLast(8));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível iniciar a verificação do Defender:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnAtualizarWindows_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtStatusLimpeza.Text = "Abrindo a atualização do Windows...";
                TxtItemAtual.Text = "Aguarde enquanto o Windows abre as atualizações disponíveis.";
                _logLimpeza.Add("Abrindo atualização do Windows...");
                TxtLogLimpeza.Text = string.Join(Environment.NewLine, _logLimpeza.TakeLast(8));

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-settings:windowsupdate",
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível abrir as atualizações do Windows:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnLimparPastaPersonalizada_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Selecione a pasta para limpar",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            var itemPersonalizado = new ItemLimpeza
            {
                Nome = "Pasta personalizada",
                CaminhoTipo = "Personalizada",
                Descricao = $"Limpeza da pasta escolhida: {dialog.SelectedPath}",
                CaminhoPersonalizado = dialog.SelectedPath,
                NivelAlerta = NivelAlertaLimpeza.Amarelo,
                Selecionado = true
            };
            itemPersonalizado.AtualizarCores();

            // Salva a pasta nas configurações para uso futuro
            try
            {
                var settings = AppState.Settings.Carregar();
                var existing = new System.Collections.Generic.List<string>(settings.PastasPersonalizadasParaLimpeza ?? System.Array.Empty<string>());
                if (!existing.Contains(dialog.SelectedPath, System.StringComparer.OrdinalIgnoreCase))
                {
                    existing.Add(dialog.SelectedPath);
                    settings.PastasPersonalizadasParaLimpeza = existing.ToArray();
                    AppState.Settings.Salvar(settings);
                }
            }
            catch { }

            await ExecutarLimpeza(new List<ItemLimpeza> { itemPersonalizado });
        }

        private async void BtnLimpar_Click(object sender, RoutedEventArgs e)
        {
            var selecionados = _itensLimpeza.Where(i => i.Selecionado).ToList();
            if (!selecionados.Any())
            {
                MessageBox.Show("Selecione ao menos um item para limpar.", "SystemWM");
                return;
            }

            await ExecutarLimpeza(selecionados);
        }

        private async Task ExecutarLimpeza(List<ItemLimpeza> selecionados)
        {
            var textoConfirmacao = "Os seguintes itens serão apagados permanentemente:\n\n" +
                string.Join("\n", selecionados.Select(i => $"• {i.Nome} (~{i.TamanhoEstimadoMB} MB)")) +
                "\n\nDeseja continuar?";

            var confirmar = MessageBox.Show(textoConfirmacao, "Confirmar limpeza", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmar != MessageBoxResult.Yes) return;

            BtnLimpar.IsEnabled = false;
            BtnLimparPastaPersonalizada.IsEnabled = false;
            BtnLimpar.Content = "Limpando...";
            BarraProgressoLimpeza.Value = 0;
            TxtStatusLimpeza.Text = "Iniciando a limpeza...";
            TxtItemAtual.Text = string.Empty;
            _logLimpeza.Clear();
            TxtLogLimpeza.Text = "Iniciando limpeza...";

            var progresso = new Progress<(int Percentual, string Mensagem)>(estado =>
            {
                BarraProgressoLimpeza.Value = Math.Min(100, Math.Max(0, estado.Percentual));
                TxtStatusLimpeza.Text = estado.Mensagem;
                TxtItemAtual.Text = estado.Mensagem;
                _logLimpeza.Add(estado.Mensagem);
                TxtLogLimpeza.Text = string.Join(Environment.NewLine, _logLimpeza.TakeLast(12));
            });

            var resultado = await Task.Run(() => AppState.Cleanup.Limpar(selecionados, progresso));
            AppState.UltimaLimpeza = resultado;

            BtnLimpar.IsEnabled = true;
            BtnLimparPastaPersonalizada.IsEnabled = true;
            BtnLimpar.Content = "🧹 Limpar selecionados";
            BarraProgressoLimpeza.Value = 100;
            TxtStatusLimpeza.Text = "Limpeza concluída.";
            TxtItemAtual.Text = "Os itens selecionados foram processados.";

            MessageBox.Show(
                "Limpeza concluída:\n\n" + string.Join("\n", resultado.Select(kv => $"• {kv.Key}: limpo")),
                "SystemWM");

            await CarregarAsync();
        }
    }
}
