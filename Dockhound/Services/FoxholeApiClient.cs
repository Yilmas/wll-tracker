using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Dockhound.Services;

public enum FoxholeShard
{
    Live1,
    Live2,
    Live3
}

public sealed class FoxholeApiClient
{
    private static readonly IReadOnlyDictionary<FoxholeShard, string> BaseUrls =
        new Dictionary<FoxholeShard, string>
        {
            [FoxholeShard.Live1] = "https://war-service-live.foxholeservices.com/api",
            [FoxholeShard.Live2] = "https://war-service-live-2.foxholeservices.com/api",
            [FoxholeShard.Live3] = "https://war-service-live-3.foxholeservices.com/api"
        };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<FoxholeApiClient> _logger;

    public FoxholeApiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<FoxholeApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ApiResult<T>> GetAsync<T>(
        FoxholeShard shard,
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        var baseUrl = BaseUrls[shard];
        var url = $"{baseUrl}/{endpoint.TrimStart('/')}";

        try
        {
            using var httpClient = _httpClientFactory.CreateClient(nameof(FoxholeApiClient));
            using var response = await httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Foxhole API shard {Shard} returned {StatusCode} for {Endpoint}.",
                    shard,
                    response.StatusCode,
                    endpoint);

                return ApiResult<T>.Failed(
                    $"Foxhole API shard {shard} returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            var data = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);

            if (data is null)
            {
                _logger.LogWarning(
                    "Foxhole API shard {Shard} returned no usable data for {Endpoint}.",
                    shard,
                    endpoint);

                return ApiResult<T>.Failed($"Foxhole API shard {shard} returned no usable data.");
            }

            return ApiResult<T>.Ok(data, baseUrl);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Foxhole API shard {Shard} timed out for {Endpoint}.",
                shard,
                endpoint);

            return ApiResult<T>.Failed($"Foxhole API shard {shard} timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Foxhole API shard {Shard} could not be reached for {Endpoint}.",
                shard,
                endpoint);

            return ApiResult<T>.Failed($"Foxhole API shard {shard} could not be reached.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error querying Foxhole API shard {Shard} for {Endpoint}.",
                shard,
                endpoint);

            return ApiResult<T>.Failed($"Unexpected error querying Foxhole API shard {shard}.");
        }
    }

    public sealed record ApiResult<T>(bool Success, T? Data, string? Instance, string? Error)
    {
        public static ApiResult<T> Ok(T data, string instance) =>
            new(Success: true, Data: data, Instance: instance, Error: null);

        public static ApiResult<T> Failed(string error) =>
            new(Success: false, Data: default, Instance: null, Error: error);
    }
}