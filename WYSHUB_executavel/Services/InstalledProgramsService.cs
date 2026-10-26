using System;
using System.Collections.Generic;
using Microsoft.Win32;
using SystemWM.Models;

namespace SystemWM.Services
{
    /// <summary>
    /// Lê a lista de programas instalados a partir do Registro do Windows,
    /// nos mesmos locais que o "Programas e Recursos" usa.
    /// </summary>
    public class InstalledProgramsService
    {
        private static readonly string[] Chaves =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        public List<ProgramaInstalado> Listar()
        {
            var lista = new List<ProgramaInstalado>();
            var nomesVistos = new HashSet<string>();

            // Ler chaves tanto em HKLM quanto em HKCU para incluir instalações por usuário
            var hives = new[] { Registry.LocalMachine, Registry.CurrentUser };
            foreach (var hive in hives)
            {
                foreach (var chave in Chaves)
                {
                    using var raiz = hive.OpenSubKey(chave);
                    if (raiz == null) continue;

                    foreach (var subNome in raiz.GetSubKeyNames())
                    {
                        using var sub = raiz.OpenSubKey(subNome);
                        var nome = sub?.GetValue("DisplayName") as string;
                        if (string.IsNullOrWhiteSpace(nome)) continue;
                        if (!nomesVistos.Add(nome)) continue; // evita duplicados entre 32/64 bits e hives

                        var programa = new ProgramaInstalado
                        {
                            Nome = nome,
                            Versao = sub?.GetValue("DisplayVersion") as string ?? "",
                            Fabricante = sub?.GetValue("Publisher") as string ?? "",
                            UninstallString = sub?.GetValue("UninstallString") as string ?? ""
                        };

                        if (sub?.GetValue("EstimatedSize") is int tamanhoKb)
                            programa.TamanhoMB = Math.Round(tamanhoKb / 1024.0, 1);

                        var dataTexto = sub?.GetValue("InstallDate") as string;
                        if (!string.IsNullOrEmpty(dataTexto) && dataTexto.Length == 8 &&
                            DateTime.TryParseExact(dataTexto, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var data))
                        {
                            programa.DataInstalacao = data;
                        }

                        lista.Add(programa);
                    }
                }
            }

            lista.Sort((a, b) => string.Compare(a.Nome, b.Nome, StringComparison.OrdinalIgnoreCase));
            return lista;
        }
    }
}
