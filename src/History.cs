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

        public string Label
        {
            get
            {
                var t = Text.Replace('\n', ' ').Replace('\r', ' ');
                if (t.Length > 24) t = t.Substring(0, 24) + "…";
                var tail = string.Join("  ·  ", new[] { Reading, Meaning }.Where(s => s.Length > 0));
                if (tail.Length > 46) tail = tail.Substring(0, 46) + "…";
                return tail.Length > 0 ? $"{t}   —   {tail}" : t;
            }
        }
    }

    /// <summary>Most recent lookups, newest first, persisted in %APPDATA%\KanjiPop\history.json.</summary>
    public sealed class History
    {
        static string FilePath => Path.Combine(Config.Dir, "history.json");
        public List<HistoryItem> Items { get; private set; } = new();

        public static History Load()
        {
            var h = new History();
            try
            {
                if (File.Exists(FilePath))
                    h.Items = JsonSerializer.Deserialize<List<HistoryItem>>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { }
            return h;
        }

        public void Add(Analysis a, int max)
        {
            if (max <= 0 || a.Tokens.Count == 0) return;
            var withEntry = a.Tokens.Where(t => t.Entries.Count > 0).ToList();
            var item = new HistoryItem { Text = a.Text };
            if (withEntry.Count == 1)
            {
                item.Reading = withEntry[0].Reading;
                var s = withEntry[0].Entries[0].Senses.FirstOrDefault();
                if (s != null) item.Meaning = string.Join(", ", s.Gloss.Take(2));
            }
            else
                item.Reading = string.Join(" ", a.Tokens.Where(t => t.HasKanji && t.Entries.Count > 0).Select(t => t.Reading));
            Items.RemoveAll(i => i.Text == item.Text);
            Items.Insert(0, item);
            if (Items.Count > max) Items.RemoveRange(max, Items.Count - max);
            Save();
        }

        public void Clear() { Items.Clear(); Save(); }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Config.Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Items));
            }
            catch { }
        }
    }
}
