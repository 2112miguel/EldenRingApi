namespace eldenRingApi.Contracts;

public sealed class GuideRequest
{
    public GuideReference? Subject { get; init; }
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public string? Status { get; init; }
    public List<GuideSection>? Sections { get; init; }
    public List<GuideSupplement>? Supplements { get; init; }
    public List<GuideMedia>? Media { get; init; }
}

public sealed class GuideReference { public string? Collection { get; init; } public string? Id { get; init; } }
public sealed class GuideSection { public int Order { get; init; } public string? Title { get; init; } public string? Content { get; init; } public List<GuideReference>? References { get; init; } public List<GuideSubsection>? Subsections { get; init; } }
public sealed class GuideSubsection { public int Order { get; init; } public string? Title { get; init; } public string? Content { get; init; } public List<GuideReference>? References { get; init; } }
public sealed class GuideSupplement { public string? Id { get; init; } public string? Type { get; init; } public string? Title { get; init; } public string? Content { get; init; } public string? Image { get; init; } public List<GuideReference>? References { get; init; } }
public sealed class GuideMedia { public string? Type { get; init; } public string? Url { get; init; } public string? Alt { get; init; } }
