using BoxLine.Core.Entities;

namespace BoxLine.Core.Interfaces;

public interface IChatRepository
{
    Task AddAsync(ChatMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// The newest <paramref name="limit"/> messages of a booking, returned oldest first, including the sender.
    /// When <paramref name="beforeId"/> is given, only messages older than that one.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> ListAsync(int bookingId, int? beforeId, int limit, CancellationToken cancellationToken);

    Task<ChatMessage?> GetByIdAsync(int id, CancellationToken cancellationToken);
}
