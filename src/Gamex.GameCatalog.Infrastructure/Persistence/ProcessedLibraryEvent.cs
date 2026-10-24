using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gamex.GameCatalog.Infrastructure.Persistence;

/// <summary>SQS message ids already applied to <c>game_stats</c>; makes the library-events consumer idempotent.</summary>
internal sealed class ProcessedLibraryEvent
{
    public required string MessageId { get; init; }
    public DateTime ProcessedAt { get; init; }
}

internal sealed class ProcessedLibraryEventConfiguration : IEntityTypeConfiguration<ProcessedLibraryEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedLibraryEvent> builder)
    {
        builder.ToTable("processed_library_events");
        builder.HasKey(e => e.MessageId);
        builder.Property(e => e.MessageId).HasMaxLength(100);
        builder.HasIndex(e => e.ProcessedAt);
    }
}
