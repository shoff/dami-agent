using System.Text.RegularExpressions;
using Dami.Core.TaskBoard;

namespace Dami.Core.BoardImport;

/// <summary>
/// Deterministic identities for imported tasks, so rerunning an import updates the board
/// it already wrote instead of creating a second one.
/// </summary>
/// <remarks>
/// Keyed on the file's own task id where there is one, because that is what survives the
/// entry being reworded or moved. An entry with no id falls back to its section and its
/// normalized title, which survives reordering but not rewording; the importer reports
/// every such entry rather than letting an unstable identity pass unnoticed.
/// </remarks>
public static partial class BoardImportIds
{
    /// <summary>Fixes this importer's id space. Changing it re-creates every board.</summary>
    private static readonly Guid importNamespace = new("3d0f6c2e-8a41-4b57-9c6d-5e1f7a2b8c30");

    /// <summary>The board's deterministic id.</summary>
    /// <param name="boardKey">The board key.</param>
    /// <returns>Its id.</returns>
    public static Guid Board(string boardKey)
    {
        return StablePlanningId.Create(importNamespace, $"board:{boardKey}");
    }

    /// <summary>An epic section's deterministic id.</summary>
    /// <param name="boardKey">The board key.</param>
    /// <param name="sectionKey">The section letter.</param>
    /// <returns>Its id.</returns>
    public static Guid Section(string boardKey, string sectionKey)
    {
        return StablePlanningId.Create(importNamespace, $"section:{boardKey}:{sectionKey}");
    }

    /// <summary>The board id of the task the file calls <paramref name="todoId"/>.</summary>
    /// <param name="boardKey">The board key.</param>
    /// <param name="todoId">The file's task id, such as <c>G5a1</c>.</param>
    /// <returns>Its id.</returns>
    public static Guid Task(string boardKey, string todoId)
    {
        return StablePlanningId.Create(importNamespace, $"task:{boardKey}:{todoId}");
    }

    /// <summary>An identity for an entry the file did not name.</summary>
    /// <param name="boardKey">The board key.</param>
    /// <param name="sectionKey">The section it sits in.</param>
    /// <param name="title">Its title, normalized to form the key.</param>
    /// <returns>Its id.</returns>
    public static Guid Derived(string boardKey, string sectionKey, string title)
    {
        return StablePlanningId.Create(importNamespace, $"derived:{boardKey}:{sectionKey}:{Normalize(title)}");
    }

    /// <summary>An acceptance criterion's deterministic id.</summary>
    /// <param name="taskId">The task it belongs to.</param>
    /// <param name="position">Its order within that task.</param>
    /// <returns>Its id.</returns>
    public static Guid Criterion(Guid taskId, int position)
    {
        return StablePlanningId.Create(importNamespace, $"criterion:{taskId:N}:{position}");
    }

    private static string Normalize(string title)
    {
        var collapsed = WhitespacePattern().Replace(title, " ").Trim().ToLowerInvariant();
        return collapsed.Length <= 120 ? collapsed : collapsed[..120];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
