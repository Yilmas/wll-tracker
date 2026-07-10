using Dockhound.Logs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System;
using System.Collections.Generic;

namespace Dockhound.Models;

public partial class DockhoundContext : DbContext
{
    public DbSet<LogEvent> LogEvents { get; set; } = null!;
    public DbSet<LogError> LogErrors { get; set; } = null!;

    public DbSet<Guild> Guilds => Set<Guild>();
    public DbSet<GuildSettings> GuildSettings => Set<GuildSettings>();
    public DbSet<GuildSettingsHistory> GuildSettingsHistories => Set<GuildSettingsHistory>();

    public DbSet<VerificationRecord> VerificationRecords => Set<VerificationRecord>();

    public DbSet<Whiteboard> Whiteboards => Set<Whiteboard>();
    public DbSet<WhiteboardRole> WhiteboardRoles => Set<WhiteboardRole>();
    public DbSet<WhiteboardVersion> WhiteboardVersions => Set<WhiteboardVersion>();
    public DbSet<War> Wars => Set<War>();

    public DockhoundContext() {}

    public DockhoundContext(DbContextOptions<DockhoundContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        OnModelCreatingPartial(modelBuilder);

        modelBuilder.Entity<Guild>(e =>
        {
            e.HasKey(x => x.GuildId);
            e.Property(x => x.GuildId).ValueGeneratedNever();
            e.Property(x => x.Tag).HasMaxLength(12);
            // Unique filtered index on Tag (SQL Server)
            e.HasIndex(x => x.Tag)
             .IsUnique()
             .HasFilter("[Tag] IS NOT NULL");
            e.HasOne(x => x.Settings)
             .WithOne(x => x.Guild)
             .HasForeignKey<GuildSettings>(x => x.GuildId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuildSettings>(e =>
        {
            e.HasKey(x => x.GuildId);
            e.Property(x => x.SchemaVersion).IsRequired();
            e.Property(x => x.Json).IsRequired().HasColumnType("nvarchar(max)");
            e.Property(x => x.RowVersion)
             .IsRowVersion()
             .IsConcurrencyToken();
        });

        modelBuilder.Entity<GuildSettingsHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Json).IsRequired().HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.GuildId);
        });

        modelBuilder.Entity<VerificationRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.ToTable("VerificationRecords");
            e.Property(x => x.Faction).HasMaxLength(32).IsRequired();
            e.Property(x => x.ImageUrl).HasColumnType("nvarchar(max)");
            e.Property(x => x.WarNumber).HasMaxLength(16);
            e.HasIndex(x => new { x.UserId, x.ApprovedAtUtc });
            e.HasIndex(x => new { x.GuildId, x.ApprovedAtUtc });
        });

        // Converters
        var ulongToDecimal = new ValueConverter<ulong, decimal>(v => v, v => (ulong)v);

        modelBuilder.Entity<Whiteboard>(b =>
        {
            b.ToTable("Whiteboards");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();

            // Discord snowflakes as decimal(20,0)
            b.Property(x => x.GuildId).HasConversion(ulongToDecimal).HasColumnType("decimal(20,0)");
            b.Property(x => x.ChannelId).HasConversion(ulongToDecimal).HasColumnType("decimal(20,0)");
            b.Property(x => x.MessageId).HasConversion(ulongToDecimal).HasColumnType("decimal(20,0)");
            b.Property(x => x.CreatedById).HasConversion(ulongToDecimal).HasColumnType("decimal(20,0)");

            b.Property(x => x.Title).HasMaxLength(200).IsRequired();

            // enum -> tinyint
            b.Property(x => x.Mode).HasConversion<byte>().HasColumnType("tinyint").IsRequired();

            b.Property(x => x.CreatedUtc).IsRequired();
            b.Property(x => x.IsArchived).HasDefaultValue(false);
            b.Property(x => x.RowVersion).IsRowVersion();

            b.HasMany(x => x.Roles)
             .WithOne(r => r.Whiteboard)
             .HasForeignKey(r => r.WhiteboardId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.Versions)
             .WithOne(v => v.Whiteboard)
             .HasForeignKey(v => v.WhiteboardId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WhiteboardRole>(b =>
        {
            b.ToTable("WhiteboardRoles");
            b.HasKey(x => new { x.WhiteboardId, x.RoleId });
            b.Property(x => x.RoleId).HasConversion(ulongToDecimal).HasColumnType("decimal(20,0)");
        });

        modelBuilder.Entity<WhiteboardVersion>(b =>
        {
            b.ToTable("WhiteboardVersions");
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();

            b.Property(x => x.WhiteboardId).IsRequired();
            b.Property(x => x.VersionIndex).IsRequired();
            b.HasIndex(x => new { x.WhiteboardId, x.VersionIndex }).IsUnique();

            b.Property(x => x.EditorId).HasConversion(ulongToDecimal).HasColumnType("decimal(20,0)");
            b.Property(x => x.EditedUtc).IsRequired();

            b.Property(x => x.Content).IsRequired();

            b.Property(x => x.PrevLength).IsRequired();
            b.Property(x => x.NewLength).IsRequired();
            b.Property(x => x.EditDistance).IsRequired();
            b.Property(x => x.PercentChanged).HasColumnType("decimal(5,2)").IsRequired();
        });
        modelBuilder.Entity<War>(b =>
        {
            b.ToTable("Wars");
            b.HasKey(x => x.WarId);
            b.Property(x => x.WarId).HasMaxLength(64).ValueGeneratedNever();
            b.Property(x => x.Winner).HasMaxLength(16).IsRequired();
            b.Property(x => x.EntityTag).HasMaxLength(256);
            b.Property(x => x.RefreshAfterUtc).IsRequired();
            b.HasIndex(x => x.ConquestStartTime);
        });
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
