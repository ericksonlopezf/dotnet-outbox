// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Outbox;

/// <summary>
/// Thrown when a message payload exceeds the configured maximum payload size limit.
/// </summary>
public sealed class OutboxPayloadTooLargeException : OutboxException
{
    /// <summary>
    /// Gets the actual serialized payload size in bytes.
    /// </summary>
    public int ActualSize { get; }

    /// <summary>
    /// Gets the configured maximum allowed payload size in bytes.
    /// </summary>
    public int MaxAllowedSize { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxPayloadTooLargeException"/> class.
    /// </summary>
    /// <param name="actualSize">The actual size of the payload in bytes.</param>
    /// <param name="maxAllowedSize">The maximum allowed size for the payload in bytes.</param>
    public OutboxPayloadTooLargeException(int actualSize, int maxAllowedSize)
        : base($"Payload size {actualSize} bytes exceeds the configured maximum of {maxAllowedSize} bytes. " +
               "Consider offloading the payload to blob storage and storing only a reference in the outbox message.")
    {
        ActualSize = actualSize;
        MaxAllowedSize = maxAllowedSize;
    }
}
