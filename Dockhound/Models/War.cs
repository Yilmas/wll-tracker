namespace Dockhound.Models;

/// <summary>
/// A persisted snapshot of the Foxhole <c>/worldconquest/war</c> endpoint.
/// Timestamps are Unix milliseconds, matching the API response.
/// </summary>
public sealed class War
{
    public string WarId { get; set; } = string.Empty;
    public int WarNumber { get; set; }
    public string Winner { get; set; } = string.Empty;
    public long ConquestStartTime { get; set; }
    public long? ConquestEndTime { get; set; }
    public long? ResistanceStartTime { get; set; }
    public long? ScheduledConquestEndTime { get; set; }
    public int RequiredVictoryTowns { get; set; }
    public int ShortRequiredVictoryTowns { get; set; }
    public string? EntityTag { get; set; }
    public DateTime RefreshAfterUtc { get; set; }
}
