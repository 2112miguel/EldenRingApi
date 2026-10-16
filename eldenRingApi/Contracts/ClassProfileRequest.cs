namespace eldenRingApi.Contracts;

public sealed class ClassProfileRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Image { get; init; }
    public StartingClassStats? Stats { get; init; }
    public Dictionary<string, List<StartingEquipmentReference>>? StartingEquipment { get; init; }
    public bool? IsBaseGameStartingClass { get; init; }
    public string? ContentSource { get; init; }
    public string? GameVersion { get; init; }
}

public sealed class StartingClassStats
{
    public int Level { get; init; }
    public int Vigor { get; init; }
    public int Mind { get; init; }
    public int Endurance { get; init; }
    public int Strength { get; init; }
    public int Dexterity { get; init; }
    public int Intelligence { get; init; }
    public int Faith { get; init; }
    public int Arcane { get; init; }
}

public sealed class StartingEquipmentReference
{
    public string? Collection { get; init; }
    public string? Id { get; init; }
    public int Quantity { get; init; } = 1;
}
