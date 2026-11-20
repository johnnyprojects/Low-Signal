using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Pressing Play starts from the title screen, as the built game does, so the main menu always shows. Untick
/// Low Signal > Play from title screen to play the open scene directly while testing it. Automated batch runs always
/// play the open scene.
/// </summary>
[InitializeOnLoad]
public static class PlayFromTitle
{
    private const string MenuPath = "Low Signal/Play from title screen";
    private const string Key = "LowSignal.PlayFromTitle";

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(Key, true);
        set => EditorPrefs.SetBool(Key, value);
    }

    static PlayFromTitle()
    {
        EditorApplication.delayCall += Apply;
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    private static void Apply()
    {
        EditorSceneManager.playModeStartScene = Enabled && !Application.isBatchMode
            ? AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScreenSetup.ScenePath)
            : null;
    }
}
