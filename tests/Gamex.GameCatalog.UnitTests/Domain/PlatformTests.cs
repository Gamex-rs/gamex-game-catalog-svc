using Gamex.GameCatalog.Domain.Games;

namespace Gamex.GameCatalog.UnitTests.Domain;

public sealed class PlatformTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Rename_SameName_ReturnsFalseAndKeepsVersion()
    {
        var platform = Platform.Create("PC", "pc", T0);

        platform.Rename("PC", T0.AddHours(1)).ShouldBeFalse();
        platform.UpdatedAt.ShouldBe(T0);
    }

    [Fact]
    public void Rename_NewName_ReturnsTrueAndBumpsVersion()
    {
        var platform = Platform.Create("PS5", "playstation5", T0);

        platform.Rename("PlayStation 5", T0.AddHours(1)).ShouldBeTrue();
        platform.Name.ShouldBe("PlayStation 5");
        platform.UpdatedAt.ShouldBe(T0.AddHours(1));
    }
}
