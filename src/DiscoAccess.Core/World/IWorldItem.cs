using System.Numerics;

namespace DiscoAccess.Core.World
{
    /// <summary>
    /// The sensing-facing view of one thing in the world: what the cursor readout, sonar, and scanner read.
    /// Implemented by a thin Module proxy over a live game object, so every property reads live (the "never
    /// cache game state" rule) and no Unity type crosses into Core - the proxy converts to
    /// <see cref="System.Numerics.Vector3"/> and the Core <see cref="ScanBounds"/> at the boundary.
    /// </summary>
    public interface IWorldItem
    {
        /// <summary>A display name (a clean object name, or an orb's text). May be empty.</summary>
        string Name { get; }

        /// <summary>The thing's live world centre (where the cursor would snap).</summary>
        Vector3 Position { get; }

        /// <summary>The thing's spatial extent, for distance/bearing to its nearest part.</summary>
        ScanBounds Bounds { get; }

        /// <summary>The <see cref="WorldTaxonomy"/> category key this thing sounds and lists under.</summary>
        string Category { get; }

        /// <summary>The actionability gate: whether a sighted player with this build could act on it now
        /// (equipment, passive checks, reachability all folded in). The signal that separates the ~90
        /// actionable things from the ~400 entities of clutter.</summary>
        bool IsAccessible { get; }

        /// <summary>Whether the player could currently know about this thing (revealed, streamed in).</summary>
        bool IsVisible { get; }

        /// <summary>Trigger the game's interaction for this thing (auto-path and act). Returns whether
        /// something was triggered.</summary>
        bool Interact();
    }
}
