using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Timers;

namespace Subtitles;

public class SubtitleList : IList<string>, IDisposable
{
    // underlying collection stores (showAfter, text)
    private volatile List<Tuple<DateTime, string>> collection = new List<Tuple<DateTime, string>>();
    private readonly object syncRoot = new object();
    private readonly Timer timer;
    private readonly TimeSpan expiration;

    // Reduced-captions cooldown (applies when Plugin.Instance.ReducedCaptions.Value == true).
    // Tune this value to be more/less forgiving.
    private const int ReducedCaptionsCooldownMs = 4200;
    private readonly TimeSpan reducedCaptionsCooldown = TimeSpan.FromMilliseconds(ReducedCaptionsCooldownMs);
    private readonly Dictionary<string, DateTime> lastShown = new Dictionary<string, DateTime>(StringComparer.Ordinal);
    private bool disposed;

    public SubtitleList()
    {
        timer = new Timer
        {
            Interval = 1000
        };
        timer.Elapsed += RemoveExpiredElements;
        timer.Start();

        expiration = TimeSpan.FromMilliseconds(Constants.DefaultExpireSubtitleTimeMs);
    }

    private void RemoveExpiredElements(object sender, ElapsedEventArgs e)
    {
        lock (syncRoot)
        {
            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if ((DateTime.Now - collection[i].Item1) >= expiration)
                {
                    collection.RemoveAt(i);
                }
            }

            // Optionally prune lastShown entries that are stale to keep memory bounded.
            var staleKeys = lastShown.Where(kv => (DateTime.Now - kv.Value) > TimeSpan.FromSeconds(30)).Select(kv => kv.Key).ToList();
            foreach (var key in staleKeys)
            {
                lastShown.Remove(key);
            }
        }
    }

    // Public API: take the most-recent visible subtitles
    public List<string> TakeLast(int number)
    {
        lock (syncRoot)
        {
            return collection
                .Where(element => DateTime.Now >= element.Item1)
                .OrderBy(element => element.Item1)
                .Select(element => element.Item2)
                .TakeLast(number)
                .ToList();
        }
    }

    public string this[int index]
    {
        get
        {
            lock (syncRoot)
            {
                return collection[index].Item2;
            }
        }
        set
        {
            lock (syncRoot)
            {
                collection[index] = new Tuple<DateTime, string>(DateTime.Now, value);
                // update lastShown so reduced-captions cooldown knows it was shown
                if (Plugin.ReducedCaptions.Value == true)
                {
                    lastShown[value] = DateTime.Now;
                }
            }
        }
    }

    public IEnumerator<string> GetEnumerator()
    {
        lock (syncRoot)
        {
            // snapshot to avoid concurrent modification while enumerating
            return collection.Select(x => x.Item2).ToList().GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        lock (syncRoot)
        {
            return collection.Select(x => x.Item2).ToList().GetEnumerator();
        }
    }

    // Adds a subtitle to show immediately (or shortly). Respects ReducedCaptions cooldown.
    public void Add(string item)
    {
        if (string.IsNullOrWhiteSpace(item)) return;

        if (Plugin.ReducedCaptions.Value == true)
        {
            lock (syncRoot)
            {
                if (lastShown.TryGetValue(item, out var last) && (DateTime.Now - last) < reducedCaptionsCooldown)
                {
                    // suppressed by reduced-captions cooldown
                    return;
                }

                lastShown[item] = DateTime.Now;
                collection.Add(new Tuple<DateTime, string>(DateTime.Now, item));
            }
        }
        else
        {
            lock (syncRoot)
            {
                collection.Add(new Tuple<DateTime, string>(DateTime.Now, item));
            }
        }
    }

    // Adds a subtitle scheduled to appear after specified seconds. Still updates cooldown when scheduled.
    public void Add(string item, float seconds)
    {
        if (string.IsNullOrWhiteSpace(item)) return;

        var showAt = DateTime.Now.AddSeconds(seconds);

        if (Plugin.ReducedCaptions.Value == true)
        {
            lock (syncRoot)
            {
                if (lastShown.TryGetValue(item, out var last) && (DateTime.Now - last) < reducedCaptionsCooldown)
                {
                    // suppressed by reduced-captions cooldown
                    return;
                }

                lastShown[item] = DateTime.Now;
                collection.Add(new Tuple<DateTime, string>(showAt, item));
            }
        }
        else
        {
            lock (syncRoot)
            {
                collection.Add(new Tuple<DateTime, string>(showAt, item));
            }
        }
    }

    public int Count
    {
        get
        {
            lock (syncRoot)
            {
                return collection.Count;
            }
        }
    }

    public bool IsSynchronized => false;

    public bool IsReadOnly => false;

    public void CopyTo(string[] array, int index)
    {
        lock (syncRoot)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                array[i + index] = collection[i].Item2;
            }
        }
    }

    public bool Remove(string item)
    {
        lock (syncRoot)
        {
            bool contained = Contains(item);

            for (int i = collection.Count - 1; i >= 0; i--)
            {
                if (collection[i].Item2 == item)
                {
                    collection.RemoveAt(i);
                }
            }

            // also clear cooldown record so future occurrences can show immediately
            if (lastShown.ContainsKey(item))
            {
                lastShown.Remove(item);
            }

            return contained;
        }
    }

    public void RemoveAt(int i)
    {
        lock (syncRoot)
        {
            collection.RemoveAt(i);
        }
    }

    public bool Contains(string item)
    {
        lock (syncRoot)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                if (collection[i].Item2 == item)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Insert(int index, string item)
    {
        if (string.IsNullOrWhiteSpace(item)) return;

        lock (syncRoot)
        {
            collection.Insert(index, new Tuple<DateTime, string>(DateTime.Now, item));
            if (Plugin.ReducedCaptions.Value == true)
            {
                lastShown[item] = DateTime.Now;
            }
        }
    }

    public int IndexOf(string item)
    {
        lock (syncRoot)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                if (collection[i].Item2 == item)
                {
                    return i;
                }
            }

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
