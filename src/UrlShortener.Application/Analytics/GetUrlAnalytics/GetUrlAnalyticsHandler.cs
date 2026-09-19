using MediatR;
using UrlShortener.Application.Common;
using UrlShortener.Domain.Interfaces;
using UrlShortener.Domain.ValueObjects;

namespace UrlShortener.Application.Analytics.GetUrlAnalytics;

public sealed class GetUrlAnalyticsHandler : IRequestHandler<GetUrlAnalyticsQuery, Result<UrlAnalyticsResult>>
{
    private readonly IShortUrlRepository _shortUrlRepository;
    private readonly IClickEventRepository _clickEventRepository;

    public GetUrlAnalyticsHandler(IShortUrlRepository shortUrlRepository, IClickEventRepository clickEventRepository)
    {
        _shortUrlRepository = shortUrlRepository;
        _clickEventRepository = clickEventRepository;
    }

    public async Task<Result<UrlAnalyticsResult>> Handle(GetUrlAnalyticsQuery request, CancellationToken ct)
    {
        var code = ShortCode.Create(request.Code);
        var shortUrl = await _shortUrlRepository.GetByCodeAsync(code, ct);
        if (shortUrl is null)
            return Result<UrlAnalyticsResult>.Failure($"Short URL '{code.Value}' not found.", ErrorCodes.NotFound);

        var clicksInRange = await _clickEventRepository.CountForShortUrlAsync(shortUrl.Id, request.FromUtc, request.ToUtc, ct);
        var daily = await _clickEventRepository.GetDailyCountsAsync(shortUrl.Id, request.FromUtc, request.ToUtc, ct);

        return Result<UrlAnalyticsResult>.Success(new UrlAnalyticsResult(
            code.Value, shortUrl.ClickCount, clicksInRange, daily, shortUrl.LastAccessedAtUtc));
    }
}
