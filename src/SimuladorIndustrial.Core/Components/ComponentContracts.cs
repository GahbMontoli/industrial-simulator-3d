using System.Text.Json;
using SimuladorIndustrial.Core.Persistence;

namespace SimuladorIndustrial.Core.Components;

public sealed record ComponentPackageManifest(
    int SchemaVersion,
    string PackageId,
    string Version,
    string DisplayName,
    string Author,
    string Description,
    string License,
    string MinimumHostVersion,
    IReadOnlyList<ComponentDefinition> Components);

public sealed record ComponentDefinition(
    string Type,
    string DisplayName,
    string Category,
    string Description,
    ComponentVisualDefinition Visual,
    IReadOnlyList<ComponentPortDefinition> Ports,
    IReadOnlyList<ComponentParameterDefinition> Parameters);

public sealed record ComponentVisualDefinition(
    ComponentVisualKind Kind,
    Vector3Document Size,
    string Color,
    string? ModelAsset = null);

public sealed record ComponentPortDefinition(
    string Name,
    string DisplayName,
    ComponentPortDirection Direction,
    ComponentPortDataType DataType);

public sealed record ComponentParameterDefinition(
    string Name,
    string DisplayName,
    ComponentParameterType DataType,
    bool Required,
    JsonElement DefaultValue,
    double? Minimum = null,
    double? Maximum = null);

public enum ComponentVisualKind
{
    Box,
    Cylinder,
    Sphere,
    Model
}

public enum ComponentPortDirection
{
    Input,
    Output,
    Bidirectional
}

public enum ComponentPortDataType
{
    Bool,
    Int32,
    Float32
}

public enum ComponentParameterType
{
    Bool,
    Int32,
    Float32,
    String
}

public sealed record ComponentRegistration(
    ComponentDefinition Definition,
    string PackageId,
    string PackageVersion,
    string PackageDirectory,
    bool IsBuiltIn);

public sealed record ComponentCatalogLoadResult(
    ComponentCatalog Catalog,
    IReadOnlyList<string> Diagnostics);

public sealed record ComponentPackageInstallation(
    ComponentPackageManifest Manifest,
    string InstallationDirectory,
    string PackageSha256);
