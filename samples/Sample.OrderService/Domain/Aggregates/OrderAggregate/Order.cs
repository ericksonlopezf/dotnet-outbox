using Sample.OrderService.Shared;

namespace Sample.OrderService.Domain.Aggregates.OrderAggregate;

public sealed class Order : AggregateRoot
{
    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = default!;
    public decimal Total { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Order() { } // For EF Core

    public static Order Create(string customerId, decimal total)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Total = total,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Generate the pure domain event (the interceptor will later save it to the Outbox)
        order.RaiseDomainEvent(new OrderCreatedEvent(
            order.Id,
            order.CustomerId,
            order.Total,
            order.CreatedAt));

        return order;
    }
}
