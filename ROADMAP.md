# TeamStation Roadmap

This document will be regenerated from in-depth research. Consider the list below a placeholder until the first full pass lands.

## Milestones

### v0.1.0 — MVP (first usable build)
- Solution scaffold: WPF (.NET 9) + MVVM + SQLite + DPAPI crypto
- Tree view with folders and entries, drag-to-reorder
- Entry editor: name, ID, password, connection mode, quality, notes, tags
- Launch via `TeamViewer.exe` CLI with `--Base64Password`
- AES-256-GCM credential storage, DPAPI-wrapped key
- CSV import / JSON export
- Catppuccin Mocha theme
- Embedded log panel and toast notifications

### v1.0.0 — Polish
- Full theme set (Catppuccin Mocha / GitHub Dark / Light)
- URI-handler launch path as an optional fallback
- Per-entry connection profiles (e.g., separate "File Transfer" and "Chat" launches of the same peer)
- Tray icon with recent connections and quick-connect
- Bulk actions, multi-select, search-by-tag
- Settings dialog, import/export of the full database
- Signed installer + portable ZIP

### Backlog
- Session history and duration tracking
- Optional TeamViewer REST API sync (address book, computer list, online status)
- Wake-on-LAN pre-launch
- Online-status ping before connect
- Pre/post-connect scripts
- Per-entry custom icons and folder colors
- Cloud-agnostic DB sync (user-chosen folder)
- Portable mode (no install, config next to exe)

## Prioritization framework

- **P0** — Must ship in `v0.1.0`. Blocks calling the app usable.
- **P1** — Ships in `v1.0.0`. Expected by anyone coming from mRemoteNG or RDM.
- **P2** — Backlog. Useful, but not gating adoption.

Detailed P0/P1/P2 entries will be populated after the feature-research pass completes.
