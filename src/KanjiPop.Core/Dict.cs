using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace KanjiPop
{
    public sealed class KForm
    {
        [JsonPropertyName("k")] public string K { get; set; } = "";
        [JsonPropertyName("rare")] public bool Rare { get; set; }
    }

    public sealed class RForm
    {
        [JsonPropertyName("r")] public string R { get; set; } = "";
        [JsonPropertyName("restr")] public List<string>? Restr { get; set; }
        [JsonPropertyName("nokanji")] public bool NoKanji { get; set; }
    }

    public sealed class SenseRaw
    {
        [JsonPropertyName("pos")] public List<string> Pos { get; set; } = new();
        [JsonPropertyName("misc")] public List<string> Misc { get; set; } = new();
        [JsonPropertyName("g")] public List<string> G { get; set; } = new();
    }

    public sealed class RawEntry
    {
        public long Id;
        public bool Common;
        public int Score;
        public int Rank; // frequency rank (1 = most common), 0 = unranked
        public List<KForm> Kanji = new();
        public List<RForm> Readings = new();
        public List<SenseRaw> Senses = new();
    }

    public sealed class KanjiInfo
    {
        public string Literal = "";
        public List<string> On = new(), Kun = new(), Meanings = new();
        public int? Jlpt, Grade, Strokes;
    }

    /// <summary>Read-only access to dict.db.</summary>
    public sealed class Dict : IDisposable
    {
        readonly SqliteConnection _conn;
        readonly SqliteCommand _find, _kanji;
        readonly Dictionary<string, KanjiInfo?> _kanjiCache = new();

        public Dict(string path)
        {
            _conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            _conn.Open();
            using (var p = _conn.CreateCommand())
            {
                p.CommandText = "PRAGMA cache_size=-8000; PRAGMA query_only=ON;";
                p.ExecuteNonQuery();
            }
            _find = _conn.CreateCommand();
            _find.CommandText =
                "SELECT e.id,e.common,e.score,e.rank,e.kanji,e.readings,e.senses FROM lookup l " +
                "JOIN entry e ON e.id=l.entry_id WHERE l.text=$t";
            _find.Parameters.Add("$t", SqliteType.Text);
            _kanji = _conn.CreateCommand();
            _kanji.CommandText = "SELECT on_yomi,kun_yomi,meanings,jlpt,grade,strokes FROM kanji WHERE literal=$l";
            _kanji.Parameters.Add("$l", SqliteType.Text);
        }

        public List<RawEntry> Find(string text)
        {
            var list = new List<RawEntry>();
            _find.Parameters["$t"].Value = text;
            using var rd = _find.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new RawEntry
                {
                    Id = rd.GetInt64(0),
                    Common = rd.GetInt32(1) != 0,
                    Score = rd.GetInt32(2),
                    Rank = rd.GetInt32(3),
                    Kanji = JsonSerializer.Deserialize<List<KForm>>(rd.GetString(4)) ?? new(),
                    Readings = JsonSerializer.Deserialize<List<RForm>>(rd.GetString(5)) ?? new(),
                    Senses = JsonSerializer.Deserialize<List<SenseRaw>>(rd.GetString(6)) ?? new(),
                });
            }
            return list;
        }

        public KanjiInfo? GetKanji(string literal)
        {
            if (_kanjiCache.TryGetValue(literal, out var c)) return c;
            _kanji.Parameters["$l"].Value = literal;
            using var rd = _kanji.ExecuteReader();
            KanjiInfo? k = null;
            if (rd.Read())
            {
                k = new KanjiInfo
                {
                    Literal = literal,
                    On = JsonSerializer.Deserialize<List<string>>(rd.GetString(0)) ?? new(),
                    Kun = JsonSerializer.Deserialize<List<string>>(rd.GetString(1)) ?? new(),
                    Meanings = JsonSerializer.Deserialize<List<string>>(rd.GetString(2)) ?? new(),
                    Jlpt = rd.IsDBNull(3) ? null : rd.GetInt32(3),
                    Grade = rd.IsDBNull(4) ? null : rd.GetInt32(4),
                    Strokes = rd.IsDBNull(5) ? null : rd.GetInt32(5),
                };
            }
            _kanjiCache[literal] = k;
            return k;
        }

        public void Dispose() => _conn.Dispose();
    }
}
