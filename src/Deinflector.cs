using System.Collections.Generic;
using System.Linq;

namespace KanjiPop
{
    /// <summary>
    /// Rule-based reversal of Japanese verb/adjective conjugation: 食べました -> 食べる.
    /// Candidates are validated against JMdict part-of-speech by the caller.
    /// </summary>
    public static class Deinflector
    {
        public const int V1 = 1, V5 = 2, VS = 4, VK = 8, ADJ = 16, VSN = 32, PSEUDO = 64;

        sealed record Rule(string From, string To, int Cls, string Reason);

        public sealed record Cand(string Word, string Full, int Cls, string[] Reasons);

        static readonly Dictionary<char, List<Rule>> ByLast = new();
        static readonly List<Rule> Stem = new();

        static Deinflector()
        {
            void Add(string from, string to, int cls, string reason)
            {
                var r = new Rule(from, to, cls, reason);
                if (from.Length == 0) Stem.Add(r);
                else
                {
                    if (!ByLast.TryGetValue(from[^1], out var l)) ByLast[from[^1]] = l = new();
                    l.Add(r);
                }
            }

            // ---- godan verbs ----
            var g = new[]
            {
                ("う","わ","い","え","お","って","った"), ("く","か","き","け","こ","いて","いた"),
                ("ぐ","が","ぎ","げ","ご","いで","いだ"), ("す","さ","し","せ","そ","して","した"),
                ("つ","た","ち","て","と","って","った"), ("ぬ","な","に","ね","の","んで","んだ"),
                ("ぶ","ば","び","べ","ぼ","んで","んだ"), ("む","ま","み","め","も","んで","んだ"),
                ("る","ら","り","れ","ろ","って","った"),
            };
            foreach (var (u, a, i, e, o, te, ta) in g)
            {
                Add(a + "ない", u, V5, "negative");
                Add(a + "ず", u, V5, "negative (literary)");
                Add(a + "れる", u, V5, "passive");
                Add(a + "せる", u, V5, "causative");
                Add(a + "せられる", u, V5, "causative-passive");
                Add(a + "される", u, V5, "causative-passive");
                Add(i + "ます", u, V5, "polite");
                Add(i + "たい", u, V5, "wants to");
                Add(i + "ながら", u, V5, "while");
                Add(i + "そう", u, V5, "seems");
                Add(i + "すぎる", u, V5, "too much");
                Add(i, u, V5, "continuative");
                Add(e + "ば", u, V5, "conditional");
                Add(e + "る", u, V5, "potential");
                Add(e, u, V5, "imperative");
                Add(o + "う", u, V5, "volitional");
                Add(te, u, V5, "te-form");
                Add(ta, u, V5, "past");
            }
            Add("って", "く", V5, "te-form"); Add("った", "く", V5, "past"); // 行く
            Add("うて", "う", V5, "te-form"); Add("うた", "う", V5, "past");

            // ---- ichidan verbs ----
            Add("ない", "る", V1, "negative");
            Add("ず", "る", V1, "negative (literary)");
            Add("ます", "る", V1, "polite");
            Add("たい", "る", V1, "wants to");
            Add("ながら", "る", V1, "while");
            Add("そう", "る", V1, "seems");
            Add("すぎる", "る", V1, "too much");
            Add("", "る", V1, "continuative");
            Add("て", "る", V1, "te-form");
            Add("た", "る", V1, "past");
            Add("れば", "る", V1, "conditional");
            Add("ろ", "る", V1, "imperative");
            Add("よう", "る", V1, "volitional");
            Add("られる", "る", V1, "potential/passive");
            Add("させる", "る", V1, "causative");
            Add("させられる", "る", V1, "causative-passive");

            // ---- suru ----
            foreach (var (f, r) in new[]
            {
                ("しない","negative"),("します","polite"),("した","past"),("して","te-form"),("しよう","volitional"),
                ("すれば","conditional"),("しろ","imperative"),("せよ","imperative"),("される","passive"),
                ("させる","causative"),("させられる","causative-passive"),("したい","wants to"),
                ("しながら","while"),("できる","potential"),("せず","negative (literary)"),("し","continuative"),
            })
                Add(f, "する", VS, r);

            // ---- kuru (kana + kanji) ----
            foreach (var (k, st, ko) in new[] { ("くる", "き", "こ"), ("来る", "来", "来") })
            {
                bool kana = k == "くる";
                Add(ko + "ない", k, VK, "negative");
                Add(st + "ます", k, VK, "polite");
                Add(st + "て", k, VK, "te-form");
                Add(st + "た", k, VK, "past");
                Add(ko + (kana ? "よう" : "よう"), k, VK, "volitional");
                Add(kana ? "くれば" : "来れば", k, VK, "conditional");
                Add(kana ? "こい" : "来い", k, VK, "imperative");
                Add(st + "たい", k, VK, "wants to");
                Add(ko + "られる", k, VK, "potential/passive");
                Add(ko + "させる", k, VK, "causative");
                Add(st, k, VK, "continuative");
            }

            // ---- i-adjectives ----
            Add("くない", "い", ADJ, "negative");
            Add("くなかった", "い", ADJ, "negative past");
            Add("かった", "い", ADJ, "past");
            Add("くて", "い", ADJ, "te-form");
            Add("ければ", "い", ADJ, "conditional");
            Add("かろう", "い", ADJ, "volitional");
            Add("く", "い", ADJ, "adverbial");
            Add("さ", "い", ADJ, "noun form (-sa)");
            Add("そう", "い", ADJ, "seems");
            Add("すぎる", "い", ADJ, "too much");

            // ---- intermediate forms (chain into the rules above) ----
            Add("ました", "ます", PSEUDO, "past");
            Add("ません", "ます", PSEUDO, "negative");
            Add("ませんでした", "ます", PSEUDO, "negative past");
            Add("ましょう", "ます", PSEUDO, "volitional");
            Add("なかった", "ない", ADJ, "past");
            Add("なくて", "ない", ADJ, "te-form");
            Add("なければ", "ない", ADJ, "conditional");
            Add("ないで", "ない", ADJ, "negative te-form");
            foreach (var t in new[] { "て", "で" })
            {
                Add(t + "いる", t, PSEUDO, "progressive");
                Add(t + "いた", t, PSEUDO, "progressive past");
                Add(t + "います", t, PSEUDO, "progressive polite");
                Add(t + "いました", t, PSEUDO, "progressive polite past");
                Add(t + "いません", t, PSEUDO, "progressive negative");
                Add(t + "いない", t, PSEUDO, "progressive negative");
                Add(t + "いなかった", t, PSEUDO, "progressive negative past");
                Add(t + "いて", t, PSEUDO, "progressive te-form");
                Add(t + "しまう", t, PSEUDO, "completed");
                Add(t + "しまった", t, PSEUDO, "completed past");
                Add(t + "しまいます", t, PSEUDO, "completed polite");
                Add(t + "ください", t, PSEUDO, "request");
            }
        }

