using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Infrastructure.Persistence.Configurations;

public sealed class ShortUrlConfiguration : IEntityTypeConfiguration<ShortUrl>
{
    public void Configure(EntityTypeBuilder<ShortUrl> builder)
    {
        builder.ToTable("ShortUrls");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code)
            .HasConversion(code => code.Value, value => ShortCode.FromTrusted(value))
            .HasMaxLength(ShortCode.MaxLength)
            .IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();

        builder.Property(x => x.TargetUrl).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.OwnerId).HasMaxLength(128);
        builder.HasIndex(x => x.OwnerId);

        builder.Property(x => x.Version).IsRowVersion();

        builder.Property(x => x.CreatedAtUtc).IsRequired();
    }
}
