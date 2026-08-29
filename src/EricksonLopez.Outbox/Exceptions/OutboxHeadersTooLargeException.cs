// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Outbox;

/// <summary>
/// Thrown when message headers exceed the configured maximum headers size limit.
/// </summary>
public sealed class OutboxHeadersTooLargeException : OutboxException
{
    /// <summary>
    /// Gets the actual serialized headers size in bytes.
    /// </summary>
    public int ActualSize { get; }

    /// <summary>
    /// Gets the configured maximum allowed headers size in bytes.
    /// </summary>
    public int MaxAllowedSize { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxHeadersTooLargeException"/> class.
    /// </summary>
    /// <param name="actualSize">The actual size of the headers in bytes.</param>
    /// <param name="maxAllowedSize">The maximum allowed size for the headers in bytes.</param>
    public OutboxHeadersTooLargeException(int actualSize, int maxAllowedSize)
        : base($"Headers size {actualSize} bytes exceeds the configured maximum of {maxAllowedSize} bytes. " +
               "Reduce the number or size of message headers.")
    {
        ActualSize = actualSize;
        MaxAllowedSize = maxAllowedSize;
    }
}
