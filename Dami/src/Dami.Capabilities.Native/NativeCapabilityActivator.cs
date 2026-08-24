namespace Dami.Capabilities.Native;

/// <summary>Activates discovered native implementations into the execution catalog.</summary>
public sealed class NativeCapabilityActivator
{
    private readonly INativeCapabilityRegistrar registrar;

    /// <summary>Creates the activation handoff.</summary>
    public NativeCapabilityActivator(INativeCapabilityRegistrar registrar)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        this.registrar = registrar;
    }

    /// <summary>Resolves and registers each discovered native implementation exactly once.</summary>
    public void Activate(
        IReadOnlyList<NativeCapabilityRegistration> registrations,
        Func<Type, INativeCapabilityHandler?> factory)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(factory);
        foreach (var registration in registrations)
        {
            var handler = factory(registration.ImplementationType)
                ?? throw new InvalidOperationException(
                    $"Native capability implementation '{registration.ImplementationType}' is not available.");
            this.registrar.Register(registration.Entry.CapabilityId, handler);
        }
    }
}
