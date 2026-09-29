---
title: Laying out a level
nav_order: 5
---

# Laying out a level

## The Add menu

**Shift+A**, or **Add** in the menu bar, sets things down on the surface under the mouse. Their
size and details are then in the **Adjust** panel, until you do something else.

| Section | What is in it |
|---|---|
| Mesh | a voxel, cube, plane, wall, sphere, cylinder, cone, pyramid, torus, stairs, arch, and voxel lettering |
| Generate | terrain, a cave, a tree, a rock or a building, each from a seed: the same seed makes the same thing |
| Prop | a crate, barrel, table, chair, tree or fence, or one of your own from the library |
| Light | a sun, a point light or a spot |
| Marker | an empty, a spawn point, a trigger or a sound source |
| Note | a note left in the level for whoever works on it next |
| Camera | a camera where the view stands |

![The Shift+A menu, with the Mesh list open](images/add-menu.png)

## Keeping a level in order

- **Collections** group objects, nested as in Blender. Each can be hidden, locked, or kept out of
  exports. **M** moves the selection into one.
- **Linked copies** (**Alt+D**) share their voxels, and go out as one shared mesh.
- The **prop library** (**File › Prop Library**) keeps props of your own, with a picture of each.
  **File › Append** brings in objects from another level.
- **Markers** carry what the game needs: a spawn point, a trigger box, a sound. Any object can have
  **custom properties**, keys and values the game reads, in its Properties.
- **Object › Align** and **Distribute** line up the selection. **Object › Scatter** strews props over
  the ground, turned and spaced at random.

## Seeing a level

- **Alt+B** draws a **section box**: nothing outside it is drawn, so you can work inside a room with
  its roof on.
- **Reference images** stand on a plane in the level, to model over a drawing or a plan. They are in
  their own tab of Properties.
- **Ctrl+Alt+Q** shows the **quad view**: top, front and right beside the perspective.
- **Shift+\`** **walks** through the level at a player's size, with collision. Set the scale under
  **Preferences › Walk**. Esc goes back, Enter stays where you are, and Tab flies.
- The **Measure** tool (**Shift+M**) measures between two points, in voxels and in metres.
- **Ctrl+Alt+Z** opens the **undo history**, to click back to any step.
- **Shift+Q** opens **Quick Favorites**, the commands you choose to keep at hand.
