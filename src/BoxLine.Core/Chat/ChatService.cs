using BoxLine.Core.Common;
using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;

namespace BoxLine.Core.Chat;

/// <summary>The conversation attached to a booking. Only the booking's shipper and Ops may read or write it.</summary>
public sealed class ChatService(IChatRepository chat, IBookingRepository bookings, TimeProvider clock)
{
    public const int MaxTextLength = 2000;
    public const int DefaultHistory = 50;
    public const int MaxHistory = 200;

    /// <summary>Confirms the actor may take part in the booking's conversation.</summary>
    /// <exception cref="NotFoundException">The booking does not exist or belongs to another shipper.</exception>
    public async Task EnsureAccessAsync(Actor actor, int bookingId, CancellationToken cancellationToken)
    {
        var shipperId = await bookings.GetShipperIdAsync(bookingId, cancellationToken);

        // Someone else's booking looks exactly like a missing one.
        if (shipperId is null || (!actor.IsOps && shipperId != actor.UserId))
        {
            throw new NotFoundException($"Booking {bookingId} was not found.");
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> HistoryAsync(
        Actor actor, int bookingId, int? beforeId, int? limit, CancellationToken cancellationToken)
    {
        await EnsureAccessAsync(actor, bookingId, cancellationToken);
        return await chat.ListAsync(bookingId, beforeId, Math.Clamp(limit ?? DefaultHistory, 1, MaxHistory), cancellationToken);
    }

    public async Task<ChatMessage> SendAsync(Actor actor, int bookingId, string? text, CancellationToken cancellationToken)
    {
        await EnsureAccessAsync(actor, bookingId, cancellationToken);

        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new DomainValidationException("Text", "Message cannot be empty.");
        }

        if (trimmed.Length > MaxTextLength)
        {
            throw new DomainValidationException("Text", $"Message must be at most {MaxTextLength} characters.");
        }

        var message = new ChatMessage
        {
            BookingId = bookingId,
            SenderUserId = actor.UserId,
            Text = trimmed,
            SentAt = clock.GetUtcNow().UtcDateTime,
        };
        await chat.AddAsync(message, cancellationToken);

        return await chat.GetByIdAsync(message.Id, cancellationToken) ?? message;
    }
}
