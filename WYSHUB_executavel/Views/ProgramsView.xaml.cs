using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SystemWM.Models;

namespace SystemWM.Views
{
    public partial class ProgramsView : UserControl
    {
        private List<ProgramaInstalado> _programas = new();
        private bool _ocultarServicos;
        private bool _ocultarDrivers;
        private bool _ocultarMicrosoft;

        public ProgramsView()
        {
            InitializeComponent();
            CarregarProgramas();
        }

        private void CarregarProgramas()
        {
            var programasCarregados = AppState.UltimosProgramas ?? AppState.Programs.Listar();
            _programas = programasCarregados;
            AppState.UltimosProgramas = _programas;

            if (AppState.UltimosProgramas == null || !AppState.UltimosProgramas.Any(p => p.IncluirNoRelatorio))
            {
                foreach (var programa in _programas)
                {
                    programa.IncluirNoRelatorio = false;
                }
            }

            AtualizarListaProgramas();
        }

        private void TxtFiltroProgramas_TextChanged(object sender, TextChangedEventArgs e)
        {
            AtualizarListaProgramas();
        }

        private void ChkOcultarDrivers_Changed(object sender, RoutedEventArgs e)
        {
            _ocultarDrivers = ChkOcultarDrivers.IsChecked == true;
            AtualizarListaProgramas();
        }

        private void ChkOcultarMicrosoft_Changed(object sender, RoutedEventArgs e)
        {
            _ocultarMicrosoft = ChkOcultarMicrosoft.IsChecked == true;
            AtualizarListaProgramas();
        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            AtualizarContadorProgramas();
            AtualizarPainelSelecionados();
        }

        private void DesinstalarPrograma_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem)
            {
                return;
            }

            if (menuItem.CommandParameter is not ProgramaInstalado programa)
            {
                return;
            }

