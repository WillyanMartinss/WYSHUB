using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using SystemWM.Models;

namespace SystemWM.Services
{
    /// <summary>
    /// Faz a limpeza básica do Windows: temp do usuário, temp do Windows,
    /// cache do Windows Update, prefetch e Lixeira.
    /// </summary>
    public class CleanupService
    {
        public List<ItemLimpeza> ListarItensDisponiveis()
        {
            var itens = new List<ItemLimpeza>
            {
                new() { Nome = "Arquivos temporários do usuário", CaminhoTipo = "TempUser",
                        Descricao = "%TEMP% - arquivos temporários gerados por programas do usuário atual",
                        NivelAlerta = NivelAlertaLimpeza.Verde,
                        Selecionado = true },
                new() { Nome = "Arquivos temporários do Windows", CaminhoTipo = "TempWindows",
                        Descricao = "C:\\Windows\\Temp - arquivos temporários do sistema",
                        NivelAlerta = NivelAlertaLimpeza.Amarelo,
                        Selecionado = false },
                new() { Nome = "Cache do Windows Update", CaminhoTipo = "CacheWindowsUpdate",
                        Descricao = "C:\\Windows\\SoftwareDistribution\\Download - pacotes de atualização já instalados",
                        NivelAlerta = NivelAlertaLimpeza.Amarelo,
                        Selecionado = false },
                new() { Nome = "Cache de Prefetch", CaminhoTipo = "PrefetchCache",
                        Descricao = "C:\\Windows\\Prefetch - cache de inicialização de programas",
                        NivelAlerta = NivelAlertaLimpeza.Amarelo,
                        Selecionado = false },
                new() { Nome = "Lixeira", CaminhoTipo = "Lixeira",
                        Descricao = "Esvaziar a Lixeira do Windows (todos os discos)",
                        NivelAlerta = NivelAlertaLimpeza.Vermelho,
                        Selecionado = false },
                new() { Nome = "Verificação de vírus (Windows Security)", CaminhoTipo = "WindowsSecurityScan",
                        Descricao = "Executa uma verificação rápida de ameaças com o mecanismo padrão do Windows Security/Defender.",
                        NivelAlerta = NivelAlertaLimpeza.Amarelo,
                        Selecionado = false },
                new() { Nome = "Pasta personalizada", CaminhoTipo = "Personalizada",
                        Descricao = "Limpar uma pasta de sua escolha. Defina o caminho antes de executar.",
                        NivelAlerta = NivelAlertaLimpeza.Amarelo,
                        Selecionado = false },
            };

            foreach (var item in itens)
            {
                item.AtualizarCores();
                item.TamanhoEstimadoMB = Math.Round(CalcularTamanhoMB(item), 1);
            }

            // Carrega pastas personalizadas salvas nas configurações e adiciona como itens
            try
            {
                var settings = AppState.Settings.Carregar();
                if (settings.PastasPersonalizadasParaLimpeza != null)
                {
                    foreach (var pasta in settings.PastasPersonalizadasParaLimpeza.Where(p => !string.IsNullOrWhiteSpace(p)))
                    {
                        var personalizado = new ItemLimpeza
                        {
                            Nome = Path.GetFileName(pasta.TrimEnd(Path.DirectorySeparatorChar)) == string.Empty ? pasta : Path.GetFileName(pasta.TrimEnd(Path.DirectorySeparatorChar)),
                            CaminhoTipo = "Personalizada",
                            CaminhoPersonalizado = pasta,
                            Descricao = $"Pasta personalizada: {pasta}",
                            NivelAlerta = NivelAlertaLimpeza.Amarelo,
                            Selecionado = false
                        };
                        personalizado.AtualizarCores();
                        personalizado.TamanhoEstimadoMB = Math.Round(CalcularTamanhoMB(personalizado), 1);
                        itens.Add(personalizado);
                    }
                }
            }
            catch { }

            return itens;
        }

        private IEnumerable<string> CaminhosParaItem(ItemLimpeza item) => item.CaminhoTipo switch
        {
            "TempUser" => new[] { Path.GetTempPath() },
            "TempWindows" => new[] { @"C:\Windows\Temp" },
            "CacheWindowsUpdate" => new[] { @"C:\Windows\SoftwareDistribution\Download" },
            "PrefetchCache" => new[] { @"C:\Windows\Prefetch" },
            "Personalizada" when !string.IsNullOrWhiteSpace(item.CaminhoPersonalizado) => new[] { item.CaminhoPersonalizado },
            _ => Array.Empty<string>()
        };

