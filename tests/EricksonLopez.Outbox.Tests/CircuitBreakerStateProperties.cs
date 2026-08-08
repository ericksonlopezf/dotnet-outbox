using System;
using EricksonLopez.Outbox.Retry;
using FsCheck;
using FsCheck.Xunit;

namespace EricksonLopez.Outbox.Tests;

public class CircuitBreakerStateProperties
{
    // A test that models sequences of success and failure calls and ensures the state machine acts correctly.
    [Property]
    public bool CircuitBreaker_Transitions_Correctly(int failureThreshold, bool[] requestResults)
    {
        // Ignore edge cases that don't make sense for testing the state machine
        if (failureThreshold <= 0 || requestResults == null || requestResults.Length == 0)
        {
            return true;
        }

        var cb = new CircuitBreakerState(failureThreshold, TimeSpan.FromDays(1));
        int consecutiveFailures = 0;

        foreach (var result in requestResults)
        {
            if (cb.State == CircuitState.Open)
            {
                // If it's open and the timeout hasn't elapsed (1 day), it must not allow requests.
                if (cb.AllowRequest()) return false;
            }
            else if (cb.State == CircuitState.Closed)
            {
                if (result)
                {
                    cb.RecordSuccess();
                    consecutiveFailures = 0;
                }
                else
                {
                    cb.RecordFailure();
                    consecutiveFailures++;
                    if (consecutiveFailures >= failureThreshold && cb.State != CircuitState.Open)
                    {
                        return false; // Should be open now
                    }
                }
            }
        }

        return true;
    }
}


