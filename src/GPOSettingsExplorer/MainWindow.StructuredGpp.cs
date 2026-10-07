using System.Windows;
using GPOSettingsExplorer.Models;

namespace GPOSettingsExplorer;

public partial class MainWindow
{
    private async void StructuredEditGppXml_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_domainContext is null ||
            GppXmlGrid.SelectedItem is not
                GppDocumentInfo selected)
        {
            return;
        }

        var gpo =
            _gpos.FirstOrDefault(
                candidate =>
                    candidate.Id ==
                    selected.GpoId);

        if (gpo is null)
            return;

        string xml;

        try
        {
            xml =
                await Task.Run(
                    () =>
                        _gppDocumentService.ReadXml(
                            selected));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Open Structured GPP Editor",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        var editor =
            new StructuredGppEditorWindow(
                selected,
                xml)
            {
                Owner =
                    this
            };

        if (editor.ShowDialog() !=
            true)
        {
            return;
        }

        await SaveGppDocumentAsync(
            gpo,
            selected,
            editor.Xml,
            "Structured edit");
    }
}
