using System;
using System.Collections.Generic;
using DiscoAccess.Core.World;
using FortressOccident;
using UnityEngine;
using Object = UnityEngine.Object; // the registry keys are Unity objects; disambiguate from System.Object

namespace DiscoAccess.Module.World
{
    /// <summary>
    /// The live registry of everything in the current area: one stable proxy per entity and orb, rebuilt by
    /// a poll-and-diff each frame against the game's pools (<see cref="BasicEntity.sceneEntitySet"/> and the
    /// active <see cref="SenseOrb"/>s). One proxy instance per game object is kept across frames - the
    /// interop wrapper cache makes the game objects stable dictionary keys - so a consumer can hold a proxy
    /// and keep reading it. Not fog-filtered; consumers apply IsVisible/IsAccessible.
    /// </summary>
    internal sealed class WorldModel : IWorldModel
    {
        // The registry refreshes at this cadence, not every frame: the per-frame cost is dominated by
        // FindObjectsOfType (a full active-scene scan), and membership need not be frame-fresh - proxies
        // read their own live state on demand, and the consumers (sonar sweeps, the scanner) act on the
        // order of seconds. A tenth of a second of membership lag is imperceptible and cuts the scan rate ~6x.
        private const float PollInterval = 0.1f;

        private readonly Dictionary<Object, IWorldItem> _items = new Dictionary<Object, IWorldItem>();
        private readonly HashSet<Object> _present = new HashSet<Object>();
        private readonly List<Object> _gone = new List<Object>();
        private float _sincePoll = PollInterval; // poll on the first tick

        public IReadOnlyCollection<IWorldItem> Items => _items.Values;

        public event Action<IWorldItem> Added;
        public event Action<IWorldItem> Removed;

        /// <summary>Poll the game's pools and diff against the held set (throttled to <see cref="PollInterval"/>),
        /// building a proxy only for a genuinely new object and dropping any that despawned or left when the
        /// area changed.</summary>
        public void Tick(float dt)
        {
            _sincePoll += dt;
            if (_sincePoll < PollInterval) return;
            _sincePoll = 0f;

            _present.Clear();

            // sceneEntitySet is an Il2Cpp list; index it rather than relying on a BCL enumerator.
            var entities = BasicEntity.sceneEntitySet;
            if (entities != null)
                for (int i = 0; i < entities.Count; i++)
                {
                    BasicEntity e = entities[i];
                    if (e == null) continue;
                    Track(e, () => new EntityProxy(e));
                }

            foreach (SenseOrb orb in Object.FindObjectsOfType<SenseOrb>())
            {
                if (orb == null) continue;
                Track(orb, () => new OrbProxy(orb));
            }

            _gone.Clear();
            foreach (Object key in _items.Keys) if (!_present.Contains(key)) _gone.Add(key);
            for (int i = 0; i < _gone.Count; i++)
            {
                Object key = _gone[i];
                IWorldItem item = _items[key];
                _items.Remove(key);
                Removed?.Invoke(item);
            }
        }

        // Mark a game object present, building (and announcing) a proxy only the first time it is seen.
        private void Track(Object key, Func<IWorldItem> make)
        {
            if (!_items.ContainsKey(key))
            {
                IWorldItem item = make();
                _items[key] = item;
                Added?.Invoke(item);
            }
            _present.Add(key);
        }
    }
}
