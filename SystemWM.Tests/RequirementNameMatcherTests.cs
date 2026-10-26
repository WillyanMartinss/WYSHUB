using SystemWM.Services;

namespace SystemWM.Tests;

public class RequirementNameMatcherTests
{
    [Fact]
    public void MatchesDotNetAndVisualCppNamesAcrossChecklistAndLogFormats()
    {
        Assert.True(RequirementNameMatcher.AreEquivalent(
            "Microsoft .NET 8 Desktop Runtime x64 (necessário se a build não for self-contained)",
            ".NET 8 Desktop Runtime x64"));

        Assert.True(RequirementNameMatcher.AreEquivalent(
            "Microsoft Visual C++ Redistributable x64 (2015-2022) - recomendado",
            "Visual C++ Redistributable 2015-2022 x64"));

        Assert.True(RequirementNameMatcher.AreEquivalent(
            "Serviço WMI ativo (Windows Management Instrumentation)",
            "Serviço WMI"));
    }
}
