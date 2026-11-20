using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes sure a scene's player that can pick things up (it has PlayerPickup) also carries a PlayerInventory and its
/// InventoryHUD, so pickups go into the inventory. Runs after scripts reload and before entering Play mode, and from
/// Low Signal > UI > Set up inventory (which also adds PlayerPickup if the player has none). Save the scene to keep it.
/// </summary>
[InitializeOnLoad]
public static class InventorySetup
{
    static InventorySetup()
    {
        // Play mode reloads scripts too; the scene must only be changed in edit mode.
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                SetUp(false);
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                SetUp(false);
        };
    }

    [MenuItem("Low Signal/UI/Set up inventory")]
    private static void SetUpFromMenu() => SetUp(true);

    private static void SetUp(bool fromMenu)
    {
        UnderwaterSwimController controller = Object.FindAnyObjectByType<UnderwaterSwimController>();
        if (controller == null)
        {
            if (fromMenu)
                Debug.LogError("Could not set up the inventory: the open scene has no object with an UnderwaterSwimController (the Player).");
            return;
        }
        // Scenes whose player cannot pick anything up are left alone, unless asked for from the menu.
        if (controller.GetComponent<PlayerPickup>() == null && !fromMenu)
            return;

        bool changed = Ensure<PlayerPickup>(controller, "PlayerPickup");
        changed |= Ensure<PlayerInventory>(controller, "Inventory");
        changed |= Ensure<InventoryHUD>(controller, "Inventory HUD");
        if (changed)
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
    }

    private static bool Ensure<T>(UnderwaterSwimController controller, string label) where T : Component
    {
        if (controller.GetComponent<T>() != null)
            return false;
        Undo.AddComponent<T>(controller.gameObject);
        Debug.Log($"{label} added to {controller.name}. Save the scene to keep it.", controller);
        return true;
    }
}
