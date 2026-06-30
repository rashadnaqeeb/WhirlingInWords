using DiscoAccess.Core.Modularity;
using DiscoAccess.Core.Strings;
using FortressOccident;
using Sunshine;
using Snv = System.Numerics.Vector3;

namespace DiscoAccess.Module.World
{
    /// <summary>
    /// The Enter verb: walk the character to a target's interaction stand-point, then interact once in
    /// range. The game fuses walking and interacting in its click handler, but <c>Interact()</c> itself does
    /// not walk - it acts in place and refuses (returns false, plays a fail animation) when out of range -
    /// so this orchestrates the two: target the stand-point, drive <c>SetDestination</c>, watch
    /// <c>movementStatus</c>, and <c>Interact</c> on arrival. A small arrival-watching state machine ticked
    /// each frame by the <see cref="WorldReader"/>, cancellable mid-path.
    ///
    /// Reachability is decided up front by the game's own oracle on the stand-point
    /// (<c>CheckIfCanCreatePathToHavePath</c>), never our own path to the body, which false-negatives on an
    /// off-mesh sliver under an NPC's feet. An unreachable target is refused here rather than walked partway
    /// and failed silently. Orbs and bare ground are out of scope: orb interaction is deferred (needs
    /// camera-follow), and a no-target Enter is a plain walk handled by <see cref="BeginWalk"/>.
    /// </summary>
    internal sealed class WalkInteract
    {
        // Consecutive non-moving frames tolerated before giving up. Generous before the walk first moves
        // (SetDestination can read IDLE for several frames while it engages a long path), and shorter once it
        // has moved (a halt then is a real stall: a dynamic obstacle, an off-mesh sliver). At ~60 fps these
        // are about a second and three-quarters of a second.
        private const int StartupGraceFrames = 60;
        private const int StallGraceFrames = 45;
        // How close (metres) to a bare-ground destination counts as arrived, when no interaction radius applies.
        private const float GroundArrivalDistance = 0.6f;

        private readonly IModHost _host;

        private EntityProxy _target; // null => bare-ground walk (arrival is the whole action, no interact)
        private string _label;       // the target's name (or "ground"), for logs only
        private Snv _dest;           // the issued destination, for bare-ground arrival distance
        private bool _active;
        private bool _movedOnce;     // movementStatus has been MOVING/ADJUSTING since this walk began
        private int _stalledTicks;   // consecutive frames the character has been non-moving and not arrived

        public WalkInteract(IModHost host) { _host = host; }

        /// <summary>Whether a committed walk is in flight (being watched for arrival).</summary>
        public bool Active => _active;

        /// <summary>Commit to walking to <paramref name="target"/>'s interaction stand-point and interacting
        /// on arrival, approaching from <paramref name="from"/> (the character's current position). The caller
        /// has already confirmed the target is reachable (it would otherwise route to <see cref="BeginWalk"/>),
        /// so the stand-point is computed and driven directly.</summary>
        public bool BeginInteract(EntityProxy target, Snv from)
        {
            Snv stand = target.Approach(from, out float heading);
            if (!Drive(stand, heading)) return false;
            _target = target;
            _label = string.IsNullOrEmpty(target.Name) ? "target" : target.Name;
            _host.Speech.Speak(Strings.WorldWalkingTo(target.Name), interrupt: true);
            return true;
        }

        /// <summary>Walk to a bare-ground spot with nothing to interact with; arrival is the whole action.
        /// <paramref name="announcement"/> is what to speak on committing (the plain "walking", or a "can't
        /// reach" naming the unreachable thing the cursor was near, since the cursor's ground is still
        /// walkable and getting closer can make that thing reachable for a follow-up).</summary>
        public bool BeginWalk(Snv point, string announcement)
        {
            if (!Drive(point, null)) return false;
            _target = null;
            _label = "ground";
            _host.Speech.Speak(announcement, interrupt: true);
            return true;
        }

        /// <summary>Player-initiated cancel (the Stop key): halt the character and say so.</summary>
        public void Cancel()
        {
            if (!_active) return;
            StopCharacter();
            _active = false;
            _host.Speech.Speak(Strings.WorldStopped, interrupt: true);
        }

