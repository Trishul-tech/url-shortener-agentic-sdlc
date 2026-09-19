using MediatR;
using UrlShortener.Application.Abstractions;
using UrlShortener.Application.Common;
using UrlShortener.Domain.Interfaces;
using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Application.UrlShortening.DeactivateShortUrl;

public sealed class DeactivateShortUrlHandler : IRequestHandler<DeactivateShortUrlCommand, Result<bool>>
{
    private readonly IShortUrlRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public DeactivateShortUrlHandler(IShortUrlRepository repository, IUnitOfWork unitOfWork, ICacheService cache)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<Result<bool>> Handle(DeactivateShortUrlCommand request, CancellationToken ct)
    {
        var code = ShortCode.Create(request.Code);
        var shortUrl = await _repository.GetByCodeAsync(code, ct);
        if (shortUrl is null)
            return Result<bool>.Failure($"Short URL '{code.Value}' not found.", ErrorCodes.NotFound);

        if (request.RequestedByOwnerId is not null && shortUrl.OwnerId != request.RequestedByOwnerId)
            return Result<bool>.Failure("Not authorized to modify this short URL.", ErrorCodes.Conflict);

        shortUrl.Deactivate();
        _repository.Update(shortUrl);
        await _unitOfWork.SaveChangesAsync(ct);
        await _cache.RemoveAsync($"resolve:{code.Value}", ct);

        return Result<bool>.Success(true);
    }
}
