// Copyright © Erickson Lopez. MIT License.
using System.Data.Common;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Defines a specialized transaction context for relational ADO.NET databases.
/// </summary>
public interface IRelationalOutboxTransactionContext : IOutboxTransactionContext
{
    /// <summary>Gets the strongly typed <see cref="DbConnection"/> associated with this context, or <see langword="null"/> if none is available.</summary>
    DbConnection? DbConnection { get; }

    /// <summary>Gets the strongly typed <see cref="DbTransaction"/> associated with this context, or <see langword="null"/> if none is active.</summary>
    DbTransaction? DbTransaction { get; }
}
