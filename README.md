# Show Path On Tabs

When several open tabs share a file name, Show Path On Tabs adds just enough of the folder path to
tell them apart: `Admin\Index.razor` and `Web\Index.razor` instead of two `Index.razor` tabs.

Works with Visual Studio 2022 (17.14+) and Visual Studio 2026.

## Features

- **Collapse middle folders** (on): show only the folder that differs, e.g. `Admin\Index.razor`
  instead of `Admin\Pages\Index.razor`.
- **Abbreviate folders** (off): shorten folder names, e.g. `EntityFrameworkCore` to `EntFraCor`,
  either only when a caption doesn't fit or always. File names are never shortened.
- **Widen tabs** (to fit): raise Visual Studio's maximum tab width so long captions fit, either as
  far as needed or up to a limit you set (300 by default).
- **File name first** (off): show `Index.razor (Admin)` instead of `Admin\Index.razor`.

Change settings from **Extensions > Show Path On Tabs** or **Tools > Options > Show Path On Tabs**.
