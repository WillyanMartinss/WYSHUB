using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SystemWM.Models;

using System.Linq;

namespace SystemWM.Views
{
    public partial class ReportsView : UserControl
    {
        private string? _ultimoHtmlGerado;
        private string? _ultimoTxtGerado;

        public ReportsView()
        {
            InitializeComponent();
            AtualizarSeçõesRelatorioDoEstado();
            CarregarProgramasParaSelecao();
        }

        private void AtualizarSeçõesRelatorioDoEstado()
        {
            ChkRelatorioResumoDashboard.IsChecked = AppState.RelatorioIncluirResumoDashboard;
            ChkRelatorioHardware.IsChecked = AppState.RelatorioIncluirHardware;
            ChkRelatorioFirewall.IsChecked = AppState.RelatorioIncluirFirewall;
            ChkRelatorioLimpeza.IsChecked = AppState.RelatorioIncluirLimpeza;
        }

        private void ChkRelatorio_Changed(object sender, RoutedEventArgs e)
        {
            if (ChkRelatorioResumoDashboard != null)
                AppState.RelatorioIncluirResumoDashboard = ChkRelatorioResumoDashboard.IsChecked == true;
            if (ChkRelatorioHardware != null)
                AppState.RelatorioIncluirHardware = ChkRelatorioHardware.IsChecked == true;
            if (ChkRelatorioFirewall != null)
                AppState.RelatorioIncluirFirewall = ChkRelatorioFirewall.IsChecked == true;
            if (ChkRelatorioLimpeza != null)
                AppState.RelatorioIncluirLimpeza = ChkRelatorioLimpeza.IsChecked == true;
        }

        private void CarregarProgramasParaSelecao()
        {
            var programas = AppState.UltimosProgramas ?? AppState.Programs.Listar();
            AppState.UltimosProgramas = programas;

            var selecionados = programas
                .Where(p => p.IncluirNoRelatorio)
                .OrderBy(p => p.Nome)
                .ToList();

            ListaProgramasRelatorioSelecao.ItemsSource = selecionados;
        }

        private ClienteVisita ColetarDadosCliente() => new()
        {
            NomeCliente = "Cliente",
            EmpresaCliente = string.Empty,
            EmailDestino = string.Empty,
            Observacoes = string.Empty,
            DataVisita = DateTime.Now
        };

        private async Task<string?> GarantirDiagnosticoAsync()
        {
            var incluirResumo = AppState.RelatorioIncluirResumoDashboard;
            var incluirHardware = AppState.RelatorioIncluirHardware;
            var incluirFirewall = AppState.RelatorioIncluirFirewall;
            var incluirLimpeza = AppState.RelatorioIncluirLimpeza;
            var algumaSecaoSelecionada = incluirResumo || incluirHardware || incluirFirewall || incluirLimpeza;

            if (!algumaSecaoSelecionada)
            {
                return null;
            }

            if (incluirResumo || incluirHardware || incluirLimpeza)
            {
                if (AppState.UltimoDiagnostico == null)
                {
                    TxtStatus.Text = "Coletando dados do hardware e sistema para o relatório...";
                    AppState.UltimoDiagnostico = await Task.Run(() => AppState.Hardware.ColetarTudo());
                }
            }

            if (incluirResumo || incluirHardware || incluirFirewall)
            {
                AppState.UltimosProgramas ??= await Task.Run(() => AppState.Programs.Listar());
            }

            if (incluirFirewall)
            {
                if (AppState.UltimasRegrasFirewall == null)
                {
                    AppState.UltimasRegrasFirewall = await Task.Run(() => AppState.Firewall.ListarRegras());
                }
                AppState.FirewallAtivoCache = AppState.Firewall.EstaAtivo();
            }

            return null;
        }

