using System.Text;
using Errandy.Application.Interfaces;

namespace Errandy.Tests.Support;

/// <summary>Tiny fake files with the right first bytes, plus an in-memory stand-in for file storage.</summary>
public static class TestFiles
{
    public static MemoryStream Jpeg(int size = 2048) => WithHeader(size, 0xFF, 0xD8, 0xFF, 0xE0);

    public static MemoryStream Png(int size = 2048) =>
        WithHeader(size, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);

    public static MemoryStream WebP(int size = 2048)
    {
        var bytes = new byte[size];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        return new MemoryStream(bytes);
    }

    public static MemoryStream Pdf(int size = 2048)
    {
        var bytes = new byte[size];
        Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(bytes, 0);
        return new MemoryStream(bytes);
    }

    public static MemoryStream NotAnImage() =>
        new(Encoding.UTF8.GetBytes("This is a text file pretending to be a photo."));

    private static MemoryStream WithHeader(int size, params byte[] header)
    {
        var bytes = new byte[Math.Max(size, header.Length)];
        header.CopyTo(bytes, 0);
        return new MemoryStream(bytes);
    }
}

public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = new();
    private int _saveCount;

    /// <summary>The Nth save throws, to simulate the storage failing part-way. 0 means never.</summary>
    public int FailOnSaveNumber { get; set; }

    public IReadOnlyCollection<string> Keys => _files.Keys;

    public async Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default)
    {
        _saveCount++;
        if (FailOnSaveNumber == _saveCount)
            throw new IOException("Simulated storage failure.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        var key = $"{folder}/{Guid.NewGuid():N}.{extension}";
        _files[key] = buffer.ToArray();
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream?>(_files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _files.Remove(key);
        return Task.CompletedTask;
    }

    public static MemoryStream Pdf(int size = 2048)
    {
        var bytes = new byte[size];
        Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(bytes, 0);
        return new MemoryStream(bytes);
    }
}