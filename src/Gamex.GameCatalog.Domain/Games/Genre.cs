namespace Gamex.GameCatalog.Domain.Games;

public sealed class Genre
{
    private Genre()
    {
        Name = null!;
        Slug = null!;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public string Slug { get; private set; }

    public static Genre Create(string name, string slug) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        Slug = slug,
    };
}
