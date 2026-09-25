namespace Errandy.Application.Common.Interfaces;
/// <summary>
/// Commits all pending changes tracked within a single business transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}