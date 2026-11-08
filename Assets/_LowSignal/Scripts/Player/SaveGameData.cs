using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SaveGameData
{
    public string format;
    public int version;
    public string savedUtc;
    public string currentScene;
    public List<SceneSaveData> scenes = new List<SceneSaveData>();
}

[Serializable]
public sealed class SceneSaveData
{
    public string scenePath;
    public PlayerSaveData player;
    public List<PickupSaveData> items = new List<PickupSaveData>();
    public List<GuardianSaveData> guardians = new List<GuardianSaveData>();
}

[Serializable]
public sealed class PlayerSaveData
{
    public Vector3 position;
    public float heading;
    public Vector2 look;
    public float cameraDistance;
    public float energy;
    public bool flashlightOn;
    public AbilityId selectedAbility;
    public List<AbilityId> unlockedAbilities = new List<AbilityId>();
}

[Serializable]
public sealed class PickupSaveData
{
    public string id;
    public Vector3 position;
    public Quaternion rotation;
    public bool active;
    public bool canPickUp;
    public bool isKinematic;
    public bool held;
}

[Serializable]
public sealed class GuardianSaveData
{
    public string id;
    public ShallowsFishBoss.Phase phase;
    public int waypoint;
    public Vector3 fishPosition;
    public Quaternion fishRotation;
    public List<string> acceptedItems = new List<string>();
}

/// <summary>Stable across reloads of the same authored scene, including duplicate item names/IDs.</summary>
public static class SaveIdentity
{
    public static string For(Component component)
    {
        string path = "";
        for (Transform node = component.transform; node != null; node = node.parent)
            path = "/" + node.name + "[" + node.GetSiblingIndex() + "]" + path;
        return path;
    }
}