        private double CalcularTamanhoMB(ItemLimpeza item)
        {
            if (item.CaminhoTipo == "Lixeira")
                return 0; // calculado de forma diferente, via Shell; deixado como 0 para não travar a tela

            double totalBytes = 0;
            foreach (var caminho in CaminhosParaItem(item))
            {
                if (!Directory.Exists(caminho)) continue;
                try
                {
                    totalBytes += new DirectoryInfo(caminho)
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .Sum(f => { try { return f.Length; } catch { return 0; } });
                }
                catch { /* pastas protegidas do sistema podem negar acesso a alguns arquivos */ }
            }
            return totalBytes / (1024.0 * 1024);
        }

        /// <summary>Executa a limpeza dos itens marcados. Retorna um resumo (item -> MB liberados).</summary>
        public Dictionary<string, double> Limpar(List<ItemLimpeza> itensSelecionados, IProgress<(int Percentual, string Mensagem)>? progresso = null)
        {
            var resultado = new Dictionary<string, double>();
            var total = Math.Max(1, itensSelecionados.Count(i => i.Selecionado));
            var indice = 0;
            var settings = AppState.Settings.Carregar();
            var pastaBackup = settings.UsarBackupLimpeza && !string.IsNullOrWhiteSpace(settings.PastaBackupLimpeza)
                ? settings.PastaBackupLimpeza.Trim()
                : null;

            if (!string.IsNullOrWhiteSpace(pastaBackup) && settings.LimparAutomaticamentePastaBackup)
            {
                PurgeBackupRetencao(pastaBackup, settings.DiasRetencaoPastaBackup);
            }

            foreach (var item in itensSelecionados.Where(i => i.Selecionado))
            {
                indice++;
                progresso?.Report(((int)Math.Round((double)indice / total * 100), $"Limpando: {item.Nome}"));
                double liberadoMB = 0;

                if (item.CaminhoTipo == "Lixeira")
                {
                    liberadoMB = EsvaziarLixeira();
                }
                else if (item.CaminhoTipo == "WindowsSecurityScan")
                {
                    progresso?.Report((indice, $"Executando verificação de vírus: {item.Nome}"));
                    liberadoMB = ExecutarVerificacaoVirusWindows();
                }
                else if (item.CaminhoTipo == "Personalizada")
                {
                    if (!string.IsNullOrWhiteSpace(item.CaminhoPersonalizado))
                        liberadoMB = LimparPasta(item.CaminhoPersonalizado, pastaBackup);
                }
                else
                {
                    foreach (var caminho in CaminhosParaItem(item))
                        liberadoMB += LimparPasta(caminho, pastaBackup);
                }

                var nomeRelatorio = ObterNomeRelatorio(item);
                resultado[nomeRelatorio] = Math.Round(liberadoMB, 1);
            }

            progresso?.Report((100, "Limpeza concluída."));
            return resultado;
        }

        private double ExecutarVerificacaoVirusWindows()
        {
            var candidatos = new[]
            {
                @"C:\Program Files\Windows Defender\MpCmdRun.exe",
                @"C:\Program Files\Microsoft Defender\MpCmdRun.exe"
            };

            var executavel = candidatos.FirstOrDefault(File.Exists);
            if (string.IsNullOrWhiteSpace(executavel))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("windowsdefender:")
                    {
                        UseShellExecute = true
                    });
                }
                catch { }

