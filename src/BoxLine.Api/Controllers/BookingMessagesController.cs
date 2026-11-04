using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Api.Controllers;

/// <summary>Chat history for a booking. New messages travel over the SignalR hub.</summary>
[ApiController]
[Authorize]
[Route("api/bookings/{bookingId:int}/messages")]
public sealed class BookingMessagesController(ChatService chat) : ControllerBase
{
    /// <summary>The newest messages, oldest first. Pass <paramref name="beforeId"/> to page further back.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChatMessageResponse>>> History(
        int bookingId, [FromQuery] int? beforeId, [FromQuery] int? limit, CancellationToken ct)
    {
        var messages = await chat.HistoryAsync(User.ToActor(), bookingId, beforeId, limit, ct);
        return messages.Select(ChatMessageResponse.From).ToList();
    }
}
