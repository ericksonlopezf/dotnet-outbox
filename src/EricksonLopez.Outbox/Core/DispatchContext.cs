using System;
using System.Threading;

namespace EricksonLopez.Outbox;

/// <summary>
/// Encapsulates contextual information for a message dispatch operation.
/// </summary>
public readonly struct DispatchContext
{
    /// <summary>
    /// Gets the cancellation token used to signal that the dispatch operation should be aborted.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Gets the current attempt number for this specific dispatch operation.
    /// </summary>
    public int Attempt { get; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="DispatchContext"/> struct.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the operation.</param>
    /// <param name="attempt">The current attempt number.</param>
    public DispatchContext(CancellationToken cancellationToken, int attempt)
    {
        CancellationToken = cancellationToken;
        Attempt = attempt;
    }
}
