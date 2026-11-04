using BoxLine.Api.Auth;
using BoxLine.Api.Contracts;
using BoxLine.Core.Chat;
using BoxLine.Core.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BoxLine.Api.Hubs;

/// <summary>Messages the server pushes to connected clients.</summary>
public interface IChatClient
{
    Task MessageReceived(ChatMessageResponse message);
}

/// <summary>
/// Live chat per booking. A connection receives a booking's messages only after joining its group, and joining
/// is allowed only for the booking's shipper and Ops. Sending re-checks access, so a group membership is never
/// the only thing standing between a user and a booking.
/// </summary>
[Authorize]
public sealed class ChatHub(ChatService chat) : Hub<IChatClient>
{
    public const string Path = "/hubs/chat";

    public static string GroupName(int bookingId) => $"booking-{bookingId}";

    public async Task JoinBooking(int bookingId)
    {
        await Guard(() => chat.EnsureAccessAsync(Context.User!.ToActor(), bookingId, Context.ConnectionAborted));
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(bookingId), Context.ConnectionAborted);
    }

    public Task LeaveBooking(int bookingId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(bookingId), Context.ConnectionAborted);

    public async Task SendMessage(int bookingId, string text)
    {
        var message = await Guard(() => chat.SendAsync(Context.User!.ToActor(), bookingId, text, Context.ConnectionAborted));
        await Clients.Group(GroupName(bookingId)).MessageReceived(ChatMessageResponse.From(message));
    }

    /// <summary>Hub methods report failures as HubException so the client sees a clean message and nothing internal.</summary>
    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (NotFoundException)
        {
            throw new HubException("You do not have access to this booking.");
        }
        catch (DomainValidationException ex)
        {
            throw new HubException(ex.Errors.Values.SelectMany(v => v).First());
        }
    }

    private static Task Guard(Func<Task> action) => Guard(async () =>
    {
        await action();
        return true;
    });
}
