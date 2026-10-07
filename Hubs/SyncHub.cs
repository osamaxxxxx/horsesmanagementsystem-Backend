using System.Collections.Concurrent;
using HorseVillage.Dtos;
using HorseVillage.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HorseVillage.Hubs;

// Server-to-client only. Events: PlotUpserted(PlotDto), PlotDeleted(Guid), ProfileChanged(), AccessRevoked({ reason }).
[Authorize]
public class SyncHub(PresenceTracker presence) : Hub
{
    public const string SyncGroup = "sync";

    public override async Task OnConnectedAsync()
    {
        presence.Add(Context);
        await Groups.AddToGroupAsync(Context.ConnectionId, SyncGroup);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        presence.Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public class PresenceTracker
{
    public record Connection(Guid UserId, Guid SessionId, HubCallerContext Context);

    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    public void Add(HubCallerContext context) =>
        _connections[context.ConnectionId] = new Connection(context.User!.GetUserId(), context.User!.GetSessionId(), context);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    public bool IsUserOnline(Guid userId) => _connections.Values.Any(c => c.UserId == userId);

    public bool IsSessionOnline(Guid sessionId) => _connections.Values.Any(c => c.SessionId == sessionId);

    public List<Connection> ForUser(Guid userId) => _connections.Values.Where(c => c.UserId == userId).ToList();

    public List<Connection> ForSession(Guid sessionId) => _connections.Values.Where(c => c.SessionId == sessionId).ToList();
}

public class SyncNotifier(IHubContext<SyncHub> hub, PresenceTracker presence)
{
    public const string AccessRevoked = "ACCESS_REVOKED";
    public const string SessionEnded = "SESSION_ENDED";

    public Task PlotUpsertedAsync(PlotDto plot) => hub.Clients.Group(SyncHub.SyncGroup).SendAsync("PlotUpserted", plot);

    public Task PlotDeletedAsync(Guid plotId) => hub.Clients.Group(SyncHub.SyncGroup).SendAsync("PlotDeleted", plotId);

    // Tells a user's open screens that their name or role changed, so they reload their profile.
    public Task ProfileChangedAsync(Guid userId) =>
        hub.Clients.Clients(presence.ForUser(userId).Select(c => c.Context.ConnectionId)).SendAsync("ProfileChanged");

    public Task RevokeUserAsync(Guid userId) => KickAsync(presence.ForUser(userId), AccessRevoked);

    public Task RevokeSessionAsync(Guid sessionId) => KickAsync(presence.ForSession(sessionId), SessionEnded);

    private async Task KickAsync(List<PresenceTracker.Connection> connections, string reason)
    {
        if (connections.Count == 0) return;

        var ids = connections.Select(c => c.Context.ConnectionId).ToList();
        // Stop data broadcasts first, then tell the client to lock its screen and wipe its cache.
        foreach (var id in ids)
            await hub.Groups.RemoveFromGroupAsync(id, SyncHub.SyncGroup);
        await hub.Clients.Clients(ids).SendAsync("AccessRevoked", new { reason });

        // Give the notice a moment to flush before dropping the socket.
        _ = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ =>
        {
            foreach (var connection in connections)
                connection.Context.Abort();
        });
    }
}
