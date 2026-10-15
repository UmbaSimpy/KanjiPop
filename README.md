# KanjiPop

A tiny always-on Windows tray app. Select Japanese text anywhere, press **Ctrl+C twice**, and a popup shows the
**hiragana reading**, the **meaning**, and a **per-kanji breakdown** (on/kun readings, meanings, JLPT level, strokes).
Everything is **offline**: the dictionary is a local SQLite file built from JMdict and KANJIDIC2.

It is a word/kanji lookup tool, not a sentence translator. Longer selections are split into words (with the
reading shown above each one) and every word gets its meaning.

## Use
1. Run `KanjiPop.exe` (tray icon "あ"). No installation, no admin rights.
2. Select text in any app and press **Ctrl+C twice** quickly.
3. **Esc** or a click elsewhere closes the popup. Tray icon -> *History* lists your recent lookups (click one to reopen it). Left-click the tray icon to look up the current clipboard.

Conjugated forms work: `食べました`, `読んでいる`, `高くなかった`, `勉強して` resolve to their dictionary form, with the
reading of the form you selected.

## Settings
Tray icon -> *Open settings file* (`%APPDATA%\KanjiPop\config.json`), then *Reload settings*.

| Key | Default | Meaning |
|---|---|---|
| `Hotkey` | `"Ctrl+C"` | Combo such as `"Ctrl+Alt+J"`, `"F8"`. Any hotkey other than Ctrl+C makes the app send the copy itself. |
| `DoubleTap` | `true` | Press the hotkey twice within `DoubleTapMs`. |
| `DoubleTapMs` | `500` | Double-press window. |
| `ShowRomaji` | `false` | Also show romaji. |
| `ShowKanaWords` | `false` | In multi-word selections, also list kana-only words. |
| `MaxEntries`, `MaxSensesPerEntry`, `MaxKanji`, `MaxChars` | `3, 5, 10, 120` | Size limits. |
| `FontSize` | `14` | Base font size. |
| `AutoCloseSeconds` | `0` | `0` = stay until dismissed. |

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
