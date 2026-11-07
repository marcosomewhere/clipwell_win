namespace ClipwellWin.Models;

/// <summary>Saved per item key: optional title, main category and optional subcategory of that main category.</summary>
public sealed record BrainAnnotation(string Name = "", string Category = "", string Subcategory = "");

/// <summary>A main category of the knowledge base with optional description and subcategories.</summary>
public sealed class KnowledgeCategory
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<KnowledgeSubcategory> Subcategories { get; set; } = [];
}

public sealed class KnowledgeSubcategory
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed record BrainRelation
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string FromKey { get; init; } = "";
    public string ToKey { get; init; } = "";
    public string Kind { get; init; } = "gehört zu";
    public string Evidence { get; init; } = "";
    public string Task { get; init; } = "";
    public string Steps { get; init; } = "";
}

public sealed class BrainSavedSummary
{
    public string NoteId { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> SourceKeys { get; set; } = [];
}
