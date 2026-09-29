---
title: Rendering
nav_order: 6
---

# Rendering

## In the viewport

The shading buttons at the right of the header, and the pie menu on **Z**, switch between:

- **Wireframe** (**Shift+Z**): the voxel lattice alone.
- **Solid**: flat studio lighting, for building.
- **Lit**: the level's own sun and lights, with sun shadows and ambient occlusion.
- **Rendered**: the path tracer, clearing in the viewport while the view is still.

**Alt+Z** turns on X-Ray, to see through the model, and **Shift+Alt+Z** hides every overlay.

## Render Image

**F12** renders a picture with the path tracer: soft shadows, light bounced off walls, glowing
colours, metal and glass, sky light and fog. The Render window sets the resolution, samples,
bounces and seed, the exposure, the sky, the sun's size, fog, bloom, depth of field, and the
backdrop: the sky, a plain colour, or nothing, for a picture with a see-through background. It saves
the picture as a PNG.

The renderer runs on every CPU core, or on the graphics card where OpenGL 4.3 is there. Both give the
same picture, and the same level, settings and seed always render the same picture.

## Cameras

**Add › Camera** puts a camera where the view stands. It can be perspective, orthographic or
isometric, and it is saved with the level.

- **Numpad 0** looks through the render camera.
- **Ctrl+Alt+Numpad 0** moves it to the view.
- **Render › Render From** chooses which camera renders are seen from, or the view.

## Animations and sprite sheets

**Render › Turntable, Sprites and Cameras** makes:

- a turntable of an object going round, as a GIF, PNGs, or an MP4 when ffmpeg is installed;
- a sprite sheet from any number of angles, for 2D and isometric games;
- a picture from every camera at once.

**Render › Save Viewport Image** saves the viewport as it is, without the overlays.
