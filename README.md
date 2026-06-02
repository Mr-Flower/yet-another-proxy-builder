<div align="center">

# 🃏 Yet Another Proxy Builder

**Turn a decklist into a clean, print‑ready PDF of proxy cards — in minutes.**

Pick a game, fetch the cards by name, choose the exact artwork, arrange them on any paper size,
and export with true bleed, cut guides and Silhouette Cameo cut lines. **Magic** and **Yu‑Gi‑Oh!**
cards are fetched automatically; 25+ more games are supported by size + your own art.

[![CI](https://github.com/Mr-Flower/mtg-proxy-builder/actions/workflows/ci.yml/badge.svg)](https://github.com/Mr-Flower/mtg-proxy-builder/actions/workflows/ci.yml)
[![Release](https://github.com/Mr-Flower/mtg-proxy-builder/actions/workflows/release.yml/badge.svg)](https://github.com/Mr-Flower/mtg-proxy-builder/releases)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-2D2D30)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/UI-Avalonia%2011-8A7FC0)

[**⬇ Download**](#-download) · [**✨ Highlights**](#-highlights) · [**📖 Usage**](#-usage-guide) · [**🛠 Build**](#-building-from-source)

</div>

---

> **Built entirely with AI.** Every line of code was written by Claude, directed by an experienced
> developer who drove all architecture, feature, UX and quality decisions — a real‑world experiment
> in how far AI‑assisted development can go on a full, shipping desktop app.

## ✨ Highlights

- 🎮 **Two‑game by‑name fetch** — a game selector switches name resolution between **Magic** (Scryfall) and **Yu‑Gi‑Oh!** (YGOPRODeck). Pick the game and the card size follows automatically.
- 🔎 **Find any printing** — full Scryfall query language for Magic; every alternate YGOPRODeck artwork for Yu‑Gi‑Oh!; plus community high‑DPI art from **MPCFill**.
- 📋 **Import whole decks** — a **Moxfield** or **Archidekt** URL, an **MPCFill `cards.xml`**, or a plain text decklist.
- 🖼️ **WYSIWYG canvas** — the multi‑page editor shows cards exactly as they'll print, **bleed included**, so the PDF holds no surprises.
- ✂️ **Print‑ready output** — true edge bleed, cut guides, configurable card outlines, duplex layout, and **Silhouette Cameo** registration marks + SVG cut lines.
- 🎯 **Printer‑offset compensation** — nudge the whole grid by an exact mm offset so a slightly off‑centre printer still cuts true.
- 🌑 **Image tuning** — per‑card or bulk brightness / contrast / saturation, and a black‑point "darken" pass to clean dark scans.
- 📚 **Persistent art libraries** — save front/back art once and reuse it instantly; export/import as ZIP, relocate, auto‑thumbnail.
- 🧩 **Card Editor** — a layered editor for custom cards (image + text layers, fonts, colours, positioning).
- 💾 **Portable projects & tabs** — several decks open at once, each a single self‑contained file with 50‑level undo.

## ⬇ Download

Grab the latest build from the [**Releases**](../../releases) page.

### 🪟 Windows
1. Download `YetAnotherProxyBuilder-vX.X.X-win-x64.zip`
2. Extract anywhere
3. Run `tcg-proxy-builder.exe`

Self‑contained single executable — **no install, no .NET runtime required** (Windows 10/11, 64‑bit).

### 🐧 Linux (AppImage)
1. Download `YetAnotherProxyBuilder-vX.X.X-linux-x86_64.AppImage`
2. `chmod +x YetAnotherProxyBuilder-*.AppImage`
3. `./YetAnotherProxyBuilder-*.AppImage`

A **native** Avalonia + .NET build, self‑contained — **no Wine, no install**. Requires a 64‑bit distro with a reasonably recent kernel.

> macOS isn't packaged, but the code is plain cross‑platform Avalonia — run it from source with `dotnet run`.

## 🎴 What it does

### Find & add cards
- **Game selector (Magic / Yu‑Gi‑Oh!)** — at the top of the *Add Cards* tab. It decides how pasted names resolve **and** sets the sheet card size:
  - **Magic** → **Scryfall** (full query syntax + a visual advanced‑search builder), card size 63 × 88 mm.
  - **Yu‑Gi‑Oh!** → **YGOPRODeck** (e.g. `3 Dark Magician`, art fetched automatically), card size 59 × 86 mm.
- **Paste a text decklist** — `2 Sol Ring`, `1x Counterspell`, or a bare name per line; section headers (`Deck`, `Sideboard`, …) are ignored. Prefix `t:` to add a Magic token.
- **Deck URL import** — paste a **Moxfield** or **Archidekt** link; the source is auto‑detected and every card's art is fetched *(Magic only)*.
- **MPCFill `cards.xml`** and **local image files** (multi‑select), with optional duplicate‑skipping (basic lands top up by quantity).

### Choose the artwork
- **Art selector** — a thumbnail gallery with a wide live preview (dimensions, estimated DPI, file size, source):
  - **Magic** cards show every Scryfall printing + matching MPCFill community art.
  - **Yu‑Gi‑Oh!** cards show every **alternate YGOPRODeck artwork** for that card.
- **Source filter** — narrow to Scryfall, MPCFill (all or one source), YGOPRODeck, or your library.
- **Printing cache** — a card's printing list is cached locally, so reopening the selector is instant and offline‑friendly.
- **Per‑copy editing** — split one copy off a stack (**✂ Scollega copia**) to give a single card its own art or overlay text.

### Libraries & card backs
- **Persistent front & back libraries** — save art once and reuse it (local‑first, no re‑download); search, filter by source, batch delete, **export/import as ZIP**, **relocate**, auto‑thumbnailing.
- **Back art** — pick from Scryfall originals, your library, **MPCFill card backs** (bulk download ~460), or local files; set a **default** applied to new cards.
- **Yu‑Gi‑Oh! default back** — new Yu‑Gi‑Oh! cards get an original generated card back out of the box (no trademarked art), overridable like any other back.
- **MPCFill Source Manager** — browse 270+ sources, favourite with a click, restrict searches to favourites.

### Visual page editor
- **Multi‑page, zoomable canvas** with all pages stacked vertically.
- **WYSIWYG bleed** — cards render with the same bleed‑extended image the PDF uses, so the preview matches the print.
- **Drag & drop** reorder, **multi‑select** (Ctrl/Shift‑click across pages), and a rich **right‑click menu** (duplicate, delete, flip, match back, select front/back art, create token).
- **Flip preview** of backs (single / selected / all), **zoom** (15–300 %, Ctrl+Wheel, Fit, 1:1), **pan** (middle‑drag).
- **Overlay text** (e.g. `TOKEN`) rendered on the front face in both preview and PDF.

### Image adjustment
- **Darken / black‑point** pass to push dark‑grey scan borders to true black.
- **Brightness / contrast / saturation**, applied **per card** or in **bulk** (all / Scryfall‑only / MPCFill‑only), with save‑as‑default and auto‑apply to new cards.

### Print & PDF
- **Card‑size dropdown** — Magic/Poker (63 × 88), Yu‑Gi‑Oh!/Japanese (59 × 86), Bridge, Mini American/European, Tarot, Oversized. It follows the game selector automatically.
- **Page presets** — A4, A3, Letter, Legal, Tabloid (+ landscape).
- **True bleed** — edge pixels are *extended* (not stretched), and **rounded scan corners are squared off** to the border colour so no white slivers survive the cut.
- **Cut guides** drawn *behind* the art (won't show through light cards).
- **Card outline guides** — colour, alignment (center/inside/outside), corner radius, full vs corner‑marks, solid/dashed, corner length, line weight.
- **Card position adjustment** — shift the whole grid by an exact horizontal/vertical offset in mm (decimals welcome, e.g. `0,45`) to compensate for an off‑centre printer. Bleed width and offsets are precise numeric fields where **`.` and `,` are interchangeable** decimal separators.
- **Silhouette Cameo** — Type‑1 registration marks (front pages only, to save ink) + matching **SVG cut lines** exported alongside the PDF; bleed/guides/outlines auto‑suppressed in this mode.
- **Print modes** — Duplex (mirrored back columns), Fronts Only, Backs Only — with **auto‑centering** or manual grid override.

### Card editor
- A **layered editor** for custom cards: image and text layers with font, size, colour, opacity, rotation, stroke and positioning; live canvas preview with drag‑to‑move and selection handles; export to PNG/JPEG.

### Projects & UX
- **Multi‑project tabs**, each with independent state, **50‑level undo/redo**, and a self‑contained portable project file.
- **Sort & filter** by name, type, rarity, colour, CMC, set, artist… in a compact bar above the grid.
- **Fluent dark theme** with an ultra‑violet accent, step‑by‑step busy overlays, async background image loading, and an automatic **update check** against GitHub releases.

## 📖 Usage Guide

### Getting started
1. Launch the app and create a **New Project** (Ctrl+N) or **Open** one (Ctrl+O).
2. In the **Add Cards** tab, pick the **Game** (Magic / Yu‑Gi‑Oh!) — the card size switches to match.
3. Add cards (below), tune the **Layout** tab, then **Export PDF** (Ctrl+E).
4. Open more projects in new tabs — each is independent.

### Adding cards
- **By name:** paste one card per line (`4 Lightning Bolt`, `3 Dark Magician`, or a bare name) into *Add Cards by Name* → *Add Cards from List*. Names resolve against the selected game.
- **Deck URL:** paste a Moxfield/Archidekt link into *Import Deck* *(Magic)*.
- **MPCFill project:** *Import cards.xml…*.
- **Local files:** *+ File* in the toolbar (multi‑select).

### Choosing art
- Double‑click a card on the canvas (or use the Card panel buttons) to open the **art selector**.
- Library matches show first (instant), then the online results for that card's game.
- Use the **source filter** to narrow the gallery; for MPCFill, expand **Filters** to tune DPI / language / types.
- Right‑click a tile to **Preview Full Size** or **Save to Library**; tick **Apply to all** to update every copy of the card.

### Layout & print (Layout tab)
- **Page size & print mode** (Duplex / Fronts / Backs).
- **Card size** — choose a preset from the dropdown (it also follows the game selector).
- **Bleed width** (mm) and **card position adjustment** (horizontal/vertical offset, mm) to re‑centre the print.
- **Cut guides** and fully configurable **card outlines** (colour, alignment, radius, full/corners, solid/dashed, weight).
- **Silhouette Cameo** — registration marks and/or SVG cut‑line export.
- **Grid** auto‑fit or manual; **storage** cache size + clear.

### Export, save, undo
- **Export PDF** — bleed‑extended art, cut guides behind the art, outlines on top, mirrored duplex backs, optional Cameo marks + SVG sidecar.
- **Save / Open** — portable project files bundle all artwork; share a single file between machines.
- **Ctrl+Z / Ctrl+Y** — 50‑level undo across every card operation.

## 🎮 Supported Card Games

| Size group | Games | Card size (mm) |
|------------|-------|----------------|
| Standard / Poker | Magic, Pokémon, Lorcana, Flesh and Blood, KeyForge, Star Wars: Unlimited, One Piece, Dragon Ball Super, Digimon, Marvel Champions, Arkham Horror, Riftbound, Altered, Sorcery, Grand Archive | 63 × 88 |
| Japanese | Yu‑Gi‑Oh!, Cardfight!! Vanguard, Weiss Schwarz, Bushiroad | 59 × 86 |
| Bridge | Bridge‑size decks | 57 × 89 |
| Board‑game | Mini American · Mini European | 41 × 63 · 44 × 68 |
| Large | Tarot · Oversized MTG / Commander | 70 × 120 · 89 × 127 |

> **By‑name fetching** is available for **Magic** (Scryfall) and **Yu‑Gi‑Oh!** (YGOPRODeck). Every other
> game is supported for layout and printing — pick its size from the dropdown and bring your own art via
> local files, MPCFill or the libraries.

## ⌨️ Keyboard Shortcuts

| Shortcut | Action | | Shortcut | Action |
|----------|--------|---|----------|--------|
| Ctrl+N | New project | | Ctrl+Z / Ctrl+Y | Undo / Redo |
| Ctrl+O | Open project | | Ctrl+Wheel | Zoom |
| Ctrl+W | Close tab | | Shift+Wheel | Horizontal scroll |
| Ctrl+S | Save | | Middle‑drag | Pan canvas |
| Ctrl+E | Export PDF | | Esc | Deselect all |
| Double‑click result | Add card | | Double‑click card | Open art selector |
| Click / Ctrl+Click / Shift+Click | Select / toggle / range | | Right‑click | Context menu |

## 🧰 Tech Stack

| Component | Technology |
|-----------|-----------|
| UI framework | **Avalonia 11.2** (Fluent **dark** theme), cross‑platform |
| Target runtime | **.NET 10** (Windows & Linux, self‑contained) |
| Image processing | **SkiaSharp 2.88.9** |
| PDF generation | **PDFsharp 6.1.1** |
| JSON | Newtonsoft.Json 13.0.3 |
| Card data | **Scryfall** API (Magic) · **YGOPRODeck** API (Yu‑Gi‑Oh!) |
| Proxy art | **MPCFill** API |
| Deck import | Moxfield (via curl) · Archidekt |
| Architecture | MVVM (Shell + per‑project ViewModels) |
| Tests | xUnit — 440+ unit/integration tests, run in CI |

## 🛠 Building from Source

**Prerequisites:** .NET SDK 10.0+ (any OS). `curl` is used for Moxfield (ships with Windows 10+, preinstalled on most Linux).

```bash
# build
dotnet build

# run
dotnet run --project MTGProxyBuilder.UI

# test (fast, no UI)
dotnet test --filter "Category!=UI"
```

## 📁 Project Structure

```
MTGProxyBuilder/
├── MTGProxyBuilder.Core/        # Business logic, no UI dependencies
│   ├── Models/                  # CardModel, PageLayout, PrintSettings, presets…
│   └── Services/                # Scryfall, YGOPRODeck, MPCFill, Moxfield, Archidekt,
│                                # PDF, Bleed, image cache, libraries, undo…
├── MTGProxyBuilder.UI/          # Avalonia presentation layer
│   ├── Assets/                  # App icon (icon.ico / icon.png)
│   ├── MainWindow.axaml         # Shell: tabs, 3‑column layout, dark theme
│   ├── Controls/                # GridEditorCanvas, CardEditorCanvas, panels…
│   ├── Converters/              # incl. ImageLoader (async remote/local images)
│   ├── Dialogs/                 # Art selector, libraries, settings, editor…
│   └── ViewModels/              # Shell + per‑project view models, coordinators
└── MTGProxyBuilder.Tests/       # 440+ unit/integration tests
```

## 📂 File Locations

Data lives under the per‑user app‑data folder — `%AppData%\MTGProxyBuilder\` on Windows, `~/.config/MTGProxyBuilder/` on Linux:

| Item | Path | Notes |
|------|------|-------|
| App settings | `app_settings.json` | Defaults, MPCFill filters, update toggle |
| Image cache | `ImageCache/` | Downloaded card images (+ Scryfall printing lists) |
| Bleed cache | `BleedCache/` | Bleed‑processed images (regenerated when the algorithm changes) |
| Generated assets | `Generated/` | App‑generated images (e.g. the Yu‑Gi‑Oh! default back) |
| Extracted projects | `ExtractedProjects/` | Temp images from opened projects (cleared on startup) |
| Front/Back libraries | `FrontArtLibrary/` · `BackArtLibrary/` | Saved art + `catalog.json` + `Thumbnails/` |
| Projects | Anywhere you choose | `.mtgproj` ZIP archives |

## 🩹 Troubleshooting

- **Scryfall errors** — check your connection; the app respects Scryfall's ~10 req/s limit automatically.
- **No Yu‑Gi‑Oh! result** — make sure the **Game** dropdown is set to *Yu‑Gi‑Oh!*; names resolve via YGOPRODeck (exact, then fuzzy). You must be online for the first fetch.
- **No MPCFill results** — you must be online for the first source fetch; turn off *Favs only*, try non‑fuzzy matching, check Settings → MPCFill filters, then *Clear Filters* → *Re‑search* in the art selector.
- **Moxfield 403** — Moxfield is behind Cloudflare; the app uses `curl` to get through. Private decks can't be imported.
- **Slow PDF / first preview** — bleed processing converts each unique image once, then caches it (shared with the canvas preview). Big decks take a few seconds; the spinner shows progress.
- **Disk usage** — Layout tab → *Clear Cache*. Bleed/extracted caches auto‑clean on startup; image cache on exit.

## 📄 License

For personal and educational use with trading‑card‑game proxies.
