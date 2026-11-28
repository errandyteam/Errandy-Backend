namespace Errandy.Application.Common.Utils;

public sealed record ImageKind(string Extension, string ContentType);

public static class ProofFileRules
{
    public const int MaxMegabytes = 8;
    public const long MaxBytes = MaxMegabytes * 1024L * 1024L;
}

/// <summary>
/// Works out what a file really is by reading its first bytes. A client can name a file
/// anything and claim any content type, so neither the name nor the type is trusted.
/// </summary>
public static class ImageFileInspector
{
    private const int HeaderLength = 12;

    /// <summary>Returns the image type, or null if it is not a JPEG, PNG or WebP. Leaves the stream where it started.</summary>
    public static ImageKind? Inspect(Stream stream)
    {
        var header = ReadHeader(stream, out var total);
        if (header is null)
            return null;

        if (total >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return new ImageKind("jpg", "image/jpeg");

        if (total >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return new ImageKind("png", "image/png");

        if (total >= 12 && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
            return new ImageKind("webp", "image/webp");

        return null;
    }

    /// <summary>
    /// A receipt may be an image or a PDF, because many banks issue receipts as PDF files.
    /// The proof photo must still be an image.
    /// </summary>
    public static ImageKind? InspectReceipt(Stream stream)
    {
        var image = Inspect(stream);
        if (image is not null)
            return image;

        var header = ReadHeader(stream, out var total);
        if (header is not null && total >= 5
            && header[0] == '%' && header[1] == 'P' && header[2] == 'D' && header[3] == 'F' && header[4] == '-')
            return new ImageKind("pdf", "application/pdf");

        return null;
    }

    public static string ContentTypeForExtension(string extension) =>
        extension.TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "png" => "image/png",
            "webp" => "image/webp",
            "pdf" => "application/pdf",
            _ => "application/octet-stream"
        };

    private static byte[]? ReadHeader(Stream stream, out int total)
    {
        total = 0;
        if (!stream.CanRead || !stream.CanSeek)
            return null;

        var start = stream.Position;
        var header = new byte[HeaderLength];
        while (total < HeaderLength)
        {
            var read = stream.Read(header, total, HeaderLength - total);
            if (read == 0) break;
            total += read;
        }
        stream.Position = start;
        return header;
    }
}