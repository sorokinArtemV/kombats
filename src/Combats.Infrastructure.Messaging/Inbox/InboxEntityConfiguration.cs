using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Combats.Infrastructure.Messaging.Inbox;

public static class InboxEntityConfiguration
{
    public static void ConfigureInboxMessage(this EntityTypeBuilder<InboxMessage> entity)
    {
        entity.ToTable("inbox_messages");
        
        // Composite primary key: (MessageId, ConsumerId)
        entity.HasKey(e => new { e.MessageId, e.ConsumerId });
        
        entity.Property(e => e.MessageId).ValueGeneratedNever();
        entity.Property(e => e.ConsumerId).IsRequired().HasMaxLength(500);
        
        // Status as enum stored as integer
        entity.Property(e => e.Status)
            .HasConversion<int>()
            .IsRequired();
        
        entity.Property(e => e.ReceivedAt).IsRequired();
        entity.Property(e => e.ProcessedAt).IsRequired(false);
        entity.Property(e => e.ExpiresAt).IsRequired();
        
        entity.HasIndex(e => e.ExpiresAt).HasDatabaseName("ix_inbox_messages_expires_at");
        entity.HasIndex(e => e.Status).HasDatabaseName("ix_inbox_messages_status");
    }
}



