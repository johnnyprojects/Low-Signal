using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TitleScreenSetup
{
    public const string ScenePath = "Assets/_LowSignal/Scenes/TitleScreen.unity";

    [MenuItem("Low Signal/UI/Open or create title screen")]
    public static void Create()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            EditorSceneManager.OpenScene(ScenePath);
            return;
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(11f / 255f, 61f / 255f, 92f / 255f);
        camera.cullingMask = 0;
        var controller = new GameObject("Title Screen").AddComponent<TitleScreenController>();
        controller.SetLogo(AssetDatabase.LoadAllAssetsAtPath("Assets/_LowSignal/Hud/Logo.png").OfType<Sprite>().FirstOrDefault());
        controller.BuildInterface();
        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Validate();
    }

    public static void Validate()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var controller = UnityEngine.Object.FindAnyObjectByType<TitleScreenController>();
        if (controller == null) throw new Exception("Title controller missing.");
        var serialized = new SerializedObject(controller);
        foreach (string field in new[] { "logo", "prompt", "menu", "options", "logoImage", "background", "startButton", "loadButton", "optionsButton", "quitButton", "volumeButton", "flickerButton", "backButton", "status" })
            if (serialized.FindProperty(field).objectReferenceValue == null) throw new Exception("Missing title reference: " + field);
        var image = (Image)serialized.FindProperty("logoImage").objectReferenceValue;
        if (image.sprite != serialized.FindProperty("logo").objectReferenceValue || !image.gameObject.activeSelf || !image.preserveAspect)
            throw new Exception("Title logo must be assigned, active, and preserve its aspect ratio.");
        if (controller.GetComponentsInChildren<Text>(true).Any(text => text.gameObject.name == "Temporary Wordmark"))
            throw new Exception("Remove the obsolete temporary title wordmark.");
        var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).ToArray();
        if (enabled[0].path != ScenePath || !enabled.Any(s => s.path.EndsWith("/Shallows.unity")))
            throw new Exception("Title or gameplay build entry missing.");
        Debug.Log("TITLE_SCREEN_VALIDATION_PASSED: scene references and build order valid.");
    }
}
