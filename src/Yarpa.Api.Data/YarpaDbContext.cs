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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── CustomerEntity ───────────────────────────────────────────────────
        modelBuilder.Entity<CustomerEntity>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(c => c.CustomerId);
            e.Property(c => c.Name).IsRequired().HasMaxLength(200);
            e.Property(c => c.CreatedAtUtc).IsRequired();
        });

        // ── ApiKeyEntity ─────────────────────────────────────────────────────
        modelBuilder.Entity<ApiKeyEntity>(e =>
        {
            e.ToTable("ApiKeys");
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
            e.ToTable("Machines");
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
            e.ToTable("Snapshots");
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
