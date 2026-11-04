namespace BoxLine.Core.Entities;

/// <summary>A message in the conversation attached to a booking.</summary>
public class ChatMessage
{
    public int Id { get; set; }

    public int BookingId { get; set; }

    public int SenderUserId { get; set; }

    public User? Sender { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime SentAt { get; set; }
}
