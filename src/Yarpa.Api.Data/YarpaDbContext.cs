using Microsoft.EntityFrameworkCore;

namespace Yarpa.Api.Data;

/// <summary>
/// EF Core database context for the Yarpa Support Agent server.
/// Stage 0 provides an empty skeleton so the solution compiles and DI is wired.
/// Entities (Customers, Machines, Snapshots, ...) and migrations are added in stage 1.
/// </summary>
public class YarpaDbContext : DbContext
{
    public YarpaDbContext(DbContextOptions<YarpaDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity configurations are registered here in stage 1.
    }
}
