# Prefab Icon Generator

Editor tool for rendering clean, transparent UI icons from prefabs — built for URP.

Namespace: `GalaxyGourd.IconGen` · Editor-only · UPM package `com.galaxygourd.icongen`

## Why two-pass capture

URP does not preserve the camera's alpha channel through its final blit, so a naive
"clear to alpha 0 and read the alpha back" approach gives you garbage transparency (and
usually a dark halo on anti-aliased edges). This tool sidesteps that entirely:

1. Render the prefab once on a **black** background and once on **white**.
2. Per pixel, `(white - black) == (1 - alpha)`, which recovers true straight-alpha coverage.
3. Un-premultiply to get the object's color; downsample with alpha-weighted averaging.

This is pipeline-version independent and produces clean edges with no fringing. When the
project is in Linear color space the reconstruction math runs in linear and converts back
to sRGB only at the end, so edge alpha stays accurate.

## Install

Copy the `com.galaxygourd.icongen` folder into your project's `Packages/` directory, or add
it via Package Manager → Add package from disk, pointing at `package.json`.

Requires Unity 2022.3+ (uses `RenderPipeline.SubmitRenderRequest`) and URP.

## Use

`Tools → Galaxy Gourd → Icon Generator`

- **Prefab** — the object to capture. A live preview updates as you adjust settings (it shares
  the exact render path with the saved file, just at a lower resolution). For multi-cell icons a
  **Grid** toggle appears in the preview's corner, overlaying the inventory cell divisions (a 1×3
  sword shows as three stacked cells) so you can judge the fit. It's a view aid only and never
  affects the saved image; single-cell (1×1) icons have nothing to subdivide, so the toggle is hidden.
- **Output Resolution** — grid-cell model for tetris-style inventories. A 1×3 sword at 256
  px/cell saves a 256×768 PNG. The Square toggle is a 1:1 shortcut. SSAA controls edge quality.
- **Framing** — Margin adds breathing room around the fitted bounds. Orthographic (default) keeps
  items distortion-free; Perspective adds depth.
- **Orientation** — presets (Front/Back/Left/Right/Top/Isometric) or a custom Euler. The object
  is rotated; the camera stays axis-aligned.
- **Lighting** — Studio/Soft/Dramatic/Flat presets, or tune the key/fill/rim rig and ambient.
- **Background** — transparent (two-pass reconstruction) or a solid color.
- **Output** — directory, file name (defaults to the prefab name), and optional auto-import as a
  Sprite (alpha-is-transparency, no mipmaps, clamp).

**Capture & Save** writes one icon. There are two batch modes, both naming each file after its
prefab and applying the current settings to every item:

- **Batch Capture Selection** — renders everything selected in the Project window. Individually
  selected prefabs are captured directly; selected folders are expanded recursively to every
  `.prefab` they contain (and their subfolders). Mixed selections work. The button shows the
  resolved count.
- **Batch Capture Folder...** — pick any folder under `Assets` and render every prefab beneath it
  recursively, without needing to select anything.

Both show a cancelable progress bar. **Save/Load Preset** persists settings as an asset so weapons,
consumables, etc. stay visually consistent.

## Notes / limitations

- Particle, trail, and line renderers are excluded from bounds fitting (unstable bounds) and won't
  capture meaningfully.
- Ambient is applied via a temporary flat override of global lighting settings, restored after each
  render; explicit lights do the bulk of the work.
- HDRP is not targeted by this build.
