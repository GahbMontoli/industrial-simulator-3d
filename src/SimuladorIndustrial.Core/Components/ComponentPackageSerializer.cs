using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimuladorIndustrial.Core.Components;

public static class ComponentPackageSerializer
{
    public const string ManifestFileName = "component-package.json";
    public const int MaximumManifestBytes = 512 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions ReadOptions = CreateOptions(false);
    private static readonly JsonSerializerOptions WriteOptions = CreateOptions(true);

    public static ComponentPackageManifest Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            if (StrictUtf8.GetByteCount(json) > MaximumManifestBytes)
            {
                throw new ComponentPackageException(
                    $"O manifesto excede o limite de {MaximumManifestBytes} bytes.");
            }

            var manifest = JsonSerializer.Deserialize<ComponentPackageManifest>(json, ReadOptions)
                ?? throw new ComponentPackageException("O manifesto não contém um pacote de componentes.");
            ComponentPackageValidator.ValidateAndThrow(manifest);
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new ComponentPackageException($"Manifesto JSON inválido: {exception.Message}", exception);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ComponentPackageException("O manifesto contém texto UTF-8 inválido.", exception);
        }
    }

    public static ComponentPackageManifest Deserialize(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) != 0)
        {
            if (buffer.Length + read > MaximumManifestBytes)
            {
                throw new ComponentPackageException(
                    $"O manifesto excede o limite de {MaximumManifestBytes} bytes.");
            }

            buffer.Write(chunk, 0, read);
        }

        try
        {
            return Deserialize(StrictUtf8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length)));
        }
        catch (DecoderFallbackException exception)
        {
            throw new ComponentPackageException("O manifesto contém texto UTF-8 inválido.", exception);
        }
    }

    public static string Serialize(ComponentPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ComponentPackageValidator.ValidateAndThrow(manifest);
        return JsonSerializer.Serialize(manifest, WriteOptions) + Environment.NewLine;
    }

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 24,
            WriteIndented = writeIndented
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
