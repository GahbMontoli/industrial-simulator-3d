using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimuladorIndustrial.Core.Persistence;

/// <summary>
/// Applies explicit, forward-only migrations to the public scene contract.
/// Version 0 was the pre-release shape: schemaVersion and connections could be absent,
/// and maxCatchUpSteps was optional.
/// </summary>
public static class SceneMigrator
{
    public const int LegacySchemaVersion = 0;

    public static string MigrateToCurrent(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var root = JsonNode.Parse(
                json,
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions { MaxDepth = 32 })
            as JsonObject
            ?? throw new SceneValidationException(["O JSON raiz deve ser um objeto."]);

        var version = ReadSchemaVersion(root);
        if (version > SceneValidator.CurrentSchemaVersion)
        {
            throw new SceneValidationException(
                [$"schemaVersion deve ser {SceneValidator.CurrentSchemaVersion}, mas recebeu {version}."]);
        }

        while (version < SceneValidator.CurrentSchemaVersion)
        {
            version = version switch
            {
                LegacySchemaVersion => MigrateVersionZeroToOne(root),
                _ => throw new SceneValidationException([$"Não existe migração para schemaVersion {version}."])
            };
        }

        return root.ToJsonString();
    }

    private static int ReadSchemaVersion(JsonObject root)
    {
        if (root["schemaVersion"] is null)
        {
            return LegacySchemaVersion;
        }

        try
        {
            return root["schemaVersion"]!.GetValue<int>();
        }
        catch (InvalidOperationException)
        {
            throw new SceneValidationException(["schemaVersion deve ser um número inteiro."]);
        }
    }

    private static int MigrateVersionZeroToOne(JsonObject root)
    {
        root["schemaVersion"] = 1;
        root["connections"] ??= new JsonArray();

        if (root["simulation"] is JsonObject simulation)
        {
            simulation["maxCatchUpSteps"] ??= 10;
        }

        return 1;
    }
}
