using MediatR;
using Sample.OrderService.Shared;

namespace Sample.OrderService.Application.Orders.Commands.CreateOrder;

public sealed record CreateOrderCommand(
    string CustomerId,
    decimal Total) : IRequest<Result<Guid>>;
