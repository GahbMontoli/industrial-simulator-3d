using System.IO.Compression;
using System.Security.Cryptography;

namespace SimuladorIndustrial.Core.Components;

public static class ComponentPackageInstaller
{
    public const string PackageExtension = ".silabcomponent";
    public const string CurrentHostVersion = "0.2.0";
    public const long MaximumPackageBytes = 64L * 1024 * 1024;
    public const long MaximumExtractedBytes = 128L * 1024 * 1024;
    public const int MaximumArchiveEntries = 512;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".glb", ".png", ".jpg", ".jpeg", ".webp",
        ".md", ".txt", ".license"
    };

    public static ComponentPackageInstallation Install(string packagePath, string packageStoreDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageStoreDirectory);

        var sourcePath = Path.GetFullPath(packagePath);
        if (!File.Exists(sourcePath))
        {
            throw new ComponentPackageException($"Pacote não encontrado: {sourcePath}");
        }
        if (!string.Equals(Path.GetExtension(sourcePath), PackageExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ComponentPackageException($"O pacote deve usar a extensão {PackageExtension}.");
        }

        var fileInfo = new FileInfo(sourcePath);
        if (fileInfo.Length == 0 || fileInfo.Length > MaximumPackageBytes)
        {
            throw new ComponentPackageException(
                $"O pacote deve ter entre 1 e {MaximumPackageBytes} bytes.");
        }

        var storeRoot = Path.GetFullPath(packageStoreDirectory);
        Directory.CreateDirectory(storeRoot);
        var stagingDirectory = Path.Combine(storeRoot, $".staging-{Guid.NewGuid():N}");

        try
        {
            using var archive = ZipFile.OpenRead(sourcePath);
            ValidateArchive(archive);
            var manifestEntry = archive.Entries.Single(entry =>
                string.Equals(entry.FullName, ComponentPackageSerializer.ManifestFileName, StringComparison.Ordinal));
            ComponentPackageManifest manifest;
            using (var manifestStream = manifestEntry.Open())
            {
                manifest = ComponentPackageSerializer.Deserialize(manifestStream);
            }
            EnsureHostCompatibility(manifest);
            ValidateReferencedAssets(archive, manifest);

            var installationDirectory = GetInstallationDirectory(storeRoot, manifest);
            if (Directory.Exists(installationDirectory))
            {
                throw new ComponentPackageException(
                    $"O pacote {manifest.PackageId} {manifest.Version} já está instalado.");
            }

            Directory.CreateDirectory(stagingDirectory);
            ExtractArchive(archive, stagingDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(installationDirectory)!);
            Directory.Move(stagingDirectory, installationDirectory);

            return new ComponentPackageInstallation(
                manifest,
                installationDirectory,
                ComputeSha256(sourcePath));
        }
        catch (ComponentPackageException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new ComponentPackageException($"Não foi possível instalar o pacote: {exception.Message}", exception);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new ComponentPackageException(
                $"O pacote deve conter entre 1 e {MaximumArchiveEntries} entradas.");
        }

        long totalBytes = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!names.Add(name))
            {
                throw new ComponentPackageException($"Entrada duplicada no pacote: {name}");
            }
            if (!IsSafeArchivePath(name))
            {
                throw new ComponentPackageException($"Caminho inseguro no pacote: {name}");
            }
            if (IsSymbolicLink(entry))
            {
                throw new ComponentPackageException($"Links simbólicos não são permitidos: {name}");
            }

            totalBytes = checked(totalBytes + entry.Length);
            if (totalBytes > MaximumExtractedBytes)
            {
                throw new ComponentPackageException(
                    $"O conteúdo extraído excede {MaximumExtractedBytes} bytes.");
            }

            if (name.EndsWith('/'))
            {
                continue;
            }

            var extension = Path.GetExtension(name);
            if (!AllowedExtensions.Contains(extension))
            {
                throw new ComponentPackageException(
                    $"Tipo de arquivo não permitido no pacote: {name}. Código executável e scripts são bloqueados.");
            }
        }

        if (!archive.Entries.Any(entry =>
                string.Equals(entry.FullName, ComponentPackageSerializer.ManifestFileName, StringComparison.Ordinal)))
        {
            throw new ComponentPackageException(
                $"O pacote deve conter {ComponentPackageSerializer.ManifestFileName} na raiz.");
        }
    }

    private static void ValidateReferencedAssets(
        ZipArchive archive,
        ComponentPackageManifest manifest)
    {
        var files = archive.Entries
            .Where(entry => !entry.FullName.EndsWith('/'))
            .Select(entry => entry.FullName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var component in manifest.Components.Where(component =>
                     component.Visual.Kind == ComponentVisualKind.Model))
        {
            if (!files.Contains(component.Visual.ModelAsset!))
            {
                throw new ComponentPackageException(
                    $"O modelo {component.Visual.ModelAsset} do componente {component.Type} não existe no pacote.");
            }
        }
    }

    private static void EnsureHostCompatibility(ComponentPackageManifest manifest)
    {
        var requiredVersion = ParseCoreVersion(manifest.MinimumHostVersion);
        var currentVersion = ParseCoreVersion(CurrentHostVersion);
        if (requiredVersion > currentVersion)
        {
            throw new ComponentPackageException(
                $"O pacote requer o Simulador Industrial {manifest.MinimumHostVersion} ou superior; " +
                $"esta versão é {CurrentHostVersion}.");
        }
    }

    private static Version ParseCoreVersion(string semanticVersion) =>
        Version.Parse(semanticVersion.Split('-', '+')[0]);

    private static string GetInstallationDirectory(
        string storeRoot,
        ComponentPackageManifest manifest)
    {
        var candidate = Path.GetFullPath(Path.Combine(storeRoot, manifest.PackageId, manifest.Version));
        if (!candidate.StartsWith(storeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ComponentPackageException("O destino calculado do pacote é inseguro.");
        }
        return candidate;
    }

    private static void ExtractArchive(ZipArchive archive, string stagingDirectory)
    {
        var stagingRoot = Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
            if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ComponentPackageException($"Caminho de extração inseguro: {entry.FullName}");
            }

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
        }
    }

    private static bool IsSafeArchivePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240 ||
            name.Contains('\\') || name.Contains(':') || name.StartsWith('/'))
        {
            return false;
        }

        var trimmed = name.EndsWith('/') ? name[..^1] : name;
        return trimmed.Split('/').All(segment =>
            !string.IsNullOrWhiteSpace(segment) && segment is not "." and not "..");
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