        private async void BtnGerar_Click(object sender, RoutedEventArgs e)
        {
            var algumaSecaoSelecionada = AppState.RelatorioIncluirResumoDashboard || AppState.RelatorioIncluirHardware || AppState.RelatorioIncluirFirewall || AppState.RelatorioIncluirLimpeza;
            if (!algumaSecaoSelecionada)
            {
                BtnGerar.IsEnabled = true;
                TxtStatus.Text = "Nenhuma seção selecionada. Marque ao menos uma opção para gerar o relatório.";
                TxtRelatorioPreview.Text = "Nenhuma seção foi selecionada para o relatório.";
                return;
            }

            BtnGerar.IsEnabled = false;
            TxtStatus.Text = "Montando o conteúdo do relatório em texto legível...";
            TxtRelatorioPreview.Text = "Gerando o conteúdo do relatório...\n\nAguarde enquanto os dados são organizados para a pré-visualização.";

            await GarantirDiagnosticoAsync();
            AppState.ClienteAtual = ColetarDadosCliente();

            _ultimoTxtGerado = await Task.Run(() => AppState.Reports.GerarTxt(
                AppState.ClienteAtual,
                AppState.UltimoDiagnostico!,
                AppState.UltimosProgramas!,
                AppState.UltimaLimpeza,
                AppState.UltimasRegrasFirewall,
                AppState.FirewallAtivoCache,
                AppState.PortasAtivasRelatorio,
                AppState.RelatorioIncluirResumoDashboard,
                AppState.RelatorioIncluirHardware,
                AppState.RelatorioIncluirFirewall,
                AppState.RelatorioIncluirLimpeza,
                AppState.DashboardCardsSelecionados));

            TxtRelatorioPreview.Text = string.IsNullOrWhiteSpace(_ultimoTxtGerado)
                ? "Nenhum conteúdo foi gerado para o relatório."
                : _ultimoTxtGerado;

            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemWM", "Relatorios");
            Directory.CreateDirectory(pasta);

            var tipo = ObterTipoRelatorioSelecionado();
            if (tipo == "txt")
            {
                var caminho = Path.Combine(pasta, $"Relatorio_{AppState.ClienteAtual.NomeCliente}_{DateTime.Now:yyyyMMdd_HHmm}.txt");
                File.WriteAllText(caminho, _ultimoTxtGerado);

                AppState.UltimoRelatorioTxtGerado = _ultimoTxtGerado;
                AppState.UltimoRelatorioHtmlGerado = _ultimoHtmlGerado ?? _ultimoTxtGerado;
                AppState.UltimoRelatorioTipo = "txt";
                AppState.UltimoRelatorioNomeAnexo = Path.GetFileName(caminho);
                AppState.UltimoRelatorioCaminho = caminho;
                AppState.RelatorioDisponivelParaEmail = true;

                TxtStatus.Text = $"Relatório TXT gerado e salvo em: {caminho}";
            }
            else if (tipo == "html")
            {
                // ensure HTML available
                var html = _ultimoHtmlGerado ?? AppState.Reports.GerarHtml(
                    AppState.ClienteAtual,
                    AppState.UltimoDiagnostico!,
                    AppState.UltimosProgramas!,
                    AppState.UltimaLimpeza,
                    AppState.UltimasRegrasFirewall,
                    AppState.FirewallAtivoCache,
                    AppState.PortasAtivasRelatorio,
                    AppState.RelatorioIncluirResumoDashboard,
                    AppState.RelatorioIncluirHardware,
                    AppState.RelatorioIncluirFirewall,
                    AppState.RelatorioIncluirLimpeza,
                    AppState.DashboardCardsSelecionados);

                var caminho = Path.Combine(pasta, $"Relatorio_{AppState.ClienteAtual.NomeCliente}_{DateTime.Now:yyyyMMdd_HHmm}.html");
                File.WriteAllText(caminho, html);

                AppState.UltimoRelatorioTxtGerado = _ultimoTxtGerado; // keep text preview
                AppState.UltimoRelatorioHtmlGerado = html;
                AppState.UltimoRelatorioTipo = "html";
                AppState.UltimoRelatorioNomeAnexo = Path.GetFileName(caminho);
                AppState.UltimoRelatorioCaminho = caminho;
                AppState.RelatorioDisponivelParaEmail = true;

                TxtStatus.Text = $"Relatório HTML gerado e salvo em: {caminho}";
            }
            else
            {
                TxtStatus.Text = "Tipo de relatório não suportado (PDF removido). Gere em TXT ou HTML.";
            }
            BtnGerar.IsEnabled = true;
        }

