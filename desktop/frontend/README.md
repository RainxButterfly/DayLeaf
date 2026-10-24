# Windows client

Planned. The Windows desktop client will be built after the Android client is stable.
It will share the same single-file `index.html` core, the same data model
(`dayleaf.prototype.v2`, for future cross-import), and the same update model — GitHub
Releases plus a `windows` manifest under `desktop/backend/`.

Size and performance matter, so the candidate shells are Tauri or WebView2; the exact
choice will be made when development starts.
