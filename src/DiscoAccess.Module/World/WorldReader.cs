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
    /// Owns the one world overlay and drives it each frame while the player is in the isometric scene, the
    /// world-layer counterpart to <see cref="Nav.ScreenManager"/> for menus. It engages the overlay on
    /// entering the world and disengages on leaving (so audio systems can build/release their voices), and
    /// ticks it so the sensing systems stay live.
    ///
    /// Live cursor keybindings are not wired yet — the world keyboard-ownership model is a deliberate
    /// follow-up — so movement is zero here and the cursor is exercised through the dev hooks below until
    /// that lands. The seam (player position, navmesh clamp, readout) is what this chunk proves.
    /// </summary>
    public sealed class WorldReader : IDisposable
    {
        /// <summary>The live reader, for dev-server introspection/driving while live keys are pending.</summary>
        public static WorldReader Active;

        private readonly IModHost _host;
        private readonly IAudioEngine _audio;
        private readonly Overlay _overlay;
        private readonly SpatialSystem _spatial;
        private readonly WorldModel _model = new WorldModel();
        private bool _engaged;
        private bool _viewReadyOnce;
        private bool _warnedViewThrow;
        private IWallTones _devTones;

        public WorldReader(IModHost host)
        {
            _host = host;
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

            // Refresh the world registry (the sonar/scanner data layer), then tick the overlay. No live
            // movement keys yet; the zero vector keeps motion tracking and the systems current.
            float dt = Time.unscaledDeltaTime;
            _model.Tick(dt);
            _overlay.Tick(dt, 0f, 0f, 0f);
        }

        // The plain in-game world is the CLEAR view. Confirmed live: during free-roam ViewsPagesBridge.Current
        // reads CLEAR steadily, and DevScan sees the full entity set; a menu, dialogue, or cutscene is its own
        // ViewType. (The LOBBY value ScreenAdapter maps to the world-screen NAME is a different page state,
        // not the free-roam view - do not switch this gate to LOBBY: it reads false while actually in-world.)
        // HasControl gates the finer cutscene/dialogue case on top. The bridge throws during early boot (no
        // view system yet) - expected and frequent - so that is swallowed; any other throw is logged once so a
        // real failure (a post-update proxy change) surfaces without spamming the per-frame pump.
        private bool InWorld()
        {
            try
            {
                ViewType view = ViewsPagesBridge.Current;
                _viewReadyOnce = true;
                return view == ViewType.CLEAR;
            }
            catch (Exception e)
            {
                // Boot transients (before the view system ever comes up) are expected and silent; a throw
                // after it has worked once is a real regression, logged a single time.
                if (_viewReadyOnce && !_warnedViewThrow)
                {
                    _warnedViewThrow = true;
                    _host.LogWarning("WorldReader: view read failed; world layer idle until it recovers: " + e.Message);
                }
                return false;
            }
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

        // World-model validation: total items, per-category counts, and how many pass the IsAccessible gate
        // (the doc's ~400 entities collapsing to ~90 actionable things).
        public string DevScan()
        {
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            int total = 0, accessible = 0, accessibleByCat;
            var accCounts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var it in _model.Items)
            {
                total++;
                counts.TryGetValue(it.Category, out int c); counts[it.Category] = c + 1;
                if (it.IsAccessible)
                {
                    accessible++;
                    accCounts.TryGetValue(it.Category, out accessibleByCat); accCounts[it.Category] = accessibleByCat + 1;
                }
            }
            var sb = new System.Text.StringBuilder();
            sb.Append("total=").Append(total).Append(" accessible=").Append(accessible).Append('\n');
            foreach (var cat in DiscoAccess.Core.World.WorldTaxonomy.All)
            {
                counts.TryGetValue(cat, out int c);
                accCounts.TryGetValue(cat, out int a);
                sb.Append("  ").Append(cat).Append(": ").Append(c).Append(" (").Append(a).Append(" accessible)\n");
            }
            return sb.ToString();
        }
    }
}
