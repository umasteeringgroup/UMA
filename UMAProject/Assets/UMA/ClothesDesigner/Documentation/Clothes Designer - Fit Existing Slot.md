# Clothes Designer: Fit Existing Slot

Fit Existing Slot moves clothing you already have onto a different body shape, keeping everything else about it: its vertex order, UVs, bone weights and blendshapes are not changed.

## Steps

1. Select the clothing's slot or wardrobe recipe and choose **Editors > Plugins > Clothes Designer**. The clothing is put on a default body of a race that wears it.
2. Press **Bind to Body**. Every vertex remembers the body triangle under it, its place on it and its distance from it. Vertices more than 15 cm from the body keep their place.
3. Change the body in the Vertex Editor window under **Body Shape (DNA)**. The clothing follows straight away (drawn in blue over the body); **Refit to Current Body** does it again on request. A short relaxation removes kinks and keeps the clothing out of the body.
4. Save:
   - **Save as New Slot**: a copy of the slot, beside the original, with the refitted shape. Use it as clothing for that body shape.
   - **Save as Blendshape**: a copy of the slot with the refit as a blendshape named *Refit*, to dial in together with the matching DNA.

The original slot asset is never changed.

## Good to know

- Bind on the body the clothing was made for. Binding checks that the slot's mesh matches how it is worn, and says so if it does not (for example clothing converted from another rig).
- The refit follows the body's surface. Very loose parts, like a long skirt or a cape, keep their distance from the body rather than draping; use the pattern workflow for those.
- Normals follow the change of shape, so authored normals keep their character.
