using System.Text.Json;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Persistence;

public sealed record SceneDocument(
    int SchemaVersion,
    Guid SceneId,
    string Name,
    SimulationSettings Simulation,
    IReadOnlyList<ComponentDocument> Components,
    IReadOnlyList<TagDefinition> Tags,
    IReadOnlyList<ConnectionDocument> Connections);

public sealed record SimulationSettings(
    int LogicStepMilliseconds,
    int MaxCatchUpSteps,
    Vector3Document Gravity);

public sealed record ComponentDocument(
    Guid Id,
    string Type,
    TransformDocument Transform,
    IReadOnlyDictionary<string, JsonElement> Parameters);

public sealed record TransformDocument(
    Vector3Document Position,
    Vector3Document RotationDegrees);

public sealed record Vector3Document(double X, double Y, double Z);

public sealed record ConnectionDocument(
    Guid Id,
    string Name,
    string Driver,
    string Role,
    string Host,
    int Port,
    int UnitId);
