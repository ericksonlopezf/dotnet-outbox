using MediatR;
using Sample.OrderService.Domain.Aggregates.OrderAggregate;
using Sample.OrderService.Infrastructure;
using Sample.OrderService.Shared;

namespace Sample.OrderService.Application.Orders.Commands.CreateOrder;

public sealed class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, Result<Guid>>
{
    private readonly AppDbContext _dbContext;

    public CreateOrderCommandHandler(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Guid>> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // 1. Instantiate the business aggregate (this generates the OrderCreatedEvent internally)
        var order = Order.Create(request.CustomerId, request.Total);

        // 2. Add it to the context
        _dbContext.Orders.Add(order);

        // 3. Save the changes. 
        // Magic!: The `PublishDomainEventsInterceptor` interceptor will catch the event 
        // and write it to the IOutbox using the same DbTransaction before the COMMIT.
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(order.Id);
    }
}
