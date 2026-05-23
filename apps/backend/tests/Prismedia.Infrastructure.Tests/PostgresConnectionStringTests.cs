using Prismedia.Infrastructure.Database;

namespace Prismedia.Infrastructure.Tests;

public sealed class PostgresConnectionStringTests {
    [Fact]
    public void NormalizeKeepsNpgsqlConnectionStrings() {
        const string input = "Host=postgres;Port=5432;Database=prismedia;Username=prismedia;Password=prismedia";

        var normalized = PostgresConnectionString.Normalize(input);

        Assert.Equal(input, normalized);
    }

    [Fact]
    public void NormalizeConvertsDockerDatabaseUrlsToNpgsqlConnectionStrings() {
        var normalized = PostgresConnectionString.Normalize(
            "postgresql://prismedia:secret@postgres:5432/prismedia");

        Assert.Equal(
            "Host=postgres;Port=5432;Database=prismedia;Username=prismedia;Password=secret",
            normalized);
    }
}
