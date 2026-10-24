namespace Gamex.GameCatalog.Domain.Games;

public enum GameChange
{
    None,

    /// <summary>Only fields that are not replicated to other services changed (description, genres, RAWG ids).</summary>
    Internal,

    /// <summary>A replicated field changed (title, cover, release date, metacritic, platforms); a <c>catalog.game.upserted</c> event must be published.</summary>
    Replicated,
}
