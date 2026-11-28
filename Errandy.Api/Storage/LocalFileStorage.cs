using Errandy.Application.Interfaces;

namespace Errandy.Api.Storage;

/// <summary>
/// Development storage: files live in App_Data/uploads next to the app. They are outside
/// the public web folder, so the only way to read one is the authenticated download endpoint.
/// NOT for production: disk is lost on redeploy and isn't shared between servers.
/// Replace with a cloud implementation of IFileStorage before launch.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["FileStorage:RootPath"];
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "uploads")
            : configured);

        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default)
    {
        var safeExtension = new string(extension.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (safeExtension.Length == 0)
            throw new ArgumentException("A file extension is required.", nameof(extension));

        var key = $"{folder.Trim('/')}/{Guid.NewGuid():N}.{safeExtension}";
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = Resolve(key);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    /// <summary>Turns a key into a path and refuses anything that would escape the uploads folder.</summary>
    private string Resolve(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!full.StartsWith(rootWithSeparator, comparison))
            throw new InvalidOperationException("Invalid storage key.");

        return full;
    }
}