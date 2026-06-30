using System;
using BepInEx.Logging;
using DiscoAccess.Core.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DiscoAccess.Audio
{
    /// <summary>
    /// Our own stereo audio backend for the spatial soundscape, independent of the game's audio so the cues
    /// aren't colored by its mixer/DSP. ONE shared <see cref="MixingSampleProvider"/> feeds ONE
    /// <see cref="WaveOutEvent"/>; every voice (one-shots, wall tones) is an input on that single mixer.
    /// Lives in the permanent host (the device is a native handle) and is lent to the module through
    /// <c>IModHost.Audio</c>. The device opens lazily on first use and self-disables on failure, so a machine
    /// with no audio device never crashes the mod. Ported from the WOTR exploration mod's NAudio engine,
    /// with the cues generated procedurally rather than read from WAV assets.
    /// </summary>
    internal sealed class NAudioEngine : IAudioEngine, IDisposable
    {
        public const int Rate = 44100;

        private readonly ManualLogSource _log;
        private MixingSampleProvider _mixer;
        private IWavePlayer _out;
        private bool _failed;

        public NAudioEngine(ManualLogSource log) { _log = log; }

        public bool Available => !_failed;

        // 100 ms buffer to ride through managed-thread (GC/CPU) pauses without underrunning into clicks.
        private bool EnsureStarted()
        {
            if (_out != null) return true;
            if (_failed) return false;
            try
            {
                _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
                _out = new WaveOutEvent { DesiredLatency = 100, NumberOfBuffers = 4 };
                _out.Init(_mixer);
                _out.Play();
                return true;
            }
            catch (Exception e)
            {
                _failed = true;
                _log?.LogWarning("[audio] output device unavailable; spatial cues disabled: " + e.Message);
                return false;
            }
        }

        internal void Add(ISampleProvider p) { if (EnsureStarted()) _mixer.AddMixerInput(p); }
        internal void Remove(ISampleProvider p)
        {
            try { _mixer?.RemoveMixerInput(p); }
            catch (Exception e) { _log?.LogWarning("[audio] mixer remove failed: " + e.Message); }
        }

        // Constant-power pan: a single source for the pan-to-(left,right) gain law, shared by the one-shot
        // and the wall-tone voices so they place a given bearing identically.
        internal static void PanGains(float pan, out float left, out float right)
        {
            float t = (pan + 1f) * 0.5f * (float)(Math.PI / 2.0); // -1 = hard left, +1 = hard right
            left = (float)Math.Cos(t);
            right = (float)Math.Sin(t);
        }

        public void PlayOneShot(float frequency, float seconds, float volume, float pan)
        {
            if (!EnsureStarted()) return;
            _mixer.AddMixerInput(new ToneShot(Rate, frequency, seconds, volume, pan));
        }

        public IWallTones CreateWallTones() { EnsureStarted(); return new WallTones(this); }

        public void Dispose()
        {
            try { _out?.Stop(); _out?.Dispose(); }
            catch (Exception e) { _log?.LogWarning("[audio] output dispose failed: " + e.Message); }
            _out = null;
            _mixer = null;
        }

        // A generated sine one-shot with a short attack/release (so it doesn't click) and a constant-power
        // pan. Returns fewer than `count` samples once finished, so the shared mixer auto-removes it.
        private sealed class ToneShot : ISampleProvider
        {
            private readonly int _total, _attack, _release, _rate;
            private readonly float _freq, _gainL, _gainR;
            private int _pos;

            public ToneShot(int rate, float freq, float seconds, float vol, float pan)
            {
                _rate = rate;
                _freq = freq;
                _total = Math.Max(1, (int)(seconds * rate));
                _attack = Math.Min(_total / 2, (int)(0.005f * rate));
                _release = Math.Min(_total / 2, (int)(0.02f * rate));
                PanGains(pan, out float l, out float r);
                _gainL = vol * l;
                _gainR = vol * r;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2, produced = 0;
                for (int f = 0; f < frames && _pos < _total; f++)
                {
                    float env = 1f;
                    if (_pos < _attack) env = _pos / (float)_attack;
                    else if (_pos > _total - _release) env = (_total - _pos) / (float)_release;
                    float s = (float)Math.Sin(2.0 * Math.PI * _freq * _pos / _rate) * env;
                    buffer[offset + f * 2] = s * _gainL;
                    buffer[offset + f * 2 + 1] = s * _gainR;
                    _pos++;
                    produced += 2;
                }
                return produced;
            }
        }

        // Four continuous oscillators, a distinct pitch per direction (so north and south, both centred,
        // stay distinguishable) at a fixed compass pan (east hard right, west hard left), summed to stereo
        // as ONE mixer input. Volumes are set live each frame; the phase advances regardless so a voice
        // coming back up is click-free.
        private sealed class WallTones : ISampleProvider, IWallTones
        {
            private sealed class Voice
            {
                public double Phase;
                public volatile float Volume;
                public float Freq;
                public float L = 0.70710677f, R = 0.70710677f;
            }

            private readonly Voice[] _voices;
            private readonly NAudioEngine _engine;
            private readonly int _rate;

            public WaveFormat WaveFormat { get; }

            public WallTones(NAudioEngine engine)
            {
                _engine = engine;
                _rate = Rate;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
                _voices = new[]
                {
                    Make(620f, 0f),   // N: centred, high
                    Make(300f, 0f),   // S: centred, low
                    Make(440f, 1f),   // E: hard right
                    Make(440f, -1f),  // W: hard left
                };
                engine.Add(this);
            }

            private static Voice Make(float freq, float pan)
            {
                PanGains(pan, out float l, out float r);
                return new Voice { Freq = freq, L = l, R = r };
            }

            public void Update(float[] volumes)
            {
                for (int i = 0; i < _voices.Length && i < volumes.Length; i++)
                {
                    float v = volumes[i];
                    _voices[i].Volume = v < 0f ? 0f : (v > 1f ? 1f : v);
                }
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                for (int f = 0; f < frames; f++)
                {
                    float l = 0f, r = 0f;
                    for (int i = 0; i < _voices.Length; i++)
                    {
                        Voice v = _voices[i];
                        float vol = v.Volume;
                        if (vol > 0f)
                        {
                            float s = (float)Math.Sin(v.Phase * 2.0 * Math.PI) * vol;
                            l += s * v.L;
                            r += s * v.R;
                        }
                        v.Phase += v.Freq / _rate;
                        if (v.Phase >= 1.0) v.Phase -= 1.0;
                    }
                    buffer[offset + f * 2] = l > 1f ? 1f : (l < -1f ? -1f : l);
                    buffer[offset + f * 2 + 1] = r > 1f ? 1f : (r < -1f ? -1f : r);
                }
                return count; // ReadFully mixer: always full (silence when all volumes are 0)
            }

            public void Dispose() => _engine.Remove(this);
        }
    }
}
