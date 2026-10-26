using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SystemWM.Services;

namespace SystemWM.Views
{
    public partial class SettingsView : UserControl
    {
        private string? _caminhoRelatorioAtual;

        public static readonly RoutedUICommand AbrirArquivoCommand = new("AbrirArquivo", "AbrirArquivo", typeof(SettingsView));
        public static readonly RoutedUICommand AbrirPastaCommand = new("AbrirPasta", "AbrirPasta", typeof(SettingsView));

        public SettingsView()
        {
            InitializeComponent();

            CommandBindings.Add(new CommandBinding(AbrirArquivoCommand, ExecutarAbrirArquivo, CanExecuteAbrirArquivo));
            CommandBindings.Add(new CommandBinding(AbrirPastaCommand, ExecutarAbrirPasta, CanExecuteAbrirPasta));

            try
            {
                var s = AppState.Settings.Carregar();
                TxtRemetente.Text = s.EmailRemetente;
                TxtDestinoPadrao.Text = s.EmailDestinoPadrao;
                TxtAssuntoEmail.Text = s.EmailAssuntoPadrao;
                DefinirMensagemPadraoEmail();
                TxtNomeTecnico.Text = s.NomeTecnico;

                CarregarLaudosSalvos();
                PrepararRelatorioParaEmail();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Erro ao abrir a aba de e-mail: " + ex.Message;
            }
        }

        private void CanExecuteAbrirArquivo(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = e.Parameter is LaudoItem;
        }

        private void CanExecuteAbrirPasta(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = e.Parameter is LaudoItem;
        }

        private void ExecutarAbrirArquivo(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is not LaudoItem laudo)
                return;

            if (File.Exists(laudo.Caminho))
            {
                Process.Start(new ProcessStartInfo { FileName = laudo.Caminho, UseShellExecute = true });
            }
        }

        private void ExecutarAbrirPasta(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter is not LaudoItem laudo)
                return;

