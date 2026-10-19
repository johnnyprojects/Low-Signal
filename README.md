# Low Signal

Open this folder as a Unity project. The editor version is recorded in `ProjectSettings/ProjectVersion.txt`.

## Where to work

```text
Assets/
  _LowSignal/                  Game-owned content
    Scenes/                   UnderwaterCity, the playable city scene
    Scripts/
      Player/                 Movement and the shared energy pool
      Abilities/              Flashlight and future powered abilities
      UI/                     Robot HUD
    World/
      UnderwaterCity/
        Editor/               City construction and repair tools
        Materials/            City materials
        Meshes/               Shared city geometry
      SeaFloor/               Seafloor models and materials
    Settings/                 Input actions and rendering settings
  Resources/RobotHUD/          Fonts and icons loaded by Resources.Load
  ThirdParty/UnityTemplate/    Starter scene and Unity tutorial content
Packages/                     Package dependency definitions
ProjectSettings/              Shared Unity configuration
```

The HUD is built by `RobotHUD.cs`; its fonts and icons stay under `Resources/RobotHUD` because the script loads them by resource path. `PlayerEnergy.cs` owns shared energy; each ability owns its consumption settings.

City authoring tools are available under **Low Signal > City**. Surface cleanup stores unique geometry in `CitySurfaceMeshes.asset`, tracked through Git LFS, and reuses identical meshes. Do not generate a separate mesh asset for every scene object.

## Local files and samples

`Library`, `Temp`, `Logs`, and `UserSettings` are local Unity files ignored by Git. `LocalArchive` is also ignored: it holds unused terrain samples, unused terrain data, and the retired flashlight meter for recovery, outside Unity's asset imports. It is not required to open or build the game and is not distributed with clones.

The Unity template scene remains in Build Settings with its previous enabled state. It is separate from the playable city scene.

Move assets through Unity's Project window whenever possible. Preserve `.meta` files when moving assets outside the editor: their GUIDs connect scenes, scripts, and materials. City editor scripts also contain asset paths that must match any future folder moves.

Install Git LFS before cloning or pulling large assets (`git lfs install`).
