# Editing the underwater city

Open `Assets/_LowSignal/Scenes/UnderwaterCity.unity`. The 65 children of `02 - City blocks (sealed exteriors)` are connected building prefabs, including the shop, transit waiting room, leaning tower and its support.

Prefabs live in `Assets/_LowSignal/World/UnderwaterCity/Prefabs/Buildings/`. Names match the scene's block/lot or landmark names. Each building has one `.prefab` and one `.asset` holding its visual meshes. Keep their `.meta` files with them. Mesh assets are tracked with Git LFS; prefab and scene files remain text.

## Everyday team workflow

1. For layout changes, move, rotate or scale the **building root** in the scene. This moves its visuals and collision together. Save the scene.
2. For building design changes, double-click its prefab in the Project window (or use Open on the prefab instance). Work in Prefab Mode and save the prefab. Changes reach its instances without editing every instance or rewriting the scene.
3. Assign different buildings to different teammates. They can change separate prefab assets independently. Coordinate edits to the scene layout and shared materials.
4. For a reusable variation, create a Prefab Variant from the desired prefab and give it a descriptive name. Drag the variant into the scene. Make instance-only adjustments with overrides; apply an override only when every instance should receive it.

## Inside a building

| Object | Purpose |
| --- | --- |
| Shell | Walls and major building surfaces |
| Facade | Windows, trim, algae and attached damage |
| Roof | Roof surfaces and details |
| Interior, where present | Counters, benches and entrance markers |
| Collision | Original collision shapes, with aligned boxes stored as multiple components on one object |
| Sealed structural mass, where present | Named solid building collider used by validation |
| Tower contact slab, on the support | Named load-bearing collider used by tower validation |

Shell/Facade/Roof/Interior each use one MeshRenderer with material slots. Static window and trim geometry has been combined; individual windows are no longer separate cube objects. This is a mesh-based prefab workflow, not a procedural width/height inspector. To change individual geometry, replace a module's mesh with an authored mesh or add new details in Prefab Mode. Changing a MeshRenderer's material slot affects that module; editing a shared material asset affects all buildings using it.

Collision shapes retain their original dimensions and physical materials. They are independent of the combined visual meshes: moving/replacing a visual module alone does not reshape collision. Adjust the matching colliders when changing the architecture, and check the shop and transit doorways in Play Mode. Scale the whole root when resizing a whole building. The leaning tower and support form a pair; keep their alignment when moving them.

## Construction and maintenance tools

`Low Signal > City > Create new tutorial city copy` creates a fresh layout copy and reuses the team's existing building prefabs by name. It only bakes a new building asset when that building is absent from the library. Existing building designs are not regenerated over artists' changes.

`Low Signal > City > Convert buildings to compact prefabs` migrates primitive buildings in the city scene. Already connected prefab instances are skipped. The conversion keeps existing geometry and collisions and saves named building assets. It does not recreate individual window objects from a combined mesh.

The old roof/facade repair batch commands reject converted buildings and direct authors to Prefab Mode. Surface cleanup skips prefab instances, so it does not produce thousands of overrides on the combined modules. The city generator still performs surface cleanup before baking new building meshes.

The conversion/editor workflow is in `UnderwaterCityPrefabs.cs`, a partial of the existing city builder. Buildings have no new runtime scripts or generators.

## Validation and scope

The refactor reduced the building hierarchy from 12,516 transforms to 472 and its renderers from 12,321 to 195. The saved scene shrank from about 36.4 MB to 4.0 MB. These are authoring/object-count measurements, not a measured frame-rate claim. Geometry is combined per building, preserving culling at building scale.

Validation checks materials, triangle counts and bounds, collider types/counts/bounds and surface samples, prefab connections after reopening the saved scene, spawn clearance, streets, roundabout and both interiors. Before/after overview and interior renders matched exactly. Local logs and screenshots are in ignored `Logs/` and `Previews/`; they are not required by teammates.

An existing tower-support check reports that the saved leaning tower is at Z=27.1 while its support is at Z=28. This refactor preserves that pre-existing placement. The local original scene is backed up in `Logs/CityPrefabBackup/UnderwaterCity.unity`.

This change covers UnderwaterCity's buildings. Streets, fountain, cars and other scenes retain their existing authoring structures.