            var pasta = Path.GetDirectoryName(laudo.Caminho);
            if (!string.IsNullOrWhiteSpace(pasta) && Directory.Exists(pasta))
            {
                Process.Start(new ProcessStartInfo { FileName = pasta, UseShellExecute = true });
            }
        }

        private void PrepararRelatorioParaEmail()
        {
            if (!string.IsNullOrWhiteSpace(AppState.UltimoRelatorioTxtGerado))
            {
                TxtRelatorioSelecionado.Text = string.IsNullOrWhiteSpace(AppState.UltimoRelatorioNomeAnexo)
                    ? "Relatório pronto para anexo. Você pode escrever a mensagem abaixo."
                    : $"Anexo pronto: {AppState.UltimoRelatorioNomeAnexo}";
                if (!string.IsNullOrWhiteSpace(AppState.UltimoRelatorioTipo))
                {
                    var tipo = AppState.UltimoRelatorioTipo.ToUpperInvariant();
                    TxtRelatorioSelecionado.Text = $"Anexo pronto ({tipo}): {AppState.UltimoRelatorioNomeAnexo}";
                }
                TxtStatus.Text = "Relatório preparado para anexo. A mensagem abaixo permanece livre para edição.";
            }
        }

        private void CarregarLaudosSalvos()
        {
            if (ListaLaudosSalvos == null)
                return;

            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemWM", "Relatorios");
            try
            {
                if (!Directory.Exists(pasta))
                {
                    ListaLaudosSalvos.ItemsSource = null;
                    ListaLaudosSalvos.Items.Add("Nenhum relatório salvo foi encontrado.");
                    return;
                }

                var arquivos = Directory.EnumerateFiles(pasta)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f)?.ToLowerInvariant();
                        return ext == ".txt" || ext == ".html"; // PDF support removed
                    })
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();

                ListaLaudosSalvos.ItemsSource = null;
                if (!arquivos.Any())
                {
                    ListaLaudosSalvos.Items.Add("Nenhum relatório salvo foi encontrado.");
                    return;
                }

                var itens = arquivos.Select(arquivo => new LaudoItem
                {
                    Nome = Path.GetFileName(arquivo),
                    Caminho = arquivo,
                    DataModificacao = File.GetLastWriteTime(arquivo),
                    Tipo = Path.GetExtension(arquivo)?.TrimStart('.').ToLowerInvariant() ?? "txt"
                }).ToList();

                var relatoriosFiltrados = AplicarFiltroData(itens);
                ListaLaudosSalvos.ItemsSource = relatoriosFiltrados;
                ListaLaudosSalvos.DisplayMemberPath = "Resumo";
                var itemSelecionado = relatoriosFiltrados.FirstOrDefault(item =>
                    string.Equals(item.Caminho, AppState.UltimoRelatorioCaminho, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Nome, AppState.UltimoRelatorioNomeAnexo, StringComparison.OrdinalIgnoreCase));

                if (itemSelecionado != null)
                {
                    ListaLaudosSalvos.SelectedItem = itemSelecionado;
                    TxtRelatorioSelecionado.Text = $"Selecionado: {itemSelecionado.Nome} • {itemSelecionado.DataModificacao:dd/MM/yyyy HH:mm}";
                    return;
                }

                TxtRelatorioSelecionado.Text = "Selecione um laudo para carregar o conteúdo no corpo do e-mail.";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Erro ao carregar a lista de laudos: " + ex.Message;

                ListaLaudosSalvos.ItemsSource = null;
                ListaLaudosSalvos.Items.Clear();
                ListaLaudosSalvos.Items.Add("Não foi possível carregar os relatórios.");
            }
        }

        private List<LaudoItem> AplicarFiltroData(List<LaudoItem> itens)
        {
            if (DatePickerFiltroRelatorio.SelectedDate is not DateTime dataSelecionada)
                return itens;

            return itens.Where(item => item.DataModificacao.Date == dataSelecionada.Date)
                        .ToList();
        }

        private void DatePickerFiltroRelatorio_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            CarregarLaudosSalvos();
        }

        private void BtnLimparFiltroRelatorio_Click(object sender, RoutedEventArgs e)
        {
            DatePickerFiltroRelatorio.SelectedDate = null;
            CarregarLaudosSalvos();
        }

        private void ListaLaudosSalvos_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListaLaudosSalvos.SelectedItem is LaudoItem laudo)
            {
                _caminhoRelatorioAtual = laudo.Caminho;
                AppState.UltimoRelatorioCaminho = laudo.Caminho;
                AppState.UltimoRelatorioNomeAnexo = laudo.Nome;
                AppState.UltimoRelatorioTipo = laudo.Tipo;
                TxtRelatorioSelecionado.Text = $"Selecionado: {laudo.Nome} • {laudo.DataModificacao:dd/MM/yyyy HH:mm}";
            }
        }

        private void ListaLaudosSalvos_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not ListBox listBox)
                return;

            var item = listBox.InputHitTest(e.GetPosition(listBox)) as FrameworkElement;
            while (item != null && item is not ListBoxItem)
            {
                item = item.Parent as FrameworkElement;
            }

            if (item is ListBoxItem listBoxItem)
            {
                listBoxItem.IsSelected = true;
            }
        }

        private void MenuAbrirArquivo_Click(object sender, RoutedEventArgs e)
        {
            if (((MenuItem)sender).DataContext is not LaudoItem laudo)
                return;

            if (File.Exists(laudo.Caminho))
            {
                Process.Start(new ProcessStartInfo { FileName = laudo.Caminho, UseShellExecute = true });
            }
        }

        private void MenuAbrirPasta_Click(object sender, RoutedEventArgs e)
        {
            if (((MenuItem)sender).DataContext is not LaudoItem laudo)
                return;

            var pasta = Path.GetDirectoryName(laudo.Caminho);
            if (!string.IsNullOrWhiteSpace(pasta) && Directory.Exists(pasta))
            {
                Process.Start(new ProcessStartInfo { FileName = pasta, UseShellExecute = true });
            }
        }

        private void BtnCarregarRelatorioSalvo_Click(object sender, RoutedEventArgs e)
        {
            if (ListaLaudosSalvos.SelectedItem is not LaudoItem laudo)
            {
                TxtStatus.Text = "Nenhum relatório selecionado.";
                return;
            }

            var caminho = laudo.Caminho;
            if (!File.Exists(caminho))
            {
                TxtStatus.Text = "Arquivo não encontrado.";
                return;
            }

            var conteudo = File.ReadAllText(caminho);
            AppState.UltimoRelatorioTxtGerado = conteudo;
            AppState.UltimoRelatorioCaminho = caminho;
            AppState.UltimoRelatorioNomeAnexo = laudo.Nome;
            AppState.UltimoRelatorioTipo = laudo.Tipo;
            var settings = AppState.Settings.Carregar();
            DefinirMensagemPadraoEmail();
            TxtRelatorioSelecionado.Text = $"Selecionado para anexo: {laudo.Nome}";
            TxtStatus.Text = $"Relatório preparado para anexo: {caminho}";
        }

        private async void BtnTestarEnvio_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtDestinoPadrao.Text))
            {
                MessageBox.Show("Informe o destinatário no campo de composição do e-mail.", "SystemWM");
                return;
            }

            var settings = AppState.Settings.Carregar();
            var apiKey = AppState.Settings.ObterApiKeyVigente();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("API Key do Resend não configurada. Vá em Configurações.", "SystemWM");
                return;
            }

            var dadosEmail = EmailService.NormalizarDadosEnvio(
                TxtRemetente.Text,
                TxtDestinoPadrao.Text,
                TxtAssuntoEmail.Text,
                TxtCopiaEmail.Text,
                settings.EmailRemetente,
                settings.EmailDestinoPadrao,
                settings.EmailAssuntoPadrao);

            settings.EmailRemetente = dadosEmail.remetente;
            settings.EmailDestinoPadrao = dadosEmail.destinatario;
            TxtRemetente.Text = settings.EmailRemetente;
            TxtDestinoPadrao.Text = settings.EmailDestinoPadrao;

            if (string.IsNullOrWhiteSpace(TxtDestinoPadrao.Text))
            {
                MessageBox.Show("Informe um destinatário para o e-mail.", "SystemWM");
                return;
            }

            try
            {
                _ = new MailAddress(dadosEmail.remetente);
            }
            catch
            {
                MessageBox.Show("O e-mail do remetente informado é inválido.", "SystemWM");
                return;
            }

            try
            {
                _ = new MailAddress(dadosEmail.destinatario);
            }
            catch
            {
                MessageBox.Show("O e-mail do destinatário informado é inválido.", "SystemWM");
                return;
            }

            if (!string.IsNullOrWhiteSpace(dadosEmail.cc))
            {
                try
                {
                    _ = new MailAddress(dadosEmail.cc);
                }
                catch
                {
                    MessageBox.Show("O e-mail em cópia é inválido.", "SystemWM");
                    return;
                }
            }

            var assunto = dadosEmail.assunto;
            var corpo = EmailService.ConstruirCorpoMensagem(
                TxtCorpoEmail.Text,
                settings.EmailCorpoPadrao,
                AppState.UltimoRelatorioTxtGerado);
            var assuntoFinal = AppState.Email.AplicarTemplateEmail(
                assunto,
                string.IsNullOrWhiteSpace(TxtDestinoPadrao.Text) ? "Cliente" : TxtDestinoPadrao.Text,
                string.Empty,
                string.IsNullOrWhiteSpace(TxtNomeTecnico.Text) ? "Técnico" : TxtNomeTecnico.Text,
                DateTime.Now.ToString("dd/MM/yyyy"));
            var corpoFinal = AppState.Email.AplicarTemplateEmail(
                corpo,
                string.IsNullOrWhiteSpace(TxtDestinoPadrao.Text) ? "Cliente" : TxtDestinoPadrao.Text,
                string.Empty,
                string.IsNullOrWhiteSpace(TxtNomeTecnico.Text) ? "Técnico" : TxtNomeTecnico.Text,
                DateTime.Now.ToString("dd/MM/yyyy"),
                relatorio: string.Empty);

            var anexos = new List<(string Nome, byte[] Conteudo)>();
            var relatorioTxt = !string.IsNullOrWhiteSpace(AppState.UltimoRelatorioTxtGerado)
                ? AppState.UltimoRelatorioTxtGerado
                : string.Empty;

            if (ChkAnexarTxt.IsChecked == true)
            {
                var nomeTxt = string.IsNullOrWhiteSpace(AppState.UltimoRelatorioNomeAnexo) ? $"Relatorio_{DateTime.Now:yyyyMMdd_HHmm}.txt" : AppState.UltimoRelatorioNomeAnexo;
                anexos.Add((nomeTxt, Encoding.UTF8.GetBytes(relatorioTxt)));
            }

            if (ChkAnexarHtml.IsChecked == true)
            {
                var htmlConteudo = CriarHtmlPersonalizadoDoRelatorio(relatorioTxt);
                var nomeHtml = Path.ChangeExtension(AppState.UltimoRelatorioNomeAnexo ?? $"Relatorio_{DateTime.Now:yyyyMMdd_HHmm}.txt", ".html");
                anexos.Add((nomeHtml, Encoding.UTF8.GetBytes(htmlConteudo)));
            }

            // PDF attachment option removed

            BtnTestarEnvio.IsEnabled = false;
            TxtStatus.Text = "Enviando e-mail...";

            var (sucesso, mensagem) = await AppState.Email.EnviarRelatorioAsync(
                apiKey,
                dadosEmail.remetente,
                dadosEmail.destinatario,
                assuntoFinal,
                corpoHtml: $"<p>{WebUtility.HtmlEncode(corpoFinal).Replace("\\n", "<br>")}</p>",
                corpoTexto: corpoFinal,
                cc: dadosEmail.cc,
                anexos: anexos);

            BtnTestarEnvio.IsEnabled = true;
            TxtStatus.Text = mensagem;

            if (sucesso)
                MessageBox.Show("E-mail de teste enviado com sucesso.", "SystemWM");
            else
                MessageBox.Show(mensagem, "SystemWM");
        }

        private string CriarHtmlPersonalizadoDoRelatorio(string relatorioTxt)
        {
            var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons", "logo.png");
            var logoTag = File.Exists(logoPath)
                ? "<img src=\"cid:systemwm-logo\" alt=\"SystemWM\" style=\"max-height:80px; margin-bottom:16px;\"/>"
                : string.Empty;

            var html = new StringBuilder();
            html.AppendLine("<!DOCTYPE html>");
            html.AppendLine("<html lang=\"pt-BR\">");
            html.AppendLine("<head>");
            html.AppendLine("  <meta charset=\"UTF-8\"/>");
            html.AppendLine("  <title>Relatório SystemWM</title>");
            html.AppendLine("  <style>");
            html.AppendLine("    body { background:#0B0F1A; color:#E5E9F0; font-family:Segoe UI, sans-serif; margin:0; padding:24px; }");
            html.AppendLine("    .container { max-width:900px; margin:0 auto; }");
            html.AppendLine("    .header { display:flex; align-items:center; justify-content:space-between; gap:16px; margin-bottom:24px; }");
            html.AppendLine("    .card { background:#131A2B; border:1px solid #232A42; border-radius:16px; padding:20px; box-shadow:0 16px 40px rgba(0,0,0,0.18); }");
            html.AppendLine("    h1 { margin:0; color:#3DDC84; }");
            html.AppendLine("    pre { white-space:pre-wrap; word-break:break-word; color:#E5E9F0; font-size:13px; line-height:1.5; background:#0F172A; border:1px solid #232A42; border-radius:12px; padding:16px; overflow:auto; }");
            html.AppendLine("    .footer { margin-top:24px; font-size:12px; color:#8EA0C9; }");
            html.AppendLine("  </style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.AppendLine("  <div class=\"container\">");
            html.AppendLine("    <div class=\"header\">");
            html.AppendLine("      <div>");
            html.AppendLine("        <h1>Relatório SystemWM</h1>");
            html.AppendLine("        <div>Relatório gerado automaticamente.</div>");
            html.AppendLine("      </div>");
            html.AppendLine($"      {logoTag}");
            html.AppendLine("    </div>");
            html.AppendLine("    <div class=\"card\">");
            html.AppendLine($"      <pre>{System.Net.WebUtility.HtmlEncode(relatorioTxt)}</pre>");
            html.AppendLine("    </div>");
            html.AppendLine("    <div class=\"footer\">");
            html.AppendLine("      <div>SystemWM — relatório convertido do TXT para HTML.</div>");
            html.AppendLine("    </div>");
            html.AppendLine("  </div>");
            html.AppendLine("</body>");
            html.AppendLine("</html>");

            return html.ToString();
        }

        private void BtnSalvar_Click(object sender, RoutedEventArgs e)
        {
            var s = AppState.Settings.Carregar();
            s.EmailRemetente = string.IsNullOrWhiteSpace(TxtRemetente.Text) ? "SystemWM <onboarding@resend.dev>" : TxtRemetente.Text;
            s.EmailDestinoPadrao = TxtDestinoPadrao.Text;
            s.EmailAssuntoPadrao = string.IsNullOrWhiteSpace(TxtAssuntoEmail.Text) ? "Relatório de Visita Técnica - {cliente}" : TxtAssuntoEmail.Text;
            s.EmailCorpoPadrao = "Boa tarde,\n\nSegue o relatório em anexo.\n\nAtenciosamente,\nWillyan";
            DefinirMensagemPadraoEmail();
            s.NomeTecnico = TxtNomeTecnico.Text;

            AppState.Settings.Salvar(s);
            TxtStatus.Text = "Configurações salvas com sucesso.";
        }

        private void BtnLimparCampos_Click(object sender, RoutedEventArgs e)
        {
            TxtAssuntoEmail.Text = string.Empty;
            TxtDestinoPadrao.Text = string.Empty;
            TxtCopiaEmail.Text = string.Empty;
            TxtRemetente.Text = string.Empty;
            TxtNomeTecnico.Text = string.Empty;
            DefinirMensagemPadraoEmail();

            TxtStatus.Text = "Mensagem limpa. O relatório anexado permanece disponível para envio.";
        }

        private void BtnLimparRelatorio_Click(object sender, RoutedEventArgs e)
        {
            AppState.UltimoRelatorioCaminho = null;
            AppState.UltimoRelatorioNomeAnexo = null;
            AppState.UltimoRelatorioTipo = null;
            AppState.UltimoRelatorioTxtGerado = null;
            _caminhoRelatorioAtual = null;
            ListaLaudosSalvos.SelectedItem = null;
            TxtRelatorioSelecionado.Text = "Nenhum relatório carregado.";
            TxtStatus.Text = "Relatório removido do anexo. A mensagem continua disponível.";
        }

        private void BtnAbrirPastaRelatorios_Click(object sender, RoutedEventArgs e)
        {
            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SystemWM", "Relatorios");
            Directory.CreateDirectory(pasta);
            Process.Start(new ProcessStartInfo { FileName = pasta, UseShellExecute = true });
        }

        private void DefinirMensagemPadraoEmail()
        {
            TxtCorpoEmail.Text = "Boa tarde,\n\nSegue o relatório em anexo.\n\nAtenciosamente,\nWillyan";
        }

        private sealed class LaudoItem
        {
            public string Nome { get; set; } = string.Empty;
            public string Caminho { get; set; } = string.Empty;
            public DateTime DataModificacao { get; set; }
            public string Tipo { get; set; } = "txt";
            public string Resumo => $"{Nome} • {DataModificacao:dd/MM/yyyy HH:mm}";
        }
    }
}
