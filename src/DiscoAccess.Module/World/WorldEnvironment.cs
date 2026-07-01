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

        /// <summary>Clamp a glide onto walkable ground: on hitting a navmesh boundary, hop the cursor across
        /// it to the ground beyond when the block is small debris the character can still round cheaply (see
        /// <see cref="TrySkipBoundary"/>), else stop at the boundary; with no boundary between the points, snap
        /// the target onto the mesh so the cursor never leaves the floor.</summary>
        public Snv TraceMove(Snv from, Snv intended)
        {
            Vector3 f = WorldConvert.ToUnity(from), t = WorldConvert.ToUnity(intended);
            if (NavMesh.Raycast(f, t, out NavMeshHit boundary, AllAreas))
            {
                Vector3 dir = t - f; dir.y = 0f;
                float len = dir.magnitude;
                if (len > 1e-4f && TrySkipBoundary(boundary.position, dir / len, out Vector3 resume))
                    return WorldConvert.ToSnv(resume);
                return WorldConvert.ToSnv(boundary.position);
            }
            if (NavMesh.SamplePosition(t, out NavMeshHit snapped, 1.5f, AllAreas))
                return WorldConvert.ToSnv(snapped.position);
            return intended;
        }

        /// <summary>Distance to the first navmesh boundary along a cardinal, for the wall tones: cast a
        /// navmesh ray out to <paramref name="range"/> and measure the planar gap to the hit, or report
        /// <paramref name="range"/> (no wall, silent) when the ray reaches the end unobstructed. Boundaries the
        /// cursor would hop (small debris; see <see cref="TrySkipBoundary"/>) are seen through and the cast
        /// continues beyond them, so the tone sounds only real walls and never contradicts the cursor. The
        /// cursor is navmesh-clamped, so an off-mesh origin (where Raycast would misbehave) does not arise in
        /// play.</summary>
        public float WallDistance(Snv from, Snv direction, float range)
        {
            Vector3 origin = WorldConvert.ToUnity(from);
            Vector3 dir = WorldConvert.ToUnity(direction); // unit cardinal
            Vector3 castFrom = origin;
            for (int hops = 0; hops <= MaxSeeThrough; hops++)
            {
                // Cast only the range still unspent, measured radially from the original origin so a
                // laterally-snapped resume point can't inflate the reported distance.
                float remaining = range - Planar(origin, castFrom);
                if (remaining <= 0f) return range;
                if (!NavMesh.Raycast(castFrom, castFrom + dir * remaining, out NavMeshHit hit, AllAreas))
                    return range;
                float dist = Planar(origin, hit.position);
                if (dist >= range) return range;
                if (TrySkipBoundary(hit.position, dir, out Vector3 resume)) { castFrom = resume; continue; }
                return dist;
            }
            return range; // saw through the hop cap without a real wall: treat as clear
        }

        // Can the cursor hop a navmesh boundary at <paramref name="boundary"/> travelling along unit
        // <paramref name="dir"/>? March past it up to SkipProbeDistance for where the mesh resumes, then take
        // the gap only when a complete path from the boundary to that ground exists and is no longer than
        // SkipDetourRatio times the straight hop - so small debris (a short walk-around) is skipped while a
        // thin wall with ground close behind it (a long walk-around, or no path at all) is not. The single
        // source of truth for "passable" shared by the cursor clamp and the wall-tone cast, so they never
        // disagree. Tuning constants below are hot-reloadable (F6): a pure-module edit re-lands them live.
        private static bool TrySkipBoundary(Vector3 boundary, Vector3 dir, out Vector3 resume)
        {
            resume = default;
            for (float t = ProbeStep; t <= SkipProbeDistance; t += ProbeStep)
            {
                if (!NavMesh.SamplePosition(boundary + dir * t, out NavMeshHit r, ProbeRadius, AllAreas))
                    continue;
                float gap = Vector3.Distance(boundary, r.position);
                if (gap <= MinGap) continue; // still snapping to the near edge: keep marching past the gap
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(boundary, r.position, AllAreas, path)) return false;
                if (path.status != NavMeshPathStatus.PathComplete) return false;
                if (PathLength(path) > gap * SkipDetourRatio) return false;
                resume = r.position;
                return true;
            }
            return false;
        }

        private static float PathLength(NavMeshPath path)
        {
            float len = 0f;
            var c = path.corners;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        private static float Planar(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // A stable lock token. While it sits in the camera's lock set the game's own camera logic - pan, zoom,
        // and the recenter-on-character that otherwise pulls the view back between our focuses - is frozen, so
        // our SetFocus is the only thing moving the camera. Confirmed live: SetFocus still drives the camera
        // while locked. One per environment instance; released on leaving the world (and on module teardown,
        // via the overlay's exit), so it never leaks a frozen camera.
        private readonly Il2CppSystem.Object _camLock = new Il2CppSystem.Object();

        /// <summary>Hold the camera on a world point so the orb streamer wakes the orbs around it. Takes the
        /// camera lock once (re-added if the controller was swapped on an area change, checked live) so the
        /// game stops reclaiming the view, then snaps the focus with instant=true so the frustum updates this
        /// frame rather than tweening; the empty zoom keeps the current zoom. A no-op before the camera exists
        /// (early boot) - a not-ready state, not a failure, so it is silent like the player-position cold read.</summary>
        public void FocusCamera(Snv point)
        {
            CameraController cam = CameraController.Current;
            if (cam == null) return;
            if (!cam.CheckLock(_camLock)) cam.AddLock(_camLock);
            cam.SetFocus(WorldConvert.ToUnity(point), new Il2CppSystem.Nullable<float>(), true);
        }

        /// <summary>Release the camera lock, handing the camera back to the game (which recenters on the
        /// character). Idempotent: only removes the lock when this controller actually holds it.</summary>
        public void ReleaseCamera()
        {
            CameraController cam = CameraController.Current;
            if (cam != null && cam.CheckLock(_camLock)) cam.RemoveLock(_camLock);
        }

        // NavMesh.AllAreas (-1, every area in the mask); the const isn't surfaced on the interop proxy.
        private const int AllAreas = -1;

        // Cursor debris-skip tuning (see TrySkipBoundary). Chosen by profiling Martinaise's navmesh: at a ~1 m
        // gap the boundaries are still thin seams and genuinely small debris (all measuring a tight sub-1.8
        // detour), while a detour within 2x the straight hop separates that debris from a thin wall with ground
        // close behind it. Wider than this reads as a leap rather than a hop.
        private const float SkipProbeDistance = 1.0f; // widest gap (metres) the cursor will hop
        private const float SkipDetourRatio = 2.0f;   // max walk-around length as a multiple of the straight hop
        private const float ProbeStep = 0.1f;         // march resolution past the boundary
        private const float ProbeRadius = 0.2f;       // snap radius when sampling for resumed ground
        private const float MinGap = 0.25f;           // ignore resume points this close to the boundary (near edge)
        private const int MaxSeeThrough = 3;          // wall-tone cast: most skippable boundaries to see through

        private static Character Main
        {
            get { Party party = Party.Player; return party != null ? party.Main : null; }
        }
    }
}
