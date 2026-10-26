using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
// PDF generation removed: QuestPDF usage cleaned up
using SystemWM.Models;

namespace SystemWM.Services
{
    /// <summary>Monta o relatório final (HTML) com todos os dados coletados na visita técnica.</summary>
    public class ReportService
    {
        public string GerarHtml(
            ClienteVisita cliente,
            DiagnosticoCompleto diagnostico,
            List<ProgramaInstalado> programas,
            Dictionary<string, double>? limpezaResultado,
            List<FirewallRegra>? regrasFirewall,
            bool firewallAtivo,
            List<PortaAtivaInfo>? portasIncluidas,
            bool incluirResumoDashboard,
            bool incluirHardware,
            bool incluirFirewall,
            bool incluirLimpeza,
            IEnumerable<string>? cardsSelecionados = null)
        {
            var sb = new StringBuilder();
            var algumaSecaoSelecionada = incluirResumoDashboard || incluirHardware || incluirFirewall || incluirLimpeza || (limpezaResultado != null && limpezaResultado.Any());
            sb.Append($@"
<html>
<head><meta charset='utf-8'>
<style>
  body {{ font-family: Segoe UI, Arial, sans-serif; background:#0b0f1a; color:#e5e9f0; padding:24px; }}
  h1 {{ color:#5b8def; }}
  h2 {{ color:#5b8def; border-bottom:1px solid #2a3350; padding-bottom:6px; margin-top:28px;}}
  table {{ width:100%; border-collapse:collapse; margin-top:8px; }}
  td, th {{ padding:6px 10px; text-align:left; border-bottom:1px solid #232a42; font-size:14px;}}
  th {{ color:#8ea0c9; }}
  .card {{ background:#131a2b; border:1px solid #232a42; border-radius:10px; padding:16px; margin-bottom:16px;}}
  .ok {{ color:#3ddc84; }}
  .warn {{ color:#f5a623; }}
  .bad {{ color:#f25555; }}
  .header {{ display:flex; justify-content:space-between; }}
</style>
</head>
<body>
  <h1>Relatório de Visita Técnica — SystemWM</h1>
  <div class='card'>
    <div class='header'>
      <div>
        <b>Cliente:</b> {cliente.NomeCliente} {(string.IsNullOrEmpty(cliente.EmpresaCliente) ? "" : $"({cliente.EmpresaCliente})")}<br>
        <b>Data da visita:</b> {cliente.DataVisita:dd/MM/yyyy HH:mm}<br>
        <b>Máquina:</b> {diagnostico.Sistema.NomeHost} — Usuário: {diagnostico.Sistema.Usuario}
      </div>
    </div>
    {(string.IsNullOrEmpty(cliente.Observacoes) ? "" : $"<p><b>Observações:</b> {cliente.Observacoes}</p>")}
  </div>" );

            if (incluirResumoDashboard)
            {
                var cardsSelecionadosLista = (cardsSelecionados ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var cardsParaResumo = cardsSelecionadosLista.Count > 0
                    ? cardsSelecionadosLista
                    : new List<string> { "CpuCard", "RamCard", "DiscoCard", "MoboCard", "GpuCard" };

                sb.Append($@"
  <h2>Resumo do Dashboard</h2>
  <div class='card'>
    <p>Coletado em: {diagnostico.ColetadoEm:dd/MM/yyyy HH:mm}</p>
    <p>Rede: IP local {diagnostico.Sistema.IpLocal}, IP pública {diagnostico.Sistema.IpPublica}, VPN {(diagnostico.Sistema.VpnAtiva ? "ativa" : "inativa")}</p>
    <ul>");

                foreach (var card in cardsParaResumo)
                {
                    sb.Append($"<li>{DetalharCardDashboard(diagnostico, card)}</li>");
                }

                sb.Append(@"
    </ul>
  </div>
");
            }

            if (incluirHardware)
            {
                sb.Append($@"
  <h2>Hardware completo</h2>
  <div class='card'>
    <table>
      <tr><td>Placa-mãe</td><td>{diagnostico.Sistema.PlacaMae}</td></tr>
      <tr><td>Chipset</td><td>{diagnostico.Sistema.Chipset}</td></tr>
      <tr><td>BIOS</td><td>{diagnostico.Sistema.BiosVersao}</td></tr>
      <tr><td>Uptime</td><td>{diagnostico.Sistema.Uptime}</td></tr>
      <tr><td>RAM</td><td>{diagnostico.Ram.TotalGB} GB ({diagnostico.Ram.UsadaGB} GB usados, {diagnostico.Ram.DisponivelGB} GB livre)</td></tr>
      <tr><td>CPU</td><td>{diagnostico.Cpu.Nome}, {diagnostico.Cpu.NucleosFisicos} núcleos, {diagnostico.Cpu.UsoAtualPercent:0}% uso, {diagnostico.Cpu.TemperaturaC:0}°C</td></tr>
      <tr><td>GPU</td><td>{(diagnostico.Gpu?.Nome ?? "N/D")}</td></tr>
    </table>
  </div>
");

                sb.Append($@"
  <h2>Armazenamento</h2>
  <div class='card'>
    <table>
      <tr><th>Modelo</th><th>Interface</th><th>Capacidade</th><th>Usado</th><th>Livre</th><th>Temp.</th><th>Saúde</th></tr>
      {string.Join("", diagnostico.Discos.Select(d => $@"
      <tr>
        <td>{d.Modelo}</td><td>{d.Interface}</td><td>{d.CapacidadeTotalGB} GB</td>
        <td>{d.EspacoUsadoGB} GB</td><td>{d.EspacoLivreGB} GB</td>
        <td class='{ClasseTemperaturaDisco(d.TemperaturaC)}'>{d.TemperaturaC:0}°C</td><td>{d.SaudePercentTexto}</td>
      </tr>"))}
    </table>
  </div>
");
            }

            if (diagnostico.Gpu != null)
            {
                sb.Append($@"
  <h2>GPU</h2>
  <div class='card'>
    <table>
      <tr><td>Modelo</td><td>{diagnostico.Gpu.Nome}</td></tr>
      <tr><td>Uso</td><td>{diagnostico.Gpu.UsoPercent:0}%</td></tr>
      <tr><td>Temperatura</td><td>{diagnostico.Gpu.TemperaturaC:0}°C</td></tr>
      <tr><td>Memória</td><td>{diagnostico.Gpu.MemoriaUsadaGB} / {diagnostico.Gpu.MemoriaTotalGB} GB</td></tr>
      <tr><td>Consumo</td><td>{diagnostico.Gpu.ConsumoWatts:0} W</td></tr>
      <tr><td>Fan</td><td>{diagnostico.Gpu.FanRpm:0} RPM</td></tr>
    </table>
  </div>");
            }

            if (incluirFirewall)
            {
                sb.Append($@"
  <h2>Firewall</h2>
  <div class='card'>
    <p>Status: <b class='{(firewallAtivo ? "ok" : "bad")}'>{(firewallAtivo ? "Ativo" : "Desativado")}</b></p>
    {(regrasFirewall != null ? $"<p>{regrasFirewall.Count} regras configuradas.</p>" : "")}
  </div>");

                if (portasIncluidas != null && portasIncluidas.Any())
                {
                    sb.Append(@"<h2>Portas incluídas no relatório</h2><div class='card'><table><tr><th>Protocolo</th><th>Local</th><th>Remoto</th><th>Processo</th><th>PID</th></tr>");
                    foreach (var porta in portasIncluidas)
                    {
                        sb.Append($"<tr><td>{porta.Protocolo}</td><td>{porta.LocalEndpoint}</td><td>{porta.RemoteEndpoint}</td><td>{porta.Processo}</td><td>{porta.Pid}</td></tr>");
                    }
                    sb.Append("</table></div>");
                }
            }

            if (limpezaResultado != null && limpezaResultado.Any())
            {
              sb.Append(@"<h2>Limpeza realizada</h2><div class='card'><table><tr><th>Item</th><th>Resultado</th></tr>");
              foreach (var (item, mb) in limpezaResultado)
                sb.Append($"<tr><td>{item}</td><td>Limpo</td></tr>");
              sb.Append("</table></div>");
            }

            if (!algumaSecaoSelecionada)
            {
                sb.Append(@"
  <h2>Seções selecionadas</h2>
  <div class='card'>
    <p>Nenhuma seção foi selecionada para este relatório.</p>
  </div>");
            }

            if (algumaSecaoSelecionada)
            {
                var programasParaRelatorio = programas.Where(p => p.IncluirNoRelatorio).ToList();
                sb.Append($@"
  <h2>Programas selecionados ({programasParaRelatorio.Count})</h2>
  <div class='card'>
    {(programasParaRelatorio.Any() ? $@"
    <table>
      <tr><th>Nome</th><th>Versão</th><th>Fabricante</th></tr>
      {string.Join("", programasParaRelatorio.Take(200).Select(p => $"<tr><td>{p.Nome}</td><td>{p.Versao}</td><td>{p.Fabricante}</td></tr>"))}
    </table>
    {(programasParaRelatorio.Count > 200 ? $"<p>... e mais {programasParaRelatorio.Count - 200} programas.</p>" : "")}" : "<p>Nenhum aplicativo selecionado para constar no relatório.</p>")}
  </div>
");
            }

            sb.Append($@"
  <p style='color:#5a6685; margin-top:32px;'>Relatório gerado automaticamente pelo SystemWM em {diagnostico.ColetadoEm:dd/MM/yyyy HH:mm:ss}.</p>
</body>
</html>");

            return sb.ToString();
        }

        public string GerarTxt(
            ClienteVisita cliente,
            DiagnosticoCompleto diagnostico,
            List<ProgramaInstalado> programas,
            Dictionary<string, double>? limpezaResultado,
            List<FirewallRegra>? regrasFirewall,
            bool firewallAtivo,
            List<PortaAtivaInfo>? portasIncluidas,
            bool incluirResumoDashboard,
            bool incluirHardware,
            bool incluirFirewall,
            bool incluirLimpeza,
            IEnumerable<string>? cardsSelecionados = null)
        {
            var sb = new StringBuilder();
            var agora = DateTime.Now;
            var tempoManutencao = agora - diagnostico.ColetadoEm;
            var cardsSelecionadosLista = (cardsSelecionados ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var cardsParaResumo = cardsSelecionadosLista.Count > 0
                ? cardsSelecionadosLista
                : new List<string> { "MoboCard", "CpuCard", "RamCard", "DiscoCard", "GpuCard" };
            var algumaSecaoSelecionada = incluirResumoDashboard || incluirHardware || incluirFirewall || incluirLimpeza || (limpezaResultado != null && limpezaResultado.Any());

            sb.AppendLine(new string('=', 50));
            sb.AppendLine("RELATÓRIO DE MANUTENÇÃO");
            sb.AppendLine();
            sb.AppendLine($"Data: {agora:dd/MM/yyyy}");
            sb.AppendLine($"Hora: {agora:HH:mm:ss}");
            sb.AppendLine($"Versão do Programa: {ObterVersaoPrograma()}");
            sb.AppendLine($"Tempo da manutenção: {tempoManutencao:hh\\:mm\\:ss}");
            sb.AppendLine($"Coleta do Dashboard: {diagnostico.ColetadoEm:dd/MM/yyyy HH:mm:ss}");

            if (algumaSecaoSelecionada)
            {
                foreach (var card in cardsParaResumo)
                {
                    switch (card)
                    {
                        case "MoboCard":
                            sb.AppendLine(new string('=', 50));
                            sb.AppendLine("COMPUTADOR");
                            sb.AppendLine($"Nome: {diagnostico.Sistema.NomeHost}");
                            sb.AppendLine($"Usuário: {diagnostico.Sistema.Usuario}");
                            sb.AppendLine($"Windows: {diagnostico.Sistema.SistemaOperacional}");
                            sb.AppendLine($"Build: {diagnostico.Sistema.BiosVersao}");
                            sb.AppendLine($"Tempo Ligado: {diagnostico.Sistema.Uptime}");
                            break;
                        case "CpuCard":
                            sb.AppendLine(new string('=', 50));
                            sb.AppendLine("PROCESSADOR");
                            sb.AppendLine($"Modelo: {diagnostico.Cpu.Nome}");
                            sb.AppendLine($"Temperatura: {diagnostico.Cpu.TemperaturaC:0.0}°C");
                            sb.AppendLine($"Uso: {diagnostico.Cpu.UsoAtualPercent:0.0}%");
                            sb.AppendLine($"Clock: {diagnostico.Cpu.FrequenciaAtualGHz:0.0} GHz");
                            sb.AppendLine($"Núcleos: {diagnostico.Cpu.NucleosFisicos}");
                            sb.AppendLine($"Threads: {diagnostico.Cpu.Threads}");
                            break;
                        case "RamCard":
                            sb.AppendLine(new string('=', 50));
                            sb.AppendLine("MEMÓRIA");
                            sb.AppendLine($"Total: {diagnostico.Ram.TotalGB:0.##} GB");
                            sb.AppendLine($"Utilizada: {diagnostico.Ram.UsadaGB:0.##} GB");
                            sb.AppendLine($"Livre: {diagnostico.Ram.DisponivelGB:0.##} GB");
                            sb.AppendLine($"Uso %: {diagnostico.Ram.UsoPercent:0.0}%");
                            break;
                        case "DiscoCard":
                            if (diagnostico.Discos.Any())
                            {
                                var disco = diagnostico.Discos[0];
                                sb.AppendLine(new string('=', 50));
                                sb.AppendLine("SSD");
                                sb.AppendLine($"Marca: {disco.Marca}");
                                sb.AppendLine($"Modelo: {disco.Modelo}");
                                sb.AppendLine($"Temperatura: {(double.IsNaN(disco.TemperaturaC) || disco.TemperaturaC <= 0 ? "Não suportado pelo dispositivo." : disco.TemperaturaC.ToString("0.0") + "°C")}");
                                sb.AppendLine($"Saúde: {disco.SaudePercentTexto}");
                                sb.AppendLine($"Espaço Livre: {disco.EspacoLivreGB:0.##} GB");
                                sb.AppendLine($"Espaço Utilizado: {disco.EspacoUsadoGB:0.##} GB");
                            }
                            break;
                        case "GpuCard":
                            if (diagnostico.Gpu != null)
                            {
                                sb.AppendLine(new string('=', 50));
                                sb.AppendLine("GPU");
                                sb.AppendLine($"Modelo: {diagnostico.Gpu.Nome}");
                                sb.AppendLine($"Temperatura: {diagnostico.Gpu.TemperaturaC:0.0}°C");
                                sb.AppendLine($"Uso: {diagnostico.Gpu.UsoPercent:0.0}%");
                                sb.AppendLine($"Memória: {diagnostico.Gpu.MemoriaUsadaGB:0.##} / {diagnostico.Gpu.MemoriaTotalGB:0.##} GB");
                                sb.AppendLine($"Consumo: {diagnostico.Gpu.ConsumoWatts:0.##} W");
                                sb.AppendLine($"Fan: {diagnostico.Gpu.FanRpm:0} RPM");
                            }
                            break;
                        default:
                            sb.AppendLine(new string('=', 50));
                            sb.AppendLine(card.ToUpperInvariant());
                            sb.AppendLine(DetalharCardDashboard(diagnostico, card));
                            break;
                    }
                }
            }

            if (limpezaResultado != null && limpezaResultado.Any())
            {
                var totalItens = limpezaResultado.Count;
                var totalRecuperado = limpezaResultado.Values.Sum();
                sb.AppendLine(new string('=', 50));
                sb.AppendLine("Limpeza realizada");
                sb.AppendLine($"Itens processados: {totalItens}");
                sb.AppendLine($"Espaço recuperado: {totalRecuperado:0.##} GB");
                sb.AppendLine($"Status geral: OK");
                foreach (var (item, recuperado) in limpezaResultado)
                {
                    sb.AppendLine($"  • {item}: {recuperado:0.##} GB");
                }
                sb.AppendLine(new string('=', 50));
                sb.AppendLine("APLICATIVOS SELECIONADOS");
                var programasParaRelatorio = programas.Where(p => p.IncluirNoRelatorio).ToList();
                sb.AppendLine($"Total: {programasParaRelatorio.Count}");
                foreach (var p in programasParaRelatorio)
                {
                    sb.AppendLine($"  • {p.Nome}");
                }
                sb.AppendLine(new string('=', 50));
                sb.AppendLine($"Espaço Recuperado: {totalRecuperado:0.##} GB");
                sb.AppendLine($"Tempo da limpeza: {tempoManutencao:hh\\:mm\\:ss}");
                sb.AppendLine($"Status Geral: OK");
            }
            else
            {
                sb.AppendLine(new string('=', 50));
                sb.AppendLine("APLICATIVOS SELECIONADOS");
                var programasParaRelatorio = programas.Where(p => p.IncluirNoRelatorio).ToList();
                sb.AppendLine($"Total: {programasParaRelatorio.Count}");
                foreach (var p in programasParaRelatorio)
                {
                    sb.AppendLine($"  • {p.Nome}");
                }
            }

            if (!algumaSecaoSelecionada && (limpezaResultado == null || !limpezaResultado.Any()))
            {
                sb.AppendLine(new string('=', 50));
                sb.AppendLine("SEÇÕES SELECIONADAS");
                sb.AppendLine("Nenhuma seção foi selecionada para este relatório.");
            }

            if (!string.IsNullOrWhiteSpace(cliente.Observacoes))
            {
                sb.AppendLine(new string('=', 50));
                sb.AppendLine("OBSERVAÇÕES");
                sb.AppendLine(cliente.Observacoes);
            }

            sb.AppendLine();
            sb.AppendLine($"Relatório gerado automaticamente pelo SystemWM em {agora:dd/MM/yyyy HH:mm:ss}.");
            return sb.ToString();
        }

        // PDF generation removed. Use TXT or HTML generation methods instead.

        private string ObterVersaoPrograma()
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return version != null ? version.ToString(2) : "1.0";
        }

        private static string DetalharCardDashboard(DiagnosticoCompleto diagnostico, string cardName)
        {
            return cardName switch
            {
                "CpuCard" => $"CPU: {diagnostico.Cpu.Nome} | Uso {diagnostico.Cpu.UsoAtualPercent:0}% | Temperatura {diagnostico.Cpu.TemperaturaC:0}°C",
                "RamCard" => $"RAM: {diagnostico.Ram.TotalGB} GB total | Uso {diagnostico.Ram.UsoPercent}% | Disponível {diagnostico.Ram.DisponivelGB:0.0} GB",
                "DiscoCard" =>
                    diagnostico.Discos.Count > 0
                        ? $"Armazenamento: {diagnostico.Discos[0].Modelo} | Uso {diagnostico.Discos[0].UsoPercent:0}% | Saúde {diagnostico.Discos[0].SaudePercentTexto}"
                        : "Armazenamento: não disponível",
                "MoboCard" => $"Placa-mãe: {diagnostico.Sistema.PlacaMae} | Chipset {diagnostico.Sistema.Chipset} | BIOS {diagnostico.Sistema.BiosVersao}",
                "GpuCard" => diagnostico.Gpu is null
                    ? "GPU: não detectada"
                    : $"GPU: {diagnostico.Gpu.Nome} | Uso {diagnostico.Gpu.UsoPercent:0}% | Temp {diagnostico.Gpu.TemperaturaC:0}°C",
                _ => cardName
            };
        }

        private string ClasseTemperaturaCpu(double t) => t >= 85 ? "bad" : t >= 70 ? "warn" : "ok";
        private string ClasseTemperaturaDisco(double t) => t >= 60 ? "bad" : t >= 45 ? "warn" : "ok";
    }
}
