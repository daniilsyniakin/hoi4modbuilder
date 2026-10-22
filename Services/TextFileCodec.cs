using System.Text;

namespace HoI4ModBuilder.Services;

public sealed record TextFileContent(string Text, Encoding Encoding);

/// <summary>
/// Reads and writes text files while preserving their encoding. HoI4 is picky here:
/// localisation (.yml) must be UTF-8 with BOM, while many vanilla scripts are Windows-1252.
/// </summary>
public static class TextFileCodec
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly UTF8Encoding Utf8Strict = new(false, throwOnInvalidBytes: true);
    private static readonly Encoding Windows1252;

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dds", ".png", ".tga", ".bmp", ".jpg", ".jpeg", ".gif", ".psd", ".ico", ".cur",
        ".ogg", ".wav", ".mp3", ".ttf", ".otf", ".exe", ".dll", ".zip", ".7z", ".bin",
        ".mesh", ".anim", ".bank",
    };

    static TextFileCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Windows1252 = Encoding.GetEncoding(1252);
    }

    public static bool IsProbablyText(string path) => !BinaryExtensions.Contains(Path.GetExtension(path));

    /// <exception cref="InvalidDataException">The bytes look like a binary file.</exception>
    public static TextFileContent Decode(byte[] bytes)
    {
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            return new TextFileContent(Utf8Bom.GetString(bytes, 3, bytes.Length - 3), Utf8Bom);
        if (bytes is [0xFF, 0xFE, ..])
            return new TextFileContent(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        if (bytes is [0xFE, 0xFF, ..])
            return new TextFileContent(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode);

        if (Array.IndexOf(bytes, (byte)0) >= 0)
            throw new InvalidDataException("Файл містить двійкові дані.");

        try
        {
            return new TextFileContent(Utf8Strict.GetString(bytes), Utf8NoBom);
        }
        catch (DecoderFallbackException)
        {
            return new TextFileContent(Windows1252.GetString(bytes), Windows1252);
        }
    }

    /// <summary>
    /// Encodes <paramref name="text"/>; if it can no longer be represented in a legacy code page
    /// (e.g. Cyrillic typed into a Windows-1252 file), falls back to UTF-8.
    /// </summary>
    public static (byte[] Bytes, Encoding Encoding) Encode(string text, Encoding encoding)
    {
        if (encoding.CodePage == Windows1252.CodePage)
        {
            var strict = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
            try
            {
                return (strict.GetBytes(text), encoding);
            }
            catch (EncoderFallbackException)
            {
                encoding = Utf8NoBom;
            }
        }

        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        if (preamble.Length == 0)
            return (body, encoding);

        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return (bytes, encoding);
    }

    public static string DisplayName(Encoding encoding) => encoding switch
    {
        UTF8Encoding utf8 => utf8.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8",
        _ when encoding.CodePage == 1200 => "UTF-16 LE",
        _ when encoding.CodePage == 1201 => "UTF-16 BE",
        _ when encoding.CodePage == 1252 => "Windows-1252",
        _ => encoding.WebName.ToUpperInvariant(),
    };
}
