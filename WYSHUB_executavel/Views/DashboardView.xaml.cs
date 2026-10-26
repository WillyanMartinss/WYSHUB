using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SystemWM.Models;

namespace SystemWM.Views
{
    public partial class DashboardView : UserControl
    {
        private List<DiscoInfo> _discosDashboard = new();
        private int _indiceDiscoExibido = 0;
        private int _indiceCpuExibido = 0;

        public DashboardView()
        {
            InitializeComponent();
            AtualizarContadorCards();
            _ = CarregarDadosAsync();
        }

        private async void BtnAtualizar_Click(object sender, RoutedEventArgs e) => await CarregarDadosAsync();

        private async Task CarregarDadosAsync()
        {
            TxtCarregando.Visibility = Visibility.Visible;
            GridCards.Visibility = Visibility.Collapsed;

            try
            {
                DiagnosticoCompleto diag = await Task.Run(() => AppState.Hardware.ColetarTudo());
                AppState.UltimoDiagnostico = diag;

                PreencherCpu(diag.Cpu);
                PreencherRam(diag.Ram);
                PreencherDisco(diag.Discos);
                PreencherPlacaMae(diag.Sistema);
                PreencherGpu(diag.Gpu);
                PreencherSistema(diag.Sistema);

                TxtCarregando.Visibility = Visibility.Collapsed;
                GridCards.Visibility = Visibility.Visible;

                // Busca o IP público sem travar a tela (a rede pode demorar/estar indisponível)
                var ipPublico = await AppState.Network.ObterIpPublicoAsync();
                diag.Sistema.IpPublica = ipPublico;
                TxtIpPublica.Text = $"IP Pública: {ipPublico}";
            }
            catch
            {
                TxtCarregando.Text = "Não foi possível carregar os sensores. O app continuará abrindo com as informações básicas do sistema.";
                GridCards.Visibility = Visibility.Visible;
            }
        }

        public static double ObterTemperaturaExibicao(CpuInfo cpu, int indice)
        {
            if (cpu is null)
                return 0;

            if (indice < 0)
                indice = 0;

            if (cpu.TemperaturaPorNucleo.Count > indice && cpu.TemperaturaPorNucleo[indice] > 0)
                return cpu.TemperaturaPorNucleo[indice];

            return cpu.TemperaturaC > 0 ? cpu.TemperaturaC : 0;
        }

        private void PreencherCpu(CpuInfo cpu)
        {
            TxtCpuNome.Text = cpu.Nome;

            var totalNucleos = Math.Max(1, Math.Max(cpu.CargaPorNucleo.Count, cpu.TemperaturaPorNucleo.Count));
            if (cpu.NucleosFisicos > totalNucleos)
                totalNucleos = cpu.NucleosFisicos;

            if (totalNucleos < 2)
            {
                totalNucleos = 1;
                _indiceCpuExibido = 0;
            }

            var indice = Math.Min(_indiceCpuExibido, totalNucleos - 1);
            var usoNucleo = cpu.CargaPorNucleo.Count > indice ? cpu.CargaPorNucleo[indice] : cpu.UsoAtualPercent;
            var tempNucleo = ObterTemperaturaExibicao(cpu, indice);

            TxtCpuUso.Text = usoNucleo > 0 ? $"{usoNucleo:0}%" : "N/D";
            TxtCpuFreq.Text = totalNucleos > 1
                ? $"Núcleo {indice + 1} / {totalNucleos}"
                : $"Freq: {cpu.FrequenciaAtualGHz} GHz (base {cpu.FrequenciaBaseGHz} GHz)";
            TxtCpuTemp.Text = totalNucleos > 1
                ? $"Temp núcleo {indice + 1}: {(tempNucleo > 0 ? tempNucleo.ToString("0") : "N/D")}°C  |  TDP: {(cpu.TdpWatts > 0 ? cpu.TdpWatts.ToString("0") : "N/D")}W"
                : $"Temp: {(cpu.TemperaturaC > 0 ? cpu.TemperaturaC.ToString("0") : "N/D")}°C  |  TDP: {(cpu.TdpWatts > 0 ? cpu.TdpWatts.ToString("0") : "N/D")}W";
            TxtCpuFans.Text = $"Fan CPU: {(cpu.FanCpuRpm > 0 ? cpu.FanCpuRpm.ToString("0") : "N/D")} RPM  |  Sistema: {(cpu.FanSistemaRpm > 0 ? cpu.FanSistemaRpm.ToString("0") : "N/D")} RPM";

            IndicadorCpu.Visibility = totalNucleos > 1 ? Visibility.Visible : Visibility.Collapsed;
            AtualizarIndicadorPontos(IndicadorCpu, totalNucleos, _indiceCpuExibido);

            AtualizarRing(CpuRing, TxtCpuRing, Math.Max(0, cpu.UsoAtualPercent), 84d, 10d);
            AtualizarTempRing(CpuTempRing, TxtCpuTempRing, Math.Max(0, tempNucleo), 95, 64d, 8d);
        }

