// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Result;
using MediatR;

namespace Sample.OrderService.Application.Orders.Commands.CreateOrder;

public sealed record CreateOrderCommand(
    string CustomerId,
    decimal Total) : IRequest<Result<Guid>>;




