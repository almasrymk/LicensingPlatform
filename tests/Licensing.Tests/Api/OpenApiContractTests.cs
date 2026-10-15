using System.Text.Json;
using System.Text.Json.Nodes;
using Licensing.Tests.Infrastructure;

namespace Licensing.Tests.Api;

/// <summary>
/// The published API contract (paths, operations, schemas) is pinned in Contracts/openapi.v1.json, so an
/// accidental change to a route or a DTO fails the build. After an intended change, regenerate the snapshot with
/// UPDATE_OPENAPI_SNAPSHOT=1 dotnet test --filter OpenApiContractTests and review the diff.
/// </summary>
public sealed class OpenApiContractTests : IClassFixture<TestAppFixture>
{
    private static readonly string SnapshotPath = Path.Combine(FindTestsDir(), "Contracts", "openapi.v1.json");
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly TestApp _app;
    public OpenApiContractTests(TestAppFixture f) => _app = f.App;

    [Fact]
    public async Task OpenApi_document_matches_the_committed_snapshot()
    {
        var response = await _app.Anonymous().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        var current = Normalize(await response.Content.ReadAsStringAsync());

        if (Environment.GetEnvironmentVariable("UPDATE_OPENAPI_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            await File.WriteAllTextAsync(SnapshotPath, current);
            return;
        }

        Assert.True(File.Exists(SnapshotPath), $"Missing snapshot {SnapshotPath}; create it with UPDATE_OPENAPI_SNAPSHOT=1.");
        var expected = Normalize(await File.ReadAllTextAsync(SnapshotPath));
        Assert.True(expected == current,
            "The OpenAPI contract changed. If intended, regenerate the snapshot with UPDATE_OPENAPI_SNAPSHOT=1 and review the diff.");
    }

    /// <summary>Drops the server list (it carries the test host's address) and re-serializes with stable formatting.</summary>
    private static string Normalize(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("servers");
        return node.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
    }

    private static string FindTestsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Licensing.Tests.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Licensing.Tests project folder not found.");
    }
}
