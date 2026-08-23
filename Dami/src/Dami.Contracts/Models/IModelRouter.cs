using Dami.Contracts.Context;

namespace Dami.Contracts.Models;

/// <summary>Chooses local or frontier for a piece of model work (§7.4).</summary>
/// <remarks>
/// The privacy boundary is a routing input, not a filter after the fact: implementations
/// MUST return <see cref="ModelTier.Local"/> for anything
/// <see cref="PrivacyClass.LocalOnly"/>, unconditionally. Everything else is policy.
/// </remarks>
public interface IModelRouter
{
    /// <summary>Routes one piece of work.</summary>
    ModelRoute Route(string workKind, PrivacyClass privacy);
}
