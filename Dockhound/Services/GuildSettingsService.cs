using Dockhound.Config;
using Dockhound.Enums;
using Dockhound.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dockhound.Services
{
    public sealed class GuildSettingsService : IGuildSettingsService
    {
        private readonly IDbContextFactory<DockhoundContext> _dbFactory;
        private readonly IMemoryCache _cache;
        private readonly GuildDefaults _defaults;
        private readonly MemoryCacheEntryOptions _cacheOptions =
            new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(10));

        private static string CacheKey(ulong gid) => $"guildcfg:{gid}";

        public const int SchemaVersionConst = 9;

        public int CurrentSchemaVersion => SchemaVersionConst;

        public GuildSettingsService(IDbContextFactory<DockhoundContext> dbFactory, IMemoryCache cache, IOptions<GuildDefaults> defaults)
        {
            _dbFactory = dbFactory;
            _cache = cache;
            _defaults = defaults.Value;
        }

        public async Task<GuildConfig> PatchAsync(ulong guildId, Action<GuildConfig> mutate, string? changedBy = null, CancellationToken ct = default)
        {
            var current = await GetAsync(guildId, ct);

            var next = Clone(current);

            mutate(next);

            await UpdateAsync(guildId, next, changedBy, ct);

            return next;
        }

        private static void EnsureDefaults(GuildConfig cfg)
        {
            cfg.SchemaVersion = Math.Max(1, cfg.SchemaVersion);
            cfg.Roles ??= new List<GuildConfig.RoleSet>();
            cfg.Verify ??= new GuildConfig.VerificationSettings();
            cfg.Verify.RestrictedAccess ??= new GuildConfig.RestrictedAccessSettings();
            cfg.Verify.RecruitAssignerRoles ??= new List<ulong>();
            cfg.Verify.AllyAssignerRoles ??= new List<ulong>();
            cfg.Verify.TrustedRoles ??= new List<ulong>();
            cfg.Verify.RestrictedAccess.AlwaysRestrictRoles ??= new List<ulong>();
            cfg.Verify.RestrictedAccess.MemberOnlyRoles ??= new List<ulong>();
            cfg.Honeypot ??= new GuildConfig.HoneypotSettings();
            cfg.Honeypot.MessagePruneDays = Math.Clamp(cfg.Honeypot.MessagePruneDays, 0, 7);
        }

        private static void MigrateToCurrent(GuildConfig cfg)
        {
            // ensure Roles exists and bump the version
            if (cfg.SchemaVersion < 5)
            {
                cfg.Roles ??= new List<GuildConfig.RoleSet>();
                cfg.SchemaVersion = 5;
            }
            if (cfg.SchemaVersion < 6)
            {
                cfg.Honeypot ??= new GuildConfig.HoneypotSettings();
                cfg.SchemaVersion = 6;
            }
            if (cfg.SchemaVersion < 7)
            {
                cfg.Honeypot ??= new GuildConfig.HoneypotSettings();
                cfg.Honeypot.MessagePruneDays = Math.Clamp(cfg.Honeypot.MessagePruneDays, 0, 7);
                cfg.SchemaVersion = 7;
            }
            if (cfg.SchemaVersion < 8)
            {
                cfg.Honeypot ??= new GuildConfig.HoneypotSettings();
                cfg.Honeypot.MessagesEnabled = true;
                cfg.Honeypot.ReactionsEnabled = true;
                cfg.SchemaVersion = 8;
            }
            if (cfg.SchemaVersion < 9)
            {
                // Guild logo URLs are optional and remain null until configured.
                cfg.SchemaVersion = 9;
            }
        }

        public async Task<GuildConfig.RestrictedAccessSettings> GetRestrictedAccessAsync(ulong guildId, CancellationToken ct = default)
        {
            var cfg = await GetAsync(guildId, ct);
            return cfg.Verify.RestrictedAccess!;
        }

        public Task UpdateRestrictedAccessAsync(ulong guildId, ulong? channelId, ulong? messageId, AccessRestriction restrictionLevel, string? changedBy = null, CancellationToken ct = default)
        {
            return PatchAsync(
                guildId,
                cfg =>
                {
                    cfg.Verify.RestrictedAccess!.ChannelId = channelId;
                    cfg.Verify.RestrictedAccess!.MessageId = messageId;
                    cfg.Verify.RestrictedAccess!.CurrentRestrictionLevel = restrictionLevel;
                },
                changedBy,
                ct);
        }

        public async Task<GuildConfig> GetAsync(ulong guildId, CancellationToken ct = default)
        {
            if (_cache.TryGetValue(CacheKey(guildId), out GuildConfig cached))
                return cached;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var guild = await db.Guilds.Include(g => g.Settings)
                .SingleOrDefaultAsync(g => g.GuildId == guildId, ct);

            if (guild is null)
            {
                guild = new Guild { GuildId = guildId };
                db.Guilds.Add(guild);
            }

            if (guild.Settings is null)
            {
                var seed = Clone(_defaults.Value);
                EnsureDefaults(seed);
                seed.SchemaVersion = Math.Max(seed.SchemaVersion, CurrentSchemaVersion);
                guild.Settings = new GuildSettings
                {
                    GuildId = guildId,
                    SchemaVersion = seed.SchemaVersion,
                    Json = Json.Serialize(seed)
                };
                await db.SaveChangesAsync(ct);
                _cache.Set(CacheKey(guildId), seed, _cacheOptions);
                return seed;
            }

            var cfg = Json.Deserialize<GuildConfig>(guild.Settings.Json) ?? new GuildConfig();
            EnsureDefaults(cfg);

            // --- Auto-migrate if needed ---
            if (cfg.SchemaVersion < CurrentSchemaVersion)
            {
                MigrateToCurrent(cfg);
                guild.Settings.SchemaVersion = cfg.SchemaVersion;
                guild.Settings.Json = Json.Serialize(cfg);

                db.GuildSettingsHistories.Add(new GuildSettingsHistory
                {
                    GuildId = guildId,
                    Json = guild.Settings.Json,
                    ChangedBy = "auto-migrate",
                    ChangedAtUtc = DateTime.UtcNow
                });

                await db.SaveChangesAsync(ct);
            }

            _cache.Set(CacheKey(guildId), cfg, _cacheOptions);
            return cfg;
        }

        public async Task UpdateAsync(ulong guildId, GuildConfig next, string? changedBy = null, CancellationToken ct = default)
        {
            // Persist next with optimistic concurrency via RowVersion
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var settings = await db.GuildSettings
                .AsTracking()
                .SingleOrDefaultAsync(s => s.GuildId == guildId, ct);

            if (settings is null)
            {
                // create guild + settings if missing
                db.Guilds.Add(new Guild { GuildId = guildId });
                settings = new GuildSettings { GuildId = guildId };
                db.GuildSettings.Add(settings);
            }

            // Optional: Validate 'next' here (FluentValidation)

            settings.SchemaVersion = next.SchemaVersion;
            settings.Json = Json.Serialize(next);
            // RowVersion is handled by EF

            // Audit (optional, but cheap and useful)
            db.GuildSettingsHistories.Add(new GuildSettingsHistory
            {
                GuildId = guildId,
                Json = settings.Json,
                ChangedBy = changedBy,
                ChangedAtUtc = DateTime.UtcNow
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                // Another instance updated concurrently; bubble a friendly error
                throw new InvalidOperationException("Configuration was updated by someone else. Please retry.", ex);
            }

            Invalidate(guildId);
            _cache.Set(CacheKey(guildId), Clone(next), _cacheOptions);
        }

        public void Invalidate(ulong guildId) => _cache.Remove(CacheKey(guildId));

        private static GuildConfig Clone(GuildConfig cfg)
            => Json.Deserialize<GuildConfig>(Json.Serialize(cfg))!;

        public async Task<string?> GetGuildNameAsync(ulong guildId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var name = await db.Guilds
                .Where(g => g.GuildId == guildId)
                .Select(g => g.Name)
                .FirstOrDefaultAsync(ct);

            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        public async Task<string?> GetGuildTagAsync(ulong guildId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var tag = await db.Guilds
                .Where(g => g.GuildId == guildId)
                .Select(g => g.Tag)
                .FirstOrDefaultAsync(ct);

            return string.IsNullOrWhiteSpace(tag) ? null : tag;
        }

        public async Task<string?> GetGuildDisplayNameAsync(ulong guildId, CancellationToken ct = default)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var guild = await db.Guilds
                .Where(g => g.GuildId == guildId)
                .Select(g => new { g.Tag, g.Name })
                .FirstOrDefaultAsync(ct);

            if (guild == null)
                return null;

            if (!string.IsNullOrWhiteSpace(guild.Tag))
                return guild.Tag;

            if (!string.IsNullOrWhiteSpace(guild.Name))
                return guild.Name;

            return null;
        }
    }
}
