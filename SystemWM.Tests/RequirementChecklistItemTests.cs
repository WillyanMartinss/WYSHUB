using System;
using System.IO;
using System.Linq;
using SystemWM.Services;

namespace SystemWM.Tests;

public class RequirementChecklistItemTests
{
    [Fact]
    public void MarkCompleted_SetsCompletionFlag()
    {
        var item = new RequirementChecklistItem { Name = ".NET 8 Runtime" };

        Assert.False(item.IsCompleted);

        item.MarkCompleted();

        Assert.True(item.IsCompleted);
    }

    [Fact]
    public void ParseRequirementsFile_AssignsDownloadUrlForDotNetAndVisualCpp()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"requirements-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempFile, ".NET 8 Desktop Runtime x64\nVisual C++ Redistributable 2015-2022 x64\n");

        try
        {
            var items = RequirementChecklistService.ParseRequirementsFile(tempFile);
            var dotNetItem = items.First(item => item.Name.Contains(".NET 8", StringComparison.OrdinalIgnoreCase));
            var visualCppItem = items.First(item => item.Name.Contains("Visual C++", StringComparison.OrdinalIgnoreCase));

            Assert.Contains("dotnet.microsoft.com", dotNetItem.DownloadUrl, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("vc-redist", visualCppItem.DownloadUrl, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