        private async void BtnEnviar_Click(object sender, RoutedEventArgs e)
        {
            var algumaSecaoSelecionada = AppState.RelatorioIncluirResumoDashboard || AppState.RelatorioIncluirHardware || AppState.RelatorioIncluirFirewall || AppState.RelatorioIncluirLimpeza;
            if (!algumaSecaoSelecionada)
            {
                TxtStatus.Text = "Nenhuma seção selecionada. Marque ao menos uma opção para enviar o relatório.";
                MessageBox.Show("Selecione pelo menos uma seção para gerar o relatório antes de enviar.", "SystemWM");
                return;
            }

            await GarantirDiagnosticoAsync();
            AppState.ClienteAtual = ColetarDadosCliente();

            if (_ultimoHtmlGerado == null)
            {
                _ultimoHtmlGerado = AppState.Reports.GerarHtml(
                    AppState.ClienteAtual,
                    AppState.UltimoDiagnostico!,
                    AppState.UltimosProgramas!,
                    AppState.UltimaLimpeza,
                    AppState.UltimasRegrasFirewall,
                    AppState.FirewallAtivoCache,
                    AppState.PortasAtivasRelatorio,
                    AppState.RelatorioIncluirResumoDashboard,
                    AppState.RelatorioIncluirHardware,
                    AppState.RelatorioIncluirFirewall,
                    AppState.RelatorioIncluirLimpeza,
                    AppState.DashboardCardsSelecionados);
            }

            if (_ultimoTxtGerado == null)
            {
                _ultimoTxtGerado = AppState.Reports.GerarTxt(
                    AppState.ClienteAtual,
                    AppState.UltimoDiagnostico!,
                    AppState.UltimosProgramas!,
                    AppState.UltimaLimpeza,
                    AppState.UltimasRegrasFirewall,
                    AppState.FirewallAtivoCache,
                    AppState.PortasAtivasRelatorio,
                    AppState.RelatorioIncluirResumoDashboard,
                    AppState.RelatorioIncluirHardware,
                    AppState.RelatorioIncluirFirewall,
                    AppState.RelatorioIncluirLimpeza,
                    AppState.DashboardCardsSelecionados);
            }

            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemWM", "Relatorios");
            Directory.CreateDirectory(pasta);
            var nomeAnexo = $"Relatorio_{(string.IsNullOrWhiteSpace(AppState.ClienteAtual.NomeCliente) ? "cliente" : AppState.ClienteAtual.NomeCliente)}_{DateTime.Now:yyyyMMdd_HHmm}.txt";
            var caminho = Path.Combine(pasta, nomeAnexo);
            File.WriteAllText(caminho, _ultimoTxtGerado);

            AppState.UltimoRelatorioTxtGerado = _ultimoTxtGerado;
            AppState.UltimoRelatorioHtmlGerado = _ultimoHtmlGerado ?? _ultimoTxtGerado;
            AppState.UltimoRelatorioTipo = ObterTipoRelatorioSelecionado();
            AppState.UltimoRelatorioNomeAnexo = nomeAnexo;
            AppState.UltimoRelatorioCaminho = caminho;
            AppState.RelatorioDisponivelParaEmail = true;

            var janela = Window.GetWindow(this) as MainWindow;
            janela?.AbrirAbaEmailComRelatorio();

            TxtStatus.Text = "Relatório preparado na guia de e-mail. Revise o anexo e clique em enviar.";
            MessageBox.Show("Relatório preparado na guia do e-mail com o anexo já selecionado.", "SystemWM");
        }

        private string ObterTipoRelatorioSelecionado()
        {
            if (CmbTipoRelatorio?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                return tag.ToLowerInvariant();

            return "txt";
        }
    }
}