        /// <summary>Silent abandon when the world reader loses control (a script grabbed the character, the
        /// area unloaded): only drop the watch, never halt the character. The game (a conversation, a scripted
        /// sequence) owns the character's movement now, so issuing StopMovement here would fight it; the player
        /// did not ask to stop, so there is nothing to say.</summary>
        public void Abandon() => _active = false;

        /// <summary>Advance the walk: when the character finishes its path (or already stands in range),
        /// interact once and finish. A broken path, or a walk that stalls (never starts, or moves then halts
        /// short of the target) ends the watch rather than hanging - each logged, and a stalled approach to a
        /// target speaks "can't reach" so the player is never left in silence after the "walking to" line.</summary>
        public void Tick()
        {
            if (!_active) return;
            Character main = Main;
            if (main == null) { _active = false; return; } // character gone (load/teleport): drop the walk

            Snv player = WorldConvert.ToSnv(main.transform.position);
            Character.MovementStatus status = main.movementStatus;
            bool moving = status == Character.MovementStatus.MOVING || status == Character.MovementStatus.ADJUSTING;
            if (moving) { _movedOnce = true; _stalledTicks = 0; } else _stalledTicks++;

            if (HasArrived(status, player)) { Arrive(player); _active = false; return; }

            if (status == Character.MovementStatus.BROKEN)
            {
                _host.LogWarning($"WalkInteract: path to {_label} broke; abandoning the walk.");
                Stall();
                return;
            }

            // Non-moving and not arrived for longer than the grace: either SetDestination never engaged (a
            // longer startup grace, since a long path can read IDLE for several frames) or the character moved
            // then halted short (a dynamic obstacle, an off-mesh sliver). Give up rather than watch forever.
            int grace = _movedOnce ? StallGraceFrames : StartupGraceFrames;
            if (_stalledTicks > grace)
            {
                _host.LogWarning($"WalkInteract: walk to {_label} stalled ({status}); abandoning.");
                Stall();
            }
        }

        // End a stalled walk. A target approach that failed mid-path speaks "can't reach" so the player is not
        // left in silence; a bare-ground walk that stalled just stops being watched.
        private void Stall()
        {
            if (_target != null) _host.Speech.Speak(Strings.WorldUnreachable(_target.Name), interrupt: true);
            _active = false;
        }

        // Arrived when the game reports the move completed, or the character already stands within the
        // target's interaction radius (a stand-point you already occupy never enters MOVING). For bare
        // ground, completion or simple proximity to the spot.
        private bool HasArrived(Character.MovementStatus status, Snv player)
        {
            if (status == Character.MovementStatus.COMPLETED) return true;
            if (_target != null) return _target.WithinInteractionRadius(player);
            return Snv.Distance(player, _dest) <= GroundArrivalDistance;
        }

        private void Arrive(Snv player)
        {
            if (_target == null) return; // bare-ground walk: nothing to interact with
            // Gate the interact on the game's own arrival-range test. At a COMPLETED stand-point this holds;
            // if somehow short, Interact() refuses in place rather than acting at the wrong spot - logged so
            // the miss is visible rather than silent.
            if (!_target.WithinInteractionRadius(player))
                _host.LogWarning($"WalkInteract: arrived near {_label} but outside its interaction radius; interacting anyway.");
            if (!_target.Interact())
                _host.LogWarning($"WalkInteract: Interact on {_label} returned false at the stand-point.");
        }

        private bool Drive(Snv point, float? heading)
        {
            Character main = Main;
            if (main == null) return false;
            // The game's heading argument is an Il2Cpp nullable: a value faces the character that way on
            // arrival, the empty case leaves the heading to the game (a bare-ground walk).
            Il2CppSystem.Nullable<float> h = heading.HasValue
                ? new Il2CppSystem.Nullable<float>(heading.Value)
                : new Il2CppSystem.Nullable<float>();
            // AUTOMATIC defers walk-versus-run to the game's own policy (the player's run preference, any
            // scripted careful-movement spot), exactly as a vanilla click does; we never hardcode RUN.
            main.SetDestination(WorldConvert.ToUnity(point), h, MovementMode.AUTOMATIC, false);
            _dest = point;
            _active = true;
            _movedOnce = false;
            _stalledTicks = 0;
            return true;
        }

        private static void StopCharacter()
        {
            GameController gc = GameController.Singleton;
            if (gc != null) gc.StopMovement(force: false);
        }

        private static Character Main
        {
            get { Party p = Party.Player; return p != null ? p.Main : null; }
        }
    }
}
