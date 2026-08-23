namespace Dami.Contracts.Proactive;

/// <summary>Exclusive, expiring ownership of one proactive service run.</summary>
public interface IProactiveRunLease : IAsyncDisposable;
