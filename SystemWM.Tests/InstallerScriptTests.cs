using System.IO;

namespace SystemWM.Tests;

public class InstallerScriptTests
{
    [Fact]
    public void InnoSetupScript_IncludesOfflineDependencyFolders()
    {
        var installerPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "installer", "WYSHUB.iss");
        var resolvedPath = Path.GetFullPath(installerPath);

        Assert.True(File.Exists(resolvedPath), $"Installer script not found: {resolvedPath}");

        var script = File.ReadAllText(resolvedPath);

        Assert.Contains("dependencias_offline", script);
        Assert.Contains("setup-wizard.ps1", script);
    }
}
