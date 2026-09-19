namespace UrlShortener.Domain.Interfaces;

/// <summary>Commits the changes tracked across repositories in a single transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
