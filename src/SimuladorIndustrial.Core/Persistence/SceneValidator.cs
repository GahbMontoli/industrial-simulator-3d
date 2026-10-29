using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Persistence;

public static partial class SceneValidator
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumComponents = 10_000;
    public const int MaximumTags = 20_000;
    public const int MaximumConnections = 64;

    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> AllowedDrivers = new(NameComparer) { "modbusTcp" };
    private static readonly HashSet<string> AllowedRoles = new(NameComparer) { "client", "server" };
    private static readonly HashSet<string> ForbiddenParameterNames = new(NameComparer)
    {
        "script",
        "scriptPath",
        "file",
        "filePath",
        "path",
        "uri",
        "url",
        "password",
        "passwd",
        "secret",
        "token",
        "apiKey",
        "connectionString",
        "privateKey"
    };

    public static IReadOnlyList<string> Validate(SceneDocument? scene)
    {
        var errors = new List<string>();
        if (scene is null)
        {
            errors.Add("O documento da cena não pode ser nulo.");
            return errors;
        }

        if (scene.SchemaVersion != CurrentSchemaVersion)
        {
            errors.Add($"schemaVersion deve ser {CurrentSchemaVersion}, mas recebeu {scene.SchemaVersion}.");
        }

        if (scene.SceneId == Guid.Empty)
        {
            errors.Add("sceneId deve ser um UUID não vazio.");
        }

        ValidateName(scene.Name, "name", errors, 120);
        ValidateSimulation(scene.Simulation, errors);

        if (scene.Components is null)
        {
            errors.Add("components é obrigatório.");
        }

        if (scene.Tags is null)
        {
            errors.Add("tags é obrigatório.");
        }

        if (scene.Connections is null)
        {
            errors.Add("connections é obrigatório.");
        }

        var components = scene.Components ?? Array.Empty<ComponentDocument>();
        var tags = scene.Tags ?? Array.Empty<TagDefinition>();
        var connections = scene.Connections ?? Array.Empty<ConnectionDocument>();

        if (components.Count > MaximumComponents)
        {
            errors.Add($"A cena excede o limite de {MaximumComponents} componentes.");
        }

        if (tags.Count > MaximumTags)
        {
            errors.Add($"A cena excede o limite de {MaximumTags} tags.");
        }

        if (connections.Count > MaximumConnections)
        {
            errors.Add($"A cena excede o limite de {MaximumConnections} conexões.");
        }

        ValidateComponents(components, errors);
        ValidateTags(tags, components, errors);
        ValidateConnections(connections, errors);
        ValidateGlobalIds(scene, components, tags, connections, errors);
        return errors;
    }

    public static void ValidateAndThrow(SceneDocument? scene)
    {
        var errors = Validate(scene);
        if (errors.Count != 0)
        {
            throw new SceneValidationException(errors);
        }
    }

    private static void ValidateSimulation(SimulationSettings? simulation, ICollection<string> errors)
    {
        if (simulation is null)
        {
            errors.Add("simulation é obrigatório.");
            return;
        }

        if (simulation.LogicStepMilliseconds is < 1 or > 1_000)
        {
            errors.Add("simulation.logicStepMilliseconds deve estar entre 1 e 1000.");
        }

        if (simulation.MaxCatchUpSteps is < 1 or > 1_000)
        {
            errors.Add("simulation.maxCatchUpSteps deve estar entre 1 e 1000.");
        }

        ValidateVector(simulation.Gravity, "simulation.gravity", errors);
    }

    private static void ValidateComponents(
        IReadOnlyList<ComponentDocument> components,
        ICollection<string> errors)
    {
        var ids = new HashSet<Guid>();
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            var prefix = $"components[{index}]";
            if (component is null)
            {
                errors.Add($"{prefix} não pode ser nulo.");
                continue;
            }

            if (component.Id == Guid.Empty)
            {
                errors.Add($"{prefix}.id deve ser um UUID não vazio.");
            }
            else if (!ids.Add(component.Id))
            {
                errors.Add($"{prefix}.id está duplicado: {component.Id}.");
            }

            if (string.IsNullOrWhiteSpace(component.Type) ||
                component.Type.Length > 100 ||
                !ComponentTypePattern().IsMatch(component.Type))
            {
                errors.Add($"{prefix}.type deve usar somente letras minúsculas, números, ponto, hífen ou sublinhado.");
            }

            if (component.Transform is null)
            {
                errors.Add($"{prefix}.transform é obrigatório.");
            }
            else
            {
                ValidateVector(component.Transform.Position, $"{prefix}.transform.position", errors);
                ValidateVector(component.Transform.RotationDegrees, $"{prefix}.transform.rotationDegrees", errors);
            }

            ValidateParameters(component.Parameters, $"{prefix}.parameters", errors);
        }
    }

    private static void ValidateTags(
        IReadOnlyList<TagDefinition> tags,
        IReadOnlyList<ComponentDocument> components,
        ICollection<string> errors)
    {
        var ids = new HashSet<Guid>();
        var canonicalNames = new HashSet<string>(NameComparer);
        var addresses = new HashSet<string>(NameComparer);
        var componentIds = components
            .Where(component => component is not null && component.Id != Guid.Empty)
            .Select(component => component.Id)
            .ToHashSet();

        for (var index = 0; index < tags.Count; index++)
        {
            var tag = tags[index];
            var prefix = $"tags[{index}]";
            if (tag is null)
            {
                errors.Add($"{prefix} não pode ser nula.");
                continue;
            }

            if (tag.Id == Guid.Empty || !ids.Add(tag.Id))
            {
                errors.Add($"{prefix}.id deve ser um UUID não vazio e único.");
            }

            ValidateName(tag.Name, $"{prefix}.name", errors, 120);
            ValidateName(tag.CanonicalName, $"{prefix}.canonicalName", errors, 120);
            if (!string.IsNullOrWhiteSpace(tag.CanonicalName) &&
                !CanonicalNamePattern().IsMatch(tag.CanonicalName))
            {
                errors.Add($"{prefix}.canonicalName deve ser um identificador sem espaços.");
            }

            if (!string.IsNullOrWhiteSpace(tag.CanonicalName) && !canonicalNames.Add(tag.CanonicalName))
            {
                errors.Add($"{prefix}.canonicalName está duplicado: {tag.CanonicalName}.");
            }

            var maximumStartAddress = tag.DataType == TagDataType.Bool
                ? ushort.MaxValue
                : ushort.MaxValue - 1;

            if (tag.Address < 0 || tag.Address > maximumStartAddress)
            {
                errors.Add(
                    $"{prefix}.address deve estar entre 0 e {maximumStartAddress} para {tag.DataType}.");
            }
            else
            {
                var area = tag.DataType == TagDataType.Bool ? "bit" : "register";
                var width = tag.DataType == TagDataType.Bool ? 1 : 2;
                for (var offset = 0; offset < width; offset++)
                {
                    var occupiedAddress = tag.Address + offset;
                    var key = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{tag.Direction}:{area}:{occupiedAddress}");
                    if (!addresses.Add(key))
                    {
                        errors.Add(
                            $"{prefix}.address colide no espaço {tag.Direction}/{area}: {occupiedAddress}.");
                    }
                }
            }

            ValidateName(tag.Source, $"{prefix}.source", errors, 200);
            if (LooksLikeExternalReference(tag.Source))
            {
                errors.Add($"{prefix}.source não pode conter caminho ou URL externa.");
            }
            else
            {
                ValidateTagSource(tag.Source, componentIds, prefix, errors);
            }
        }
    }

    private static void ValidateTagSource(
        string? source,
        IReadOnlySet<Guid> componentIds,
        string prefix,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var separator = source.IndexOf('.');
        if (separator <= 0 || separator == source.Length - 1 || source.IndexOf('.', separator + 1) >= 0)
        {
            errors.Add($"{prefix}.source deve usar o formato <uuid-do-componente>.<porta>.");
            return;
        }

        if (!Guid.TryParseExact(source[..separator], "D", out var componentId) ||
            !PortNamePattern().IsMatch(source[(separator + 1)..]))
        {
            errors.Add($"{prefix}.source deve usar UUID canônico e nome de porta válido.");
            return;
        }

        if (!componentIds.Contains(componentId))
        {
            errors.Add($"{prefix}.source referencia um componente inexistente: {componentId}.");
        }
    }

    private static void ValidateConnections(
        IReadOnlyList<ConnectionDocument> connections,
        ICollection<string> errors)
    {
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(NameComparer);

        for (var index = 0; index < connections.Count; index++)
        {
            var connection = connections[index];
            var prefix = $"connections[{index}]";
            if (connection is null)
            {
                errors.Add($"{prefix} não pode ser nula.");
                continue;
            }

            if (connection.Id == Guid.Empty || !ids.Add(connection.Id))
            {
                errors.Add($"{prefix}.id deve ser um UUID não vazio e único.");
            }

            ValidateName(connection.Name, $"{prefix}.name", errors, 120);
            if (!string.IsNullOrWhiteSpace(connection.Name) && !names.Add(connection.Name))
            {
                errors.Add($"{prefix}.name está duplicado: {connection.Name}.");
            }

            if (!AllowedDrivers.Contains(connection.Driver))
            {
                errors.Add($"{prefix}.driver não é suportado: {connection.Driver}.");
            }

            if (!AllowedRoles.Contains(connection.Role))
            {
                errors.Add($"{prefix}.role deve ser client ou server.");
            }

            if (!IsSafeHost(connection.Host))
            {
                errors.Add($"{prefix}.host deve ser um host ou endereço IP simples, sem URL ou caminho.");
            }

            if (connection.Port is < 1 or > 65_535)
            {
                errors.Add($"{prefix}.port deve estar entre 1 e 65535.");
            }

            if (connection.UnitId is < 0 or > 255)
            {
                errors.Add($"{prefix}.unitId deve estar entre 0 e 255.");
            }
        }
    }

    private static void ValidateParameters(
        IReadOnlyDictionary<string, JsonElement>? parameters,
        string prefix,
        ICollection<string> errors)
    {
        if (parameters is null)
        {
            errors.Add($"{prefix} é obrigatório, mesmo quando vazio.");
            return;
        }

        if (parameters.Count > 128)
        {
            errors.Add($"{prefix} excede o limite de 128 propriedades.");
            return;
        }

        foreach (var (name, value) in parameters)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
            {
                errors.Add($"{prefix} contém um nome inválido.");
                continue;
            }

            if (ForbiddenParameterNames.Contains(name))
            {
                errors.Add($"{prefix}.{name} não é permitido em uma cena.");
            }

            ValidateParameterValue(value, $"{prefix}.{name}", errors, 0);
        }
    }

    private static void ValidateParameterValue(
        JsonElement value,
        string prefix,
        ICollection<string> errors,
        int depth)
    {
        if (depth > 8)
        {
            errors.Add($"{prefix} excede a profundidade máxima de 8.");
            return;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
                errors.Add($"{prefix} contém valor indefinido.");
                break;
            case JsonValueKind.String:
                var text = value.GetString() ?? string.Empty;
                if (text.Length > 512)
                {
                    errors.Add($"{prefix} excede 512 caracteres.");
                }
                if (LooksLikeExternalReference(text))
                {
                    errors.Add($"{prefix} não pode conter caminho ou URL externa.");
                }
                break;
            case JsonValueKind.Number:
                if (!value.TryGetDouble(out var number) || !double.IsFinite(number))
                {
                    errors.Add($"{prefix} deve conter um número finito.");
                }
                break;
            case JsonValueKind.Array:
                if (value.GetArrayLength() > 256)
                {
                    errors.Add($"{prefix} excede 256 itens.");
                    break;
                }
                var arrayIndex = 0;
                foreach (var item in value.EnumerateArray())
                {
                    ValidateParameterValue(item, $"{prefix}[{arrayIndex++}]", errors, depth + 1);
                }
                break;
            case JsonValueKind.Object:
                var propertyCount = 0;
                foreach (var property in value.EnumerateObject())
                {
                    propertyCount++;
                    if (ForbiddenParameterNames.Contains(property.Name))
                    {
                        errors.Add($"{prefix}.{property.Name} não é permitido em uma cena.");
                    }
                    ValidateParameterValue(property.Value, $"{prefix}.{property.Name}", errors, depth + 1);
                }
                if (propertyCount > 128)
                {
                    errors.Add($"{prefix} excede 128 propriedades.");
                }
                break;
        }
    }

    private static void ValidateVector(Vector3Document? value, string prefix, ICollection<string> errors)
    {
        if (value is null)
        {
            errors.Add($"{prefix} é obrigatório.");
            return;
        }

        if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z))
        {
            errors.Add($"{prefix} deve conter somente números finitos.");
        }
    }

    private static void ValidateName(string? value, string field, ICollection<string> errors, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{field} é obrigatório.");
        }
        else if (value.Length > maximumLength)
        {
            errors.Add($"{field} excede {maximumLength} caracteres.");
        }
        else if (value.Any(char.IsControl))
        {
            errors.Add($"{field} contém caractere de controle.");
        }
    }

    private static bool IsSafeHost(string? host)
    {
        return !string.IsNullOrWhiteSpace(host) &&
               host.Length <= 253 &&
               HostPattern().IsMatch(host) &&
               !LooksLikeExternalReference(host);
    }

    private static bool LooksLikeExternalReference(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.Contains("://", StringComparison.Ordinal) ||
               value.Contains("..", StringComparison.Ordinal) ||
               value.Contains('\\') ||
               value.Contains('/');
    }

    private static void ValidateGlobalIds(
        SceneDocument scene,
        IReadOnlyList<ComponentDocument> components,
        IReadOnlyList<TagDefinition> tags,
        IReadOnlyList<ConnectionDocument> connections,
        ICollection<string> errors)
    {
        var ids = new HashSet<Guid>();
        if (scene.SceneId != Guid.Empty)
        {
            ids.Add(scene.SceneId);
        }

        foreach (var (id, field) in components
                     .Where(component => component is not null)
                     .Select((component, index) => (component.Id, $"components[{index}].id"))
                     .Concat(tags
                         .Where(tag => tag is not null)
                         .Select((tag, index) => (tag.Id, $"tags[{index}].id")))
                     .Concat(connections
                         .Where(connection => connection is not null)
                         .Select((connection, index) => (connection.Id, $"connections[{index}].id"))))
        {
            if (id != Guid.Empty && !ids.Add(id))
            {
                errors.Add($"{field} reutiliza um UUID já usado na cena: {id}.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentTypePattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9._-]{0,119}$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalNamePattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex PortNamePattern();

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();
}
