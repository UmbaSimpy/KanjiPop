using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KanjiPop
{
    public sealed class HistoryItem
    {
        public string Text { get; set; } = "";
        public string Reading { get; set; } = "";
        public string Meaning { get; set; } = "";
        public DateTime Time { get; set; } = DateTime.Now;

        public static HistoryItem From(Analysis a)
        {
            var withEntry = a.Tokens.Where(t => t.Entries.Count > 0).ToList();
            var item = new HistoryItem { Text = a.Text };
            if (withEntry.Count == 1)
            {
                item.Reading = withEntry[0].Reading;
                var s = withEntry[0].Entries[0].Senses.FirstOrDefault();
                if (s != null) item.Meaning = string.Join(", ", s.Gloss.Take(3));
            }
            else
            {
                item.Reading = string.Join(" ", a.Tokens.Where(t => t.HasKanji && t.Entries.Count > 0).Select(t => t.Reading));
                var first = withEntry.FirstOrDefault(t => t.HasKanji)?.Entries[0].Senses.FirstOrDefault();
                if (first != null) item.Meaning = string.Join(", ", first.Gloss.Take(2)) + " …";
            }
            return item;
        }
    }

    /// <summary>Newest-first list of lookups persisted as JSON in %APPDATA%\KanjiPop.</summary>
    public sealed class ItemStore
    {
        readonly string _path;
        public List<HistoryItem> Items { get; private set; } = new();
        public event Action? Changed;

        public ItemStore(string fileName)
        {
            _path = Path.Combine(Config.Dir, fileName);
            try
            {
                if (File.Exists(_path))
                    Items = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(_path)) ?? new();
            }
            catch { }
        }

        public bool Contains(string text) => Items.Any(i => i.Text == text);

        /// <param name="max">Keep at most this many (0 or less = unlimited).</param>
        public void Add(HistoryItem item, int max)
        {
            Items.RemoveAll(i => i.Text == item.Text);
            Items.Insert(0, item);
            if (max > 0 && Items.Count > max) Items.RemoveRange(max, Items.Count - max);
            Save();
        }

        public void Remove(string text) { Items.RemoveAll(i => i.Text == text); Save(); }
        public void Clear() { Items.Clear(); Save(); }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                File.WriteAllText(_path, JsonSerializer.Serialize(Items));
            }
            catch { }
            Changed?.Invoke();
        }
    }

    /// <summary>Small persisted UI state (remembered popup position).</summary>
    public sealed class AppState
    {
        public bool HasPos { get; set; }
        public int X { get; set; }
        public int Y { get; set; }

        static string FilePath => Path.Combine(Config.Dir, "state.json");

        public static AppState Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonSerializer.Deserialize<AppState>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { }
            return new();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch { }
        }
    }
}
