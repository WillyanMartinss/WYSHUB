using System;
using SystemWM.Services;

namespace SystemWM.Tests;

public class CleanupServiceTests
{
    [Fact]
    public void ListarItensDisponiveis_IncludesWindowsSecurityScanItem()
    {
        var service = new CleanupService();
        var itens = service.ListarItensDisponiveis();

        Assert.Contains(itens, item => item.Nome.Contains("vírus", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ListarItensDisponiveis_MarksOnlyTempUserAsSelectedByDefault()
    {
        var service = new CleanupService();
        var itens = service.ListarItensDisponiveis();

        Assert.Contains(itens, item => item.CaminhoTipo == "TempUser" && item.Selecionado);
        Assert.DoesNotContain(itens, item => item.CaminhoTipo != "TempUser" && item.Selecionado);
    }
}
