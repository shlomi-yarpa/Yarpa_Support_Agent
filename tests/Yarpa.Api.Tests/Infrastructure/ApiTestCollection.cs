namespace Yarpa.Api.Tests.Infrastructure;

/// <summary>
/// Groups all API integration tests into one xUnit collection so that a single
/// <see cref="TestApiFactory"/> instance is shared. This prevents the
/// <c>HostFactoryResolver</c> from being invoked concurrently by multiple fixtures,
/// which would cause "The entry point exited without ever building an IHost".
/// </summary>
[CollectionDefinition("API Integration Tests")]
public sealed class ApiTestCollection : ICollectionFixture<TestApiFactory>
{
    // This class is intentionally empty.
    // ICollectionFixture<TestApiFactory> registers the shared fixture.
}
