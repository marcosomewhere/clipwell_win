using System.IO;
using System.Windows;
using Merkautomat.Services;

namespace Merkautomat.Views;

public partial class KnowledgeBaseWindow
{
    private void BrainRelations_Click(object sender, RoutedEventArgs e)
        => OpenBrainRelations(editRelations: false);

    private void BrainEditRelations_Click(object sender, RoutedEventArgs e)
        => OpenBrainRelations(editRelations: true);

    private void OpenBrainRelations(bool editRelations)
    {
        if (_brain == null || _brain.Nodes.Count == 0) return;
        var settings = _app.CurrentSettings;
        var dialog = new BrainRelationsDialog(_brain, settings.BrainRelations ?? [], SelectedBrainNode?.ItemKey,
            BrainCanvasView.VisibleEdgeKinds, editRelations) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var previous = settings.BrainRelations ?? [];
        settings.BrainRelations = dialog.Relations;
        try { _app.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            settings.BrainRelations = previous;
            App.LogCrash(ex);
            Merkautomat.Views.AppMessageBox.Show(this, $"Beziehungen konnten nicht gespeichert werden: {ex.Message}", AppNames.KnowledgeBase);
            return;
        }
        RebuildBrain(keepLayout: true);
    }
}
