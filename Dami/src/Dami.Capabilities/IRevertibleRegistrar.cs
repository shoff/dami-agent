namespace Dami.Capabilities;

/// <summary>Publishes one instance and can remove only that same instance.</summary>
/// <typeparam name="TRegistration">The immutable registration type.</typeparam>
public interface IRevertibleRegistrar<in TRegistration>
    where TRegistration : class
{
    /// <summary>Publishes one registration or throws when its key is occupied.</summary>
    void Register(TRegistration registration);

    /// <summary>Removes the registration only while the same object remains published.</summary>
    bool TryRemoveExact(TRegistration registration);
}
