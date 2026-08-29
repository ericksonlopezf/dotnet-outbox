// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace EricksonLopez.Outbox.EntityFrameworkCore;

/// <summary>
/// Provides extension methods for configuring Entity Framework Core entity mappings for the outbox tables.
/// </summary>
public static class OutboxModelBuilderExtensions
{
    /// <summary>
    /// Applies entity type configurations for <see cref="Entities.OutboxMessageEntity"/>,
    /// <see cref="Entities.IdempotencyRecordEntity"/>, and <see cref="Entities.DeadLetterMessageEntity"/>
    /// to the provided <see cref="ModelBuilder"/>.
    /// </summary>
    /// <param name="modelBuilder">The EF Core model builder to configure.</param>
    /// <param name="schema">The database schema that contains the outbox tables. Defaults to <c>"outbox"</c>.</param>
    /// <returns>The same <see cref="ModelBuilder"/> instance to allow further fluent configuration calls.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="modelBuilder"/> is <see langword="null"/>.</exception>
    public static ModelBuilder ApplyOutboxEntityConfigurations(
        this ModelBuilder modelBuilder,
        string schema = "outbox")
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // --- 1. OutboxMessageEntity Mapping ---
        modelBuilder.Entity<OutboxMessageEntity>(builder =>
        {
            builder.ToTable("messages", schema);


            builder.HasKey(m => m.Id);

            builder.Property(m => m.Id)
                .ValueGeneratedNever();

            builder.Property(m => m.MessageType)
                .HasColumnName("type")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(m => m.Payload)
                .HasColumnName("payload")
                .IsRequired();

            builder.Property(m => m.CorrelationId)
                .HasColumnName("correlation_id")
                .HasMaxLength(255);

            builder.Property(m => m.CausationId)
                .HasColumnName("causation_id")
                .HasMaxLength(255);

            builder.Property(m => m.HeadersJson)
                .HasColumnName("headers_json")
                .HasDefaultValue("{}");

            builder.Property(m => m.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            builder.Property(m => m.ProcessedAt)
                .HasColumnName("processed_at");

            builder.Property(m => m.DeliverAt)
                .HasColumnName("deliver_at");

            builder.Property(m => m.State)
                .HasColumnName("state")
                .IsRequired();

            builder.Property(m => m.RetryCount)
                .HasColumnName("retry_count")
                .IsRequired();

            builder.Property(m => m.Error)
                .HasColumnName("error");

            builder.HasIndex(m => new { m.State, m.CreatedAt })
                .HasDatabaseName("idx_outbox_messages_state_created");
        });

        // --- 2. IdempotencyRecordEntity Mapping ---
        modelBuilder.Entity<IdempotencyRecordEntity>(builder =>
        {
            builder.ToTable("idempotency", schema);

            builder.HasKey(r => new { r.MessageId, r.ConsumerId });

            builder.Property(r => r.MessageId)
                .HasColumnName("message_id")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(r => r.ConsumerId)
                .HasColumnName("consumer_id")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(r => r.ProcessedAt)
                .HasColumnName("processed_at")
                .IsRequired();
        });

        // --- 3. DeadLetterMessageEntity Mapping ---
        modelBuilder.Entity<DeadLetterMessageEntity>(builder =>
        {
            builder.ToTable("dead_letters", schema);


            builder.HasKey(d => d.Id);

            builder.Property(d => d.Id)
                .ValueGeneratedNever();

            builder.Property(d => d.OriginalMessageId)
                .HasColumnName("original_message_id")
                .IsRequired();

            builder.Property(d => d.MessageType)
                .HasColumnName("message_type")
                .HasMaxLength(255)
                .IsRequired();

            builder.Property(d => d.Payload)
                .HasColumnName("payload")
                .IsRequired();

            builder.Property(d => d.CorrelationId)
                .HasColumnName("correlation_id")
                .HasMaxLength(255);

            builder.Property(d => d.CausationId)
                .HasColumnName("causation_id")
                .HasMaxLength(255);

            builder.Property(d => d.HeadersJson)
                .HasColumnName("headers_json")
                .HasDefaultValue("{}");

            builder.Property(d => d.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            builder.Property(d => d.DeadLetteredAt)
                .HasColumnName("dead_lettered_at")
                .IsRequired();

            builder.Property(d => d.RetryCount)
                .HasColumnName("retry_count")
                .IsRequired();

            builder.Property(d => d.Reason)
                .HasColumnName("reason")
                .HasMaxLength(500);

            builder.Property(d => d.LastError)
                .HasColumnName("last_error");
        });

        return modelBuilder;
    }
}

