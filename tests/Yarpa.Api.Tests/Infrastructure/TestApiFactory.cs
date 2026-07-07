using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarpa.Api.Data;
using Yarpa.Api.Data.Entities;

namespace Yarpa.Api.Tests.Infrastructure;

/// <summary>
/// WebApplicationFactory configured for integration tests.
/// Replaces SQL Server with an in-memory database and seeds a known test customer + API key.
/// The "Testing" environment is set so the startup migration code is skipped.
/// </summary>
public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Test API key (plain text) that matches the seeded key hash.</summary>
    public const string ValidApiKey = "test-api-key-for-integration";

    public static readonly Guid TestCustomerId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // Unique DB name per factory instance so parallel test runs don't share state
    private readonly string _dbName = $"YarpaTest_{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Remove the real SQL Server DbContext options
            ServiceDescriptor? descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<YarpaDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Replace with an in-memory database
            services.AddDbContext<YarpaDbContext>(options =>
                options.UseInMemoryDatabase(_dbName));
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host = base.CreateHost(builder);

        // Seed test data after the host is built and services are fully initialised
        using IServiceScope scope = host.Services.CreateScope();
        YarpaDbContext db = scope.ServiceProvider.GetRequiredService<YarpaDbContext>();
        db.Database.EnsureCreated();
        SeedTestData(db);

        return host;
    }

    private static void SeedTestData(YarpaDbContext db)
    {
        // Idempotent: a derived host (WithWebHostBuilder) shares the same in-memory
        // database, so re-seeding must not add duplicate keys.
        if (db.Customers.Any(c => c.CustomerId == TestCustomerId))
            return;

        db.Customers.Add(new CustomerEntity
        {
            CustomerId = TestCustomerId,
            Name = "Test Customer",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        db.ApiKeys.Add(new ApiKeyEntity
        {
            ApiKeyId = Guid.NewGuid(),
            CustomerId = TestCustomerId,
            KeyHash = YarpaDbContext.ComputeKeyHash(ValidApiKey),
            IsActive = true,
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        db.SaveChanges();
    }
}
