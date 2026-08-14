using System.Threading;

namespace CoderCommander.Viewers;

/// <summary>
/// Produces a <see cref="ViewerPayload"/> for one format. Runs on a thread-pool thread via
/// <c>Task.Run</c> - implementations must not touch any UI control, exactly like the
/// <c>static LoadFileCore</c> this replaces. Stateless: one instance is created per load
/// (<see cref="IViewerFormat.CreateLoader"/>), so implementations may keep local state during
/// <see cref="LoadAsync"/> without any cross-load leakage to guard against.
/// </summary>
public interface IViewerLoader
{
    Task<ViewerPayload> LoadAsync(ViewerSource source, CancellationToken ct);
}
