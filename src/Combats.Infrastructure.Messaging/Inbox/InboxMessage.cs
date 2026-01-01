using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Combats.Infrastructure.Messaging.Inbox;

[Table("inbox_messages")]
public class InboxMessage
{
    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Required]
    [Column("consumer_id")]
    [MaxLength(500)]
    public string ConsumerId { get; set; } = string.Empty;

    [Required]
    [Column("status")]
    public InboxMessageStatus Status { get; set; } = InboxMessageStatus.Received;

    [Required]
    [Column("received_at")]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    [Required]
    [Column("expires_at")]
    public DateTime ExpiresAt { get; set; }
}



