using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimuladorIndustrial.Core.Components;

public static partial class ComponentPackageValidator
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumComponentsPerPackage = 256;
    public const int MaximumPortsPerComponent = 128;
    public const int MaximumParametersPerComponent = 128;

    public static IReadOnlyList<string> Validate(ComponentPackageManifest? manifest)
    {
        var errors = new List<string>();
        if (manifest is null)
        {
            return ["O manifesto não pode ser nulo."];
        }

        if (manifest.SchemaVersion != CurrentSchemaVersion)
        {
            errors.Add($"schemaVersion deve ser {CurrentSchemaVersion}.");
        }

        ValidateIdentifier(manifest.PackageId, "packageId", errors, 100);
        if (!SemanticVersionPattern().IsMatch(manifest.Version ?? string.Empty))
        {
            errors.Add("version deve usar SemVer no formato MAJOR.MINOR.PATCH, com sufixo opcional.");
        }

        ValidateText(manifest.DisplayName, "displayName", errors, 120, required: true);
        ValidateText(manifest.Author, "author", errors, 120, required: true);
        ValidateText(manifest.Description, "description", errors, 1000, required: false);
        ValidateText(manifest.License, "license", errors, 120, required: true);
        if (!SemanticVersionPattern().IsMatch(manifest.MinimumHostVersion ?? string.Empty))
        {
            errors.Add("minimumHostVersion deve usar SemVer.");
        }

        if (manifest.Components is null || manifest.Components.Count == 0)
        {
            errors.Add("components deve conter pelo menos um componente.");
            return errors;
        }

        if (manifest.Components.Count > MaximumComponentsPerPackage)
        {
            errors.Add($"O pacote excede {MaximumComponentsPerPackage} componentes.");
        }

        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < manifest.Components.Count; index++)
        {
            ValidateComponent(manifest.Components[index], index, types, errors);
        }

        return errors;
    }

    public static void ValidateAndThrow(ComponentPackageManifest? manifest)
    {
        var errors = Validate(manifest);
        if (errors.Count != 0)
        {
            throw new ComponentPackageException(string.Join(Environment.NewLine, errors));
        }
    }

    private static void ValidateComponent(
        ComponentDefinition? component,
        int index,
        ISet<string> types,
        ICollection<string> errors)
    {
        var prefix = $"components[{index}]";
        if (component is null)
        {
            errors.Add($"{prefix} não pode ser nulo.");
            return;
        }

        ValidateIdentifier(component.Type, $"{prefix}.type", errors, 100);
        if (!string.IsNullOrWhiteSpace(component.Type) && !types.Add(component.Type))
        {
            errors.Add($"{prefix}.type está duplicado: {component.Type}.");
        }

        ValidateText(component.DisplayName, $"{prefix}.displayName", errors, 120, required: true);
        ValidateText(component.Category, $"{prefix}.category", errors, 80, required: true);
        ValidateText(component.Description, $"{prefix}.description", errors, 1000, required: false);
        ValidateVisual(component.Visual, prefix, errors);

        if (component.Ports is null)
        {
            errors.Add($"{prefix}.ports é obrigatório, mesmo quando vazio.");
        }
        else
        {
            if (component.Ports.Count > MaximumPortsPerComponent)
            {
                errors.Add($"{prefix}.ports excede {MaximumPortsPerComponent} itens.");
            }

            var portNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var portIndex = 0; portIndex < component.Ports.Count; portIndex++)
            {
                var port = component.Ports[portIndex];
                var portPrefix = $"{prefix}.ports[{portIndex}]";
                if (port is null)
                {
                    errors.Add($"{portPrefix} não pode ser nulo.");
                    continue;
                }

                ValidateMemberName(port.Name, $"{portPrefix}.name", errors);
                if (!string.IsNullOrWhiteSpace(port.Name) && !portNames.Add(port.Name))
                {
                    errors.Add($"{portPrefix}.name está duplicado: {port.Name}.");
                }
                ValidateText(port.DisplayName, $"{portPrefix}.displayName", errors, 120, required: true);
            }
        }

        ValidateParameters(component.Parameters, prefix, errors);
    }

    private static void ValidateVisual(
        ComponentVisualDefinition? visual,
        string componentPrefix,
        ICollection<string> errors)
    {
        var prefix = $"{componentPrefix}.visual";
        if (visual is null)
        {
            errors.Add($"{prefix} é obrigatório.");
            return;
        }

        if (visual.Size is null ||
            !double.IsFinite(visual.Size.X) || !double.IsFinite(visual.Size.Y) || !double.IsFinite(visual.Size.Z) ||
            visual.Size.X <= 0 || visual.Size.Y <= 0 || visual.Size.Z <= 0 ||
            visual.Size.X > 100 || visual.Size.Y > 100 || visual.Size.Z > 100)
        {
            errors.Add($"{prefix}.size deve conter dimensões finitas entre 0 e 100 metros.");
        }

        if (!ColorPattern().IsMatch(visual.Color ?? string.Empty))
        {
            errors.Add($"{prefix}.color deve usar #RRGGBB ou #RRGGBBAA.");
        }

        if (visual.Kind == ComponentVisualKind.Model)
        {
            if (!IsSafeRelativeAssetPath(visual.ModelAsset) ||
                !string.Equals(Path.GetExtension(visual.ModelAsset), ".glb", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{prefix}.modelAsset deve apontar para um arquivo .glb relativo dentro do pacote.");
            }
        }
        else if (!string.IsNullOrWhiteSpace(visual.ModelAsset))
        {
            errors.Add($"{prefix}.modelAsset só pode ser usado quando kind for model.");
        }
    }

    private static void ValidateParameters(
        IReadOnlyList<ComponentParameterDefinition>? parameters,
        string componentPrefix,
        ICollection<string> errors)
    {
        var prefix = $"{componentPrefix}.parameters";
        if (parameters is null)
        {
            errors.Add($"{prefix} é obrigatório, mesmo quando vazio.");
            return;
        }

        if (parameters.Count > MaximumParametersPerComponent)
        {
            errors.Add($"{prefix} excede {MaximumParametersPerComponent} itens.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            var itemPrefix = $"{prefix}[{index}]";
            if (parameter is null)
            {
                errors.Add($"{itemPrefix} não pode ser nulo.");
                continue;
            }

            ValidateMemberName(parameter.Name, $"{itemPrefix}.name", errors);
            if (!string.IsNullOrWhiteSpace(parameter.Name) && !names.Add(parameter.Name))
            {
                errors.Add($"{itemPrefix}.name está duplicado: {parameter.Name}.");
            }
            ValidateText(parameter.DisplayName, $"{itemPrefix}.displayName", errors, 120, required: true);
            ValidateDefaultValue(parameter, itemPrefix, errors);
            if (parameter.Minimum is { } minimum && !double.IsFinite(minimum) ||
                parameter.Maximum is { } maximum && !double.IsFinite(maximum))
            {
                errors.Add($"{itemPrefix} contém limite não finito.");
            }
            if (parameter.Minimum is { } min && parameter.Maximum is { } max && min > max)
            {
                errors.Add($"{itemPrefix}.minimum não pode ser maior que maximum.");
            }
        }
    }

    private static void ValidateDefaultValue(
        ComponentParameterDefinition parameter,
        string prefix,
        ICollection<string> errors)
    {
        var valid = parameter.DataType switch
        {
            ComponentParameterType.Bool => parameter.DefaultValue.ValueKind is JsonValueKind.True or JsonValueKind.False,
            ComponentParameterType.Int32 => parameter.DefaultValue.ValueKind == JsonValueKind.Number &&
                                            parameter.DefaultValue.TryGetInt32(out _),
            ComponentParameterType.Float32 => parameter.DefaultValue.ValueKind == JsonValueKind.Number &&
                                              parameter.DefaultValue.TryGetDouble(out var number) &&
                                              double.IsFinite(number),
            ComponentParameterType.String => parameter.DefaultValue.ValueKind == JsonValueKind.String &&
                                             (parameter.DefaultValue.GetString()?.Length ?? 0) <= 512,
            _ => false
        };
        if (!valid)
        {
            errors.Add($"{prefix}.defaultValue não corresponde ao dataType.");
        }
    }

    internal static bool IsSafeRelativeAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 ||
            path.Contains('\\') || path.Contains(':') || path.StartsWith('/'))
        {
            return false;
        }

        var segments = path.Split('/');
        return segments.All(segment =>
            !string.IsNullOrWhiteSpace(segment) &&
            segment is not "." and not ".." &&
            SafeAssetSegmentPattern().IsMatch(segment));
    }

    private static void ValidateIdentifier(string? value, string field, ICollection<string> errors, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || !IdentifierPattern().IsMatch(value))
        {
            errors.Add($"{field} deve usar letras minúsculas, números, ponto, hífen ou sublinhado.");
        }
    }

    private static void ValidateMemberName(string? value, string field, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || !MemberNamePattern().IsMatch(value))
        {
            errors.Add($"{field} deve começar com letra minúscula e usar somente letras, números, hífen ou sublinhado.");
        }
    }

    private static void ValidateText(
        string? value,
        string field,
        ICollection<string> errors,
        int maxLength,
        bool required)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || (value?.Length ?? 0) > maxLength)
        {
            errors.Add($"{field} deve ter entre {(required ? 1 : 0)} e {maxLength} caracteres.");
        }
        if (value?.Any(char.IsControl) == true)
        {
            errors.Add($"{field} não pode conter caracteres de controle.");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeAssetSegmentPattern();

    [GeneratedRegex("^[a-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MemberNamePattern();
}
