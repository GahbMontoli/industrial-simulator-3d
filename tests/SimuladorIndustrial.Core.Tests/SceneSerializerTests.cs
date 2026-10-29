using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.Core.Tests;

public sealed class SceneSerializerTests
{
    [Fact]
    public void LoadsAndValidatesTheShippedFillingCellSample()
    {
        var scene = SceneSerializer.Deserialize(File.ReadAllText(SamplePath));

        Assert.Equal(SceneValidator.CurrentSchemaVersion, scene.SchemaVersion);
        Assert.Equal("Célula de Envase 01", scene.Name);
        Assert.Equal(12, scene.Components.Count);
        Assert.Contains(scene.Components, component => component.Type == "process.tank.v1");
        Assert.Contains(scene.Components, component => component.Type == "sink.remover.v1");
        Assert.Contains(scene.Components, component => component.Type == "indicator.display.v1");
        Assert.Equal(4, scene.Tags.Count);
        Assert.Empty(SceneValidator.Validate(scene));
    }

    [Fact]
    public void RoundTripPreservesTheSceneContract()
    {
        var original = SceneSerializer.Deserialize(File.ReadAllText(SamplePath));

        var serialized = SceneSerializer.Serialize(original);
        var restored = SceneSerializer.Deserialize(serialized);

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.SceneId, restored.SceneId);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.Simulation, restored.Simulation);
        Assert.Equal(
            original.Components.Select(component => component.Id),
            restored.Components.Select(component => component.Id));
        Assert.Equal(original.Tags, restored.Tags);
        Assert.Equal(original.Connections, restored.Connections);
    }

    [Fact]
    public void RejectsScenesFromAnUnsupportedFutureSchema()
    {
        var json = File.ReadAllText(SamplePath)
            .Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);

        var exception = Assert.Throws<SceneValidationException>(
            () => SceneSerializer.Deserialize(json));

        Assert.Contains(
            $"schemaVersion deve ser {SceneValidator.CurrentSchemaVersion}",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MigratesThePreReleaseSceneShapeToVersionOne()
    {
        var sceneId = Guid.NewGuid();
        var legacyJson = $$"""
            {
              "sceneId": "{{sceneId}}",
              "name": "Cena legada",
              "simulation": {
                "logicStepMilliseconds": 20,
                "gravity": { "x": 0, "y": -9.81, "z": 0 }
              },
              "components": [],
              "tags": []
            }
            """;

        var scene = SceneSerializer.Deserialize(legacyJson);

        Assert.Equal(SceneValidator.CurrentSchemaVersion, scene.SchemaVersion);
        Assert.Equal(10, scene.Simulation.MaxCatchUpSteps);
        Assert.Empty(scene.Connections);
    }

    [Fact]
    public void RejectsSceneTextLargerThanTheConfiguredLimit()
    {
        var oversized = new string('x', SceneSerializer.MaximumSceneBytes + 1);

        var exception = Assert.Throws<SceneValidationException>(
            () => SceneSerializer.Deserialize(oversized));

        Assert.Contains("excede o limite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsInvalidUtf8FromAStream()
    {
        await using var stream = new MemoryStream([0xC3, 0x28]);

        var exception = await Assert.ThrowsAsync<SceneValidationException>(
            async () => await SceneSerializer.DeserializeAsync(stream));

        Assert.Contains("UTF-8 inválido", exception.Message, StringComparison.Ordinal);
    }

    private static string SamplePath => Path.Combine(
        AppContext.BaseDirectory,
        "samples",
        "celula-envase.scene.json");
}
