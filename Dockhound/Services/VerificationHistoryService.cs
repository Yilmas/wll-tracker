using Dockhound.Enums;
using Dockhound.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dockhound.Services
{
    public sealed class VerificationHistoryService : IVerificationHistoryService
    {
        private readonly IDbContextFactory<DockhoundContext> _dbFactory;

        public VerificationHistoryService(IDbContextFactory<DockhoundContext> dbFactory)
            => _dbFactory = dbFactory;

        public async Task LogApprovalAsync(ulong guildId, ulong userId, Faction faction, string? imageUrl, ulong? approvedByUserId, ulong? steam64Id, string? warNumber, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.VerificationRecords.Add(new VerificationRecord
            {
                GuildId = guildId,
                UserId = userId,
                Faction = faction,
                ImageUrl = imageUrl,
                ApprovedByUserId = approvedByUserId,
                ApprovedAtUtc = DateTime.UtcNow,
                Steam64Id = steam64Id,
                WarNumber = warNumber
            });
            await db.SaveChangesAsync(ct);
        }

        public async Task<IReadOnlyList<VerificationBrief>> GetTrackRecordAsync(ulong userId, int take = 5, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var items = await db.VerificationRecords
                .Where(v => v.UserId == userId)
                .OrderByDescending(v => v.ApprovedAtUtc)
                .Take(Math.Max(1, take))
                .Select(v => new VerificationBrief(v.GuildId, v.Faction, v.ApprovedAtUtc, v.WarNumber))
                .ToListAsync(ct);

            return items;
        }

        public static class FactionParser
        {
            public static Faction Parse(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    throw new ArgumentException("Faction string cannot be null or empty.", nameof(input));

                if (Enum.TryParse<Faction>(input, true, out var faction))
                    return faction;

                throw new ArgumentOutOfRangeException(nameof(input), input, "Invalid faction value.");
            }
        }
    }
}
