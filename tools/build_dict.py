"""Build data/dict.db (SQLite) from JMdict_e and KANJIDIC2.

Usage:  python tools/build_dict.py [raw_dir] [out_db]
Downloads the source files into raw_dir if missing.
Source data (c) EDRDG, licensed CC BY-SA 4.0 - see README.
"""
import csv, gzip, json, os, sqlite3, sys, urllib.request
import xml.etree.ElementTree as ET

RAW = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), "raw")
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(__file__), "..", "data", "dict.db")
SOURCES = {
    "JMdict_e.gz": "http://ftp.edrdg.org/pub/Nihongo/JMdict_e.gz",
    "kanjidic2.xml.gz": "http://ftp.edrdg.org/pub/Nihongo/kanjidic2.xml.gz",
}
FREQ_URL = "https://api.jiten.moe/api/frequency-list/download?downloadType=csv"  # Jiten, CC BY-SA 4.0
COMMON_TAGS = {"news1", "ichi1", "spec1", "spec2", "gai1"}


def load_freq():
    """(written form, reading) -> best rank. Returns {} (no ranking) if the list is unavailable."""
    path = os.path.join(RAW, "jiten_freq.csv")
    try:
        if not os.path.exists(path):
            print("downloading", FREQ_URL)
            req = urllib.request.Request(FREQ_URL, headers={"User-Agent": "KanjiPop-build"})
            with urllib.request.urlopen(req, timeout=120) as r, open(path, "wb") as f:
                f.write(r.read())
        freq = {}
        with open(path, encoding="utf-8", newline="") as f:
            for row in csv.DictReader(f):
                key = (row["Word"], row["Form"])
                rank = int(row["Rank"])
                if key not in freq or rank < freq[key]:
                    freq[key] = rank
        print("frequency pairs:", len(freq))
        return freq
    except Exception as e:  # build still works, just without frequency ordering
        print("WARNING: frequency list unavailable:", e)
        return {}


def fetch():
    os.makedirs(RAW, exist_ok=True)
    for name, url in SOURCES.items():
        path = os.path.join(RAW, name)
        if not os.path.exists(path):
            print("downloading", url)
            urllib.request.urlretrieve(url, path)


def short_pos(p):
    # entities expand to e.g. "noun (common) (futsuumeishi)"; keep the readable head
    p = p.split(" (")[0]
    return p


WEIGHTS = {"ichi1": 20, "news1": 20, "spec1": 15, "gai1": 10, "spec2": 8, "news2": 8, "ichi2": 5, "gai2": 4}


def score_of(tags):
    """Popularity score from the union of priority tags (higher = more common)."""
    sc = 0
    nf = 99
    for t in tags:
        sc += WEIGHTS.get(t, 0)
        if t.startswith("nf") and t[2:].isdigit():
            nf = min(nf, int(t[2:]))
    if nf < 99:
        sc += 50 - nf
    return sc


