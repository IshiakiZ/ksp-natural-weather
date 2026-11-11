using System.Collections.Generic;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// The mod's sounds held while the game is paused (docs/sound-rules.md, B1). The game pauses its own sounds at its pause
    /// menu one by one, not Unity's listener, so a source of a mod's played on through it (seen 2026-11-03: the rain heard at
    /// -38 dBFS all through a pause, the game's own sounds stopped). Each source is told here as it is made; at the game's
    /// pause those playing are paused, and at its end they go on from where they were. What is made while the game is paused
    /// is paused as it is told here. A scene change lets go of all (the game may leave its pause without saying so then).
    /// </summary>
    internal static class Hush
    {
        static readonly List<AudioSource> sources = new List<AudioSource>(), held = new List<AudioSource>();
        static Hooks hooks;

        // (the game's events take an object's own methods: a static one is turned down with a null reference in the event's own
        // code, seen 2026-11-03)
        sealed class Hooks
        {
            internal void Pause() => OnPause();
            internal void Unpause() => OnUnpause();
            internal void Scene(GameScenes scene) => OnScene(scene);
        }

        /// <summary>Whether the game is paused: what starts sounds waits while it is.</summary>
        internal static bool Paused { get; private set; }

        /// <summary>A source of the mod's, to be held while the game is paused.</summary>
        internal static void Add(AudioSource source)
        {
            if (source == null) return;
            if (hooks == null)
            {
                var h = new Hooks();
                GameEvents.onGamePause.Add(h.Pause);
                GameEvents.onGameUnpause.Add(h.Unpause);
                GameEvents.onGameSceneLoadRequested.Add(h.Scene);
                hooks = h;
            }
            sources.RemoveAll(s => s == null);
            if (!sources.Contains(source)) sources.Add(source);
            if (Paused && source.isPlaying) { source.Pause(); held.Add(source); }
        }

        static void OnPause()
        {
            Paused = true;
            sources.RemoveAll(s => s == null);
            foreach (AudioSource s in sources)
                if (s.isPlaying && !held.Contains(s)) { s.Pause(); held.Add(s); }
        }

        static void OnUnpause()
        {
            Paused = false;
            foreach (AudioSource s in held) if (s != null) s.UnPause();
            held.Clear();
        }

        static void OnScene(GameScenes scene) { Paused = false; held.Clear(); sources.RemoveAll(s => s == null); }
    }
}
