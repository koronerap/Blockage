---
title: Modelling
nav_order: 3
---

# Modelling

There is no tool that places a block. Volume comes from pulling faces out and pushing them in, from
shapes added with **Shift+A**, and from sculpting.

## Objects and the selection

A level is made of objects, each with voxels of its own. The tools work on what is selected, as in
Blender.

- **Click** an object to select it, **Shift+click** to add it to the selection or take it away, and
  drag for a box round several. Clicking nothing selects nothing.
- **A** selects everything, **Alt+A** nothing, and **Ctrl+I** turns the selection round.
- The last one clicked is the **active** object: Properties shows it, and Ctrl+P and Ctrl+J use it.

## Moving and turning

**G** moves the selection and **R** turns it, with a gizmo. Snapping is off until you turn it on,
with the magnet in the header or **Shift+Tab**. It snaps to steps of the grid, to corners, to the
middles of edges, or onto other surfaces. With several objects selected, they move and turn about
their middle, about the active one, or each about its own centre.

- **Ctrl+D** duplicates the selection, and **Alt+D** makes linked copies that share their voxels:
  edit one and they all change.
- **Ctrl+J** joins the selected objects into the active one.
- **Ctrl+P** parents the selection to the active object; children then move and turn with it.
  **Alt+P** lets them go.
- **H** hides and **L** locks; **Alt+H** and **Alt+L** undo both for everything.

## Extrude

Extrude (**E**) is where most shaping happens.

1. Click a surface to take the whole flat patch of it, or drag a box across it for part.
2. Pull the arrow out to add voxels, or push it in to carve.
3. Let go to keep it, or press **Esc** to go back.

Shift adds to the surface you are taking, and Alt takes from it. The tool's options in the header
can draw a line, a rectangle or an ellipse on a face and extrude that.

## Edit Mode

**Tab** goes into the selected object to work on its voxels, and out again.

- Choose voxels by clicking, by box, with the magic wand (the joined voxels of one colour), or by
  colour everywhere in the object.
- **Ctrl+=** and **Ctrl+−** grow and shrink the choice.
- Move and turn the chosen voxels with the Transform tool, fill them with a colour, or press
  **Delete** to empty them.
- **P** separates them into an object of their own.

## Sculpt

Sculpt (**S**) works a surface with a round or square brush: build it up, carve it, raise it, lower
it, flatten it or smooth it. Ctrl turns a dab round, from adding to removing or from raising to
lowering, and Shift smooths. It is made for terrain and anything else that is not all right angles.

## Loop Cut

Loop Cut (**Ctrl+R**) splits an object in two along a plane.

## Changing a whole object

The **Object** menu, and F3, have:

- **Boolean** union, difference and intersection between the selected objects.
- **Hollow** and **thicken**, **thin**, **smooth**, and clearing away small loose pieces.
- **Resample** to half the resolution or by any factor, and **Subdivide**, which makes every voxel
  2 × 2 × 2 of half the size.
- Quarter turns and mirrors, from the header too.

Every object has its own **voxel size**, in Properties: how big one of its voxels is in the world.

## Modifiers

**Mirror** and **Array** change what an object shows and exports, without touching the voxels you
edit. A Mirror copies them across a plane of the object, and an Array repeats them in a row. Add them
in the object's Properties, and switch them off, change them or apply them at any time.
