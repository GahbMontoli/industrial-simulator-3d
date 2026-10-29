using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SimuladorIndustrial.Core.Persistence;

public static class UiPreferencesStore
{
    public const int MaximumPreferencesBytes = 64 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions ReadOptions = CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions WriteOptions = CreateOptions(writeIndented: true);

    public static UiPreferences Load(string path)
    {
        var fullPath = NormalizePath(path);
        if (!File.Exists(fullPath))
        {
            return UiPreferences.Default;
        }

        try
        {
            var file = new FileInfo(fullPath);
            if (file.Length > MaximumPreferencesBytes)
            {
                throw new UiPreferencesException(
                    $"O arquivo de preferências excede {MaximumPreferencesBytes} bytes.");
            }

            return Deserialize(StrictUtf8.GetString(File.ReadAllBytes(fullPath)));
        }
        catch (DecoderFallbackException exception)
        {
            throw new UiPreferencesException("O arquivo de preferências não contém UTF-8 válido.", exception);
        }
        catch (IOException exception)
        {
            throw new UiPreferencesException("Não foi possível ler as preferências da interface.", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new UiPreferencesException("Acesso negado ao arquivo de preferências.", exception);
        }
    }

    public static void Save(string path, UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Validate(preferences);

        var fullPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new UiPreferencesException("O caminho das preferências não possui diretório válido.");
        var fileName = Path.GetFileName(fullPath);
        var temporaryPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp");
        var payload = StrictUtf8.GetBytes(Serialize(preferences));

        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new UiPreferencesException("Não foi possível salvar as preferências da interface.", exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // A falha de limpeza não deve ocultar o resultado da gravação atômica.
            }
        }
    }

    public static string Serialize(UiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        Validate(preferences);
        return JsonSerializer.Serialize(preferences, WriteOptions) + Environment.NewLine;
    }

    public static UiPreferences Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        try
        {
            EnsureSize(StrictUtf8.GetByteCount(json));
            var migrated = Migrate(json);
            var preferences = JsonSerializer.Deserialize<UiPreferences>(migrated, ReadOptions)
                ?? throw new UiPreferencesException("O JSON não contém preferências de interface.");
            Validate(preferences);
            return preferences;
        }
        catch (UiPreferencesException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new UiPreferencesException($"JSON de preferências inválido: {exception.Message}", exception);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or FormatException or OverflowException)
        {
            throw new UiPreferencesException("Os tipos dos campos de preferências são inválidos.", exception);
        }
        catch (EncoderFallbackException exception)
        {
            throw new UiPreferencesException("As preferências não contêm texto UTF-8 válido.", exception);
        }
    }

    private static string Migrate(string json)
    {
        var root = JsonNode.Parse(
            json,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            }) as JsonObject
            ?? throw new UiPreferencesException("As preferências devem ser um objeto JSON.");

        var version = root["schemaVersion"]?.GetValue<int>() ?? 0;
        if (version > UiPreferences.CurrentSchemaVersion)
        {
            throw new UiPreferencesException(
                $"A versão {version} das preferências é mais nova que a versão suportada {UiPreferences.CurrentSchemaVersion}.");
        }

        if (version < 0)
        {
            throw new UiPreferencesException("A versão das preferências não pode ser negativa.");
        }

        if (version == 0)
        {
            Rename(root, "scale", "uiScale");
            Rename(root, "startSection", "startupSection");
            if (root.Remove("showWelcome", out var showWelcome) && showWelcome is not null)
            {
                root["welcomeDismissed"] = !showWelcome.GetValue<bool>();
            }

            root["schemaVersion"] = UiPreferences.CurrentSchemaVersion;
        }

        return root.ToJsonString();
    }

    private static void Rename(JsonObject root, string oldName, string newName)
    {
        if (root.Remove(oldName, out var value) && value is not null)
        {
            if (root.ContainsKey(newName))
            {
                throw new UiPreferencesException(
                    $"As propriedades '{oldName}' e '{newName}' não podem coexistir.");
            }

            root[newName] = value;
        }
    }

    private static void Validate(UiPreferences preferences)
    {
        if (preferences.SchemaVersion != UiPreferences.CurrentSchemaVersion)
        {
            throw new UiPreferencesException(
                $"schemaVersion deve ser {UiPreferences.CurrentSchemaVersion}.");
        }

        if (!Enum.IsDefined(preferences.Theme))
        {
            throw new UiPreferencesException("O tema da interface é inválido.");
        }

        if (!double.IsFinite(preferences.UiScale) || preferences.UiScale is < 0.75d or > 1.50d)
        {
            throw new UiPreferencesException("A escala da interface deve estar entre 0,75 e 1,50.");
        }

        if (!Enum.IsDefined(preferences.StartupSection))
        {
            throw new UiPreferencesException("A seção inicial da interface é inválida.");
        }
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new UiPreferencesException("O caminho das preferências é inválido.", exception);
        }
    }

    private static void EnsureSize(int byteCount)
    {
        if (byteCount > MaximumPreferencesBytes)
        {
            throw new UiPreferencesException(
                $"As preferências excedem o limite de {MaximumPreferencesBytes} bytes.");
        }
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = writeIndented,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 8
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
