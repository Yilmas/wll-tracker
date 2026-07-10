using Dockhound.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Dockhound.Services;

public sealed class WarService : IWarService
{
    private const string CacheKey = "foxhole:war:live1";
    private static readonly TimeSpan FallbackRefreshInterval = TimeSpan.FromHours(1);
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private readonly IDbContextFactory<DockhoundContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly FoxholeApiClient _foxholeApiClient;
    private readonly ILogger<WarService> _logger;

    public WarService(IDbContextFactory<DockhoundContext> dbFactory, IMemoryCache cache, FoxholeApiClient foxholeApiClient, ILogger<WarService> logger)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _foxholeApiClient = foxholeApiClient;
        _logger = logger;
    }

    public async Task<string> GetActiveWarNumberAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out War? cached) && cached is not null)
            return ResolveWarNumber(cached, DateTime.UtcNow);

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached) && cached is not null)
                return ResolveWarNumber(cached, DateTime.UtcNow);

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var war = await db.Wars.OrderByDescending(x => x.ConquestStartTime).FirstOrDefaultAsync(cancellationToken);
            var now = DateTime.UtcNow;

            if (war is null || now >= war.RefreshAfterUtc)
                war = await RefreshAsync(db, war, now, cancellationToken);

            Cache(war, now);
            return ResolveWarNumber(war, now);
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private async Task<War> RefreshAsync(DockhoundContext db, War? current, DateTime now, CancellationToken cancellationToken)
    {
        var response = await _foxholeApiClient.GetWarAsync(FoxholeShard.Live1, current?.EntityTag, cancellationToken);

        if (!response.Success)
        {
            if (current is not null)
            {
                _logger.LogWarning("Unable to refresh Foxhole war data; using the last persisted record. {Error}", response.Error);
                return current;
            }

            throw new InvalidOperationException(response.Error ?? "Unable to retrieve Foxhole war data.");
        }

        if (response.NotModified)
        {
            if (current is null)
                throw new InvalidOperationException("Foxhole API returned Not Modified without a persisted war record.");

            current.EntityTag = response.EntityTag ?? current.EntityTag;
            current.RefreshAfterUtc = GetRefreshAfterUtc(now, response.CacheDuration);
            await db.SaveChangesAsync(cancellationToken);
            return current;
        }

        if (response.Data is null)
            throw new InvalidOperationException("Foxhole API returned a successful response without war data.");

        var war = await db.Wars.SingleOrDefaultAsync(x => x.WarId == response.Data.WarId, cancellationToken);
        if (war is null)
        {
            war = response.Data;
            db.Wars.Add(war);
        }
        else
        {
            CopyEndpointValues(response.Data, war);
        }

        war.EntityTag = response.EntityTag;
        war.RefreshAfterUtc = GetRefreshAfterUtc(now, response.CacheDuration);
        await db.SaveChangesAsync(cancellationToken);
        return war;
    }

    private void Cache(War war, DateTime now)
    {
        _cache.Set(CacheKey, war, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = new DateTimeOffset(GetCacheExpiryUtc(war, now))
        });
    }

    private static string ResolveWarNumber(War war, DateTime now)
    {
        var nowMilliseconds = new DateTimeOffset(now).ToUnixTimeMilliseconds();
        if (nowMilliseconds < war.ConquestStartTime)
            throw new InvalidOperationException("The next Foxhole war has not started yet.");

        if (war.ResistanceStartTime is long resistanceStartTime && nowMilliseconds >= resistanceStartTime)
            return "000";

        if (war.ConquestEndTime is null || nowMilliseconds < war.ConquestEndTime.Value)
            return war.WarNumber.ToString(CultureInfo.InvariantCulture);

        throw new InvalidOperationException("Foxhole war data has no valid active or resistance phase.");
    }

    private static DateTime GetRefreshAfterUtc(DateTime now, TimeSpan? apiCacheDuration)
    {
        var duration = apiCacheDuration ?? FallbackRefreshInterval;
        return now.Add(duration < TimeSpan.Zero ? TimeSpan.Zero : duration);
    }

    private static DateTime GetCacheExpiryUtc(War war, DateTime now)
    {
        var nowMilliseconds = new DateTimeOffset(now).ToUnixTimeMilliseconds();
        var expirations = new List<DateTime> { war.RefreshAfterUtc };
        AddFutureTimestamp(war.ConquestStartTime);
        AddFutureTimestamp(war.ConquestEndTime);
        AddFutureTimestamp(war.ResistanceStartTime);

        return expirations.Where(x => x > now).DefaultIfEmpty(now.AddSeconds(1)).Min();

        void AddFutureTimestamp(long? timestamp)
        {
            if (timestamp is long value && value > nowMilliseconds)
                expirations.Add(DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime);
        }
    }

    private static void CopyEndpointValues(War source, War destination)
    {
        destination.WarNumber = source.WarNumber;
        destination.Winner = source.Winner;
        destination.ConquestStartTime = source.ConquestStartTime;
        destination.ConquestEndTime = source.ConquestEndTime;
        destination.ResistanceStartTime = source.ResistanceStartTime;
        destination.ScheduledConquestEndTime = source.ScheduledConquestEndTime;
        destination.RequiredVictoryTowns = source.RequiredVictoryTowns;
        destination.ShortRequiredVictoryTowns = source.ShortRequiredVictoryTowns;
    }
}
