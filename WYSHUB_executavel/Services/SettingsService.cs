using System;
using System.IO;
using System.Text.Json;

namespace SystemWM.Services
{
    public class AppSettings
    {
        public string ResendApiKey { get; set; } = "";
        public string EmailRemetente { get; set; } = "SystemWM <onboarding@resend.dev>";
        public string EmailDestinoPadrao { get; set; } = "martins.willyan20@gmail.com";
        public string EmailAssuntoPadrao { get; set; } = "Relatório de Visita Técnica - {cliente}";
        public string EmailCorpoPadrao { get; set; } = "Boa tarde,\n\nSegue o relatório em anexo.\n\nAtenciosamente,\nWillyan";
        public string NomeTecnico { get; set; } = "";
        public string NomeClientePadrao { get; set; } = "";
        public string EmpresaPadrao { get; set; } = "";
        public string ObservacoesPadrao { get; set; } = "";
        public bool UsarBackupLimpeza { get; set; } = false;
        public string PastaBackupLimpeza { get; set; } = "";
        public bool LimparAutomaticamentePastaBackup { get; set; } = false;
        public int DiasRetencaoPastaBackup { get; set; } = 30;
        public string[] PastasPersonalizadasParaLimpeza { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Salva/lê as configurações do app em um json local, na pasta AppData do usuário.
    /// Assim a API Key do Resend não fica exposta no código-fonte nem versionada.
    /// </summary>
    public class SettingsService
    {
        private static readonly object _lockArquivo = new();
        private readonly string _caminhoArquivo;
        private readonly string _caminhoArquivoApi;

        public SettingsService()
        {
            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SystemWM");
            Directory.CreateDirectory(pasta);
            _caminhoArquivo = Path.Combine(pasta, "settings.json");
            _caminhoArquivoApi = Path.Combine(AppContext.BaseDirectory, "config", "resend_api.key");
        }

        public string ObterCaminhoArquivoApi() => _caminhoArquivoApi;

        private static bool EhChavePlaceholder(string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return true;

            var valor = apiKey.Trim();
            return valor.Contains("Adrian Gurvitz", StringComparison.OrdinalIgnoreCase)
                || valor.Contains("Classic", StringComparison.OrdinalIgnoreCase)
                || valor.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
                || valor.Contains("example", StringComparison.OrdinalIgnoreCase);
        }

        public string CarregarApiKey()
        {
            lock (_lockArquivo)
            {
                if (!File.Exists(_caminhoArquivoApi)) return string.Empty;
                try
                {
                    var apiKey = File.ReadAllText(_caminhoArquivoApi).Trim();
                    return EhChavePlaceholder(apiKey) ? string.Empty : apiKey;
                }
                catch { return string.Empty; }
            }
        }

        public string ObterApiKeyVigente()
        {
            var apiKeyArquivo = CarregarApiKey();
            if (!string.IsNullOrWhiteSpace(apiKeyArquivo))
                return apiKeyArquivo;

            var settings = Carregar();
            if (!string.IsNullOrWhiteSpace(settings.ResendApiKey) && !EhChavePlaceholder(settings.ResendApiKey))
                return settings.ResendApiKey.Trim();

            return string.Empty;
        }

        public void SalvarApiKey(string apiKey)
        {
            lock (_lockArquivo)
            {
                var valor = string.IsNullOrWhiteSpace(apiKey) ? string.Empty : apiKey.Trim();
                if (EhChavePlaceholder(valor))
                    valor = string.Empty;

                var pasta = Path.GetDirectoryName(_caminhoArquivoApi);
                if (!string.IsNullOrWhiteSpace(pasta))
                    Directory.CreateDirectory(pasta);

                File.WriteAllText(_caminhoArquivoApi, valor);
            }
        }

        public AppSettings Carregar()
        {
            lock (_lockArquivo)
            {
                if (!File.Exists(_caminhoArquivo)) return new AppSettings();
                try
                {
                    var json = File.ReadAllText(_caminhoArquivo);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                    if (string.IsNullOrWhiteSpace(settings.ResendApiKey))
                    {
                        var apiKeyArquivo = CarregarApiKey();
                        if (!string.IsNullOrWhiteSpace(apiKeyArquivo))
                            settings.ResendApiKey = apiKeyArquivo;
                    }

                    return settings;
                }
                catch { return new AppSettings(); }
            }
        }

        public void Salvar(AppSettings settings)
        {
            lock (_lockArquivo)
            {
                settings.ResendApiKey = string.IsNullOrWhiteSpace(settings.ResendApiKey)
                    ? string.Empty
                    : settings.ResendApiKey.Trim();

                if (EhChavePlaceholder(settings.ResendApiKey))
                    settings.ResendApiKey = string.Empty;

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_caminhoArquivo, json);
                if (!string.IsNullOrWhiteSpace(settings.ResendApiKey))
                    SalvarApiKey(settings.ResendApiKey);
                else
                    SalvarApiKey(string.Empty);
            }
        }
    }
}
