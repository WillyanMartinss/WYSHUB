using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SystemWM.Models;

namespace SystemWM.Views
{
    public partial class FirewallView : UserControl
    {
        private const string PlaceholderPortaRapida = "Digite uma porta (ex.: 3050)";
        private const string PlaceholderBusca = "Buscar porta, app ou IP";

        private List<PortaAtivaInfo> _portasOriginais = new();

        public FirewallView()
        {
            try
            {
                InitializeComponent();
                InicializarPlaceholders();
                InicializarMenuContextoPortas();
                Loaded += FirewallView_Loaded;
                LogDiagnostic("FirewallView constructed successfully.");
            }
            catch (Exception ex)
            {
                LogException(ex, "FirewallView.Constructor");
                throw;
            }
        }

        private void InicializarMenuContextoPortas()
        {
            var menu = new ContextMenu();
            var item = new MenuItem { Header = "Adicionar ao relatório" };
            item.Click += MenuAdicionarPortaRelatorio_Click;
            menu.Items.Add(item);
            ListaPortasAtivas.ContextMenu = menu;
        }

        private async void FirewallView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= FirewallView_Loaded;
            try
            {
                await CarregarAsync();
                await AtualizarPortasAtivasAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar a guia Firewall:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAtualizar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await CarregarAsync();
                await AtualizarPortasAtivasAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao atualizar Firewall:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InicializarPlaceholders()
        {
            SetPlaceholder(TxtPortaRapida, PlaceholderPortaRapida);
            SetPlaceholder(TxtBusca, PlaceholderBusca);
        }

        private void SetPlaceholder(TextBox textBox, string placeholder)
        {
            if (textBox == null)
                return;

            if (string.IsNullOrWhiteSpace(textBox.Text) || textBox.Text == placeholder)
            {
                textBox.Text = placeholder;
                textBox.Foreground = System.Windows.Media.Brushes.LightGray;
            }
        }

        private void TextBoxPlaceholder_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var placeholder = GetPlaceholderForTextBox(textBox);
            if (string.IsNullOrWhiteSpace(placeholder))
                return;

            if (textBox.Text == placeholder)
            {
                textBox.Text = string.Empty;
                textBox.Foreground = TryFindResource("TextoPrimario") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
            }
        }

