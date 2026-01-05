using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Timers;
using UnityEngine;

namespace Subtitles;

public class SubtitleList : IList<string>, IDisposable
{
    // compact value type to avoid Tuple allocations
    private struct SubtitleEntry
    {
        public long ShowAtTicks;
        public string Text;
        public string Key;

        public SubtitleEntry(DateTime showAt, string text, string key = null)
        {
            ShowAtTicks = showAt.ToUniversalTime().Ticks;
            Text = text;
            Key = key;
        }
    }

    private readonly object syncRoot = new object();
    private readonly List<SubtitleEntry> collection = new List<SubtitleEntry>();
    private readonly Timer timer;
    private readonly TimeSpan expiration;
    private bool disposed;

    private readonly TimeSpan reducedCaptionsCooldown;
    private readonly Dictionary<string, DateTime> lastShown = new Dictionary<string, DateTime>(StringComparer.Ordinal);

    public SubtitleList()
    {
        expiration = TimeSpan.FromMilliseconds(Constants.DefaultExpireSubtitleTimeMs);
        reducedCaptionsCooldown = TimeSpan.FromMilliseconds(Constants.DefaultExpireSubtitleTimeMs);

        timer = new Timer(1000)
        {
            AutoReset = true,
            Enabled = true
        };

        timer.Elapsed += RemoveExpiredElements;
    }

    private void RemoveExpiredElements(object sender, EventArgs e)
    {
        var now = DateTime.UtcNow;

        lock (syncRoot)
        {
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                var entry = collection[i];
                var age = now - new DateTime(entry.ShowAtTicks, DateTimeKind.Utc);

                if (entry.Key != null &&
                    lastShown.TryGetValue(entry.Key, out var last) &&
                    (now - last) < reducedCaptionsCooldown)
                {
                    continue;
                }

                if (age >= expiration)
                    collection.RemoveAt(i);
            }

            // prune stale cooldown entries
            var staleThreshold = TimeSpan.FromSeconds(30);
            var stale = new List<string>();

            foreach (var kv in lastShown)
                if ((now - kv.Value) > staleThreshold)
                    stale.Add(kv.Key);

            foreach (var key in stale)
                lastShown.Remove(key);
        }
    }

    public List<(int alpha, string text)> TakeLast(int number)
    {
        if (number <= 0) return new List<(int alpha, string text)>(0);

        long nowTicks = DateTime.UtcNow.Ticks;
        long expirationTicks = expiration.Ticks;

        lock (syncRoot)
        {
            var visible = new List<SubtitleEntry>();
            foreach (var entry in collection)
                if (entry.ShowAtTicks <= nowTicks)
                    visible.Add(entry);

            if (visible.Count == 0)
                return new List<(int, string)>(0);

            visible.Sort((a, b) => a.ShowAtTicks.CompareTo(b.ShowAtTicks));

            int start = Math.Max(0, visible.Count - number);
            var result = new List<(int, string)>(visible.Count - start);

            if (Plugin.ExprementalPolish.Value == false)
            {
                for (int i = start; i < visible.Count; i++)
                    result.Add((100, visible[i].Text));
                return result;
            }

            long fadeWindowTicks = TimeSpan.FromSeconds(2).Ticks;

            for (int i = start; i < visible.Count; i++)
            {
                var entry = visible[i];
                long ageTicks = nowTicks - entry.ShowAtTicks;

                float alpha01;

                long fadeStartTicks = expirationTicks - fadeWindowTicks;

                if (ageTicks < fadeStartTicks)
                {
                    alpha01 = 1f;
                }
                else
                {
                    long fadeProgressTicks = ageTicks - fadeStartTicks;
                    float t = Mathf.Clamp01((float)fadeProgressTicks / fadeWindowTicks);
                    alpha01 = 1f - t;
                }

                if (Plugin.ReducedCaptions.Value && lastShown.TryGetValue(entry.Text, out var last) && (DateTime.UtcNow - last) < reducedCaptionsCooldown) alpha01 = 1f;

                int alpha0to100 = Mathf.RoundToInt(alpha01 * 100f);
                result.Add((alpha0to100, entry.Text));
            }

            return result;
        }
    }

    public IEnumerator<string> GetEnumerator()
    {
        lock (syncRoot)
            return collection.Select(e => e.Text).ToList().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(string text)
    {
        var now = DateTime.UtcNow;
        long nowTicks = now.Ticks;

        string key = Regex.Replace(text, @"<color=\#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?>", "");
        key = Regex.Replace(key, @"</color>", "");
        if (Plugin.BackgroundVisible.Value == true)
        {
            key = Regex.Replace(key, @"<mark=\#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?>", "");
            key = Regex.Replace(key, @"</mark>", "");
        }

        lock (syncRoot)
        {
            if (key == null)
            {
                collection.Add(new SubtitleEntry(now, text, null));
                return;
            }

            if (Plugin.ReducedCaptions.Value)
                if (lastShown.TryGetValue(key, out var last) && (now - last) < reducedCaptionsCooldown) return;

            lastShown[key] = now;
        }

        for (int i = 0; i < collection.Count; i++)
        {
            if (collection[i].Key == key)
            {
                var entry = collection[i];
                entry.ShowAtTicks = nowTicks;
                entry.Text = text;
                collection[i] = entry;
                return;
            }
        }

        collection.Add(new SubtitleEntry(now, text, key));
    }

    public void Add(string item, float seconds)
    {
        lock (syncRoot)
            collection.Add(new(DateTime.UtcNow.AddSeconds(seconds), item));
    }

    public int Count
    {
        get { lock (syncRoot) return collection.Count; }
    }

    public bool IsSynchronized => false;
    public bool IsReadOnly => false;

    public string this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public void CopyTo(string[] array, int index)
    {
        lock (syncRoot)
        {
            for (int i = 0; i < collection.Count; i++)
                array[i + index] = collection[i].Text;
        }
    }

    public bool Remove(string item)
    {
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

            return removed;
        }
    }

    public void RemoveAt(int index)
    {
        lock (syncRoot)
            collection.RemoveAt(index);
    }

    public bool Contains(string item)
    {
        lock (syncRoot)
            return collection.Any(e => e.Text == item);
    }

    public void Insert(int index, string item)
    {
        lock (syncRoot)
            collection.Insert(index, new(DateTime.UtcNow, item));
    }

    public int IndexOf(string item)
    {
        lock (syncRoot)
        {
            for (int i = 0; i < collection.Count; i++)
                if (collection[i].Text == item)
                    return i;
            return -1;
        }
    }

    public void Clear()
    {
        lock (syncRoot)
            collection.Clear();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        timer.Elapsed -= RemoveExpiredElements;
        timer.Stop();
        timer.Dispose();
    }
}