def build_words(db):
    db.executescript(
        """
        CREATE TABLE entry(id INTEGER PRIMARY KEY, common INTEGER, score INTEGER, rank INTEGER,
                           kanji TEXT, readings TEXT, senses TEXT);
        CREATE TABLE lookup(text TEXT, entry_id INTEGER);
        """
    )
    freq = load_freq()
    path = os.path.join(RAW, "JMdict_e.gz")
    rows, looks = [], []
    n = 0
    with gzip.open(path, "rb") as f:
        for _, el in ET.iterparse(f, events=("end",)):
            if el.tag != "entry":
                continue
            eid = int(el.findtext("ent_seq"))
            alltags = set()
            kanji = []
            for k in el.findall("k_ele"):
                keb = k.findtext("keb")
                inf = [i.text for i in k.findall("ke_inf")]
                pri = [p.text for p in k.findall("ke_pri")]
                alltags.update(pri)
                # rarely-used / irregular forms are searchable but flagged
                rare = any(("rarely" in x or "out-dated" in x or "irregular" in x or "search-only" in x)
                           for x in inf)
                kanji.append({"k": keb, "rare": rare})
            readings = []
            for r in el.findall("r_ele"):
                reb = r.findtext("reb")
                pri = [p.text for p in r.findall("re_pri")]
                alltags.update(pri)
                restr = [x.text for x in r.findall("re_restr")]
                nokanji = r.find("re_nokanji") is not None
                readings.append({"r": reb, "restr": restr or None, "nokanji": nokanji})
            senses = []
            last_pos = []
            for s in el.findall("sense"):
                glosses = [g.text for g in s.findall("gloss") if g.text]
                if not glosses:
                    continue
                pos = [short_pos(p.text) for p in s.findall("pos")]
                if pos:
                    last_pos = pos  # JMdict only repeats <pos> when it changes
                else:
                    pos = last_pos
                misc = [short_pos(m.text) for m in s.findall("misc")]
                senses.append({"pos": pos, "misc": misc, "g": glosses})
            rank = 0  # 0 = not in the frequency list
            best = None
            # the bare-kana spelling only counts for words normally written in kana, otherwise
            # every homophone (葉, 歯, 派...) would inherit the rank of the particle は
            kana_ok = not kanji or any("usually written using kana alone" in m for sn in senses for m in sn["misc"])
            for r in readings:
                for k in [x["k"] for x in kanji] + ([r["r"]] if kana_ok else []):
                    v = freq.get((k, r["r"]))
                    if v is not None and (best is None or v < best):
                        best = v
            if best is not None:
                rank = best
            rows.append((eid, int(bool(alltags & COMMON_TAGS)), score_of(alltags), rank,
                         json.dumps(kanji, ensure_ascii=False),
                         json.dumps(readings, ensure_ascii=False),
                         json.dumps(senses, ensure_ascii=False)))
            for k in kanji:
                looks.append((k["k"], eid))
            for r in readings:
                looks.append((r["r"], eid))
            el.clear()
            n += 1
    db.executemany("INSERT INTO entry VALUES (?,?,?,?,?,?,?)", rows)
    db.executemany("INSERT INTO lookup VALUES (?,?)", looks)
    db.execute("CREATE INDEX idx_lookup ON lookup(text)")
    print("entries:", n, "lookup rows:", len(looks))


def build_kanji(db):
    db.execute(
        """CREATE TABLE kanji(literal TEXT PRIMARY KEY, on_yomi TEXT, kun_yomi TEXT, meanings TEXT,
                              jlpt INTEGER, grade INTEGER, strokes INTEGER, freq INTEGER)"""
    )
    path = os.path.join(RAW, "kanjidic2.xml.gz")
    rows = []
    with gzip.open(path, "rb") as f:
        for _, el in ET.iterparse(f, events=("end",)):
            if el.tag != "character":
                continue
            lit = el.findtext("literal")
            rm = el.find("reading_meaning")
            on, kun, mean = [], [], []
            if rm is not None:
                grp = rm.find("rmgroup")
                if grp is not None:
                    for r in grp.findall("reading"):
                        if r.get("r_type") == "ja_on":
                            on.append(r.text)
                        elif r.get("r_type") == "ja_kun":
                            kun.append(r.text)
                    mean = [m.text for m in grp.findall("meaning") if m.get("m_lang") in (None, "en")]
            misc = el.find("misc")
            def num(tag):
                v = misc.findtext(tag) if misc is not None else None
                return int(v) if v else None
            rows.append((lit, json.dumps(on, ensure_ascii=False), json.dumps(kun, ensure_ascii=False),
                         json.dumps(mean, ensure_ascii=False), num("jlpt"), num("grade"),
                         num("stroke_count"), num("freq")))
            el.clear()
    db.executemany("INSERT INTO kanji VALUES (?,?,?,?,?,?,?,?)", rows)
    print("kanji:", len(rows))


def main():
    fetch()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    if os.path.exists(OUT):
        os.remove(OUT)
    db = sqlite3.connect(OUT)
    build_words(db)
    build_kanji(db)
    db.commit()
    db.execute("VACUUM")
    db.close()
    print("wrote", OUT, os.path.getsize(OUT) // 1024 // 1024, "MB")


if __name__ == "__main__":
    main()
