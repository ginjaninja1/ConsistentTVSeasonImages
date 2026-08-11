# Repository instructions

- Whenever an HTML or JavaScript file is changed, increment the plugin version by `0.0.0.1` to invalidate cached web assets.
- Keep `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` in `src/ConsistentTVSeasonImages/ConsistentTVSeasonImages.csproj` synchronized.



Emby drag/drop: On Emby Web 4.10.0.23 with Chrome/Windows, dragdroptouch.js can emit a synthetic dragend before the continuing native drop. Do not store active drag metadata in controller/DOM state that is cleared on dragend. Put source metadata in DataTransfer, which survives into native dragover and drop. When a draggable container contains an <img>, set the image to draggable="false" to prevent redundant native image dragging and unwanted payload types.