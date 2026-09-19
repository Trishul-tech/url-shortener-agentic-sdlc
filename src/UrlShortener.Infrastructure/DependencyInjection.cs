using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UrlShortener.Application.Abstractions;
using UrlShortener.Domain.Interfaces;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Persistence.Repositories;
using UrlShortener.Infrastructure.Services;

namespace UrlShortener.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=urlshortener.db";

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

        services.AddMemoryCache();

        services.AddScoped<IShortUrlRepository, ShortUrlRepository>();
        services.AddScoped<IClickEventRepository, ClickEventRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICodeGenerator, Base62CodeGenerator>();
        services.AddSingleton<ICustomAliasPolicy, DefaultCustomAliasPolicy>();
        services.AddSingleton<ICacheService, MemoryCacheService>();

        return services;
    }
}
