using System.Security.Cryptography;
using System.Text.Json;

namespace SimuladorIndustrial.Core.Scenarios;

public sealed record FillingCellFaultStudyExportManifest(
    int SchemaVersion,
    string ProjectId,
    string RunId,
    string SourceId,
    string SourceSha256,
    string CsvFile,
    string CsvSha256,
    DateTimeOffset ExportedAtUtc,
    string EvidenceClass,
    string QualityStatus,
    string EvidenceLimit,
    int RowCount,
    bool AllExpectationsMet);

public sealed record FillingCellFaultStudyPackage(
    string CsvPath,
    string ManifestPath,
    FillingCellFaultStudyExportManifest Manifest);

public static class FillingCellFaultStudyPackageExporter
{
    private const string ProjectId = "industrial-simulator-3d";

    public static async Task<FillingCellFaultStudyPackage> WriteAsync(
        string csvPath,
        string sourcePath,
        IEnumerable<FillingCellFaultStudyResult> results,
        string? runId = null,
        DateTimeOffset? exportedAtUtc = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(results);

        var rows = results.ToArray();
        if (rows.Length == 0)
        {
            throw new ArgumentException("At least one study result is required.", nameof(results));
        }

        var fullCsvPath = Path.GetFullPath(csvPath);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var manifestPath = Path.ChangeExtension(fullCsvPath, ".manifest.json");
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("The study source file was not found.", fullSourcePath);
        }

        if (PathsEqual(fullCsvPath, fullSourcePath) ||
            PathsEqual(manifestPath, fullSourcePath) ||
            PathsEqual(fullCsvPath, manifestPath))
        {
            throw new InvalidOperationException(
                "The CSV and manifest outputs must be distinct from the study source file.");
        }

        var outputDirectory = Path.GetDirectoryName(fullCsvPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var timestamp = (exportedAtUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var resolvedRunId = string.IsNullOrWhiteSpace(runId)
            ? $"fault-study-{timestamp:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}"
            : runId.Trim();
        var temporaryCsvPath = CreateSiblingPath(fullCsvPath, ".csv.tmp");
        var temporaryManifestPath = CreateSiblingPath(manifestPath, ".json.tmp");

        try
        {
            await using (var destination = new FileStream(
                             temporaryCsvPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4_096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await FillingCellFaultStudy.WriteCsvAsync(destination, rows, cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            var allExpectationsMet = rows.All(row => row.ExpectationMet);
            var manifest = new FillingCellFaultStudyExportManifest(
                SchemaVersion: 1,
                ProjectId,
                resolvedRunId,
                Path.GetFileName(fullSourcePath),
                await ComputeSha256Async(fullSourcePath, cancellationToken),
                Path.GetFileName(fullCsvPath),
                await ComputeSha256Async(temporaryCsvPath, cancellationToken),
                timestamp,
                EvidenceClass: "synthetic",
                QualityStatus: allExpectationsMet ? "valid" : "warning",
                EvidenceLimit:
                    "A simulação valida o comportamento do software e não representa evidência de campo nem prova causal.",
                RowCount: rows.Length,
                AllExpectationsMet: allExpectationsMet);

            await WriteManifestAsync(temporaryManifestPath, manifest, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            PromotePair(
                temporaryCsvPath,
                fullCsvPath,
                temporaryManifestPath,
                manifestPath);

            return new FillingCellFaultStudyPackage(fullCsvPath, manifestPath, manifest);
        }
        finally
        {
            TryDelete(temporaryCsvPath);
            TryDelete(temporaryManifestPath);
        }
    }

    private static bool PathsEqual(string first, string second) =>
        string.Equals(
            Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static string CreateSiblingPath(string destinationPath, string suffix)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("The output path must have a valid directory.");
        return Path.Combine(
            directory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}{suffix}");
    }

    private static async Task WriteManifestAsync(
        string path,
        FillingCellFaultStudyExportManifest manifest,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4_096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            },
            cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static void PromotePair(
        string temporaryCsvPath,
        string csvPath,
        string temporaryManifestPath,
        string manifestPath)
    {
        var csvBackupPath = CreateSiblingPath(csvPath, ".bak");
        var manifestBackupPath = CreateSiblingPath(manifestPath, ".bak");
        var csvBackedUp = false;
        var manifestBackedUp = false;
        var csvPromoted = false;
        var manifestPromoted = false;
        var promotionSucceeded = false;

        try
        {
            if (File.Exists(csvPath))
            {
                File.Move(csvPath, csvBackupPath);
                csvBackedUp = true;
            }

            if (File.Exists(manifestPath))
            {
                File.Move(manifestPath, manifestBackupPath);
                manifestBackedUp = true;
            }

            File.Move(temporaryCsvPath, csvPath);
            csvPromoted = true;
            File.Move(temporaryManifestPath, manifestPath);
            manifestPromoted = true;
            promotionSucceeded = true;
        }
        catch (Exception promotionException) when (
            promotionException is IOException or UnauthorizedAccessException)
        {
            try
            {
                if (manifestPromoted)
                {
                    File.Delete(manifestPath);
                }
                if (csvPromoted)
                {
                    File.Delete(csvPath);
                }
                if (manifestBackedUp)
                {
                    File.Move(manifestBackupPath, manifestPath);
                    manifestBackedUp = false;
                }
                if (csvBackedUp)
                {
                    File.Move(csvBackupPath, csvPath);
                    csvBackedUp = false;
                }
            }
            catch (Exception rollbackException) when (
                rollbackException is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    "The fault-study package failed and its previous files could not be fully restored. " +
                    $"Preserved backup paths: '{csvBackupPath}' and '{manifestBackupPath}'.",
                    new AggregateException(promotionException, rollbackException));
            }

            throw;
        }
        finally
        {
            if (promotionSucceeded && csvBackedUp)
            {
                TryDelete(csvBackupPath);
            }
            if (promotionSucceeded && manifestBackedUp)
            {
                TryDelete(manifestBackupPath);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup failure must not hide the package result or rollback outcome.
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
