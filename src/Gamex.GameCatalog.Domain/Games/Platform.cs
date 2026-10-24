namespace Gamex.GameCatalog.Domain.Games;

public sealed class Platform
{
    private Platform()
    {
        Name = null!;
        Slug = null!;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; }

    /// <summary>Unique, stable identifier shared with RAWG (e.g. <c>playstation5</c>).</summary>
    public string Slug { get; private set; }

    /// <summary>Version for <c>catalog.platform.upserted</c> events.</summary>
    public DateTime UpdatedAt { get; private set; }

    public static Platform Create(string name, string slug, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        Name = name,
        Slug = slug,
        UpdatedAt = now,
    };

    /// <returns><c>true</c> if the name changed.</returns>
    public bool Rename(string name, DateTime now)
    {
        if (Name == name)
        {
            return false;
        }

        Name = name;
        UpdatedAt = now;
        return true;
    }
}
