using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SystemWM.Models
{
    public class FirewallRegra
    {
        public string Nome { get; set; } = "";
        public string Direcao { get; set; } = ""; // Entrada / Saída
        public string Protocolo { get; set; } = "";
        public string Porta { get; set; } = "";
        public string Acao { get; set; } = ""; // Permitir / Bloquear
        public bool Habilitada { get; set; }
        public string Perfil { get; set; } = ""; // Domínio/Privado/Público
    }

    public class PortaAtivaInfo
    {
        private int _porta;

        public string Protocolo { get; set; } = "";
        public string LocalEndpoint { get; set; } = "";
        public string RemoteEndpoint { get; set; } = "";
        public string Estado { get; set; } = "";
        public int Pid { get; set; }
        public string Processo { get; set; } = "";
        public bool IncluirNoRelatorio { get; set; }

        public int Porta
        {
            get => _porta > 0 ? _porta : ObterPorta(LocalEndpoint);
            set => _porta = value;
        }

        private static int ObterPorta(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) return 0;

            var partes = endpoint.Split(':');
            if (partes.Length == 0) return 0;

            var ultimo = partes[^1];
            return int.TryParse(ultimo, out var porta) ? porta : 0;
        }
    }

    public enum NivelAlertaLimpeza
    {
        Verde,
        Amarelo,
        Vermelho
    }

    public class ItemLimpeza : INotifyPropertyChanged
    {
        private bool _selecionado = true;
        private string _nome = "";
        private string _descricao = "";
        private string _caminhoTipo = "";
        private string? _caminhoPersonalizado;
        private double _tamanhoEstimadoMB;
        private NivelAlertaLimpeza _nivelAlerta = NivelAlertaLimpeza.Verde;
        private Brush _corFundo = new SolidColorBrush(Color.FromArgb(40, 34, 197, 94));
        private Brush _corBorda = new SolidColorBrush(Color.FromArgb(180, 34, 197, 94));

        public string Nome
        {
            get => _nome;
            set { if (_nome != value) { _nome = value; OnPropertyChanged(); } }
        }

        public string Descricao
        {
            get => _descricao;
            set { if (_descricao != value) { _descricao = value; OnPropertyChanged(); } }
        }

        public string CaminhoTipo
        {
            get => _caminhoTipo;
            set { if (_caminhoTipo != value) { _caminhoTipo = value; OnPropertyChanged(); } }
        }

        public bool Selecionado
        {
            get => _selecionado;
            set { if (_selecionado != value) { _selecionado = value; OnPropertyChanged(); } }
        }

        public double TamanhoEstimadoMB
        {
            get => _tamanhoEstimadoMB;
            set { if (_tamanhoEstimadoMB != value) { _tamanhoEstimadoMB = value; OnPropertyChanged(); } }
        }

        public string? CaminhoPersonalizado
        {
            get => _caminhoPersonalizado;
            set { if (_caminhoPersonalizado != value) { _caminhoPersonalizado = value; OnPropertyChanged(); OnPropertyChanged(nameof(PodePersonalizar)); OnPropertyChanged(nameof(CaminhoExibicao)); } }
        }

        public NivelAlertaLimpeza NivelAlerta
        {
            get => _nivelAlerta;
            set { if (_nivelAlerta != value) { _nivelAlerta = value; OnPropertyChanged(); OnPropertyChanged(nameof(NivelAlertaTexto)); AtualizarCores(); } }
        }

        public Brush CorFundo
        {
            get => _corFundo;
            set { if (_corFundo != value) { _corFundo = value; OnPropertyChanged(); } }
        }

        public Brush CorBorda
        {
            get => _corBorda;
            set { if (_corBorda != value) { _corBorda = value; OnPropertyChanged(); } }
        }

        public bool PodePersonalizar => CaminhoTipo == "Personalizada";

        public string CaminhoExibicao => string.IsNullOrWhiteSpace(CaminhoPersonalizado) ? "Nenhuma pasta escolhida" : CaminhoPersonalizado;

        public string NivelAlertaTexto => NivelAlerta switch
        {
            NivelAlertaLimpeza.Amarelo => "Risco moderado",
            NivelAlertaLimpeza.Vermelho => "Risco alto",
            _ => "Risco baixo"
        };

        public void AtualizarCores()
        {
            switch (NivelAlerta)
            {
                case NivelAlertaLimpeza.Amarelo:
                    CorFundo = CreateFrozenBrush(Color.FromArgb(45, 255, 193, 7));
                    CorBorda = CreateFrozenBrush(Color.FromArgb(180, 255, 193, 7));
                    break;
                case NivelAlertaLimpeza.Vermelho:
                    CorFundo = CreateFrozenBrush(Color.FromArgb(45, 244, 67, 54));
                    CorBorda = CreateFrozenBrush(Color.FromArgb(180, 244, 67, 54));
                    break;
                default:
                    CorFundo = CreateFrozenBrush(Color.FromArgb(40, 34, 197, 94));
                    CorBorda = CreateFrozenBrush(Color.FromArgb(180, 34, 197, 94));
                    break;
            }
        }

        private static Brush CreateFrozenBrush(Color color)
        {
            var b = new SolidColorBrush(color);
            if (b.CanFreeze)
            {
                try { b.Freeze(); } catch { }
            }
            return b;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ProgramaInstalado
    {
        public string Nome { get; set; } = "";
        public string Versao { get; set; } = "";
        public string Fabricante { get; set; } = "";
        public DateTime? DataInstalacao { get; set; }
        public double TamanhoMB { get; set; }
        public string UninstallString { get; set; } = "";
        public bool IncluirNoRelatorio { get; set; }
    }

    public class ClienteVisita
    {
        public string NomeCliente { get; set; } = "";
        public string EmpresaCliente { get; set; } = "";
        public string EmailDestino { get; set; } = "";
        public string Observacoes { get; set; } = "";
        public DateTime DataVisita { get; set; } = DateTime.Now;
    }
}
