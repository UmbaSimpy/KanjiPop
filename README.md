# KanjiPop

A tiny always-on Windows tray app. Select Japanese text anywhere, press **Ctrl+C twice**, and a popup shows the
**hiragana reading**, the **meaning**, and a **per-kanji breakdown** (on/kun readings, meanings, JLPT level, strokes).
Everything is **offline**: the dictionary is a local SQLite file built from JMdict and KANJIDIC2.

It is a word/kanji lookup tool, not a sentence translator. Longer selections are split into words (with the
reading shown above each one) and every word gets its meaning.

## Use
1. Run `KanjiPop.exe` (pink "あ" tray icon). No installation, no admin rights.
2. Select text in any app and press **Ctrl+C twice** quickly.
3. **Esc** or a click elsewhere closes the popup. Left-click the tray icon to open your history.

In the popup:
- **Drag** it anywhere; it will reappear in that spot next time. The **⟲** button (shown once you have moved it) puts it back
  next to the text again; moving it again makes it remember the new spot.
- **☆ / ★** adds or removes the lookup from your favorites.

Tray menu: **History…** and **Favorites…** open a page listing past lookups (search, click to look up again, star, delete),
and **Settings…** opens the settings page (hotkey recorder, popup options, start with Windows).

Conjugated forms work: `食べました`, `読んでいる`, `高くなかった`, `勉強して` resolve to their dictionary form, with the
reading of the form you selected.

## Settings
Use the tray icon -> *Settings…*. Values are stored in `%APPDATA%\KanjiPop\config.json`; history, favorites and the
remembered popup position are in the same folder.

| Setting | Default | Meaning |
|---|---|---|
| Shortcut | `Ctrl+C` | Click the box and press any combo (`Ctrl+Alt+J`, `F8`…). Anything other than Ctrl+C makes the app send the copy itself. |
| Double-tap | on, 500 ms | Press the shortcut twice within the window. |
| Show romaji / kana-only words | off | Extra popup content. |
| Font size, auto-close | 14, never | Popup look. |
| Max entries / meanings / kanji / characters | 3 / 5 / 10 / 120 | Size limits. |
| History size | 30 | `0` turns history off. |

## Build from source
Requires Python 3 and the .NET SDK (10.x).
```
python tools/build_dict.py        # downloads JMdict/KANJIDIC2, writes data/dict.db (~77 MB)
dotnet run --project src          # run from source
powershell -File scripts/build.ps1   # portable zip in dist/
```
Pushing a `v*` tag builds the release zip on GitHub Actions and attaches it to a GitHub release.

## Notes and limitations
- Selection is read through the clipboard (the app you select in performs the copy). Text drawn as pictures
  (games, scans) cannot be read.
- Word splitting is longest-match against the dictionary. It is not a full morphological analyser, so unusual
  sentences can be split imperfectly, and homographs are ordered by JMdict priority tags (the most likely reading
  is not always first; up to 3 entries are shown).
- Dictionary data: see [NOTICE.md](NOTICE.md) (EDRDG, CC BY-SA 4.0).