            DesinstalarPrograma(programa);
        }

        private void DesinstalarPrograma(ProgramaInstalado programa)
        {
            if (programa == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(programa.UninstallString))
            {
                MessageBox.Show($"Não foi possível encontrar um comando de desinstalação para '{programa.Nome}'.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirmacao = MessageBox.Show($"Deseja iniciar a desinstalação de '{programa.Nome}'?", "SystemWM", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmacao != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var startInfo = CriarStartInfoDesinstalacao(programa.UninstallString);
                Process.Start(startInfo);
                MessageBox.Show($"A desinstalação de '{programa.Nome}' foi iniciada.", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível iniciar a desinstalação: {ex.Message}", "SystemWM", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static ProcessStartInfo CriarStartInfoDesinstalacao(string uninstallString)
        {
            var comando = uninstallString.Trim();
            var startInfo = new ProcessStartInfo { UseShellExecute = true, Verb = "runas" };

            if (string.IsNullOrWhiteSpace(comando))
            {
                throw new InvalidOperationException("Comando de desinstalação vazio.");
            }

            if (comando.StartsWith('"'))
            {
                var fimCaminho = comando.IndexOf('"', 1);
                if (fimCaminho > 1)
                {
                    startInfo.FileName = comando[1..fimCaminho];
                    startInfo.Arguments = comando[(fimCaminho + 1)..].Trim();
                    return startInfo;
                }
            }

            var partes = comando.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            startInfo.FileName = partes[0];
            if (partes.Length > 1)
            {
                startInfo.Arguments = partes[1];
            }

            return startInfo;
        }

        private void BtnSelecionarTodos_Click(object sender, RoutedEventArgs e)
        {
            foreach (var programa in ObterProgramasVisiveis())
            {
                programa.IncluirNoRelatorio = true;
            }

            AtualizarListaProgramas();
        }

        private void BtnDesmarcarTodos_Click(object sender, RoutedEventArgs e)
        {
            foreach (var programa in ObterProgramasVisiveis())
            {
                programa.IncluirNoRelatorio = false;
            }

            AtualizarListaProgramas();
        }

        private void BtnOcultarServicos_Click(object sender, RoutedEventArgs e)
        {
            _ocultarServicos = !_ocultarServicos;
            BtnOcultarServicos.Content = _ocultarServicos ? "👁 Mostrar serviços" : "🕵 Ocultar serviços";
            AtualizarListaProgramas();
        }

        private void BtnImportarLista_Click(object sender, RoutedEventArgs e)
        {
            var dialogo = new OpenFileDialog
            {
                Title = "Selecionar lista de programas",
                Filter = "Arquivos de texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*"
            };

            if (dialogo.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var termos = File.ReadAllLines(dialogo.FileName)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .Select(NormalizarTexto)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                if (termos.Count == 0)
                {
                    MessageBox.Show("O arquivo não contém programas para importar.", "SystemWM");
                    return;
                }

                var marcados = 0;
                foreach (var programa in _programas)
                {
                    var nomeNormalizado = NormalizarTexto(programa.Nome);
                    if (termos.Any(termo => CorrespondenciaInteligente(nomeNormalizado, termo)))
                    {
                        programa.IncluirNoRelatorio = true;
                        marcados++;
                    }
                }

                AtualizarListaProgramas();
                MessageBox.Show($"{marcados} programa(s) foram selecionados automaticamente a partir do arquivo.", "SystemWM");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível importar a lista: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private IEnumerable<ProgramaInstalado> ObterProgramasVisiveis()
        {
            var filtro = TxtFiltroProgramas.Text?.Trim() ?? string.Empty;
            IEnumerable<ProgramaInstalado> visiveis = _programas;

            if (_ocultarServicos)
            {
                visiveis = visiveis.Where(p => !EhServico(p));
            }

            if (_ocultarDrivers)
            {
                visiveis = visiveis.Where(p => !EhDriver(p));
            }

            if (_ocultarMicrosoft)
            {
                visiveis = visiveis.Where(p => !EhMicrosoft(p));
            }

            if (!string.IsNullOrWhiteSpace(filtro))
            {
                var filtroNormalizado = NormalizarTexto(filtro);
                visiveis = visiveis.Where(p =>
                    (!string.IsNullOrWhiteSpace(p.Nome) && NormalizarTexto(p.Nome).Contains(filtroNormalizado, StringComparison.Ordinal))
                    || (!string.IsNullOrWhiteSpace(p.Fabricante) && NormalizarTexto(p.Fabricante).Contains(filtroNormalizado, StringComparison.Ordinal))
                    || (!string.IsNullOrWhiteSpace(p.Versao) && NormalizarTexto(p.Versao).Contains(filtroNormalizado, StringComparison.Ordinal))
                );
            }

            return visiveis.ToList();
        }

        private void AtualizarListaProgramas()
        {
            ListaProgramasRelatorio.ItemsSource = ObterProgramasVisiveis().ToList();
            AtualizarContadorProgramas();
            AtualizarPainelSelecionados();
        }

        private void AtualizarContadorProgramas()
        {
            var selecionados = _programas.Count(p => p.IncluirNoRelatorio);
            TxtContadorProgramas.Text = $"{selecionados} programas selecionados";
        }

        private void AtualizarPainelSelecionados()
        {
            var selecionados = _programas.Where(p => p.IncluirNoRelatorio).OrderBy(p => p.Nome).ToList();
            ListaProgramasSelecionados.ItemsSource = selecionados;
        }

        private static bool EhServico(ProgramaInstalado programa)
        {
            if (string.IsNullOrWhiteSpace(programa.Nome))
                return false;

            return programa.Nome.Contains("service", StringComparison.OrdinalIgnoreCase)
                || programa.Nome.Contains("serviço", StringComparison.OrdinalIgnoreCase)
                || programa.Nome.Contains("serviços", StringComparison.OrdinalIgnoreCase)
                || programa.Nome.Contains("runtime", StringComparison.OrdinalIgnoreCase);
        }

        private static bool EhDriver(ProgramaInstalado programa)
        {
            if (programa == null) return false;

            var nome = (programa.Nome ?? string.Empty).ToLowerInvariant();
            var fab = (programa.Fabricante ?? string.Empty).ToLowerInvariant();
            var combinado = nome + " " + fab;

            // Palavras-chave comuns para drivers e componentes de sistema
            var keywords = new[]
            {
                "driver","controlador","amd","nvidia","intel","realtek","display","graphics","audio","hd audio",
                "directx","vulkan","chipset","gpu","video","printer","impressora","monitor","touchpad","bluetooth",
                "network","lan","wifi","wireless","conexão","radeon","geforce","adrenalin","intel(r)","intel",
                "amd software","nvidia geforce","driver package"
            };

            return keywords.Any(k => combinado.Contains(k));
        }

        private static bool EhMicrosoft(ProgramaInstalado programa)
        {
            if (programa == null) return false;
            if (!string.IsNullOrWhiteSpace(programa.Fabricante) && programa.Fabricante.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrWhiteSpace(programa.Nome) && programa.Nome.IndexOf("microsoft", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static string NormalizarTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var normalized = texto.Normalize(NormalizationForm.FormD);
            foreach (var ch in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC)
                .ToLowerInvariant()
                .Replace("&", " ")
                .Replace("-", " ")
                .Replace("_", " ")
                .Replace("/", " ")
                .Replace("\\", " ")
                .Replace(".", " ")
                .Replace(",", " ")
                .Replace("(", " ")
                .Replace(")", " ")
                .Replace("[", " ")
                .Replace("]", " ")
                .Replace("+", " ");
        }

        private static bool CorrespondenciaInteligente(string nomePrograma, string termo)
        {
            if (string.IsNullOrWhiteSpace(nomePrograma) || string.IsNullOrWhiteSpace(termo))
            {
                return false;
            }

            var termoNormalizado = NormalizarTexto(termo);
            if (termoNormalizado.Length <= 2)
            {
                return nomePrograma.Contains(termoNormalizado, StringComparison.Ordinal);
            }

            if (nomePrograma.Contains(termoNormalizado, StringComparison.Ordinal))
            {
                return true;
            }

            var palavrasNome = nomePrograma.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var palavrasTermo = termoNormalizado.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (palavrasTermo.Length == 0)
            {
                return false;
            }

            if (palavrasTermo.Length == 1)
            {
                return palavrasNome.Any(p => p.StartsWith(palavrasTermo[0], StringComparison.Ordinal) || p.Contains(palavrasTermo[0], StringComparison.Ordinal));
            }

            return palavrasTermo.All(p => palavrasNome.Any(w => w.StartsWith(p, StringComparison.Ordinal) || w.Contains(p, StringComparison.Ordinal)));
        }
    }
}
