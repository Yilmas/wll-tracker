using Dockhound.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dockhound.Models
{
    public sealed class VerificationRecord
    {
        public long Id { get; set; }
        public ulong GuildId { get; set; }
        public ulong UserId { get; set; }
        public Faction Faction { get; set; }
        public string? ImageUrl { get; set; }
        public ulong? ApprovedByUserId { get; set; }
        public DateTime ApprovedAtUtc { get; set; } = DateTime.UtcNow;
        public ulong? Steam64Id { get; set; }
        public string? WarNumber { get; set; }
    }
}
