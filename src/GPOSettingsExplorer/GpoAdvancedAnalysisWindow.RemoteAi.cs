using System.Windows;
using System.Windows.Controls;
using GPOSettingsExplorer.Services;

namespace GPOSettingsExplorer;

/// <summary>
/// Explicitly disabled cloud AI configuration. No provider contact happens
/// when the tab opens. API key is transient PasswordBox input, never stored.
/// </summary>
public sealed partial class GpoAdvancedAnalysisWindow
{
    private readonly CheckBox _remoteAiEnabled = new()
    {
        Content = "Enable REMOTE AI for this window only (off by default)",
        IsChecked = false,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 4, 8, 4),
        ToolTip = "Only anonymized integer/bool findings; never raw domain or GPO data."
    };

    private readonly ComboBox _remoteAiProvider = new()
    {
        MinWidth = 170,
        ItemsSource = new[] { "OpenAI", "Azure OpenAI" },
        SelectedIndex = 0,
        ToolTip = "Only approved official HTTPS API endpoints are supported."
    };

    private readonly TextBox _remoteAiResource = new()
    {
        MinWidth = 160,
        ToolTip = "Azure resource name only (not a URL), e.g. example-resource."
    };

    private readonly TextBox _remoteAiModel = new()
    {
        MinWidth = 165,
        Text = "gpt-4.1-mini",
        ToolTip = "OpenAI model or your Azure OpenAI deployment name."
    };

    private readonly PasswordBox _remoteAiApiKey = new()
    {
        MinWidth = 190,
        ToolTip = "Transient API key for this request only. Not stored or written to diagnostics."
    };

    private TabItem SetupRemoteAiTab()
    {
        var tab = Tab("Remote AI (opt-in)", out var controls);
        controls.Children.Add(_remoteAiEnabled);
        controls.Children.Add(Label("Provider:"));
        controls.Children.Add(_remoteAiProvider);
        controls.Children.Add(Label("Azure resource:"));
        controls.Children.Add(_remoteAiResource);
        controls.Children.Add(Label("Model / deployment:"));
        controls.Children.Add(_remoteAiModel);
        controls.Children.Add(Label("API key (one request):"));
        controls.Children.Add(_remoteAiApiKey);
        AddAction(controls, "Analyze anonymized summary...", RemoteAiAsync);
        controls.Children.Add(new TextBlock
        {
            Text = "OFF by default. Build Unified overview first. Each request " +
                "requires a second affirmative confirmation. ONLY fixed-schema " +
                "integer/boolean GPO-health counters go to the selected external " +
                "provider; NO domain name, GPO name, OU/SID, XML, policy value, " +
                "secret, script or prompt text from the operator. Your organization " +
                "must authorize external AI use. API charges/data processing may apply. " +
                "No credential is persisted, and AI never performs GPO changes.",
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 240,
            MaxWidth = 950,
            Margin = new Thickness(7, 8, 7, 5),
            Foreground = UiStyle.WarningBrush
        });
        return tab;
    }

    private async Task<string> RemoteAiAsync()
    {
        if (_remoteAiEnabled.IsChecked != true)
            throw new InvalidOperationException(
                "Remote AI is disabled. Enable it explicitly for this session first.");
        var snapshot = _unifiedReport ??
            throw new InvalidOperationException(
                "Build Unified overview from the selected source before using remote AI.");
        var provider = _remoteAiProvider.SelectedIndex == 1
            ? GpoRemoteAiProvider.AzureOpenAI : GpoRemoteAiProvider.OpenAI;
        var resource = _remoteAiResource.Text.Trim();
        var model = _remoteAiModel.Text.Trim();
        var endpoint = GpoRemoteAiService.Endpoint(provider, resource);
        if (MessageBox.Show(this,
                "Send anonymized GPO diagnostic COUNTS to this EXTERNAL AI service?\n\n" +
                "Provider: " + provider +
                "\nHTTPS destination: " + endpoint.Host +
                "\nModel/deployment: " + model +
                "\n\nOnly fixed-schema counts and flags are sent. There is NO " +
                "domain/GPO identity, registry data, paths, SID, XML, script, " +
                "password or user-written prompt. External data processing " +
                "and possible provider fees apply. Confirm organizational permission. " +
                "No changes to policies will be made.\n\nProceed once?",
                "Remote AI - external data transfer consent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            _remoteAiApiKey.Clear();
            return "Remote AI canceled; no data was transmitted.";
        }

        var secret = _remoteAiApiKey.Password;
        try
        {
            return await GpoRemoteAiService.ExplainAsync(
                snapshot, provider, resource, model, secret, _lifetime.Token);
        }
        finally
        {
            // PasswordBox is never bound to settings or diagnostics.
            _remoteAiApiKey.Clear();
        }
    }
}
