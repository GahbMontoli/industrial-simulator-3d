using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace SimuladorIndustrial.Core.Persistence;

public static class SceneSerializer
{
    public const int MaximumSceneBytes = 8 * 1024 * 1024;

    private static readonly JsonSerializerOptions ReadOptions = CreateOptions(writeIndented: false);
    private static readonly JsonSerializerOptions WriteOptions = CreateOptions(writeIndented: true);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Serialize(SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SceneValidator.ValidateAndThrow(scene);
        return JsonSerializer.Serialize(scene, WriteOptions) + Environment.NewLine;
    }

    public static SceneDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            EnsureSize(StrictUtf8.GetByteCount(json));
            var migratedJson = SceneMigrator.MigrateToCurrent(json);
            var scene = JsonSerializer.Deserialize<SceneDocument>(migratedJson, ReadOptions)
                ?? throw new SceneValidationException(["O JSON não contém uma cena."]);
            SceneValidator.ValidateAndThrow(scene);
            return scene;
        }
        catch (JsonException exception)
        {
            throw new SceneValidationException([$"JSON inválido: {exception.Message}"]);
        }
        catch (EncoderFallbackException exception)
        {
            throw new SceneValidationException([$"Texto UTF-8 inválido: {exception.Message}"]);
        }
        catch (ArgumentException exception)
        {
            throw new SceneValidationException([$"Conteúdo inválido: {exception.Message}"]);
        }
    }

    public static async ValueTask<SceneDocument> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
            {
                EnsureSize(checked((int)(buffer.Length + bytesRead)));
                await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            }

            return Deserialize(StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length)));
        }
        catch (JsonException exception)
        {
            throw new SceneValidationException([$"JSON inválido: {exception.Message}"]);
        }
        catch (DecoderFallbackException exception)
        {
            throw new SceneValidationException([$"Fluxo UTF-8 inválido: {exception.Message}"]);
        }
        catch (ArgumentException exception)
        {
            throw new SceneValidationException([$"Conteúdo inválido: {exception.Message}"]);
        }
    }

    public static async ValueTask SerializeAsync(
        Stream destination,
        SceneDocument scene,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(scene);
        SceneValidator.ValidateAndThrow(scene);
        await JsonSerializer.SerializeAsync(destination, scene, WriteOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = writeIndented,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 32
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static void EnsureSize(int byteCount)
    {
        if (byteCount > MaximumSceneBytes)
        {
            throw new SceneValidationException(
                [$"A cena excede o limite de {MaximumSceneBytes} bytes."]);
        }
    }
}
