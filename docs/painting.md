---
title: Painting
nav_order: 4
---

# Painting

Paint (**B**) only ever changes colours. Every face of a voxel can be its own colour.

## The Paint tool

| Do | To |
|---|---|
| Click or drag | paint with the brush |
| Shift + drag | paint a line |
| Ctrl + drag | paint a box |
| Ctrl + scroll | change the brush size |
| Alt + click | pick up the colour under the mouse |

**X** or the header switches between the brush, the bucket, which fills a connected surface, and the
pattern, which tiles an image over it. Fills can run in two colours, as a **gradient**, **noise** or
**dither**. **Fill Enclosed** fills the space a shape closes in.

A **stencil** lays an image over the view: painting through it puts the picture onto the faces under
it, a pixel to a face. That is how a sign, a pattern or a texture gets onto a model.

## The palette

A level has one palette of 256 colours, shown in its own tab of Properties. One row of it holds
colours of your own.

- Every colour can have a **material**: glow, metal, roughness and glass. They show in the viewport
  and go to the game in glTF.
- Palettes come in and go out as `.gpl`, `.hex` and `.png`, Lospec's included. Ramps make a run of
  colours between two, and a library keeps the palettes you use.
- **Object › Adjust Colours** shifts hue, saturation and value, or replaces one colour with another
  everywhere.