        private void PreencherRam(RamInfo ram)
        {
            TxtRamTotal.Text = $"Total instalado: {ram.TotalGB} GB ({ram.Tipo} @ {ram.VelocidadeMHz} MHz)";
            TxtRamUso.Text = $"{ram.UsoPercent}%";
            TxtRamUsada.Text = $"Em uso: {ram.UsadaGB} GB";
            TxtRamDisp.Text = $"Disponível: {ram.DisponivelGB} GB";

            AtualizarRing(RamRing, TxtRamRing, ram.UsoPercent, 100d, 10d);
        }

        private void PreencherDisco(System.Collections.Generic.List<DiscoInfo> discos)
        {
            _discosDashboard = discos?.Take(2).ToList() ?? new List<DiscoInfo>();
            _indiceDiscoExibido = 0;
            AtualizarCardDiscoAtual();
        }

        private void AtualizarCardDiscoAtual()
        {
            var lista = _discosDashboard;
            var d = lista.Count > 0 ? lista[0] : new DiscoInfo();
            var dAtual = lista.Count > _indiceDiscoExibido ? lista[_indiceDiscoExibido] : d;

            var marca = string.IsNullOrWhiteSpace(dAtual.Marca) ? "Desconhecida" : dAtual.Marca;
            var modelo = string.IsNullOrWhiteSpace(dAtual.Modelo) ? "SSD" : dAtual.Modelo;
            if (modelo.Contains(marca, StringComparison.OrdinalIgnoreCase))
                modelo = modelo.Replace(marca, string.Empty, StringComparison.OrdinalIgnoreCase).Trim(' ', '-', '_');

            TxtDiscoMarca.Text = $"Marca: {marca}";
            TxtDiscoModelo.Text = $"Modelo: {modelo}";

            var usoPercent = dAtual.CapacidadeTotalGB > 0 && dAtual.EspacoUsadoGB >= 0
                ? Math.Round((dAtual.EspacoUsadoGB / dAtual.CapacidadeTotalGB) * 100d)
                : dAtual.UsoPercent;

            TxtDiscoUso.Text = usoPercent > 0 ? $"{usoPercent:0}%" : "N/D";
            TxtDiscoLivre.Text = dAtual.CapacidadeTotalGB > 0
                ? $"Livre: {dAtual.EspacoLivreGB:0.0} GB de {dAtual.CapacidadeTotalGB:0.0} GB"
                : "Livre: N/D";
            TxtDiscoTemp.Text = dAtual.TemperaturaC > 0
                ? $"Temp: {dAtual.TemperaturaC:0}°C  |  {dAtual.Interface}"
                : $"Temp: N/D  |  {dAtual.Interface}";
            TxtDiscoSaude.Text = string.IsNullOrWhiteSpace(dAtual.SaudePercentTexto)
                ? "Saúde: Saudável"
                : $"Saúde: {dAtual.SaudePercentTexto}";

            IndicadorDisco.Visibility = lista.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            AtualizarIndicadorPontos(IndicadorDisco, 2, _indiceDiscoExibido);

            AtualizarRing(DiscoRing, TxtDiscoRing, Math.Max(0, usoPercent), 84d, 10d);
            AtualizarTempRing(DiscoTempRing, TxtDiscoTempRing, Math.Max(0, dAtual.TemperaturaC), 70, 64d, 8d);
        }

        private void DiscoCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_discosDashboard.Count < 2)
                return;

