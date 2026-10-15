# KanjiPop

A tiny always-on tray / menu-bar app for **Windows** and **macOS**. Select Japanese text anywhere, press **Ctrl+C twice**, and a popup shows the
**hiragana reading**, the **meaning**, and a **per-kanji breakdown** (on/kun readings, meanings, JLPT level, strokes).
Everything is **offline**: the dictionary is a local SQLite file built from JMdict and KANJIDIC2.

It is a word/kanji lookup tool, not a sentence translator. Longer selections are split into words (with the
reading shown above each one) and every word gets its meaning.

## macOS version
> **Untested on real hardware** (the author has no Mac). It is built and smoke-tested on GitHub's macOS runners, and the
> interface was checked on Windows, but the Mac-specific parts (global shortcut, clipboard, menu-bar icon) have not been
> run on a Mac yet. Please open an issue if something does not work.

1. Download `KanjiPop-mac-arm64.zip` (Apple silicon) or `KanjiPop-mac-x64.zip` (Intel) from the Releases page, unzip,
   and drag **KanjiPop.app** to Applications.
2. The app is not notarized by Apple, so the first time use **right-click -> Open -> Open**
   (or run `xattr -dr com.apple.quarantine /Applications/KanjiPop.app`).
3. macOS asks for **Input Monitoring** (System Settings -> Privacy & Security). Switch KanjiPop on and reopen the app.
   This is only used to notice the shortcut; nothing is recorded.
4. Select Japanese text, press **Cmd+C twice**. The icon lives in the menu bar (no Dock icon).

Differences from Windows: default shortcut is `Cmd+C` x2; a custom shortcut must include Cmd/Ctrl/Option and is a letter, digit
or Space, and it is not blocked from reaching the app you are in (macOS only lets us listen). "Pause in these apps" uses app
names such as `Steam`. Settings are in `~/Library/Application Support/KanjiPop/`. The shortcut also works as Cmd+C in the
app you copy from, as usual.

## Use (Windows)
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
| Pause in these apps | none | The hotkey is ignored while one of these apps has focus (add by name, or pick from running apps). The tray menu also has *Pause everywhere*. |
| History size | 30 | `0` turns history off. |

## Build from source
Requires Python 3 and the .NET SDK (10.x).
```
python tools/build_dict.py        # downloads JMdict/KANJIDIC2, writes data/dict.db (~77 MB)
dotnet run --project src/KanjiPop        # Windows app from source
dotnet run --project src/KanjiPop.Mac     # cross-platform (Avalonia) app; shortcut only active on macOS
powershell -File scripts/build.ps1        # Windows portable zip in dist/
scripts/build-mac.sh osx-arm64            # on a Mac: KanjiPop.app zip in dist/
```
Layout: `src/KanjiPop.Core` (dictionary, lookup engine, settings; shared), `src/KanjiPop` (Windows, WPF),
`src/KanjiPop.Mac` (macOS, Avalonia).
Pushing a `v*` tag builds the Windows and macOS zips on GitHub Actions and attaches them to a GitHub release.

## Notes and limitations
- Selection is read through the clipboard (the app you select in performs the copy). Text drawn as pictures
  (games, scans) cannot be read.
- Word splitting is longest-match against the dictionary. It is not a full morphological analyser, so unusual
  sentences can be split imperfectly. Homographs are ordered by real-world word frequency (Jiten list), so the most
  common word comes first; up to 3 entries are shown.
- Dictionary data: see [NOTICE.md](NOTICE.md) (EDRDG, CC BY-SA 4.0).
