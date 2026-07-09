using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Data;

/// <summary>
/// EF Core database context for the Yarpa Support Agent server.
/// All tables are append-only; snapshots are never deleted or overwritten.
/// </summary>
public class YarpaDbContext : DbContext
{
    // ── Well-known dev GUIDs (used in seed data only) ────────────────────────
    private static readonly Guid DevCustomerId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly Guid DevApiKeyId =
        Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// Development API key (plain text). The DB stores only its SHA-256 hash.
    /// Use this value in the Agent's appsettings.json when running locally.
    /// Never commit real customer keys.
    /// </summary>
    public const string DevApiKeyPlainText = "dev-yarpa-api-key-2026";

    public YarpaDbContext(DbContextOptions<YarpaDbContext> options)
        : base(options)
    {
    }

    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();
    public DbSet<MachineEntity> Machines => Set<MachineEntity>();
    public DbSet<SnapshotEntity> Snapshots => Set<SnapshotEntity>();
    public DbSet<ChangeEntity> Changes => Set<ChangeEntity>();
    public DbSet<AlertEntity> Alerts => Set<AlertEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── CustomerEntity ───────────────────────────────────────────────────
        modelBuilder.Entity<CustomerEntity>(e =>
        {
            e.ToTable("YarpaAgent_Customers");
            e.HasKey(c => c.CustomerId);
            e.Property(c => c.Name).IsRequired().HasMaxLength(200);
            e.Property(c => c.CreatedAtUtc).IsRequired();
        });

        // ── ApiKeyEntity ─────────────────────────────────────────────────────
        modelBuilder.Entity<ApiKeyEntity>(e =>
        {
            e.ToTable("YarpaAgent_ApiKeys");
            e.HasKey(k => k.ApiKeyId);
            e.Property(k => k.KeyHash).IsRequired().HasMaxLength(64);
            e.HasIndex(k => k.KeyHash); // fast lookup per incoming request
            e.Property(k => k.IsActive).IsRequired();
            e.Property(k => k.CreatedAtUtc).IsRequired();

            e.HasOne(k => k.Customer)
             .WithMany(c => c.ApiKeys)
             .HasForeignKey(k => k.CustomerId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── MachineEntity ────────────────────────────────────────────────────
        modelBuilder.Entity<MachineEntity>(e =>
        {
            e.ToTable("YarpaAgent_Machines");
            e.HasKey(m => m.MachineId);
            e.Property(m => m.MachineId).HasMaxLength(128).IsRequired();
            e.Property(m => m.ComputerName).HasMaxLength(200);
            e.Property(m => m.FirstSeenUtc).IsRequired();
            e.Property(m => m.LastSeenUtc).IsRequired();

            e.HasOne(m => m.Customer)
             .WithMany(c => c.Machines)
             .HasForeignKey(m => m.CustomerId)
             .OnDelete(DeleteBehavior.Restrict);

            // LastSnapshotId is a non-FK nullable Guid (FK would create a circular dependency)
            e.Property(m => m.LastSnapshotId).IsRequired(false);
        });

        // ── SnapshotEntity ───────────────────────────────────────────────────
        modelBuilder.Entity<SnapshotEntity>(e =>
        {
            e.ToTable("YarpaAgent_Snapshots");
            e.HasKey(s => s.SnapshotId);
            e.Property(s => s.MachineId).HasMaxLength(128).IsRequired();
            e.Property(s => s.AgentVersion).HasMaxLength(50);
            e.Property(s => s.SchemaVersion).HasMaxLength(20);
            e.Property(s => s.RawJson).IsRequired().HasColumnType("nvarchar(max)");
            e.Property(s => s.OsCaption).HasMaxLength(200);
            e.Property(s => s.OsBuild).HasMaxLength(20);
            e.Property(s => s.YarpaVersion).HasMaxLength(100);
            e.Property(s => s.CollectedAtUtc).IsRequired();
            e.Property(s => s.ReceivedAtUtc).IsRequired();

            e.HasIndex(s => new { s.MachineId, s.CollectedAtUtc });

            e.HasOne(s => s.Machine)
             .WithMany(m => m.Snapshots)
             .HasForeignKey(s => s.MachineId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── ChangeEntity ─────────────────────────────────────────────────────
        modelBuilder.Entity<ChangeEntity>(e =>
        {
            e.ToTable("YarpaAgent_Changes");
            e.HasKey(c => c.ChangeId);
            e.Property(c => c.MachineId).HasMaxLength(128).IsRequired();
            e.Property(c => c.ChangeType).HasMaxLength(64).IsRequired();
            e.Property(c => c.SectionName).HasMaxLength(64).IsRequired();
            e.Property(c => c.OldValue).HasColumnType("nvarchar(max)");
            e.Property(c => c.NewValue).HasColumnType("nvarchar(max)");
            e.Property(c => c.DetectedAtUtc).IsRequired();

            e.HasIndex(c => new { c.MachineId, c.DetectedAtUtc });

            e.HasOne(c => c.Machine)
             .WithMany(m => m.Changes)
             .HasForeignKey(c => c.MachineId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.Snapshot)
             .WithMany(s => s.Changes)
             .HasForeignKey(c => c.SnapshotId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── AlertEntity ──────────────────────────────────────────────────────
        modelBuilder.Entity<AlertEntity>(e =>
        {
            e.ToTable("YarpaAgent_Alerts");
            e.HasKey(a => a.AlertId);
            e.Property(a => a.MachineId).HasMaxLength(128).IsRequired();
            e.Property(a => a.AlertType).HasMaxLength(64).IsRequired();
            e.Property(a => a.Severity).HasMaxLength(20).IsRequired();
            e.Property(a => a.Message).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(a => a.State).HasMaxLength(20).IsRequired();
            e.Property(a => a.CreatedAtUtc).IsRequired();
            e.Property(a => a.ResolvedAtUtc).IsRequired(false);
            e.Property(a => a.SourceSnapshotId).IsRequired(false);
            e.Property(a => a.SourceChangeId).IsRequired(false);

            // Fast lookup of open alerts per machine/type (dedup + resolution reconciliation)
            e.HasIndex(a => new { a.MachineId, a.AlertType, a.State });

            e.HasOne(a => a.Machine)
             .WithMany(m => m.Alerts)
             .HasForeignKey(a => a.MachineId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.SourceSnapshot)
             .WithMany()
             .HasForeignKey(a => a.SourceSnapshotId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.SourceChange)
             .WithMany()
             .HasForeignKey(a => a.SourceChangeId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Dev seed data ────────────────────────────────────────────────────
        // One customer + one API key for local development. The plain-text key is
        // DevApiKeyPlainText; only its SHA-256 hash is stored.
        string devKeyHash = ComputeKeyHash(DevApiKeyPlainText);

        modelBuilder.Entity<CustomerEntity>().HasData(new CustomerEntity
        {
            CustomerId = DevCustomerId,
            Name = "Yarpa Dev",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        modelBuilder.Entity<ApiKeyEntity>().HasData(new ApiKeyEntity
        {
            ApiKeyId = DevApiKeyId,
            CustomerId = DevCustomerId,
            KeyHash = devKeyHash,
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            RevokedAtUtc = null
        });
    }

    /// <summary>
    /// Computes the lowercase-hex SHA-256 hash of an API key.
    /// This is the canonical hash stored in the ApiKeys table.
    /// </summary>
    public static string ComputeKeyHash(string apiKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
