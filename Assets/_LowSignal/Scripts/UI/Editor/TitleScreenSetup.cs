using System;
using System.IO;
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
        camera.backgroundColor = HudStyle.Ink;
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

    [MenuItem("Low Signal/UI/Rebuild title screen in HUD style")]
    public static void Rebuild()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var controller = UnityEngine.Object.FindAnyObjectByType<TitleScreenController>();
        if (controller == null) throw new Exception("Title controller missing.");
        Undo.RegisterFullObjectHierarchyUndo(controller.gameObject, "Rebuild title screen");
        controller.RebuildInterface();
        var camera = Camera.main;
        if (camera != null) camera.backgroundColor = HudStyle.Ink;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Validate();
    }

    public static void RebuildBatch()
    {
        try
        {
            Rebuild();
            Capture("Title - prompt", "Press Any Key");
            Capture("Title - menu", "Main Menu");
            Capture("Title - options", "Options");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Renders the title UI with one of its groups showing into Previews/, without saving the scene.
    static void Capture(string fileName, string group)
    {
        EditorSceneManager.OpenScene(ScenePath);
        var canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
        foreach (var candidate in canvas.GetComponentsInChildren<CanvasGroup>(true))
        {
            if (candidate.name == "Visor Frame") continue;
            bool show = candidate.name == group;
            candidate.gameObject.SetActive(show);
            candidate.alpha = show ? 1 : 0;
        }
        var camera = Camera.main;
        camera.cullingMask = ~0;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        var texture = new RenderTexture(1920, 1080, 24);
        camera.targetTexture = texture;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
        image.Apply();
        Directory.CreateDirectory("Previews");
        File.WriteAllBytes("Previews/" + fileName + ".png", image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(texture);
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
