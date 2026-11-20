using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes sure a scene's player with PlayerEnergy also carries PowerFailure (grey screen and warning at low energy,
/// death at none). Runs after scripts reload and before entering Play mode, and from
/// Low Signal > UI > Set up low energy warning. Save the scene to keep it.
/// </summary>
[InitializeOnLoad]
public static class PowerFailureSetup
{
    static PowerFailureSetup()
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

    [MenuItem("Low Signal/UI/Set up low energy warning")]
    private static void SetUpFromMenu() => SetUp(true);

    private static void SetUp(bool fromMenu)
    {
        UnderwaterSwimController controller = Object.FindAnyObjectByType<UnderwaterSwimController>();
        if (controller == null || controller.GetComponent<PlayerEnergy>() == null)
        {
            if (fromMenu)
                Debug.LogError("Could not set up the low energy warning: the open scene has no Player with an UnderwaterSwimController and PlayerEnergy.");
            return;
        }
        if (controller.GetComponent<PowerFailure>() != null)
            return;
        Undo.AddComponent<PowerFailure>(controller.gameObject);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Debug.Log($"Low energy warning (PowerFailure) added to {controller.name}. Save the scene to keep it.", controller);
    }
}