        private void TextBoxPlaceholder_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            var placeholder = GetPlaceholderForTextBox(textBox);
            if (string.IsNullOrWhiteSpace(placeholder))
                return;

            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Text = placeholder;
                textBox.Foreground = System.Windows.Media.Brushes.LightGray;
            }
        }

        private string GetPlaceholderForTextBox(TextBox textBox)
        {
            return textBox.Name switch
            {
                nameof(TxtPortaRapida) => PlaceholderPortaRapida,
                nameof(TxtBusca) => PlaceholderBusca,
                _ => string.Empty,
            };
        }

        private bool GarantirElevacaoSeNecessaria()
        {
            if (IsAdministrator())
                return true;

            var resultado = MessageBox.Show(
                "Este recurso do Firewall precisa de privilégios de administrador. Reabrir o WYSHUB como administrador?",
                "SystemWM",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (resultado != MessageBoxResult.Yes)
                return false;

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
                Application.Current.Shutdown();
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível reabrir como administrador:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private async Task CarregarAsync()
        {
            if (!GarantirElevacaoSeNecessaria())
                return;

            try
            {
                TxtStatusFirewall.Text = "Verificando...";
                bool ativo = await Task.Run(() => AppState.Firewall.EstaAtivo());
                AtualizarStatusVisual(ativo);

                var regras = await Task.Run(() => AppState.Firewall.ListarRegras());
                AppState.UltimasRegrasFirewall = regras;
                AppState.FirewallAtivoCache = ativo;
            }
            catch (Exception ex)
            {
                LogException(ex, "FirewallView.CarregarAsync");
                AtualizarStatusVisual(false);
                MessageBox.Show($"Falha ao carregar informações do Firewall:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AtualizarStatusVisual(bool ativo)
        {
            TxtStatusFirewall.Text = ativo ? "🟢 Ativo" : "🔴 Desativado";
        }

        private async void BtnAtivar_Click(object sender, RoutedEventArgs e)
        {
            if (!GarantirElevacaoSeNecessaria())
                return;

            bool ok = await Task.Run(() => AppState.Firewall.AtivarFirewall());
            if (ok) AtualizarStatusVisual(true);
            else MessageBox.Show("Não foi possível ativar o Firewall. Confirme se o programa está rodando como Administrador.", "SystemWM");
        }

        private async void BtnDesativar_Click(object sender, RoutedEventArgs e)
        {
            if (!GarantirElevacaoSeNecessaria())
                return;

            var confirmar = MessageBox.Show(
                "Tem certeza que deseja DESATIVAR o Firewall do Windows? Isso reduz a proteção da máquina.",
                "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirmar != MessageBoxResult.Yes) return;

            bool ok = await Task.Run(() => AppState.Firewall.DesativarFirewall());
            if (ok) AtualizarStatusVisual(false);
            else MessageBox.Show("Não foi possível desativar o Firewall.", "SystemWM");
        }

        private async void RegraCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.Tag is not FirewallRegra regra) return;
            await Task.Run(() => AppState.Firewall.HabilitarRegra(regra.Nome, cb.IsChecked == true));
        }

        private async void PortaRelatorio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not PortaAtivaInfo porta) return;
            if (AppState.PortasAtivasRelatorio.Any(p => p.LocalEndpoint == porta.LocalEndpoint && p.RemoteEndpoint == porta.RemoteEndpoint && p.Pid == porta.Pid && p.Protocolo == porta.Protocolo))
                return;
            AppState.PortasAtivasRelatorio.Add(porta);
        }

        private void PortaRelatorio_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not PortaAtivaInfo porta) return;
            AppState.PortasAtivasRelatorio.RemoveAll(p => p.LocalEndpoint == porta.LocalEndpoint && p.RemoteEndpoint == porta.RemoteEndpoint && p.Pid == porta.Pid && p.Protocolo == porta.Protocolo);
        }

        private async void BtnMarcarParaRelatorio_Click(object sender, RoutedEventArgs e)
        {
            if (ListaPortasAtivas.SelectedItem is not PortaAtivaInfo porta)
            {
                MessageBox.Show("Selecione uma porta ativa para marcar no relatório.", "SystemWM");
                return;
            }

            SalvarPortaNoRelatorio(porta);
            ListaPortasAtivas.Items.Refresh();
        }

        private void BtnSalvarSelecionadasParaRelatorio_Click(object sender, RoutedEventArgs e)
        {
            var selecionadas = ListaPortasAtivas.SelectedItems.OfType<PortaAtivaInfo>().ToList();
            if (!selecionadas.Any())
            {
                MessageBox.Show("Selecione ao menos uma porta ativa para salvar no relatório.", "SystemWM");
                return;
            }

            foreach (var porta in selecionadas)
            {
                SalvarPortaNoRelatorio(porta);
            }

            ListaPortasAtivas.Items.Refresh();
            MessageBox.Show($"{selecionadas.Count} porta(s) adicionada(s) ao relatório.", "SystemWM");
        }

        private void SalvarPortaNoRelatorio(PortaAtivaInfo porta)
        {
            porta.IncluirNoRelatorio = true;
            if (!AppState.PortasAtivasRelatorio.Any(p => p.LocalEndpoint == porta.LocalEndpoint && p.RemoteEndpoint == porta.RemoteEndpoint && p.Pid == porta.Pid && p.Protocolo == porta.Protocolo))
            {
                AppState.PortasAtivasRelatorio.Add(porta);
            }
            AtualizarPortasRelatorioLista();
        }

        private async void BtnVerEstado_Click(object sender, RoutedEventArgs e)
        {
            await CarregarAsync();
        }

        private void BtnFiltroDirecao_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton toggleButton)
                return;

            var direcao = toggleButton.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(direcao))
                return;

            foreach (var button in new[] { BtnFiltroSaida, BtnFiltroEntrada, BtnFiltroAmbos })
            {
                if (button != null && button != toggleButton)
                    button.IsChecked = false;
            }

            toggleButton.IsChecked = true;

            if (CmbDirecao != null)
            {
                foreach (ComboBoxItem item in CmbDirecao.Items)
                {
                    if (string.Equals(item.Content?.ToString(), direcao, StringComparison.OrdinalIgnoreCase))
                    {
                        CmbDirecao.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private async void BtnLiberarPortaRapida_Click(object sender, RoutedEventArgs e)
        {
            if (!GarantirElevacaoSeNecessaria())
                return;

            var portaTexto = TxtPortaRapida.Text?.Trim();
            if (string.IsNullOrWhiteSpace(portaTexto) || string.Equals(portaTexto, PlaceholderPortaRapida, StringComparison.OrdinalIgnoreCase) || !int.TryParse(portaTexto, out int porta))
            {
                MessageBox.Show("Informe uma porta válida.", "SystemWM");
                return;
            }

            string direcaoSelecionada = (CmbDirecao.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var direcoes = new List<string>();

            if (string.Equals(direcaoSelecionada, "Saída", StringComparison.OrdinalIgnoreCase))
                direcoes.Add("out");
            else if (string.Equals(direcaoSelecionada, "Entrada", StringComparison.OrdinalIgnoreCase))
                direcoes.Add("in");
            else
            {
                direcoes.Add("in");
                direcoes.Add("out");
            }

            bool ok = true;
            foreach (var direcao in direcoes)
            {
                ok &= await Task.Run(() => AppState.Firewall.CriarRegraPorta($"SystemWM_Liberar_{porta}_{direcao}", porta, "TCP", true, direcao));
            }

            if (ok)
            {
                MessageBox.Show("Porta liberada com sucesso.", "SystemWM");
                await CarregarAsync();
                await AtualizarPortasAtivasAsync();
            }
            else
            {
                MessageBox.Show("Não foi possível liberar a porta.", "SystemWM");
            }
        }

        private async void BtnLiberarExecutavel_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Esta ação ainda não está implementada.", "SystemWM");
        }

        private void TxtBusca_TextChanged(object sender, TextChangedEventArgs e)
        {
            FiltrarPortasAtivas();
        }

        private string ObterTextoBuscaSeguro()
        {
            var texto = TxtBusca.Text?.Trim() ?? string.Empty;
            if (string.Equals(texto, PlaceholderBusca, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return texto;
        }

        private void BtnLimparPesquisa_Click(object sender, RoutedEventArgs e)
        {
            TxtBusca.Text = string.Empty;
            SetPlaceholder(TxtBusca, PlaceholderBusca);
            FiltrarPortasAtivas();
        }

        private void ListaPortasAtivas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ListaPortasAtivas.SelectedItem is not PortaAtivaInfo porta)
                return;

            AlternarPortaRelatorio(porta);
        }

        private void ListaPortasAtivas_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var item = (e.OriginalSource as FrameworkElement)?.DataContext as PortaAtivaInfo;
                if (item == null)
                    return;

                ListaPortasAtivas.SelectedItem = item;
                if (ListaPortasAtivas.ContextMenu is ContextMenu menu)
                {
                    foreach (var obj in menu.Items)
                    {
                        if (obj is MenuItem menuItem)
                        {
                            menuItem.Tag = item;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogException(ex, "FirewallView.ListaPortasAtivas_PreviewMouseRightButtonDown");
            }
        }

        private void MenuAdicionarPortaRelatorio_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not MenuItem menuItem || menuItem.Tag is not PortaAtivaInfo porta)
                {
                    if (ListaPortasAtivas.SelectedItem is PortaAtivaInfo selected)
                    {
                        SalvarPortaNoRelatorio(selected);
                    }
                    return;
                }

                SalvarPortaNoRelatorio(porta);
                ListaPortasAtivas.Items.Refresh();
                AtualizarPortasRelatorioLista();
            }
            catch (Exception ex)
            {
                LogException(ex, "FirewallView.MenuAdicionarPortaRelatorio_Click");
            }
        }

        private void AlternarPortaRelatorio(PortaAtivaInfo porta)
        {
            var jaExiste = AppState.PortasAtivasRelatorio.Any(p =>
                p.LocalEndpoint == porta.LocalEndpoint &&
                p.RemoteEndpoint == porta.RemoteEndpoint &&
                p.Pid == porta.Pid &&
                p.Protocolo == porta.Protocolo);

            if (jaExiste)
            {
                AppState.PortasAtivasRelatorio.RemoveAll(p =>
                    p.LocalEndpoint == porta.LocalEndpoint &&
                    p.RemoteEndpoint == porta.RemoteEndpoint &&
                    p.Pid == porta.Pid &&
                    p.Protocolo == porta.Protocolo);
                porta.IncluirNoRelatorio = false;
            }
            else
            {
                SalvarPortaNoRelatorio(porta);
            }

            ListaPortasAtivas.Items.Refresh();
            AtualizarPortasRelatorioLista();
        }

        private void FiltrarPortasAtivas()
        {
            try
            {
                if (_portasOriginais == null || !_portasOriginais.Any()) return;

                string termo = ObterTextoBuscaSeguro();
                if (string.IsNullOrEmpty(termo))
                {
                    ListaPortasAtivas.ItemsSource = _portasOriginais.ToList();
                    return;
                }

                var filtradas = _portasOriginais.Where(p =>
                    p != null &&
                    (
                        p.Porta.ToString().Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                        p.LocalEndpoint.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                        p.RemoteEndpoint.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                        p.Processo.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                        p.Protocolo.Contains(termo, StringComparison.OrdinalIgnoreCase) ||
                        p.Estado.Contains(termo, StringComparison.OrdinalIgnoreCase)
                    )
                ).ToList();

                ListaPortasAtivas.ItemsSource = filtradas;
            }
            catch (Exception ex)
            {
                LogException(ex, "FirewallView.FiltrarPortasAtivas");
            }
        }

        private async void BtnAtualizarPortas_Click(object sender, RoutedEventArgs e) => await AtualizarPortasAtivasAsync();

        private async Task AtualizarPortasAtivasAsync()
        {
            try
            {
                ListaPortasAtivas.ItemsSource = null;
                var portas = await Task.Run(() => AppState.Firewall.ListarPortasAtivas());
                _portasOriginais = MesclarPortasLiberadas(portas);
                SincronizarPortasRelatorio(_portasOriginais);
                ListaPortasAtivas.ItemsSource = _portasOriginais;
                AtualizarPortasRelatorioLista();
            }
            catch (Exception ex)
            {
                LogException(ex, "FirewallView.AtualizarPortasAtivasAsync");
                MessageBox.Show($"Falha ao listar portas ativas:\n{ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
                _portasOriginais = new List<PortaAtivaInfo>();
                ListaPortasAtivas.ItemsSource = _portasOriginais;
                AtualizarPortasRelatorioLista();
            }
        }

        private List<PortaAtivaInfo> MesclarPortasLiberadas(List<PortaAtivaInfo> portas)
        {
            var resultado = portas?.ToList() ?? new List<PortaAtivaInfo>();

            try
            {
                var regras = AppState.Firewall.ListarRegras();
                foreach (var regra in regras.Where(r => r.Habilitada && (string.Equals(r.Acao, "Allow", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Acao, "Permitir", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(r.Acao, "Allow", StringComparison.OrdinalIgnoreCase))))
                {
                    if (!int.TryParse(regra.Porta, out var porta))
                        continue;

                    var protocolo = string.Equals(regra.Protocolo, "UDP", StringComparison.OrdinalIgnoreCase) ? "UDP" : "TCP";
                    if (resultado.Any(p => p.Protocolo.Equals(protocolo, StringComparison.OrdinalIgnoreCase) && p.Porta == porta))
                        continue;

                    resultado.Add(new PortaAtivaInfo
                    {
                        Protocolo = protocolo,
                        LocalEndpoint = $"0.0.0.0:{porta}",
                        RemoteEndpoint = "0.0.0.0:0",
                        Estado = "Liberada",
                        Processo = "Regra do Firewall",
                        Pid = 0,
                        Porta = porta
                    });
                }
            }
            catch
            {
                // Mantém a lista original caso não seja possível consultar as regras.
            }

            return resultado.OrderBy(p => p.Protocolo).ThenBy(p => p.Porta).ToList();
        }

        private void SincronizarPortasRelatorio(List<PortaAtivaInfo> portas)
        {
            if (portas == null || portas.Count == 0) return;

            foreach (var porta in portas)
            {
                if (AppState.PortasAtivasRelatorio.Any(p =>
                    p.LocalEndpoint == porta.LocalEndpoint &&
                    p.RemoteEndpoint == porta.RemoteEndpoint &&
                    p.Pid == porta.Pid &&
                    p.Protocolo == porta.Protocolo))
                {
                    porta.IncluirNoRelatorio = true;
                }
                else
                {
                    porta.IncluirNoRelatorio = false;
                }
            }
        }

        private void AtualizarPortasRelatorioLista()
        {
            ListaPortasRelatorio.ItemsSource = null;
            ListaPortasRelatorio.ItemsSource = AppState.PortasAtivasRelatorio.ToList();
            TxtPortasRelatorioTitulo.Text = $"Portas salvas no relatório ({AppState.PortasAtivasRelatorio.Count})";
            TxtContadorPortasRelatorio.Text = AppState.PortasAtivasRelatorio.Count.ToString();
        }

        private void BtnLimparPortasRelatorio_Click(object sender, RoutedEventArgs e)
        {
            AppState.PortasAtivasRelatorio.Clear();
            foreach (var porta in _portasOriginais)
            {
                porta.IncluirNoRelatorio = false;
            }

            ListaPortasAtivas.Items.Refresh();
            AtualizarPortasRelatorioLista();
        }

        private void BtnVerPortasRelatorio_Click(object sender, RoutedEventArgs e)
        {
            var portas = AppState.PortasAtivasRelatorio
                .OrderBy(p => p.Porta)
                .ToList();

            var janela = new Window
            {
                Title = "Portas do relatório",
                Width = 520,
                Height = 360,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Window.GetWindow(this),
                ShowInTaskbar = false,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 15, 26)),
                Foreground = System.Windows.Media.Brushes.White
            };

            var border = new Border
            {
                Margin = new Thickness(12),
                Padding = new Thickness(12),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(19, 26, 43)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 42, 66)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10)
            };

            var stack = new StackPanel();

            var titulo = new TextBlock
            {
                Text = portas.Any() ? $"Portas selecionadas ({portas.Count})" : "Nenhuma porta selecionada",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };

            var lista = new ListBox
            {
                Height = 220,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(11, 15, 26)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 141, 239)),
                BorderThickness = new Thickness(1)
            };

            foreach (var porta in portas)
            {
                lista.Items.Add($"Porta {porta.Porta} | {porta.Protocolo} | {porta.LocalEndpoint} | {porta.Processo}");
            }

            if (!portas.Any())
            {
                lista.Items.Add("Nenhuma porta foi selecionada para o relatório.");
            }

            var fechar = new Button
            {
                Content = "Fechar",
                Width = 110,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(91, 141, 239)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 123, 232)),
                BorderThickness = new Thickness(1)
            };

            fechar.Click += (_, __) => janela.Close();

            stack.Children.Add(titulo);
            stack.Children.Add(lista);
            stack.Children.Add(fechar);
            border.Child = stack;
            janela.Content = border;
            janela.ShowDialog();
        }

        private void LogException(Exception exception, string context)
        {
            try
            {
                var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemWM", "Logs");
                Directory.CreateDirectory(pasta);
                var caminho = Path.Combine(pasta, $"SystemWM_FirewallError_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.WriteAllText(caminho, $"Context: {context}\n{exception}\n");
            }
            catch
            {
                // Não interromper o app durante o log.
            }
        }

        private void LogDiagnostic(string message)
        {
            try
            {
                var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemWM", "Logs");
                Directory.CreateDirectory(pasta);
                var caminho = Path.Combine(pasta, $"SystemWM_FirewallDiagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.log");
                File.WriteAllText(caminho, message);
            }
            catch
            {
                // Não interromper o app durante o log.
            }
        }

        private async void BtnBloquearPortaSelecionada_Click(object sender, RoutedEventArgs e)
        {
            if (ListaPortasAtivas.SelectedItem is not PortaAtivaInfo porta || porta.Porta == 0)
            {
                MessageBox.Show("Selecione uma porta ativa para bloquear.", "SystemWM");
                return;
            }

            string nome = $"SystemWM_Bloquear_{porta.Protocolo}_{porta.Porta}";
            bool ok = await Task.Run(() => AppState.Firewall.CriarRegraPorta(nome, porta.Porta, porta.Protocolo, false));
            if (ok)
            {
                MessageBox.Show($"Porta {porta.Porta}/{porta.Protocolo} bloqueada.", "SystemWM");
                await CarregarAsync();
                await AtualizarPortasAtivasAsync();
            }
            else
            {
                MessageBox.Show("Não foi possível bloquear a porta.", "SystemWM");
            }
        }

        private async void BtnPermitirPortaSelecionada_Click(object sender, RoutedEventArgs e)
        {
            if (ListaPortasAtivas.SelectedItem is not PortaAtivaInfo porta || porta.Porta == 0)
            {
                MessageBox.Show("Selecione uma porta ativa para permitir.", "SystemWM");
                return;
            }

            string nome = $"SystemWM_Permitir_{porta.Protocolo}_{porta.Porta}";
            bool ok = await Task.Run(() => AppState.Firewall.CriarRegraPorta(nome, porta.Porta, porta.Protocolo, true));
            if (ok)
            {
                MessageBox.Show($"Porta {porta.Porta}/{porta.Protocolo} permitida.", "SystemWM");
                await CarregarAsync();
                await AtualizarPortasAtivasAsync();
            }
            else
            {
                MessageBox.Show("Não foi possível permitir a porta.", "SystemWM");
            }
        }

        private async void BtnRemoverRegraPorPorta_Click(object sender, RoutedEventArgs e)
        {
            if (ListaPortasAtivas.SelectedItem is not PortaAtivaInfo porta || porta.Porta == 0)
            {
                MessageBox.Show("Selecione uma porta ativa para remover a regra.", "SystemWM");
                return;
            }

            var regraBloquear = $"SystemWM_Bloquear_{porta.Protocolo}_{porta.Porta}";
            var regraPermitir = $"SystemWM_Permitir_{porta.Protocolo}_{porta.Porta}";

            bool okBloquear = await Task.Run(() => AppState.Firewall.RemoverRegra(regraBloquear));
            bool okPermitir = await Task.Run(() => AppState.Firewall.RemoverRegra(regraPermitir));

            if (okBloquear || okPermitir)
            {
                MessageBox.Show($"Regras vinculadas à porta {porta.Porta} foram removidas.", "SystemWM");
                await CarregarAsync();
                await AtualizarPortasAtivasAsync();
            }
            else
            {
                MessageBox.Show("Nenhuma regra encontrada para remover ou ocorreu um erro.", "SystemWM");
            }
        }
    }
}
