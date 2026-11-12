using System.Text;
using System.Windows;
using System.Windows.Controls;
using Merkautomat.Models;
using Merkautomat.Services;

namespace Merkautomat.Views;

public partial class BrainRelationsDialog : Window
{
    public sealed record NodeChoice(string Key, string Label, string Meta = "");
    public sealed record RelationChoice(BrainRelation Relation, string Label);
    private readonly BrainGraph _graph;
    private readonly HashSet<GraphEdgeKind> _visibleKinds;
    private readonly List<NodeChoice> _nodes;
    private readonly List<NodeChoice> _relationNodes;
    private readonly List<BrainRelation> _originalRelations;
    private bool _updatingRelations;
    private bool _loadingEditor;
    private string? _editingId;
    private BrainRelation? _editorBaseline;
    public List<BrainRelation> Relations { get; }

    public BrainRelationsDialog(BrainGraph graph, IEnumerable<BrainRelation> relations, string? selectedKey,
        IEnumerable<GraphEdgeKind> visibleKinds, bool editRelations = false)
    {
        InitializeComponent();
        EditorChrome.ApplyTheme(this);
        SourceInitialized += (_, _) => EditorChrome.ApplyFrame(this);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
        _graph = graph;
        _visibleKinds = visibleKinds.ToHashSet();
        Relations = relations.ToList();
        _originalRelations = Relations.ToList();
        _nodes = graph.Nodes.Select(n => new NodeChoice(n.ItemKey,
            n.Item.Title.Replace('\n', ' ').Replace('\r', ' '),
            $"{SemanticChunk.SourceName(n.Source)} · {n.Item.Date:dd.MM.yyyy HH:mm} · {n.ItemKey}"))
            .OrderBy(n => n.Label).ToList();
        var known = _nodes.Select(n => n.Key).ToHashSet();
        _relationNodes = _nodes.Concat(Relations.SelectMany(r => new[] { r.FromKey, r.ToKey }).Distinct()
            .Where(key => !known.Contains(key)).Select(key => new NodeChoice(key, $"Nicht im aktuellen Netz: {key}", "Gespeicherter Bezug außerhalb der aktiven Quellen oder des Ausschnitts"))).ToList();
        PathFrom.ItemsSource = _nodes;
        PathTo.ItemsSource = _nodes;
        RelationFrom.ItemsSource = _relationNodes;
        RelationTo.ItemsSource = _relationNodes;
        RelationKind.ItemsSource = new[] { "gehört zu", "benötigt", "beschreibt", "folgt auf", "verweist auf", "widerspricht" }
            .Concat(Relations.Select(r => r.Kind)).Distinct().Order().ToList();
        PathFrom.SelectedItem = _nodes.FirstOrDefault(n => n.Key == selectedKey) ?? _nodes.FirstOrDefault();
        RelationFrom.SelectedItem = PathFrom.SelectedItem;
        ResetEditor();
        RefreshRelations();
        RelationTabs.SelectedIndex = editRelations ? 1 : 0;
        ScopeInfo.Text = $"{graph.Nodes.Count} Einträge im aktuellen Netz · Aktive Quellenfilter und höchstens 1.000 Einträge. Alle Änderungen bleiben lokal.";
        RelationKind.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler(Editor_Changed));
        UpdateDirtyState();
    }

    private string Label(string key) => _nodes.FirstOrDefault(n => n.Key == key)?.Label ?? $"Nicht im aktuellen Brain: {key}";

    private void RefreshRelations()
    {
        _updatingRelations = true;
        var query = RelationFilter.Text.Trim();
        var choices = Relations.Select(r => new RelationChoice(r, $"{Label(r.FromKey)} → {Label(r.ToKey)}"))
            .Where(c => $"{c.Label} {c.Relation.Kind} {c.Relation.Evidence}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        RelationsList.ItemsSource = choices;
        RelationsList.SelectedItem = choices.FirstOrDefault(c => c.Relation.Id == _editingId);
        RelationCount.Text = $"Beziehungen ({Relations.Count})";
        RelationsEmpty.Visibility = choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RelationsEmpty.Text = Relations.Count == 0 ? "Noch keine Beziehungen. Rechts Start, Ziel und Begründung eingeben." : "Keine Beziehungen für diesen Filter.";
        _updatingRelations = false;
        PathResult.Text = "Bitte Start und Ziel wählen und den Weg berechnen.";
        UpdateDirtyState();
    }

    private void RelationFilter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_nodes != null) RefreshRelations();
    }

    private void Tabs_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == RelationTabs && _nodes != null) UpdateDirtyState();
    }

    private void Editor_Changed(object sender, RoutedEventArgs e)
    {
        if (_editorBaseline != null && !_loadingEditor) UpdateDirtyState();
    }

    private void UpdateDirtyState()
    {
        if (_editorBaseline == null || SaveRelationsButton == null) return;
        var dirty = !_originalRelations.SequenceEqual(Relations) || ReadEditor() != _editorBaseline;
        SaveRelationsButton.IsEnabled = dirty;
        CloseButton.Content = dirty ? "Abbrechen" : "Schließen";
        Feedback.Text = dirty ? "Ungespeicherte Änderungen. Speichern übernimmt auch die aktuelle Eingabe."
            : "Keine ungespeicherten Änderungen.";
        EditorHeading.Text = _editingId == null ? "Neue Beziehung" : "Beziehung bearbeiten";
        ApplyRelationButton.Content = _editingId == null ? "Zur Liste hinzufügen" : "Änderung übernehmen";
        RemoveRelationButton.IsEnabled = _editingId != null;
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        var start = PathFrom.SelectedItem;
        PathFrom.SelectedItem = PathTo.SelectedItem;
        PathTo.SelectedItem = start;
    }

    private void PathOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (PathResult != null) PathResult.Text = "Bitte den Weg mit diesen Einstellungen berechnen.";
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        if (PathFrom.SelectedItem is not NodeChoice from || PathTo.SelectedItem is not NodeChoice to)
        { PathResult.Text = "Bitte Start und Ziel auswählen."; return; }
        var byKey = _graph.Nodes.ToDictionary(n => n.ItemKey, n => n.Index);
        var edges = _graph.Edges.Where(e => e.Kind != GraphEdgeKind.Manual).ToList();
        foreach (var r in Relations)
            if (byKey.TryGetValue(r.FromKey, out var a) && byKey.TryGetValue(r.ToKey, out var b) && a != b)
                edges.Add(new GraphEdge(a, b, GraphEdgeKind.Manual, 1,
                    $"Manuell gepflegt: {r.Kind}\nBegründung: {r.Evidence}"
                    + (r.Task.Length > 0 ? $"\nAufgabe: {r.Task}" : "")
                    + (r.Steps.Length > 0 ? $"\nArbeitsschritte:\n{r.Steps}" : "")) { Directed = true });
        var allowed = new HashSet<GraphEdgeKind> { GraphEdgeKind.Manual };
        if (IncludeSummary.IsChecked == true) allowed.Add(GraphEdgeKind.Summary);
        if (IncludeHints.IsChecked == true) allowed.UnionWith(_visibleKinds.Where(k => k != GraphEdgeKind.Summary));
        var graph = new BrainGraph(_graph.Nodes, edges, _graph.Clusters);
        var path = BrainTraversal.Find(graph, byKey[from.Key], byKey[to.Key], allowed);
        if (path == null)
        {
            PathResult.Text = "Kein gerichteter Weg mit diesen Bezugsarten im aktuellen Brain gefunden.\n\nDas gilt nur für die geladenen Einträge und Filter, nicht für den gesamten Wissensbestand. Automatische Hinweise können bei Bedarf zugeschaltet werden.";
            return;
        }
        var sb = new StringBuilder($"{path.Steps.Count} Verbindung(en) · kürzester Weg nach Anzahl\n");
        if (path.Steps.Any(s => s.Edge.Kind is not (GraphEdgeKind.Manual or GraphEdgeKind.Summary)))
            sb.AppendLine("Enthält automatische Hinweise: Dieser Weg ist keine belegte fachliche Schlussfolgerung.");
        if (path.Steps.Count == 0) sb.AppendLine("Start und Ziel sind derselbe Eintrag.");
        sb.AppendLine($"\nStart: {Label(from.Key)}");
        foreach (var step in path.Steps)
            sb.AppendLine($"\n→ {step.Edge.Detail}\nZiel: {Label(graph.Nodes[step.To].ItemKey)}");
        PathResult.Text = sb.ToString();
    }

    private BrainRelation ReadEditor() => new()
    {
        Id = _editingId ?? "", FromKey = (RelationFrom.SelectedItem as NodeChoice)?.Key ?? "",
        ToKey = (RelationTo.SelectedItem as NodeChoice)?.Key ?? "", Kind = RelationKind.Text.Trim(),
        Evidence = Evidence.Text.Trim(), Task = TaskText.Text.Trim(), Steps = StepsText.Text.Trim(),
    };

    private void ResetEditor()
    {
        _loadingEditor = true;
        _editingId = null;
        RelationFrom.SelectedItem = PathFrom.SelectedItem;
        RelationTo.SelectedItem = null;
        RelationKind.Text = "gehört zu";
        Evidence.Text = TaskText.Text = StepsText.Text = "";
        _editorBaseline = ReadEditor();
        _loadingEditor = false;
        _updatingRelations = true;
        RelationsList.SelectedItem = null;
        _updatingRelations = false;
        UpdateDirtyState();
    }

    private bool ApplyEditor()
    {
        var relation = ReadEditor();
        var allowedKeys = _nodes.Select(n => n.Key).ToHashSet();
        if (Relations.FirstOrDefault(r => r.Id == _editingId) is { } existing)
        {
            allowedKeys.Add(existing.FromKey);
            allowedKeys.Add(existing.ToKey);
        }
        var error = BrainTraversal.Validate(relation, allowedKeys);
        if (error != null) { Feedback.Text = error; return false; }
        if (Relations.Any(r => r.Id != _editingId && r.FromKey == relation.FromKey && r.ToKey == relation.ToKey
            && string.Equals(r.Kind, relation.Kind, StringComparison.OrdinalIgnoreCase)))
        { Feedback.Text = "Diese gerichtete Beziehung existiert bereits. Bitte links auswählen und bearbeiten."; return false; }
        relation = relation with { Id = _editingId ?? Guid.NewGuid().ToString("N") };
        Relations.RemoveAll(r => r.Id == relation.Id);
        Relations.Add(relation);
        _editingId = relation.Id;
        _editorBaseline = ReadEditor();
        RefreshRelations();
        Feedback.Text = "Übernommen. Mit Speichern dauerhaft lokal sichern.";
        return true;
    }

    private bool KeepPending() => _editorBaseline == null || ReadEditor() == _editorBaseline || ApplyEditor();
    private void Apply_Click(object sender, RoutedEventArgs e) => ApplyEditor();
    private void New_Click(object sender, RoutedEventArgs e) { if (KeepPending()) ResetEditor(); }

    private void Relation_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingRelations) return;
        if (RelationsList.SelectedItem is not RelationChoice choice) return;
        if (!KeepPending())
        {
            _updatingRelations = true;
            RelationsList.SelectedItem = RelationsList.Items.Cast<RelationChoice>().FirstOrDefault(c => c.Relation.Id == _editingId);
            _updatingRelations = false;
            return;
        }
        var r = choice.Relation;
        _loadingEditor = true;
        _editingId = r.Id;
        RelationFrom.SelectedItem = _relationNodes.FirstOrDefault(n => n.Key == r.FromKey);
        RelationTo.SelectedItem = _relationNodes.FirstOrDefault(n => n.Key == r.ToKey);
        RelationKind.Text = r.Kind;
        Evidence.Text = r.Evidence;
        TaskText.Text = r.Task;
        StepsText.Text = r.Steps;
        _editorBaseline = ReadEditor();
        _loadingEditor = false;
        _updatingRelations = true;
        RelationsList.SelectedItem = RelationsList.Items.Cast<RelationChoice>().FirstOrDefault(c => c.Relation.Id == r.Id);
        _updatingRelations = false;
        UpdateDirtyState();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_editingId == null) return;
        Relations.RemoveAll(r => r.Id == _editingId);
        ResetEditor();
        RefreshRelations();
        Feedback.Text = "Beziehung entfernt; wirksam nach Speichern. Die Einträge bleiben erhalten.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (KeepPending()) DialogResult = true;
    }
}
