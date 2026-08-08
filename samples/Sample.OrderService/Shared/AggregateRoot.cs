using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
namespace Sample.OrderService.Shared;

public abstract class AggregateRoot
{
    private readonly List<object> _domainEvents = new();
    
    public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseDomainEvent(object domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }
}

