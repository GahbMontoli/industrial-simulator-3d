using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.Core.Tests;

public sealed class SceneSecurityTests
{
    [Theory]
    [InlineData("script", "return 1")]
    [InlineData("payload", "../../outside")]
    [InlineData("token", "not-a-real-secret")]
    [InlineData("connectionString", "Server=example")]
    public void RejectsExecutableExternalAndSecretParameters(string name, string value)
    {
        var json = File.ReadAllText(SamplePath)
            .Replace(
                "\"intervalSeconds\": 4.0",
                $"\"intervalSeconds\": 4.0, \"{name}\": \"{value}\"",
                StringComparison.Ordinal);

        var exception = Assert.Throws<SceneValidationException>(
            () => SceneSerializer.Deserialize(json));

        Assert.Contains("não", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsUrlShapedConnectionHosts()
    {
        var json = File.ReadAllText(SamplePath)
            .Replace("\"host\": \"127.0.0.1\"", "\"host\": \"tcp://127.0.0.1\"", StringComparison.Ordinal);

        var exception = Assert.Throws<SceneValidationException>(
            () => SceneSerializer.Deserialize(json));

        Assert.Contains("host", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string SamplePath => Path.Combine(
        AppContext.BaseDirectory,
        "samples",
        "celula-envase.scene.json");
}
