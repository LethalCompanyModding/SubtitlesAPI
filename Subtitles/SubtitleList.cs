using System;
using System.Collections;
using System.Collections.Generic;
using System.Timers;

namespace Subtitles;

public class SubtitleList : IList<string>, IDisposable
{
    // compact value type to avoid Tuple allocations
    private struct SubtitleEntry
    {
        public long ShowAtTicks; // DateTime.UtcNow.Ticks
        public string Text;

        public SubtitleEntry(DateTime showAt, string text)
        {
            ShowAtTicks = showAt.ToUniversalTime().Ticks;
            Text = text;
        }
    }

    private readonly object syncRoot = new object();
    private readonly List<SubtitleEntry> collection = new List<SubtitleEntry>();
    private readonly Timer timer;
    private readonly TimeSpan expiration;
    private bool disposed;

    // Reduced-captions cooldown (applies when Plugin.ReducedCaptions.Value == true).
    // Uses same expiration as default; tune if needed.
    private readonly TimeSpan reducedCaptionsCooldown;
    private readonly Dictionary<string, DateTime> lastShown = new Dictionary<string, DateTime>(StringComparer.Ordinal);

    public SubtitleList()
    {
        expiration = TimeSpan.FromMilliseconds(Constants.DefaultExpireSubtitleTimeMs);
        reducedCaptionsCooldown = TimeSpan.FromMilliseconds(Constants.DefaultExpireSubtitleTimeMs);

        timer = new Timer(1000) { AutoReset = true };
        timer.Elapsed += RemoveExpiredElements;
        timer.Start();
    }