            IndicadorDisco.Visibility = Visibility.Visible;
            AtualizarIndicadorPontos(IndicadorDisco, 2, _indiceDiscoExibido);
        }

        private void DiscoCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_discosDashboard.Count < 2)
                return;

            IndicadorDisco.Visibility = Visibility.Collapsed;
        }

        private void DiscoCard_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            try
            {
                if (_discosDashboard.Count < 2)
                {
                    e.Handled = true;
                    return;
                }

                _indiceDiscoExibido = e.Delta > 0 ? 0 : 1;
                AtualizarCardDiscoAtual();
                e.Handled = true;
            }
            catch
            {
                e.Handled = true;
            }
        }

        private void DiscoCard_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (_discosDashboard.Count < 2)
                    return;

                _indiceDiscoExibido = _indiceDiscoExibido == 0 ? 1 : 0;
                AtualizarCardDiscoAtual();
                e.Handled = true;
            }
            catch
            {
                e.Handled = true;
            }
        }

        private void CpuCard_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            try
            {
                if (AppState.UltimoDiagnostico?.Cpu is null)
                {
                    e.Handled = true;
                    return;
                }

                var totalNucleos = Math.Max(1, AppState.UltimoDiagnostico.Cpu.CargaPorNucleo.Count > 0
                    ? AppState.UltimoDiagnostico.Cpu.CargaPorNucleo.Count
                    : AppState.UltimoDiagnostico.Cpu.NucleosFisicos);

                if (totalNucleos < 2)
                {
                    e.Handled = true;
                    return;
                }

                if (e.Delta > 0)
                    _indiceCpuExibido = _indiceCpuExibido <= 0 ? totalNucleos - 1 : _indiceCpuExibido - 1;
                else
                    _indiceCpuExibido = _indiceCpuExibido >= totalNucleos - 1 ? 0 : _indiceCpuExibido + 1;

                AtualizarCardCpuAtual();
                e.Handled = true;
            }
            catch
            {
                e.Handled = true;
            }
        }

        private void CpuCard_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (AppState.UltimoDiagnostico?.Cpu is null)
                    return;

                var totalNucleos = Math.Max(1, AppState.UltimoDiagnostico.Cpu.CargaPorNucleo.Count > 0
                    ? AppState.UltimoDiagnostico.Cpu.CargaPorNucleo.Count
                    : AppState.UltimoDiagnostico.Cpu.NucleosFisicos);

                if (totalNucleos < 2)
                    return;

                _indiceCpuExibido = _indiceCpuExibido >= totalNucleos - 1 ? 0 : _indiceCpuExibido + 1;
                AtualizarCardCpuAtual();
                e.Handled = true;
            }
            catch
            {
                e.Handled = true;
            }
        }

        private void CpuCard_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (AppState.UltimoDiagnostico?.Cpu is null)
                    return;

                var totalNucleos = Math.Max(1, AppState.UltimoDiagnostico.Cpu.CargaPorNucleo.Count > 0
                    ? AppState.UltimoDiagnostico.Cpu.CargaPorNucleo.Count
                    : AppState.UltimoDiagnostico.Cpu.NucleosFisicos);

                IndicadorCpu.Visibility = totalNucleos > 1 ? Visibility.Visible : Visibility.Collapsed;
                AtualizarIndicadorPontos(IndicadorCpu, totalNucleos, _indiceCpuExibido);
            }
            catch
            {
                IndicadorCpu.Visibility = Visibility.Collapsed;
            }
        }

        private void CpuCard_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                IndicadorCpu.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // ignora
            }
        }

        private void AtualizarCardCpuAtual()
        {
            if (AppState.UltimoDiagnostico?.Cpu is null)
                return;

            PreencherCpu(AppState.UltimoDiagnostico.Cpu);
        }

        private void CardDashboard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not Border card)
                return;

            var nome = card.Name;
            if (string.IsNullOrWhiteSpace(nome))
                return;

            if (AppState.DashboardCardsSelecionados.Contains(nome))
            {
                AppState.DashboardCardsSelecionados.Remove(nome);
            }
            else
            {
                AppState.DashboardCardsSelecionados.Add(nome);
            }

            AtualizarBordaCards();
            AtualizarContadorCards();
            e.Handled = true;
        }

        private void CpuCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CardDashboard_MouseLeftButtonDown(sender, e);
        }

        private void DiscoCard_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            CardDashboard_MouseLeftButtonDown(sender, e);
        }

        private void AtualizarBordaCards()
        {
            foreach (var border in new[] { CpuCard, RamCard, DiscoCard, MoboCard, GpuCard })
            {
                if (border == null) continue;

                var selecionado = AppState.DashboardCardsSelecionados.Contains(border.Name);
                var brushPadrao = (Brush)FindResource("BorderCard");
                var brushSelecionado = new SolidColorBrush(Color.FromRgb(90, 180, 140));

                border.BorderBrush = selecionado ? brushSelecionado : brushPadrao;
                border.BorderThickness = selecionado ? new Thickness(1.5) : new Thickness(1);
                border.Padding = selecionado ? new Thickness(2) : new Thickness(0);
                border.Background = selecionado
                    ? new SolidColorBrush(Color.FromRgb(21, 32, 45))
                    : new SolidColorBrush(Color.FromRgb(16, 25, 37));
                border.Opacity = selecionado ? 1 : 0.96;
                border.Cursor = System.Windows.Input.Cursors.Hand;
                border.Effect = selecionado
                    ? new System.Windows.Media.Effects.DropShadowEffect
                    {
                        BlurRadius = 10,
                        ShadowDepth = 0,
                        Opacity = 0.22,
                        Color = Color.FromRgb(92, 182, 140)
                    }
                    : null;
                border.ToolTip = selecionado
                    ? "Card incluído no relatório"
                    : "Clique para incluir no relatório";
            }
        }

        private void AtualizarContadorCards()
        {
            TxtCardsSelecionados.Text = AppState.DashboardCardsSelecionados.Count.ToString();
        }

        private void AtualizarIndicadorPontos(StackPanel painel, int totalPontos, int pontoAtivo)
        {
            if (painel == null)
                return;

            var pontos = painel.Children.OfType<Ellipse>().ToList();
            var indiceAtivo = Math.Min(Math.Max(0, pontoAtivo), Math.Max(0, pontos.Count - 1));

            for (var i = 0; i < pontos.Count; i++)
            {
                var preenchimento = (i == indiceAtivo && totalPontos > 1)
                    ? (Brush)FindResource("AzulDestaque")
                    : (Brush)FindResource("BorderCard");

                pontos[i].Fill = preenchimento;
                pontos[i].Opacity = i == indiceAtivo && totalPontos > 1 ? 1 : 0.6;
                pontos[i].RenderTransform = new ScaleTransform(i == indiceAtivo && totalPontos > 1 ? 1.15 : 1, i == indiceAtivo && totalPontos > 1 ? 1.15 : 1);
            }
        }

        private void PreencherPlacaMae(SistemaInfo sis)
        {
            TxtMoboNome.Text = sis.PlacaMae;
            TxtMoboChipset.Text = sis.Chipset;
            TxtMoboBios.Text = $"BIOS: {sis.BiosVersao}";
            TxtMoboUptime.Text = $"Uptime: {sis.Uptime}";
            AtualizarRing(MoboRing, TxtMoboRing, 100, 84d, 10d);
        }

        private void PreencherGpu(GpuInfo? gpu)
        {
            if (gpu == null)
            {
                TxtGpuNome.Text = "GPU não detectada";
                TxtGpuUso.Text = "-";
                TxtGpuTemp.Text = "Temp: -";
                TxtGpuMem.Text = "Memória: -";
                TxtGpuFans.Text = "Fan: -";
                AtualizarRing(GpuRing, TxtGpuRing, 0, 84d, 10d);
                AtualizarTempRing(GpuTempRing, TxtGpuTempRing, 0, 100, 64d, 8d);
                return;
            }

            TxtGpuNome.Text = gpu.Nome;
            TxtGpuUso.Text = $"{gpu.UsoPercent:0}%";
            TxtGpuTemp.Text = $"{gpu.TemperaturaC:0}°C";
            TxtGpuMem.Text = $"{gpu.MemoriaUsadaGB:0.0} / {gpu.MemoriaTotalGB:0.0} GB";
            TxtGpuFans.Text = $"{gpu.FanRpm:0} RPM";

            AtualizarRing(GpuRing, TxtGpuRing, gpu.UsoPercent, 84d, 10d);
            AtualizarTempRing(GpuTempRing, TxtGpuTempRing, gpu.TemperaturaC, 95, 64d, 8d);
        }

        private void PreencherSistema(SistemaInfo sis)
        {
            TxtSisHost.Text = $"Host: {sis.NomeHost}";
            TxtSisBios.Text = $"Usuário: {sis.Usuario}  |  BIOS: {sis.BiosVersao}";
            TxtSisUptime.Text = $"SO: {sis.SistemaOperacional} ({sis.Arquitetura})  |  Uptime: {sis.Uptime}";

            TxtRede.Text = $"Rede: {(string.IsNullOrEmpty(sis.RedeStatus) ? "Conectado" : sis.RedeStatus)}";
            TxtIpLocal.Text = $"IP Local: {sis.IpLocal}";
            TxtIpPublica.Text = $"IP Pública: {sis.IpPublica}";
        }

        private static void AtualizarRing(Path ring, TextBlock valor, double percent, double size, double thickness)
        {
            var pct = Math.Max(0, Math.Min(100, percent));
            ring.Width = size;
            ring.Height = size;
            ring.StrokeThickness = thickness;
            ring.Data = CriarRingGeometry(pct, size, thickness);
            valor.Text = $"{pct:0}%";
            ring.Stroke = GetRingBrush(pct);
            AnimarRing(ring);
        }

        private static void AtualizarTempRing(Path ring, TextBlock valor, double temp, double maxTemp, double size, double thickness)
        {
            if (temp <= 0 || maxTemp <= 0)
            {
                ring.Width = size;
                ring.Height = size;
                ring.StrokeThickness = thickness;
                ring.Data = CriarRingGeometry(0, size, thickness);
                valor.Text = "N/D";
                ring.Stroke = GetRingBrush(0, true);
                AnimarRing(ring);
                return;
            }

            var pct = Math.Max(0, Math.Min(100, (temp / maxTemp) * 100d));
            ring.Width = size;
            ring.Height = size;
            ring.StrokeThickness = thickness;
            ring.Data = CriarRingGeometry(pct, size, thickness);
            valor.Text = $"{temp:0}°C";
            ring.Stroke = GetRingBrush(pct, true);
            AnimarRing(ring);
        }

        private static void AnimarRing(FrameworkElement ring)
        {
            ring.Opacity = 0;
            ring.RenderTransform = new ScaleTransform(0.86, 0.86);
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var scale = new DoubleAnimation(0.86, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            ring.BeginAnimation(OpacityProperty, fade);
            ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            ((ScaleTransform)ring.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        }

        private static Geometry CriarRingGeometry(double percent, double size, double thickness)
        {
            var radius = (size - thickness) / 2d;
            var center = size / 2d;
            var angle = Math.Max(1d, percent / 100d * 360d);
            var start = GetPointOnCircle(center, center, radius, -90d);
            var end = GetPointOnCircle(center, center, radius, -90d + angle);

            var figure = new PathFigure
            {
                StartPoint = start,
                IsClosed = false,
            };

            figure.Segments.Add(new ArcSegment(
                end,
                new Size(radius, radius),
                0,
                angle > 180,
                SweepDirection.Clockwise,
                true));

            return new PathGeometry(new[] { figure });
        }

        private static Brush GetRingBrush(double percent, bool isTemperature = false)
        {
            if (isTemperature)
            {
                if (percent < 50) return new SolidColorBrush(Colors.LimeGreen);
                if (percent < 75) return new SolidColorBrush(Colors.Gold);
                return new SolidColorBrush(Colors.OrangeRed);
            }

            if (percent < 50) return new SolidColorBrush(Colors.DodgerBlue);
            if (percent < 75) return new SolidColorBrush(Colors.MediumPurple);
            return new SolidColorBrush(Colors.OrangeRed);
        }

        private static Point GetPointOnCircle(double centerX, double centerY, double radius, double degrees)
        {
            var rad = degrees * Math.PI / 180d;
            return new Point(centerX + Math.Cos(rad) * radius, centerY + Math.Sin(rad) * radius);
        }
    }
}
