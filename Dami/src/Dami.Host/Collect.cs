using System.Runtime.CompilerServices;

namespace Dami.Host;

/// <summary>Stream helpers shared by the endpoint groups.</summary>
internal static class Collect
{
    /// <summary>Re-yields a stream so minimal APIs serialize it as a JSON array.</summary>
    internal static async IAsyncEnumerable<T> Async<T>(
        IAsyncEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>Materializes a stream.</summary>
    internal static async Task<List<T>> ListAsync<T>(
        IAsyncEnumerable<T> source,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>Matches an id against a dashless hex prefix.</summary>
    internal static bool Matches(Guid id, string prefix)
    {
        return id.ToString("N").StartsWith(
            prefix.Replace("-", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
    }
}