    // Timer event handler - runs on ThreadPool thread from System.Timers.
    private void RemoveExpiredElements(object sender, ElapsedEventArgs e)
    {
        var now = DateTime.UtcNow;
        lock (syncRoot)
        {
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                var age = now - new DateTime(collection[i].ShowAtTicks, DateTimeKind.Utc);
                if (age >= expiration)
                {
                    collection.RemoveAt(i);
                }
            }

            // prune stale cooldown entries (keep bounded)
            var staleThreshold = TimeSpan.FromSeconds(30);
            var stale = new List<string>();
            foreach (var kv in lastShown)
            {
                if ((now - kv.Value) > staleThreshold) stale.Add(kv.Key);
            }

            foreach (var key in stale) lastShown.Remove(key);
        }
    }

    // New: Return up to `number` visible subtitles ordered by time (older -> newer),
    // with a per-entry alpha (1 -> 0). Fade is delayed by 3000ms, then proceeds linearly.
    // If Plugin.ExprementalPolish.Value == false this method disables fading and returns
    // entries at full opacity (behaves like the old TakeLast but returns tuples).
    // If ReducedCaptions is enabled and the entry is within the duplicate cooldown window,
    // alpha is forced to 1 (no fade).
    public List<(float alpha, string text)> TakeLastWithAlpha(int number)
    {
        if (number <= 0) return new List<(float alpha, string text)>(0);
        long nowTicks = DateTime.UtcNow.Ticks;
        long expirationTicks = expiration.Ticks;
        var now = DateTime.UtcNow;

        // Delay before starting fade (compute ticks directly)
        long fadeDelayTicks = TimeSpan.FromMilliseconds(3000).Ticks;

        lock (syncRoot)
        {
            // collect visible entries
            var visible = new List<SubtitleEntry>(Math.Min(collection.Count, number));
            for (int i = 0; i < collection.Count; i++)
            {
                if (collection[i].ShowAtTicks <= nowTicks)
                    visible.Add(collection[i]);
            }

            if (visible.Count == 0) return new List<(float, string)>(0);

            // sort by time ascending
            visible.Sort((a, b) => a.ShowAtTicks.CompareTo(b.ShowAtTicks));

            int start = Math.Max(0, visible.Count - number);
            var result = new List<(float, string)>(Math.Min(number, visible.Count));

            // If experimental polish is disabled, return entries with full opacity (no fade).
            if (Plugin.ExprementalPolish?.Value == false)
            {
                for (int i = start; i < visible.Count; i++)
                {
                    result.Add((1f, visible[i].Text));
                }
                return result;
            }

            for (int i = start; i < visible.Count; i++)
            {
                var entry = visible[i];
                long ageTicks = nowTicks - entry.ShowAtTicks;

                // If still within the delay window keep full alpha
                if (ageTicks <= fadeDelayTicks)
                {
                    result.Add((1f, entry.Text));
                    continue;
                }

                // compute linear fade after delay
                float alpha;

                // If expiration is less than or equal to delay, fade over full expiration
                if (expirationTicks <= fadeDelayTicks || expirationTicks <= 0)
                {
                    // fallback linear fade over full expiration
                    float t;
                    if (ageTicks <= 0) t = 0f;
                    else if (ageTicks >= expirationTicks) t = 1f;
                    else t = (float)ageTicks / (float)expirationTicks;
                    alpha = 1f - t;
                }
                else
                {
                    long effectiveTicks = expirationTicks;
                    long progressed = ageTicks - fadeDelayTicks;
                    float t;
                    if (progressed <= 0) t = 0f;
                    else if (progressed >= effectiveTicks) t = 1f;
                    else t = (float)progressed / (float)effectiveTicks;

                    // linear fade
                    alpha = 1f - t;
                }

                // clamp
                if (alpha < 0f) alpha = 0f;
                if (alpha > 1f) alpha = 1f;

                // If reduced-captions is enabled and this text was recently shown (within cooldown),
                // keep it fully opaque (no fade) so suppressed repeats remain visible.
                if (Plugin.ReducedCaptions?.Value == true && lastShown.TryGetValue(entry.Text, out var last))
                {
                    if ((now - last) < reducedCaptionsCooldown)
                    {
                        alpha = 1f;
                    }
                }

                result.Add((alpha, entry.Text));
            }

            return result;
        }
    }

    // Return up to `number` visible subtitles ordered by time (older -> newer), but only those with show time <= now.
    public List<string> TakeLast(int number)
    {
        if (number <= 0) return new List<string>(0);
        var nowTicks = DateTime.UtcNow.Ticks;

        lock (syncRoot)
        {
            // collect visible entries
            var visible = new List<SubtitleEntry>(Math.Min(collection.Count, number));
            for (int i = 0; i < collection.Count; i++)
            {
                if (collection[i].ShowAtTicks <= nowTicks)
                    visible.Add(collection[i]);
            }

            if (visible.Count == 0) return new List<string>(0);

            // sort by time ascending
            visible.Sort((a, b) => a.ShowAtTicks.CompareTo(b.ShowAtTicks));

            int start = Math.Max(0, visible.Count - number);
            var result = new List<string>(Math.Min(number, visible.Count));
            for (int i = start; i < visible.Count; i++) result.Add(visible[i].Text);
            return result;
        }
    }

    public string this[int index]
    {
        get
        {
            lock (syncRoot) return collection[index].Text;
        }
        set
        {
            if (value is null) throw new ArgumentNullException(nameof(value));
            lock (syncRoot)
            {
                collection[index] = new SubtitleEntry(DateTime.UtcNow, value);
                if (Plugin.ReducedCaptions?.Value == true) lastShown[value] = DateTime.UtcNow;
            }
        }
    }

    public IEnumerator<string> GetEnumerator()
    {
        lock (syncRoot)
        {
            var snapshot = new string[collection.Count];
            for (int i = 0; i < collection.Count; i++) snapshot[i] = collection[i].Text;
            return ((IEnumerable<string>)snapshot).GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Add immediately visible subtitle (respects reduced-captions)
    public void Add(string item)
    {
        if (string.IsNullOrWhiteSpace(item)) return;
        var now = DateTime.UtcNow;

        if (Plugin.ReducedCaptions?.Value == true)
        {
            lock (syncRoot)
            {
                if (lastShown.TryGetValue(item, out var last) && (now - last) < reducedCaptionsCooldown)
                {
                    return;
                }
                lastShown[item] = now;
                collection.Add(new SubtitleEntry(now, item));
            }
        }
        else
        {
            lock (syncRoot)
            {
                collection.Add(new SubtitleEntry(now, item));
            }
        }
    }

    // Add scheduled subtitle
    public void Add(string item, float seconds)
    {
        if (string.IsNullOrWhiteSpace(item)) return;
        var showAt = DateTime.UtcNow.AddSeconds(seconds);
        var now = DateTime.UtcNow;

        if (Plugin.ReducedCaptions?.Value == true)
        {
            lock (syncRoot)
            {
                if (lastShown.TryGetValue(item, out var last) && (now - last) < reducedCaptionsCooldown)
                {
                    return;
                }
                lastShown[item] = now;
                collection.Add(new SubtitleEntry(showAt, item));
            }
        }
        else
        {
            lock (syncRoot)
            {
                collection.Add(new SubtitleEntry(showAt, item));
            }
        }
    }

    public int Count
    {
        get { lock (syncRoot) return collection.Count; }
    }

    public bool IsSynchronized => false;

    public bool IsReadOnly => false;

    public void CopyTo(string[] array, int index)
    {
        if (array is null) throw new ArgumentNullException(nameof(array));
        lock (syncRoot)
        {
            for (int i = 0; i < collection.Count && i + index < array.Length; i++) array[i + index] = collection[i].Text;
        }
    }

    public bool Remove(string item)
    {
        if (item is null) return false;
        lock (syncRoot)
        {
            bool removed = false;
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if (collection[i].Text == item)
                {
                    collection.RemoveAt(i);
                    removed = true;
                }
            }

            if (lastShown.ContainsKey(item)) lastShown.Remove(item);
            return removed;
        }
    }

    public void RemoveAt(int i)
    {
        lock (syncRoot) { collection.RemoveAt(i); }
    }

    public bool Contains(string item)
    {
        if (item is null) return false;
        lock (syncRoot)
        {
            for (int i = 0; i < collection.Count; i++) if (collection[i].Text == item) return true;
            return false;
        }
    }

    public void Insert(int index, string item)
    {
        if (string.IsNullOrWhiteSpace(item)) return;
        lock (syncRoot)
        {
            collection.Insert(index, new SubtitleEntry(DateTime.UtcNow, item));
            if (Plugin.ReducedCaptions?.Value == true) lastShown[item] = DateTime.UtcNow;
        }
    }

    public int IndexOf(string item)
    {
        if (item is null) return -1;
        lock (syncRoot)
        {
            for (int i = 0; i < collection.Count; i++) if (collection[i].Text == item) return i;
            return -1;
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            collection.Clear();
            lastShown.Clear();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer?.Stop();
        timer?.Dispose();
    }
}