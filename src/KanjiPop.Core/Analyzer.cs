using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KanjiPop
{
    public sealed class Sense
    {
        public List<string> Pos = new();
        public List<string> Gloss = new();
        public List<string> Misc = new();
    }

    public sealed class EntryView
    {
        public long Id;
        public string Headword = "";
        public List<string> Readings = new();
        public List<string> Written = new();
        public List<Sense> Senses = new();
        public bool Common;
        public int Score;
        public int Rank;
        public bool Direct = true;
        public string DictForm = "";
        public string Reasons = "";
        /// <summary>Reading of the selected surface text (differs from Readings when conjugated).</summary>
        public string SurfaceReading = "";
    }

    public sealed class Token
    {
        public string Surface = "";
        public List<EntryView> Entries = new();
        public bool HasKanji;
        public string Reading => Entries.Count > 0 ? Entries[0].SurfaceReading : "";
    }

    public sealed class Analysis
    {
        public string Text = "";
        public List<Token> Tokens = new();
        public List<KanjiInfo> Kanji = new();
        public bool Truncated;
    }

    /// <summary>Greedy longest-match tokenizer over JMdict with conjugation handling.</summary>
    public sealed class Analyzer
    {
        const int MaxWordLen = 14;
        readonly Dict _dict;
        readonly Config _cfg;

        public Analyzer(Dict dict, Config cfg) { _dict = dict; _cfg = cfg; }

        public Analysis Analyze(string raw)
        {
            var text = raw.Trim().Normalize(NormalizationForm.FormKC);
            var res = new Analysis();
            if (text.Length > _cfg.MaxChars) { text = text.Substring(0, _cfg.MaxChars); res.Truncated = true; }
            res.Text = text;

            var cache = new Dictionary<string, List<EntryView>>();
            int i = 0;
            while (i < text.Length)
            {
                if (!Kana.IsWordChar(text[i])) { i++; continue; }
                int limit = 0;
                while (i + limit < text.Length && limit < MaxWordLen && Kana.IsWordChar(text[i + limit])) limit++;

                Token? tok = null;
                for (int len = limit; len >= 1; len--)
                {
                    var sub = text.Substring(i, len);
                    if (!cache.TryGetValue(sub, out var entries)) cache[sub] = entries = Find(sub);
                    if (entries.Count > 0)
                    {
                        tok = new Token { Surface = sub, Entries = entries, HasKanji = Kana.HasKanji(sub) };
                        i += len;
                        break;
                    }
                }
                if (tok == null)
                {
                    var c = text.Substring(i, 1);
                    if (Kana.IsKanji(text[i])) res.Tokens.Add(new Token { Surface = c, HasKanji = true });
                    i++;
                }
                else res.Tokens.Add(tok);
            }

            var seen = new HashSet<char>();
            foreach (var ch in text)
            {
                if (!Kana.IsKanji(ch) || !seen.Add(ch)) continue;
                var k = _dict.GetKanji(ch.ToString());
                if (k != null) res.Kanji.Add(k);
                if (res.Kanji.Count >= _cfg.MaxKanji) break;
            }
            return res;
        }

        List<EntryView> Find(string sub)
        {
            var found = new List<EntryView>();
            var ids = new HashSet<long>();
            foreach (var e in _dict.Find(sub))
                if (ids.Add(e.Id)) found.Add(Make(e, sub, sub, null));
            if (sub.Length >= 2 || Kana.IsKana(sub[0]))
            {
                foreach (var c in Deinflector.Candidates(sub))
                    foreach (var e in _dict.Find(c.Word))
                        if (Deinflector.Matches(e, c.Cls) && ids.Add(e.Id))
                            found.Add(Make(e, c.Word, sub, c));
            }
            return found
                .OrderByDescending(v => v.Direct)
                .ThenBy(v => v.Rank)
                .ThenByDescending(v => v.Score)
                .ThenByDescending(v => v.Common)
                .ThenBy(v => v.Id)
                .ToList();
        }

        EntryView Make(RawEntry e, string matched, string surface, Deinflector.Cand? cand)
        {
            var v = new EntryView { Id = e.Id, Common = e.Common, Score = e.Score, Rank = e.Rank == 0 ? int.MaxValue : e.Rank, Direct = cand == null };
            bool asKanji = e.Kanji.Any(k => k.K == matched);
            v.Headword = matched;
            if (asKanji)
            {
                v.Readings = e.Readings
                    .Where(r => !r.NoKanji && (r.Restr == null || r.Restr.Contains(matched)))
                    .Select(r => r.R).ToList();
                if (v.Readings.Count == 0) v.Readings = e.Readings.Select(r => r.R).ToList();
            }
            else
            {
                v.Readings = new List<string> { matched };
                v.Written = e.Kanji.Where(k => !k.Rare).Select(k => k.K).Take(3).ToList();
            }
            foreach (var s in e.Senses)
                v.Senses.Add(new Sense { Pos = s.Pos, Gloss = s.G, Misc = s.Misc });

            if (cand == null)
            {
                v.DictForm = matched;
                v.SurfaceReading = Kana.ToHiragana(asKanji ? v.Readings[0] : surface);
            }
            else
            {
                v.DictForm = cand.Full;
                v.Reasons = string.Join(" + ", cand.Reasons.Reverse().Distinct());
                string dictReading = v.Readings[0];
                if (cand.Cls == Deinflector.VSN) dictReading += "する";
                v.SurfaceReading = InflectReading(surface, cand.Full, dictReading);
                if (asKanji) { /* keep kanji headword as dictionary form */ }
            }
            return v;
        }

        /// <summary>Reading of an inflected surface given the dictionary form and its reading.</summary>
        static string InflectReading(string surface, string dictForm, string dictReading)
        {
            if (!Kana.HasKanji(dictForm)) return Kana.ToHiragana(surface);
            if (dictForm == "来る")
            {
                var rest = surface.Substring(1);
                string stem = rest.StartsWith("な") || rest.StartsWith("よ") || rest.StartsWith("られ") || rest.StartsWith("させ") || rest.StartsWith("い")
                    ? "こ" : rest.StartsWith("れ") || rest.StartsWith("る") ? "く" : "き";
                return Kana.ToHiragana(stem + rest);
            }
            int p = 0;
            while (p < surface.Length && p < dictForm.Length && surface[p] == dictForm[p]) p++;
            string y = dictForm.Substring(p), x = surface.Substring(p);
            string rd = Kana.ToHiragana(dictReading);
            string yh = Kana.ToHiragana(y);
            if (rd.EndsWith(yh, StringComparison.Ordinal))
                return rd.Substring(0, rd.Length - yh.Length) + Kana.ToHiragana(x);
            return rd;
        }
    }
}
