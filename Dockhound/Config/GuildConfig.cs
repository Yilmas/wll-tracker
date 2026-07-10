using Dockhound.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dockhound.Config
{
    public sealed class GuildConfig
    {
        public int SchemaVersion { get; set; } = 1;
        public string? GuildLogoColonial { get; set; }
        public string? GuildLogoWarden { get; set; }
        public VerificationSettings Verify { get; set; } = new ();
        public List<RoleSet> Roles { get; set; } = new();
        public HoneypotSettings Honeypot { get; set; } = new();

        public sealed class  VerificationSettings
        {
            public string ImageUrl { get; set; }
            public ulong? ReviewChannelId { get; set; }
            public ulong? NotificationChannelId { get; set; }
            public ulong? ColonialSecureChannelId { get; set; }
            public ulong? WardenSecureChannelId { get; set; }
            public List<ulong>? RecruitAssignerRoles { get; set; }
            public List<ulong>? AllyAssignerRoles { get; set; }
            public List<ulong>? TrustedRoles { get; set; }
            public RestrictedAccessSettings RestrictedAccess { get; set; }
            public bool IsSteamRequired { get; set; } = false;
        }

        public sealed class RestrictedAccessSettings
        {
            public List<ulong>? AlwaysRestrictRoles { get; set; }
            public List<ulong>? MemberOnlyRoles { get; set; }
            public ulong? ChannelId { get; set; }
            public ulong? MessageId { get; set; }
            public AccessRestriction CurrentRestrictionLevel { get; set; } = AccessRestriction.Open;
        }

        public sealed class HoneypotSettings
        {
            public bool Enabled { get; set; } = false;
            public bool MessagesEnabled { get; set; } = true;
            public bool ReactionsEnabled { get; set; } = true;
            public ulong? ChannelId { get; set; }
            public ulong? ReactionChannelId { get; set; }
            public ulong? ReactionMessageId { get; set; }
            public ulong? ReportChannelId { get; set; }
            public int MessagePruneDays { get; set; } = 0;
        }

        public sealed class RoleSet
        {
            public string Name { get; set; } = string.Empty;
            public ulong? Colonial { get; set; }
            public ulong? Warden { get; set; }
            public ulong? Generic { get; set; }
        }
    }
}
