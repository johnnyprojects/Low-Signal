# Shallows treasure guardian

Open Shallows and enter Play mode. Find the three gold relics with blue pearls, aim at one within interaction reach, and press F to carry it. Carry it to the golden ring outside the northeast cave and press F while aiming at clear ground inside the ring. Each accepted relic stays there. After the third offering, the fish swims out and stays distracted. Swim into the rear chamber to load UnderwaterCity.

The encounter is under **Shallows Treasure Guardian** in the Hierarchy. Its ShallowsFishBoss component exposes treasure references, required count, offering radius, swimming speed, and route points. Keep the required count no greater than the number of assigned treasures. The final route point should remain away from the cave entrance. The Cave exit to UnderwaterCity child exposes the destination scene name, which must be enabled in Build Settings. Boss components now live in `Assets/_LowSignal/Scripts/Enemies/ShallowsBoss`; their editor setup and validation scripts are in its `Editor` subfolder.

Treasure uses the existing PlayerPickup/PickupItem interaction. Only assigned treasures placed in the ring count. Held objects, other props, and repeat placements do not count. Accepted pieces become unpickable. Progress lasts for the current scene visit; restarting Shallows resets the puzzle. The passage stays blocked until the fish reaches the last waypoint. There is no combat or distraction timer.

The fish has gentle procedural hovering and turns along the route. This does not add a skeletal tail animation to the source model.

The editor menu **Low Signal > Shallows > Set up treasure guardian** creates the encounter in a Shallows scene without one; it refuses to duplicate an existing encounter. Rebuilding the cave dressing may require moving the encounter's independent route, offering, and exit objects to fit the new geometry.

For isolated batch validation, run ShallowsBossValidation.Run against a copy of the project. It enters Play mode, checks invalid offerings and placement events, completes the route, and verifies the destination scene loads. It exits the editor on completion, so do not run it in your working editor session.
