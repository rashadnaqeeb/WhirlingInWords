using System;
using DiscoAccess.Core.Audio;
using DiscoAccess.Core.Modularity;
using DiscoAccess.Core.World.Overlays;
using DiscoAccess.Core.World.Overlays.Systems;
using Sunshine.Views;
using UnityEngine;
using PlayMode = DiscoAccess.Core.World.Overlays.PlayMode; // disambiguate from UnityEngine.PlayMode

namespace DiscoAccess.Module.World
{
    /// <summary>
    /// Owns the one world overlay and drives it each frame while the player is in the isometric scene (the
    /// LOBBY view), the world-layer counterpart to <see cref="Nav.ScreenManager"/> for menus. It engages the
    /// overlay on entering the world and disengages on leaving (so audio systems can build/release their
    /// voices), and ticks it so the sensing systems stay live.
    ///
    /// Live cursor keybindings are not wired yet — the world keyboard-ownership model is a deliberate
    /// follow-up — so movement is zero here and the cursor is exercised through the dev hooks below until
    /// that lands. The seam (player position, navmesh clamp, readout) is what this chunk proves.
    /// </summary>
    public sealed class WorldReader : IDisposable
    {
        /// <summary>The live reader, for dev-server introspection/driving while live keys are pending.</summary>
        public static WorldReader Active;

        private readonly IAudioEngine _audio;
        private readonly Overlay _overlay;
        private readonly SpatialSystem _spatial;
        private bool _engaged;
        private IWallTones _devTones;

        public WorldReader(IModHost host)
        {
            _audio = host.Audio;
            _overlay = new Overlay(new WorldEnvironment(), host.Speech);
            _spatial = new SpatialSystem();
            // Until the settings menu wires the world systems, the cursor readout is simply on.
            _spatial.BindMode(() => PlayMode.Continuous);
            _overlay.With(_spatial);
            Active = this;
        }

        /// <summary>Engage/disengage on world entry/exit and tick the overlay while in the world.</summary>
        public void Tick()
        {
            bool inWorld = InWorld();
            if (inWorld && !_engaged) { _overlay.OnEnter(); _engaged = true; }
            else if (!inWorld && _engaged) { _overlay.OnExit(); _engaged = false; }
            if (!inWorld) return;

            // No live movement keys yet; ticking with a zero vector keeps motion tracking and the systems
            // current so the dev hooks read a live overlay.
            _overlay.Tick(Time.unscaledDeltaTime, 0f, 0f, 0f);
        }

        // The plain in-game world is the CLEAR view (no menu/page up); a menu, dialogue, or cutscene is its
        // own ViewType, and HasControl gates the finer cutscene/dialogue case on top. The bridge throws
        // during early boot (no view system yet), which reads as "not in the world".
        private static bool InWorld()
        {
            try { return ViewsPagesBridge.Current == ViewType.CLEAR; }
            catch { return false; }
        }

        public void Dispose()
        {
            if (_engaged) { _overlay.OnExit(); _engaged = false; }
            _devTones?.Dispose();
            _devTones = null;
            if (Active == this) Active = null;
        }

        // ---- dev hooks (drive/inspect the cursor over the dev /eval server until live keys land) ----

        public string DevState()
            => $"player={_overlay.Cursor.PlayerPosition}, cursor={_overlay.Cursor.Position}, inWorld={InWorld()}";

        public string DevView()
        {
            try { return "view=" + ViewsPagesBridge.Current; }
            catch (Exception e) { return "view threw: " + e.GetType().Name + " " + e.Message; }
        }

        public void DevAnnounce() => _overlay.AnnounceCurrent();

        /// <summary>Glide the cursor one ~quarter-second step in (dx east, dz north) at 4 m/s, then read it.</summary>
        public void DevGlide(float dx, float dz)
        {
            _overlay.Tick(0.25f, dx, dz, 4f);
            _overlay.AnnounceCurrent();
        }

        public void DevRecenter() => _overlay.Recenter();

        // Audio-backbone validation: a panned one-shot, and the four wall-tone voices driven directly.
        public string DevAudioState() => "available=" + _audio.Available;
        public void DevBeep(float pan) => _audio.PlayOneShot(440f, 0.3f, 0.8f, pan);
        public void DevWall(float n, float s, float e, float w)
        {
            if (_devTones == null) _devTones = _audio.CreateWallTones();
            _devTones.Update(new[] { n, s, e, w });
        }
        public void DevWallStop() { _devTones?.Dispose(); _devTones = null; }
    }
}
