using System;
using System.Collections.Generic;
using UnityEngine;

namespace NaturalWeather
{
    /// <summary>
    /// (Since 0.6.0.) Recordings of real weather, where the mod has them: rain light to hard and on a cockpit's glass, wind gusty
    /// to a hurricane's and howling at a cockpit, thunder close by and far off (Sonniss's libraries; docs/sounds-plan.md), in the
    /// mod's one packed file read by Keystone's Sounds (Keystone 0.10.0). All are asked for at the first use, so that a loop or a
    /// clap does not wait for its sound when it is wanted; until one is in, and without them (a repository's build, or an older
    /// Keystone), the mod's made sounds are heard, as before.
    /// </summary>
    static class Recordings
    {
        const string Mod = "NaturalWeather";
        static bool asked, gone;
        static readonly List<string> names = new List<string>();

        static void Ask()
        {
            if (asked) return;
            asked = true;
            try { AskAll(); }
            catch (Exception) { gone = true; }      // (a Keystone older than 0.10.0: no Sounds there)
        }

        static void AskAll()
        {
            foreach (string name in Keystone.Sounds.Names(Mod))
            {
                names.Add(name);
                // (the loops of rain and wind, one source each, streamed; thunder whole, as two claps may roll at once)
                if (name.StartsWith("rain-", StringComparison.Ordinal) || name.StartsWith("wind-", StringComparison.Ordinal)) Keystone.Sounds.Stream(Mod, name, _ => { });
                else Keystone.Sounds.Get(Mod, name, _ => { });
            }
        }

        // (those found kept here: asked for every frame until it is in, and a name put together for Keystone each time was garbage)
        static readonly Dictionary<string, AudioClip> found = new Dictionary<string, AudioClip>();

        /// <summary>A recording by name, once loaded; null without the recordings, or until it is in.</summary>
        public static AudioClip Clip(string name)
        {
            if (found.TryGetValue(name, out AudioClip known) && known != null) return known;
            Ask();
            if (gone || names.Count == 0) return null;
            AudioClip clip;
            try { clip = Ready(name); }
            catch (Exception) { gone = true; return null; }
            if (clip != null) found[name] = clip;
            return clip;
        }

        static AudioClip Ready(string name) => Keystone.Sounds.Ready(Mod, name);

        /// <summary>The recordings whose names begin so (thunder-near-, thunder-far-), loaded ones only.</summary>
        public static List<AudioClip> Starting(string start)
        {
            Ask();
            var those = new List<AudioClip>();
            foreach (string name in names)
                if (name.StartsWith(start, StringComparison.Ordinal)) { AudioClip c = Clip(name); if (c != null) those.Add(c); }
            return those;
        }
    }

    /// <summary>
    /// Recorded loops heard together, each as loud as it is wanted, the change eased: for the rain, light, medium and hard
    /// crossfaded by how hard it rains, and the wind, gusty, strong and a hurricane's, by how hard it blows. Heard as the camera
    /// hears what is all round it (not from a place). Each source is made when its loop is first wanted and stopped when it
    /// has faded out.
    /// </summary>
    sealed class Loops
    {
        readonly string name;
        GameObject holder;
        readonly Dictionary<string, AudioSource> playing = new Dictionary<string, AudioSource>();
        readonly Dictionary<string, float> wanted = new Dictionary<string, float>();
        readonly List<string> ended = new List<string>();

        public Loops(string name) { this.name = name; }

        /// <summary>How loud each loop is wanted this frame (those not named: none).</summary>
        public void Want(string loop, float volume) { wanted[loop] = Mathf.Max(0f, volume); }

        /// <summary>(Each frame, after the Wants.) Each loop brought towards its volume at so much a second; muffled above cutoff (Hz); pitch.</summary>
        public void Step(float rate, float cutoff = 22000f, float pitch = 1f)
        {
            foreach (KeyValuePair<string, float> w in wanted)
            {
                if (playing.ContainsKey(w.Key) || w.Value < 0.002f) continue;
                AudioClip clip = Recordings.Clip(w.Key);
                if (clip == null) continue;
                if (holder == null) holder = new GameObject(name);
                var go = new GameObject(name + " " + w.Key);
                go.transform.SetParent(holder.transform, false);
                AudioSource s = go.AddComponent<AudioSource>();
                s.clip = clip; s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.dopplerLevel = 0f; s.volume = 0f;
                go.AddComponent<AudioLowPassFilter>().cutoffFrequency = 22000f;
                // (begun at a place of its own in the loop: two of them begun together are not heard in step)
                s.time = UnityEngine.Random.Range(0f, Mathf.Max(0f, clip.length - 0.1f));
                s.Play();
                playing[w.Key] = s;
            }
            ended.Clear();
            foreach (KeyValuePair<string, AudioSource> p in playing)
            {
                AudioSource s = p.Value;
                if (s == null) { ended.Add(p.Key); continue; }
                float to = wanted.TryGetValue(p.Key, out float v) ? v : 0f;
                s.volume = Mathf.MoveTowards(s.volume, to, Time.unscaledDeltaTime * rate);
                s.pitch = pitch;
                AudioLowPassFilter f = s.GetComponent<AudioLowPassFilter>();
                if (f != null) f.cutoffFrequency = cutoff;
                if (to < 0.002f && s.volume <= 0f) { UnityEngine.Object.Destroy(s.gameObject); ended.Add(p.Key); }
            }
            foreach (string e in ended) playing.Remove(e);
            wanted.Clear();
        }

        /// <summary>Whether any of them is heard just now.</summary>
        public bool Heard { get { foreach (AudioSource s in playing.Values) if (s != null && s.volume > 0.002f) return true; return false; } }

        /// <summary>Whether a loop of this name can be had (its recording is loaded).</summary>
        public static bool Can(string loop) => Recordings.Clip(loop) != null;

        public void Drop()
        {
            if (holder != null) UnityEngine.Object.Destroy(holder);
            holder = null;
            playing.Clear(); wanted.Clear();
        }
    }
}
