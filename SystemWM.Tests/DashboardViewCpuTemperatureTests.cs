using SystemWM.Models;
using SystemWM.Views;

namespace SystemWM.Tests;

public class DashboardViewCpuTemperatureTests
{
    [Fact]
    public void ObterTemperaturaExibicao_UsaTemperaturaDoNucleoSelecionadoQuandoDisponivel()
    {
        var cpu = new CpuInfo
        {
            TemperaturaC = 62,
            TemperaturaPorNucleo = new List<double> { 48, 74, 67 }
        };

        var temperatura = DashboardView.ObterTemperaturaExibicao(cpu, 1);

        Assert.Equal(74, temperatura);
    }

    [Fact]
    public void ObterTemperaturaExibicao_FallbackParaTemperaturaGeralQuandoNaoHaNucleosDetalhados()
    {
        var cpu = new CpuInfo
        {
            TemperaturaC = 58,
            TemperaturaPorNucleo = new List<double>()
        };

        var temperatura = DashboardView.ObterTemperaturaExibicao(cpu, 2);

        Assert.Equal(58, temperatura);
    }
}
