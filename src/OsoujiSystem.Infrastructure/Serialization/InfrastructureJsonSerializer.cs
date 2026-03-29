using System.Text.Json;

namespace OsoujiSystem.Infrastructure.Serialization;

internal sealed class InfrastructureJsonSerializer
{
    public InfrastructureJsonSerializer()
    {
        Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public JsonSerializerOptions Options { get; }

    public string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, Options);

    public string Serialize(object? value, Type inputType)
        => JsonSerializer.Serialize(value, inputType, Options);

    public byte[] SerializeToUtf8Bytes<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, Options);

    public T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
        => JsonSerializer.Deserialize<T>(utf8Json, Options);
}
