using Microsoft.EntityFrameworkCore;
using UrlShortener.Domain.Entities;
using UrlShortener.Domain.Interfaces;

namespace UrlShortener.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext, IUnitOfWork
{
    public DbSet<ShortUrl> ShortUrls => Set<ShortUrl>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    // DbContext already declares this exact signature (virtual) - IUnitOfWork.SaveChangesAsync
    // is satisfied by that inherited member directly, so no override/shadow is needed here.
}