                return 0;
            }

            try
            {
                using var processo = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = executavel,
                        Arguments = "-Scan -ScanType 1",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
                    }
                };

                processo.Start();
                processo.StandardOutput.ReadToEnd();
                processo.StandardError.ReadToEnd();
                processo.WaitForExit();
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private double LimparPasta(string caminho, string? pastaBackup)
        {
            if (!Directory.Exists(caminho)) return 0;
            bool usarBackup = !string.IsNullOrWhiteSpace(pastaBackup);
            if (usarBackup)
            {
                try
                {
                    var backupFull = Path.GetFullPath(pastaBackup!);
                    var caminhoFull = Path.GetFullPath(caminho);
                    if (string.Equals(backupFull, caminhoFull, StringComparison.OrdinalIgnoreCase) ||
                        backupFull.StartsWith(caminhoFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        usarBackup = false;
                    }
                }
                catch
                {
                    usarBackup = false;
                }
            }

            if (usarBackup)
            {
                Directory.CreateDirectory(pastaBackup!);
            }

            double liberadoBytes = 0;

            foreach (var arquivo in Directory.EnumerateFiles(caminho, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var fi = new FileInfo(arquivo);
                    long tamanho = fi.Length;

                    if (usarBackup)
                    {
                        var relPath = Path.GetRelativePath(caminho, arquivo);
                        var destino = Path.Combine(pastaBackup!, relPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destino) ?? pastaBackup!);
                        destino = GetUniqueFilePath(destino);

                        try
                        {
                            File.Move(arquivo, destino);
                        }
                        catch
                        {
                            try
                            {
                                File.Copy(arquivo, destino, true);
                                fi.Attributes = FileAttributes.Normal;
                                fi.Delete();
                            }
                            catch
                            {
                                continue;
                            }
                        }
                    }
                    else
                    {
                        fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                    }

                    liberadoBytes += tamanho;
                }
                catch { /* arquivo em uso ou protegido: ignora e segue */ }
            }

            // Remove subpastas vazias
            foreach (var dir in Directory.EnumerateDirectories(caminho, "*", SearchOption.AllDirectories).Reverse())
            {
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                catch { }
            }

            return liberadoBytes / (1024.0 * 1024);
        }

        public bool RestaurarBackup(string pastaBackup, string? pastaOrigem = null)
        {
            if (!Directory.Exists(pastaBackup))
                return false;

            var arquivos = Directory.EnumerateFiles(pastaBackup, "*", SearchOption.AllDirectories).ToList();
            if (!arquivos.Any())
                return false;

            foreach (var arquivo in arquivos)
            {
                try
                {
                    var relPath = Path.GetRelativePath(pastaBackup, arquivo);
                    var destino = string.IsNullOrWhiteSpace(pastaOrigem)
                        ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(arquivo) ?? pastaBackup, relPath))
                        : Path.Combine(pastaOrigem, relPath);

                    var destinoDir = Path.GetDirectoryName(destino);
                    if (!string.IsNullOrWhiteSpace(destinoDir))
                        Directory.CreateDirectory(destinoDir);

                    if (File.Exists(destino))
                    {
                        File.SetAttributes(destino, FileAttributes.Normal);
                        File.Delete(destino);
                    }

                    File.Move(arquivo, destino);
                }
                catch { }
            }

            foreach (var dir in Directory.EnumerateDirectories(pastaBackup, "*", SearchOption.AllDirectories).Reverse())
            {
                try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
                catch { }
            }

            try
            {
                if (!Directory.EnumerateFileSystemEntries(pastaBackup).Any())
                    Directory.Delete(pastaBackup);
            }
            catch { }

            return true;
        }

        private void PurgeBackupRetencao(string pastaBackup, int diasRetencao)
        {
            if (diasRetencao <= 0 || !Directory.Exists(pastaBackup))
                return;

            var limite = DateTime.Now.AddDays(-diasRetencao);
            foreach (var arquivo in Directory.EnumerateFiles(pastaBackup, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTime(arquivo) < limite)
                    {
                        File.SetAttributes(arquivo, FileAttributes.Normal);
                        File.Delete(arquivo);
                    }
                }
                catch { }
            }

            foreach (var dir in Directory.EnumerateDirectories(pastaBackup, "*", SearchOption.AllDirectories).Reverse())
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { }
            }
        }

        private string GetUniqueFilePath(string path)
        {
            if (!File.Exists(path))
                return path;

            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            var index = 1;

            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileName} ({index}){extension}");
                index++;
            }
            while (File.Exists(newPath));

            return newPath;
        }

        private string ObterNomeRelatorio(ItemLimpeza item)
        {
            if (item.CaminhoTipo == "Personalizada" && !string.IsNullOrWhiteSpace(item.CaminhoPersonalizado))
                return $"Pasta personalizada: {item.CaminhoPersonalizado}";

            return item.Nome;
        }

        private double EsvaziarLixeira()
        {
            try
            {
                // SHEmptyRecycleBin via API nativa do Shell do Windows
                Shell32Interop.SHEmptyRecycleBin(IntPtr.Zero, null, Shell32Interop.SHERB_NOCONFIRMATION | Shell32Interop.SHERB_NOPROGRESSUI | Shell32Interop.SHERB_NOSOUND);
                return 0; // Windows não retorna o tamanho liberado diretamente
            }
            catch
            {
                return 0;
            }
        }
    }

    internal static class Shell32Interop
    {
        public const uint SHERB_NOCONFIRMATION = 0x00000001;
        public const uint SHERB_NOPROGRESSUI = 0x00000002;
        public const uint SHERB_NOSOUND = 0x00000004;

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
    }
}