        /// <summary>All plausible dictionary-form candidates for an inflected word (depth-limited BFS).</summary>
        public static List<Cand> Candidates(string word)
        {
            var result = new List<Cand>();
            var seen = new HashSet<(string, int)>();
            var queue = new Queue<(string w, int depth, string[] reasons)>();
            queue.Enqueue((word, 0, System.Array.Empty<string>()));
            while (queue.Count > 0)
            {
                var (w, depth, reasons) = queue.Dequeue();
                if (depth >= 4) continue;
                var applicable = new List<Rule>();
                if (ByLast.TryGetValue(w[^1], out var l)) applicable.AddRange(l);
                if (depth == 0) applicable.AddRange(Stem);
                foreach (var r in applicable)
                {
                    if (!w.EndsWith(r.From, System.StringComparison.Ordinal)) continue;
                    var nw = w.Substring(0, w.Length - r.From.Length) + r.To;
                    if (nw.Length == 0 || nw == w) continue;
                    var rs = reasons.Append(r.Reason).ToArray();
                    if (!seen.Add((nw, r.Cls))) continue;
                    if (r.Cls != PSEUDO) result.Add(new Cand(nw, nw, r.Cls, rs));
                    if (r.Cls == VS && nw.Length > 2 && nw.EndsWith("する"))
                        result.Add(new Cand(nw[..^2], nw, VSN, rs));
                    queue.Enqueue((nw, depth + 1, rs));
                }
            }
            return result;
        }

        public static bool Matches(RawEntry e, int cls)
        {
            foreach (var s in e.Senses)
                foreach (var p in s.Pos)
                {
                    switch (cls)
                    {
                        case V1: if (p.StartsWith("Ichidan verb")) return true; break;
                        case V5: if (p.StartsWith("Godan verb")) return true; break;
                        case VS: if (p.StartsWith("Suru verb")) return true; break;
                        case VK: if (p.StartsWith("Kuru verb")) return true; break;
                        case ADJ: if (p == "adjective") return true; break;
                        case VSN: if (p.Contains("aux. verb suru")) return true; break;
                    }
                }
            return false;
        }
    }
}
