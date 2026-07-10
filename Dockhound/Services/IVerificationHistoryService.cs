using Dockhound.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dockhound.Services
{
    public sealed record VerificationBrief(ulong GuildId, Faction Faction, DateTime ApprovedAtUtc, string? WarNumber);

    public interface IVerificationHistoryService
    {
        Task LogApprovalAsync(ulong guildId, ulong userId, Faction faction, string? imageUrl, ulong? approvedByUserId, ulong? steam64Id, string? warNumber, CancellationToken ct = default);

        Task<IReadOnlyList<VerificationBrief>> GetTrackRecordAsync(ulong userId, int take = 5, CancellationToken ct = default);

    }
}
