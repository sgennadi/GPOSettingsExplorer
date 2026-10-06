using System.Windows;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

public partial class App : Application
{
    protected override void OnStartup(
        StartupEventArgs e)
    {
        AdaptiveWindowManager.Register();
        base.OnStartup(e);
    }
}
