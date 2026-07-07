using Yarpa.Api.Data.Entities;
using Yarpa.Contracts;

namespace Yarpa.Api.Services;

/// <summary>
/// Compares a newly received <see cref="DiagnosticsSnapshot"/> against the raw JSON
/// of the previous snapshot for the same machine and produces a list of
/// <see cref="ChangeEntity"/> records describing every meaningful difference.
/// </summary>
public interface ISnapshotComparer
{
    /// <summary>
    /// Compares <paramref name="newSnapshot"/> against the snapshot stored as
    /// <paramref name="previousRawJson"/>.
    /// Returns an empty list when <paramref name="previousRawJson"/> is null (baseline).
    /// </summary>
    IReadOnlyList<ChangeEntity> Compare(
        DiagnosticsSnapshot newSnapshot,
        string? previousRawJson);
}
