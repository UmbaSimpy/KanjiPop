using System.Text;

namespace KanjiPop
{
    public static class Kana
    {
        public static bool IsKanji(char c) =>
            (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || c == 0x3005 || (c >= 0xF900 && c <= 0xFAFF);

        public static bool IsKana(char c) =>
            (c >= 0x3041 && c <= 0x309F) || (c >= 0x30A1 && c <= 0x30FA) || c == 0x30FC;

        public static bool IsWordChar(char c) => IsKanji(c) || IsKana(c);

        public static bool HasKanji(string s)
        {
            foreach (var c in s) if (IsKanji(c)) return true;
            return false;
        }

        public static bool HasJapanese(string s)
        {
            foreach (var c in s) if (IsWordChar(c)) return true;
            return false;
        }

        public static string ToHiragana(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
                sb.Append(c >= 0x30A1 && c <= 0x30F6 ? (char)(c - 0x60) : c);
            return sb.ToString();
        }

        static readonly string[] Base =
        {
            "a","i","u","e","o","ka","ki","ku","ke","ko","sa","shi","su","se","so","ta","chi","tsu","te","to",
            "na","ni","nu","ne","no","ha","hi","fu","he","ho","ma","mi","mu","me","mo","ya","yu","yo",
            "ra","ri","ru","re","ro","wa","wo","n",
            "ga","gi","gu","ge","go","za","ji","zu","ze","zo","da","ji","zu","de","do","ba","bi","bu","be","bo",
            "pa","pi","pu","pe","po"
        };
        const string BaseKana =
            "あいうえおかきくけこさしすせそたちつてと" +
            "なにぬねのはひふへほまみむめもやゆよ" +
            "らりるれろわをん" +
            "がぎぐげござじずぜぞだぢづでどばびぶべぼ" +
            "ぱぴぷぺぽ";

        public static string ToRomaji(string kana)
        {
            var s = ToHiragana(kana);
            var sb = new StringBuilder();
            bool sokuon = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == 'っ') { sokuon = true; continue; }
                if (c == 'ー')
                {
                    if (sb.Length > 0) sb.Append(sb[^1]);
                    continue;
                }
                string r;
                bool small = i + 1 < s.Length && "ゃゅょ".IndexOf(s[i + 1]) >= 0;
                int idx = BaseKana.IndexOf(c);
                if (idx < 0) { r = c.ToString(); }
                else
                {
                    r = Base[idx];
                    if (small && r.EndsWith("i"))
                    {
                        char y = s[i + 1];
                        string v = y == 'ゃ' ? "a" : y == 'ゅ' ? "u" : "o";
                        string head = r == "shi" ? "sh" : r == "chi" ? "ch" : r == "ji" ? "j" : r.Substring(0, r.Length - 1) + "y";
                        r = head + v;
                        i++;
                    }
                }
                if (sokuon && r.Length > 0)
                {
                    sb.Append(r.StartsWith("ch") ? 't' : r[0]);
                    sokuon = false;
                }
                sb.Append(r);
            }
            return sb.ToString();
        }
    }
}
