using Gamex.GameCatalog.Domain.Games;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gamex.GameCatalog.Infrastructure.Persistence.Configurations;

internal sealed class PlatformConfiguration : IEntityTypeConfiguration<Platform>
{
    public void Configure(EntityTypeBuilder<Platform> builder)
    {
        builder.ToTable("platforms");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(p => p.Slug).IsUnique();
    }
}

internal sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("genres");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(100).IsRequired();
        builder.Property(g => g.Slug).HasMaxLength(100).IsRequired();
        builder.HasIndex(g => g.Slug).IsUnique();
    }
}

internal sealed class GameStatsConfiguration : IEntityTypeConfiguration<GameStats>
{
    public void Configure(EntityTypeBuilder<GameStats> builder)
    {
        builder.ToTable("game_stats");
        builder.HasKey(s => s.GameId);
        builder.Property(s => s.UpdatedAt).HasColumnType("datetime");
        builder.HasIndex(s => s.OwnedCount);
    }
}
