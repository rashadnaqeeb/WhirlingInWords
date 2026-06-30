using DiscoAccess.Core.World.Overlays;
using FortressOccident;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.AI;
using Snv = System.Numerics.Vector3;

namespace DiscoAccess.Module.World
{
    /// <summary>
    /// The <see cref="IWorldEnvironment"/> over the live game: the overlay framework reads the player's
    /// position, whether the player has control, and the navmesh clamp through here. This is the thin
    /// engine-touching adapter the Core world layer is kept free of; it converts Unity's <c>Vector3</c> to
    /// <see cref="System.Numerics.Vector3"/> at the boundary so no Unity type crosses into Core.
    /// </summary>
    internal sealed class WorldEnvironment : IWorldEnvironment
    {
        /// <summary>The main party character's live transform position (the readout origin). The transform
        /// is the freshest source — the data position lags it during a move — and Zero before a game loads.</summary>
        public Snv PlayerPosition
        {
            get
            {
                Character main = Main;
                return main != null ? WorldConvert.ToSnv(main.transform.position) : default;
            }
        }

        /// <summary>The player controls the character when one exists and no conversation is up. The world
        /// reader already only ticks on the in-game (LOBBY) view, so this is the finer cutscene/dialogue
        /// gate on top of that.</summary>
        public bool HasControl => Main != null && !DialogueManager.isConversationActive;

        /// <summary>Clamp a glide onto walkable ground: stop at the first navmesh boundary between the points,
        /// else snap the target onto the mesh so the cursor never leaves the floor.</summary>
        public Snv TraceMove(Snv from, Snv intended)
        {
            Vector3 f = WorldConvert.ToUnity(from), t = WorldConvert.ToUnity(intended);
            if (NavMesh.Raycast(f, t, out NavMeshHit boundary, AllAreas))
                return WorldConvert.ToSnv(boundary.position);
            if (NavMesh.SamplePosition(t, out NavMeshHit snapped, 1.5f, AllAreas))
                return WorldConvert.ToSnv(snapped.position);
            return intended;
        }

        // NavMesh.AllAreas (-1, every area in the mask); the const isn't surfaced on the interop proxy.
        private const int AllAreas = -1;

        private static Character Main
        {
            get { Party party = Party.Player; return party != null ? party.Main : null; }
        }
    }
}
