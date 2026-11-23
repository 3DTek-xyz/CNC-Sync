using System.Net;
using System.Text;

namespace CNCSync.Infrastructure.Processing;

internal static class ProCutResponseOutput
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

    public static Task SaveAsync(HttpResponseMessage response, string outputPath, CancellationToken cancellationToken)
    {
        var headers = response.Content.Headers;
        if (response.StatusCode == HttpStatusCode.PartialContent || headers.Contains("Content-Range"))
        {
            throw new ProCutOutputException("The ProCut Suite API returned a partial response.", isRetryable: true);
        }

        if (!string.Equals(headers.ContentType?.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase))
        {
            throw new ProCutOutputException("The ProCut Suite API response was not text/plain.");
        }

        var expectedLength = headers.ContentLength;
        return AtomicOutputFile.WriteAsync(outputPath, overwrite: false, async (stream, token) =>
        {
            await response.Content.CopyToAsync(stream, token);
            if (expectedLength is not null && stream.Length != expectedLength.Value)
            {
                throw new ProCutOutputException("The ProCut Suite API response did not match its declared length.", isRetryable: true);
            }
        }, cancellationToken, ValidateTextAsync);
    }

    private static async Task ValidateTextAsync(string path, CancellationToken cancellationToken)
    {
        // Check the response format, not the program's machining semantics.
        using var reader = new StreamReader(path, StrictUtf8, detectEncodingFromByteOrderMarks: false);
        var buffer = new char[4096];
        var firstCharacters = new char[6];
        var prefixLength = 0;
        var hasText = false;
        var numericOnly = true;
        var whitespaceAfterText = false;
        var containsInternalWhitespace = false;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
            {
                for (var i = 0; i < count; i++)
                {
                    var character = buffer[i];
                    if (char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
                    {
                        throw new ProCutOutputException("The ProCut Suite API response contained non-text bytes.");
                    }

                    if (char.IsWhiteSpace(character))
                    {
                        whitespaceAfterText |= hasText;
                        continue;
                    }

                    if (!hasText && character is '<' or '{' or '[' or '"')
                    {
                        throw new ProCutOutputException("The ProCut Suite API response looked like HTML or JSON instead of G-code text.");
                    }

                    hasText = true;
                    containsInternalWhitespace |= whitespaceAfterText;
                    numericOnly &= char.IsAsciiDigit(character) || character is '-' or '+' or '.' or 'e' or 'E';
                    if (prefixLength < firstCharacters.Length)
                    {
                        firstCharacters[prefixLength++] = character;
                    }
                }
            }
        }
        catch (DecoderFallbackException)
        {
            throw new ProCutOutputException("The ProCut Suite API response was not valid UTF-8 text.");
        }

        if (!hasText)
        {
            throw new ProCutOutputException("The ProCut Suite API response contained no output text.");
        }

        var prefix = firstCharacters.AsSpan(0, prefixLength);
        if (!containsInternalWhitespace &&
            ((numericOnly && (char.IsAsciiDigit(prefix[0]) || prefix[0] == '-')) || prefix is "null" or "true" or "false"))
        {
            throw new ProCutOutputException("The ProCut Suite API response looked like JSON instead of G-code text.");
        }
    }
}

internal sealed class ProCutOutputException(string message, bool isRetryable = false, TimeSpan? retryAfter = null) : Exception(message)
{
    public bool IsRetryable { get; } = isRetryable;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
