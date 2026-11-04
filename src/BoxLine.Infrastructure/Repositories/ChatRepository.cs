using BoxLine.Core.Entities;
using BoxLine.Core.Interfaces;
using BoxLine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BoxLine.Infrastructure.Repositories;

public sealed class ChatRepository(BoxLineDbContext db) : IChatRepository
{
    public async Task AddAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> ListAsync(int bookingId, int? beforeId, int limit, CancellationToken cancellationToken)
    {
        var messages = db.ChatMessages.AsNoTracking().Where(m => m.BookingId == bookingId);

        if (beforeId is { } before)
        {
            messages = messages.Where(m => m.Id < before);
        }

        var newest = await messages
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .Include(m => m.Sender)
            .ToListAsync(cancellationToken);

        newest.Reverse();
        return newest;
    }

    public Task<ChatMessage?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.ChatMessages.AsNoTracking().Include(m => m.Sender).FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
}
