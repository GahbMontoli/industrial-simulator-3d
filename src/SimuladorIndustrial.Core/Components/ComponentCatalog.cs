namespace SimuladorIndustrial.Core.Components;

public sealed class ComponentCatalog
{
    private readonly Dictionary<string, ComponentRegistration> _components =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ComponentRegistration> Components => _components.Values
        .OrderBy(item => item.Definition.Category, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(item => item.Definition.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public bool TryGet(string type, out ComponentRegistration registration) =>
        _components.TryGetValue(type, out registration!);

    public ComponentRegistration GetRequired(string type) =>
        TryGet(type, out var registration)
            ? registration
            : throw new KeyNotFoundException($"Componente não registrado: {type}.");

    public static ComponentCatalogLoadResult LoadDefault(string packageStoreDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageStoreDirectory);
        var catalog = new ComponentCatalog();
        var diagnostics = new List<string>();

        foreach (var definition in BuiltInComponentCatalog.Components)
        {
            catalog.Add(new ComponentRegistration(
                definition,
                BuiltInComponentCatalog.PackageId,
                BuiltInComponentCatalog.PackageVersion,
                string.Empty,
                IsBuiltIn: true));
        }

        if (!Directory.Exists(packageStoreDirectory))
        {
            return new ComponentCatalogLoadResult(catalog, diagnostics);
        }

        foreach (var packageDirectory in SelectLatestPackageVersions(packageStoreDirectory, diagnostics))
        {
            try
            {
                var manifestPath = Path.Combine(packageDirectory, ComponentPackageSerializer.ManifestFileName);
                var manifest = ComponentPackageSerializer.Deserialize(File.ReadAllText(manifestPath));
                foreach (var definition in manifest.Components)
                {
                    if (catalog._components.ContainsKey(definition.Type))
                    {
                        diagnostics.Add(
                            $"Pacote {manifest.PackageId} ignorou o tipo duplicado {definition.Type}.");
                        continue;
                    }

                    catalog.Add(new ComponentRegistration(
                        definition,
                        manifest.PackageId,
                        manifest.Version,
                        packageDirectory,
                        IsBuiltIn: false));
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ComponentPackageException)
            {
                diagnostics.Add($"Pacote inválido em {packageDirectory}: {exception.Message}");
            }
        }

        return new ComponentCatalogLoadResult(catalog, diagnostics);
    }

    private void Add(ComponentRegistration registration)
    {
        ComponentPackageValidator.ValidateAndThrow(new ComponentPackageManifest(
            ComponentPackageValidator.CurrentSchemaVersion,
            registration.PackageId,
            registration.PackageVersion,
            registration.PackageId,
            "Simulador Industrial",
            string.Empty,
            "Proprietary",
            "1.0.0",
            [registration.Definition]));
        _components.Add(registration.Definition.Type, registration);
    }

    private static IEnumerable<string> SelectLatestPackageVersions(
        string packageStoreDirectory,
        ICollection<string> diagnostics)
    {
        foreach (var packageDirectory in Directory.EnumerateDirectories(packageStoreDirectory)
                     .Where(path => !Path.GetFileName(path).StartsWith(".staging-", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = new List<(Version Version, string Path)>();
            foreach (var versionDirectory in Directory.EnumerateDirectories(packageDirectory))
            {
                var versionName = Path.GetFileName(versionDirectory);
                var coreVersion = versionName.Split('-', '+')[0];
                if (Version.TryParse(coreVersion, out var version) &&
                    File.Exists(Path.Combine(versionDirectory, ComponentPackageSerializer.ManifestFileName)))
                {
                    candidates.Add((version, versionDirectory));
                }
            }

            if (candidates.Count == 0)
            {
                diagnostics.Add($"Nenhuma versão válida encontrada em {packageDirectory}.");
                continue;
            }

            yield return candidates
                .OrderByDescending(candidate => candidate.Version)
                .ThenByDescending(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .First().Path;
        }
    }
}
