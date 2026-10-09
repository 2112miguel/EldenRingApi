namespace eldenRingApi.Contracts;

public sealed class CollectionQuery
{
    public int Page { get; init; } = 0;
    public int Limit { get; init; } = 20;
    public string? Name { get; init; }

    public bool IsValid => Page >= 0 && Limit is > 0 and <= 100;
}
