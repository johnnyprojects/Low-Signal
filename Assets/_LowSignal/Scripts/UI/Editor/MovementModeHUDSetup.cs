using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes sure the robot pose images in Assets/_LowSignal/Hud import as UI sprites, and that the scene's player carries a
/// MovementModeHUD with those sprites assigned, plus the AbilityWheel and its LIGHT and SONAR abilities. Runs after scripts reload and before entering Play mode, and from
/// Low Signal > UI > Set up movement mode HUD.
/// </summary>
[InitializeOnLoad]
public static class MovementModeHUDSetup
{
    private const string Folder = "Assets/_LowSignal/Hud/";
    private const string SonarPulseMaterialPath = "Assets/_LowSignal/Characters/Robot/Effects/SonarPulse.mat";
    private static readonly (string property, string file)[] Sprites =
    {
        // The rhombus and SPACE keycap are drawn by the HUD itself, so only the robot poses are images.
        ("hoverSprite", "hovering.png"),
        ("swimSprite", "swimming icon.png"),
    };

    static MovementModeHUDSetup()
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

    [MenuItem("Low Signal/UI/Set up movement mode HUD")]
    private static void SetUpFromMenu() => SetUp(true);

    private static void SetUp(bool fromMenu)
    {
        UnderwaterSwimController controller = Object.FindAnyObjectByType<UnderwaterSwimController>();
        // Scenes without the player have nothing to set up.
        if (controller == null)
        {
            if (fromMenu)
                Debug.LogError("Could not set up the movement mode HUD: the open scene has no object with an UnderwaterSwimController (the Player).");
            return;
        }

        MovementModeHUD hud = controller.GetComponent<MovementModeHUD>();
        bool added = hud == null;
        if (added)
            hud = Undo.AddComponent<MovementModeHUD>(controller.gameObject);

        SerializedObject serializedHud = new SerializedObject(hud);
        bool changed = added;
        foreach ((string property, string file) in Sprites)
        {
            SerializedProperty slot = serializedHud.FindProperty(property);
            if (slot.objectReferenceValue != null && !fromMenu)
                continue;
            Sprite sprite = LoadSprite(Folder + file);
            if (sprite == null)
                continue;
            slot.objectReferenceValue = sprite;
            changed = true;
        }
        serializedHud.ApplyModifiedProperties();

        // The Tab ability wheel and the abilities it uses live on the player too.
        changed |= EnsureComponent<AbilityWheel>(controller, "Ability wheel");
        changed |= EnsureComponent<Flashlight>(controller, "LIGHT ability");
        changed |= EnsureComponent<Sonar>(controller, "SONAR ability");
        SerializedObject sonar = new SerializedObject(controller.GetComponent<Sonar>());
        SerializedProperty pulseMaterial = sonar.FindProperty("pulseMaterial");
        if (pulseMaterial.objectReferenceValue == null)
        {
            Material pulse = AssetDatabase.LoadAssetAtPath<Material>(SonarPulseMaterialPath);
            if (pulse == null)
                Debug.LogError($"Sonar pulse material missing: expected {SonarPulseMaterialPath}.");
            pulseMaterial.objectReferenceValue = pulse;
            changed |= sonar.ApplyModifiedProperties();
        }

        if (!changed)
            return;
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Debug.Log($"Movement mode HUD {(added ? "added to" : "updated on")} {controller.name}. Save the scene to keep it.", hud);
    }

    private static bool EnsureComponent<T>(UnderwaterSwimController controller, string label) where T : Component
    {
        if (controller.GetComponent<T>() != null)
            return false;
        Undo.AddComponent<T>(controller.gameObject);
        Debug.Log($"{label} added to {controller.name}. Save the scene to keep it.", controller);
        return true;
    }

    private static Sprite LoadSprite(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"Movement mode HUD image missing: expected {path}. It may have been moved or renamed.");
            return null;
        }
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
        {
            // UI icons: a single sprite with clean transparent edges and no mipmap blur.
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            Debug.LogError($"Movement mode HUD image {path} did not import as a sprite.");
        return sprite;
    }
}
