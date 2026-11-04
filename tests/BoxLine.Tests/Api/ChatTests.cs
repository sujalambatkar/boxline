using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BoxLine.Api.Contracts;
using BoxLine.Core.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace BoxLine.Tests.Api;

/// <summary>The booking chat: the history endpoint and the SignalR hub, including who may join.</summary>
[Collection(ApiCollection.Name)]
public class ChatTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private readonly List<HubConnection> connections = [];
    private HttpClient ops = null!;

    public async Task InitializeAsync() => ops = await factory.CreateOpsClientAsync();

    public async Task DisposeAsync()
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    /// <summary>Collects the messages a connection receives and lets a test wait for the next ones.</summary>
    private sealed class Inbox
    {
        private readonly List<ChatMessageResponse> messages = [];
        private readonly SemaphoreSlim arrived = new(0);

        public IReadOnlyList<ChatMessageResponse> Messages
        {
            get { lock (messages) { return messages.ToList(); } }
        }

        public void Add(ChatMessageResponse message)
        {
            lock (messages) { messages.Add(message); }
            arrived.Release();
        }

        public async Task<ChatMessageResponse> NextAsync()
        {
            (await arrived.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("a message should have arrived");
            lock (messages) { return messages[^1]; }
        }

        public async Task NothingMoreAsync() =>
            (await arrived.WaitAsync(TimeSpan.FromMilliseconds(600))).Should().BeFalse("no further message was expected");
    }

    private async Task<(HubConnection Connection, Inbox Inbox)> ConnectAsync(HttpClient signedIn)
    {
        var token = signedIn.DefaultRequestHeaders.Authorization!.Parameter!;
        var inbox = new Inbox();

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        connection.On<ChatMessageResponse>("MessageReceived", inbox.Add);

        await connection.StartAsync();
        connections.Add(connection);
        return (connection, inbox);
    }

    private static async Task<List<ChatMessageResponse>> HistoryAsync(HttpClient client, int bookingId, string query = "") =>
        (await client.GetFromJsonAsync<List<ChatMessageResponse>>($"/api/bookings/{bookingId}/messages{query}", TestJson.Options))!;

    [Fact]
    public async Task TheShipperAndOps_ChatInRealTime_AndMessagesCarryWhoSentThem()
    {
        var shipper = await factory.RegisterShipperAsync("Blue Ocean Traders");
        var booking = await TestData.ConfirmedBookingAsync(shipper);
        var (shipperHub, shipperInbox) = await ConnectAsync(shipper.Client);
        var (opsHub, opsInbox) = await ConnectAsync(ops);
        await shipperHub.InvokeAsync("JoinBooking", booking.Id);
        await opsHub.InvokeAsync("JoinBooking", booking.Id);

        await shipperHub.InvokeAsync("SendMessage", booking.Id, "  When will the container be ready?  ");
        var atOps = await opsInbox.NextAsync();
        var echoed = await shipperInbox.NextAsync();

        atOps.Text.Should().Be("When will the container be ready?");
        atOps.SenderName.Should().Be("Blue Ocean Traders");
        atOps.SenderRole.Should().Be(UserRole.Shipper);
        atOps.SenderUserId.Should().Be(shipper.User.Id);
        atOps.BookingId.Should().Be(booking.Id);
        atOps.SentAt.Kind.Should().Be(DateTimeKind.Utc);
        echoed.Id.Should().Be(atOps.Id);

        await opsHub.InvokeAsync("SendMessage", booking.Id, "Tomorrow morning.");
        var reply = await shipperInbox.NextAsync();
        reply.Text.Should().Be("Tomorrow morning.");
        reply.SenderRole.Should().Be(UserRole.Ops);
    }

    [Fact]
    public async Task Messages_ArePersisted_AndServedOldestFirstThroughTheHistoryEndpoint()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);
        var (hub, inbox) = await ConnectAsync(shipper.Client);
        await hub.InvokeAsync("JoinBooking", booking.Id);
        (await HistoryAsync(shipper.Client, booking.Id)).Should().BeEmpty();

        foreach (var text in new[] { "one", "two", "three" })
        {
            await hub.InvokeAsync("SendMessage", booking.Id, text);
            await inbox.NextAsync();
        }

        (await HistoryAsync(shipper.Client, booking.Id)).Select(m => m.Text).Should().Equal("one", "two", "three");
        (await HistoryAsync(ops, booking.Id)).Should().HaveCount(3);

        var lastTwo = await HistoryAsync(shipper.Client, booking.Id, "?limit=2");
        lastTwo.Select(m => m.Text).Should().Equal("two", "three");
        var earlier = await HistoryAsync(shipper.Client, booking.Id, $"?beforeId={lastTwo[0].Id}");
        earlier.Select(m => m.Text).Should().Equal("one");
    }

    [Fact]
    public async Task AnotherShipper_CannotJoinReadOrWriteTheConversation()
    {
        var owner = await factory.RegisterShipperAsync();
        var intruder = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(owner);
        var (ownerHub, ownerInbox) = await ConnectAsync(owner.Client);
        var (intruderHub, intruderInbox) = await ConnectAsync(intruder.Client);
        await ownerHub.InvokeAsync("JoinBooking", booking.Id);

        var join = () => intruderHub.InvokeAsync("JoinBooking", booking.Id);
        var send = () => intruderHub.InvokeAsync("SendMessage", booking.Id, "let me in");

        (await join.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("do not have access");
        (await send.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("do not have access");
        (await intruder.Client.GetAsync($"/api/bookings/{booking.Id}/messages")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await ownerHub.InvokeAsync("SendMessage", booking.Id, "private");
        await ownerInbox.NextAsync();
        await intruderInbox.NothingMoreAsync();
        (await HistoryAsync(owner.Client, booking.Id)).Should().ContainSingle().Which.Text.Should().Be("private");
    }

    [Fact]
    public async Task SendingWithoutJoining_StillRequiresAccess_AndOnlyJoinedConnectionsReceive()
    {
        var owner = await factory.RegisterShipperAsync();
        var bystander = await factory.RegisterShipperAsync();
        var ownersBooking = await TestData.ConfirmedBookingAsync(owner);
        var bystandersBooking = await TestData.ConfirmedBookingAsync(bystander);
        var (ownerHub, ownerInbox) = await ConnectAsync(owner.Client);
        var (bystanderHub, bystanderInbox) = await ConnectAsync(bystander.Client);
        await bystanderHub.InvokeAsync("JoinBooking", bystandersBooking.Id);

        // The owner never called JoinBooking but is entitled to the booking: the message is stored, and
        // nobody else's connection hears it.
        await ownerHub.InvokeAsync("SendMessage", ownersBooking.Id, "hello");

        await bystanderInbox.NothingMoreAsync();
        await ownerInbox.NothingMoreAsync();
        (await HistoryAsync(owner.Client, ownersBooking.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task LeavingABooking_StopsDelivery()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);
        var (hub, inbox) = await ConnectAsync(shipper.Client);
        var (opsHub, _) = await ConnectAsync(ops);
        await hub.InvokeAsync("JoinBooking", booking.Id);
        await opsHub.InvokeAsync("JoinBooking", booking.Id);

        await hub.InvokeAsync("LeaveBooking", booking.Id);
        await opsHub.InvokeAsync("SendMessage", booking.Id, "after you left");

        await inbox.NothingMoreAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public async Task BlankMessages_AreRejected(string text)
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);
        var (hub, _) = await ConnectAsync(shipper.Client);

        var send = () => hub.InvokeAsync("SendMessage", booking.Id, text);

        (await send.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("cannot be empty");
        (await HistoryAsync(shipper.Client, booking.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task OverlongMessages_AreRejected_ButTheLimitItselfIsAccepted()
    {
        var shipper = await factory.RegisterShipperAsync();
        var booking = await TestData.ConfirmedBookingAsync(shipper);
        var (hub, inbox) = await ConnectAsync(shipper.Client);
        await hub.InvokeAsync("JoinBooking", booking.Id);

        await hub.InvokeAsync("SendMessage", booking.Id, new string('x', 2000));
        (await inbox.NextAsync()).Text.Should().HaveLength(2000);

        var tooLong = () => hub.InvokeAsync("SendMessage", booking.Id, new string('x', 2001));
        (await tooLong.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("at most 2000");
    }

    [Fact]
    public async Task AnUnknownBooking_LooksTheSameAsSomeoneElsesBooking()
    {
        var shipper = await factory.RegisterShipperAsync();
        var (hub, _) = await ConnectAsync(shipper.Client);

        var join = () => hub.InvokeAsync("JoinBooking", 999_999);

        (await join.Should().ThrowAsync<HubException>()).Which.Message.Should().Contain("do not have access");
        (await shipper.Client.GetAsync("/api/bookings/999999/messages")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Connecting_RequiresAValidToken()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "/hubs/chat"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        connections.Add(connection);

        var start = () => connection.StartAsync();

        await start.Should().ThrowAsync<HttpRequestException>().WithMessage("*401*");
        (await factory.CreateClient().GetAsync("/api/bookings/1/messages")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
