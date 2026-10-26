using SystemWM.Services;

namespace SystemWM.Tests;

public class EmailServiceTests
{
    [Fact]
    public void NormalizarDadosEnvio_UsaValoresInformadosOuFallback()
    {
        var (remetente, destinatario, assunto, cc) = EmailService.NormalizarDadosEnvio(
            "  Operador <teste@empresa.com>  ",
            "  cliente@empresa.com  ",
            "  Assunto teste  ",
            "  copia@empresa.com  ",
            "SystemWM <onboarding@resend.dev>",
            "destino@padrao.com",
            "Assunto padrão");

        Assert.Equal("Operador <teste@empresa.com>", remetente);
        Assert.Equal("cliente@empresa.com", destinatario);
        Assert.Equal("Assunto teste", assunto);
        Assert.Equal("copia@empresa.com", cc);
    }

    [Fact]
    public void NormalizarDadosEnvio_UsaFallbackQuandoCamposVazios()
    {
        var (remetente, destinatario, assunto, cc) = EmailService.NormalizarDadosEnvio(
            "   ",
            null,
            "",
            null,
            "SystemWM <onboarding@resend.dev>",
            "destino@padrao.com",
            "Assunto padrão");

        Assert.Equal("SystemWM <onboarding@resend.dev>", remetente);
        Assert.Equal("destino@padrao.com", destinatario);
        Assert.Equal("Assunto padrão", assunto);
        Assert.Null(cc);
    }

    [Fact]
    public void ConstruirCorpoMensagem_UsaMensagemPadraoQuandoCampoEhIgualAoRelatorio()
    {
        var corpoPadrao = "Boa tarde,\n\nSegue o relatório em anexo.\n\nAtenciosamente,\nWillyan";
        var relatorio = "Resumo do relatório";

        var corpo = EmailService.ConstruirCorpoMensagem(relatorio, corpoPadrao, relatorio);

        Assert.Equal(corpoPadrao, corpo);
    }

    [Fact]
    public void ConstruirCorpoMensagem_PreservaMensagemCustomizadaQuandoNaoEhRelatorio()
    {
        var corpoPadrao = "Boa tarde,\n\nSegue o relatório em anexo.\n\nAtenciosamente,\nWillyan";
        var corpoCustomizado = "Olá, segue a mensagem personalizada.";

        var corpo = EmailService.ConstruirCorpoMensagem(corpoCustomizado, corpoPadrao, "Resumo do relatório");

        Assert.Equal(corpoCustomizado, corpo);
    }

    [Fact]
    public void ConstruirCorpoMensagem_UsaMensagemPadraoQuandoCorpoPadraoEhRelatorio()
    {
        var corpoPadrao = "Resumo do relatório";
        var corpo = EmailService.ConstruirCorpoMensagem(string.Empty, corpoPadrao, "Resumo do relatório");

        Assert.Equal("Boa tarde,\n\nSegue o relatório em anexo.\n\nAtenciosamente,\nWillyan", corpo);
    }
}

public class HardwareMonitorServiceTests
{
    [Theory]
    [InlineData(100d, "100% (Excelente)")]
    [InlineData(90d, "90% (Excelente)")]
    [InlineData(84d, "84% (Bom)")]
    [InlineData(72d, "72% (Saudável)")]
    [InlineData(60d, "60% (OK)")]
    [InlineData(40d, "40% (Ruim)")]
    [InlineData(20d, "20% (Crítico)")]
    [InlineData(null, "Saudável")]
    public void FormatarSaudeDisco_UsaEscalaMaisDetalhada(double? valor, string esperado)
    {
        Assert.Equal(esperado, HardwareMonitorService.FormatarSaudeDisco(valor));
    }
}
