namespace Errandy.Application.Interfaces;

/// <summary>
/// Where uploaded files (proof photos, receipts) are kept. The app only talks to this
/// interface, so moving from local disk to cloud storage later changes one class.
/// </summary>
public interface IFileStorage
{
    /// <summary>
    /// Saves the content under a NEW, server-generated name inside the folder and returns
    /// that storage key. The client's own file name is never used.
    /// </summary>
    Task<string> SaveAsync(string folder, string extension, Stream content, CancellationToken cancellationToken = default);

    /// <summary>Opens a stored file for reading, or returns null if it doesn't exist.</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}