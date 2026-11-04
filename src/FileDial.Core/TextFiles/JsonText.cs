using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FileDial.Core.TextFiles;

/// <summary>Ошибка в JSON с местом: строка и символ считаются с 1.</summary>
public sealed class JsonSyntaxException(long line, long column, Exception inner) : Exception(inner.Message, inner)
{
    public long Line { get; } = line;
    public long Column { get; } = column;
}

/// <summary>Форматирование и сжатие JSON. Числа сохраняются как записаны, \uXXXX раскрываются в символы.</summary>
public static class JsonText
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 512,
    };

    /// <param name="newLine">Перевод строки для отступов — как в исходном файле.</param>
    public static string Rewrite(string json, bool indented, string newLine = "\r\n")
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            throw new JsonSyntaxException((ex.LineNumber ?? 0) + 1, (ex.BytePositionInLine ?? 0) + 1, ex);
        }

        using (document)
        {
            var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
                   {
                       Indented = indented,
                       IndentSize = 2,
                       NewLine = newLine,
                       // Кириллица и прочее — как есть, а не А.
                       Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                       SkipValidation = false,
                       MaxDepth = 512,
                   }))
            {
                document.RootElement.WriteTo(writer);
            }
            var text = Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            return indented ? text + newLine : text;
        }
    }

    /// <summary>Причина, почему JSON не изменится или не разберётся; null — действие имеет смысл.</summary>
    public static (bool Valid, string? Pretty, string? Minified, JsonSyntaxException? Error) Inspect(string json, string newLine)
    {
        try
        {
            return (true, Rewrite(json, true, newLine), Rewrite(json, false, newLine), null);
        }
        catch (JsonSyntaxException ex)
        {
            return (false, null, null, ex);
        }
    }
}
