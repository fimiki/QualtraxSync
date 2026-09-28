namespace QualtraxSync.Domain.Entities;

public record Lifecycle
{
    private Lifecycle() { }

    private Lifecycle(string name) { Name = name; }

    public string Name { get; private set; } = null!;

    public static readonly Lifecycle Current = new("Current");
    public static readonly Lifecycle Archived = new("Archived");
    public static readonly Lifecycle Retired = new("Retired");
}
