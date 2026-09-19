using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrlShortener.Domain.Entities;

namespace UrlShortener.Infrastructure.Persistence.Configurations;

public sealed class ClickEventConfiguration : IEntityTypeConfiguration<ClickEvent>
{
    public void Configure(EntityTypeBuilder<ClickEvent> builder)
    {
        builder.ToTable("ClickEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        builder.HasIndex(x => new { x.ShortUrlId, x.OccurredAtUtc });

        builder.Property(x => x.RefererHost).HasMaxLength(255);
        builder.Property(x => x.UserAgent).HasMaxLength(512);
        builder.Property(x => x.CountryCode).HasMaxLength(8);
        builder.Property(x => x.IpHash).HasMaxLength(64);
    }
}
