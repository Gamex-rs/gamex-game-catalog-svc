using Gamex.GameCatalog.Domain.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gamex.GameCatalog.Infrastructure.Persistence.Configurations;

internal sealed class GameConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.ToTable("games");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.RawgSlug).HasMaxLength(200);
        builder.Property(g => g.Title).HasMaxLength(255).IsRequired();
        builder.Property(g => g.Description).HasColumnType("text");
        builder.Property(g => g.CoverImageUrl).HasMaxLength(500);
        builder.Property(g => g.CoverSourceUrl).HasMaxLength(500);
        builder.Property(g => g.CreatedAt).HasColumnType("datetime");

        builder.HasIndex(g => g.RawgId).IsUnique();
        builder.HasIndex(g => g.RawgSlug).IsUnique();
        builder.HasIndex(g => g.Title);
        builder.HasIndex(g => g.ReleaseDate);
        builder.HasIndex(g => g.Metacritic);
        builder.HasIndex(g => g.RefreshedAt);

        builder.HasMany(g => g.Platforms)
            .WithMany()
            .UsingEntity(
                "game_platforms",
                right => right.HasOne(typeof(Platform)).WithMany().HasForeignKey("PlatformId").OnDelete(DeleteBehavior.Restrict),
                left => left.HasOne(typeof(Game)).WithMany().HasForeignKey("GameId").OnDelete(DeleteBehavior.Cascade),
                join => join.HasKey("GameId", "PlatformId"));

        builder.HasMany(g => g.Genres)
            .WithMany()
            .UsingEntity(
                "game_genres",
                right => right.HasOne(typeof(Genre)).WithMany().HasForeignKey("GenreId").OnDelete(DeleteBehavior.Restrict),
                left => left.HasOne(typeof(Game)).WithMany().HasForeignKey("GameId").OnDelete(DeleteBehavior.Cascade),
                join => join.HasKey("GameId", "GenreId"));

        builder.HasOne(g => g.Stats)
            .WithOne()
            .HasForeignKey<GameStats>(s => s.GameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(g => g.Platforms).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(g => g.Genres).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
