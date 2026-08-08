using System.Data.Common;
using EricksonLopez.Outbox.Persistence;

namespace Sample.OrderService.Infrastructure;

public static class TransactionExtensions
{
    public static IOutboxTransactionContext ToOutboxContext(this DbTransaction transaction)
    {
        return new OutboxTransactionContext(transaction.Connection!, transaction);
    }
}
