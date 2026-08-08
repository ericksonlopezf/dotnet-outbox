namespace EricksonLopez.Outbox;

/// <summary>
/// Represents an exclusive lock acquired on a specific resource.
/// </summary>
/// <param name="ResourceId">The unique identifier of the locked resource.</param>
/// <param name="OwnerId">The unique identifier of the process or instance that acquired the lock.</param>
public readonly record struct Lock(
    string ResourceId,
    string OwnerId);
