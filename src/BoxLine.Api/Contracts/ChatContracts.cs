using BoxLine.Core.Entities;

namespace BoxLine.Api.Contracts;

public sealed record ChatMessageResponse(
    int Id,
    int BookingId,
    int SenderUserId,
    string SenderName,
    UserRole SenderRole,
    string Text,
    DateTime SentAt)
{
    public static ChatMessageResponse From(ChatMessage message)
    {
        var sender = message.Sender;
        var name = sender is null
            ? "Unknown"
            : sender.Role == UserRole.Shipper ? sender.ShipperCompany ?? sender.Email : sender.Email;

        return new ChatMessageResponse(
            message.Id,
            message.BookingId,
            message.SenderUserId,
            name,
            sender?.Role ?? UserRole.Shipper,
            message.Text,
            DateTime.SpecifyKind(message.SentAt, DateTimeKind.Utc));
    }
}
