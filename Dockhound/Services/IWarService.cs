namespace Dockhound.Services;

public interface IWarService
{
    /// <summary>
    /// Gets the active war number. Returns <c>"000"</c> during resistance and
    /// throws when there is no active or resistance-phase war to report.
    /// </summary>
    Task<string> GetActiveWarNumberAsync(CancellationToken cancellationToken = default);
}
