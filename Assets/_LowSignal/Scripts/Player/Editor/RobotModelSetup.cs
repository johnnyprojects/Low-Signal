using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts the RobotMesh model on the scene's player as its visible body:
/// Player / Robot Visual (bob and sway) / Model Offset (orientation, scale, centring) / RobotMesh.
/// The offset faces the model's front along +Z with its jets behind, scales it to TargetHeight,
/// centres it on the capsule, and fits the CapsuleCollider around it. It also adds a nozzle at each jet and
/// wires up RobotThrusterEffects for the hover and swim effects. Run again after re-exporting the model.
/// It also runs by itself whenever the open scene's player has no body visual yet (after scripts reload and
/// before entering Play mode), so the robot never silently falls back to the placeholder capsule.
/// </summary>
[InitializeOnLoad]
public static class RobotModelSetup
{
    static RobotModelSetup()
    {
        // Play mode reloads scripts too; the scene must only be changed in edit mode.
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                SetUpIfMissing();
        };
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
                SetUpIfMissing();
        };
    }

    private static void SetUpIfMissing()
    {
        UnderwaterSwimController controller = Object.FindAnyObjectByType<UnderwaterSwimController>();
        // Scenes without the player have nothing to set up.
        if (controller == null)
            return;
        Transform bodyVisual = new SerializedObject(controller).FindProperty("bodyVisual").objectReferenceValue as Transform;
        if (bodyVisual == null)
        {
            Debug.Log($"{controller.name} has no body visual, so the RobotMesh model is being set up on it now.", controller);
            SetUp();
            return;
        }
        // Rebuild a robot this script made once its model is replaced or deleted; leave any other body visual alone.
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (bodyVisual.name == VisualName && model != null && !ContainsInstanceOf(bodyVisual, model))
        {
            Debug.Log($"{controller.name}'s robot was built from a model that is no longer at {ModelPath}, " +
                      "so it is being rebuilt from the current RobotMesh.", controller);
            SetUp();
            return;
        }
        if (bodyVisual.name == VisualName && controller.GetComponent<RobotThrusterEffects>() == null)
        {
            Debug.Log($"{controller.name} has no hover and swim effects yet, so the robot is being rebuilt with jet nozzles and effects.", controller);
            SetUp();
        }
    }

    private static bool ContainsInstanceOf(Transform root, GameObject model)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (PrefabUtility.GetCorrespondingObjectFromOriginalSource(child.gameObject) == model)
                return true;
        return false;
    }

    private const string ModelPath = "Assets/_LowSignal/Characters/Robot/RobotMesh.fbx";
    private const string VisualName = "Robot Visual";
    private const string OffsetName = "Model Offset";
    private const string GlowMaterialPath = "Assets/_LowSignal/Characters/Robot/Effects/RobotFX_Glow.mat";
    private const string SoftMaterialPath = "Assets/_LowSignal/Characters/Robot/Effects/RobotFX_Soft.mat";
    // Metres from the bottom of the robot to the top of its head.
    private const float TargetHeight = 1.8f;
    // Parts of the Blender model used to find which way it faces.
    private const string BodyPart = "Cylinder";
    private const string HeadPart = "Sphere";
    private const string JetsPart = "Cylinder.001";

    [MenuItem("Low Signal/Player/Set up RobotMesh model")]
    private static void SetUp()
    {
        UnderwaterSwimController controller = Object.FindAnyObjectByType<UnderwaterSwimController>();
        if (controller == null)
        {
            Debug.LogError("Could not set up the robot: the open scene has no object with an UnderwaterSwimController (the Player).");
            return;
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"Could not set up the robot: no model at {ModelPath}. It may have been moved, renamed or " +
                           "failed to import. Put RobotMesh.fbx back there, or update ModelPath in RobotModelSetup.cs.");
            return;
        }
        if (!HasAnyMesh(model))
        {
            Debug.LogError($"Could not set up the robot: {ModelPath} imported without any mesh geometry. " +
                           "Check its import settings and the Console for FBX import errors, then re-export it from Blender.", model);
            return;
        }

        Transform player = controller.transform;
        Undo.SetCurrentGroupName("Set up RobotMesh model");
        Transform previous = player.Find(VisualName);
        if (previous != null)
            Undo.DestroyObjectImmediate(previous.gameObject);

        Transform visual = new GameObject(VisualName).transform;
        Undo.RegisterCreatedObjectUndo(visual.gameObject, "Create robot visual");
        visual.SetParent(player, false);
        Transform offset = new GameObject(OffsetName).transform;
        offset.SetParent(visual, false);
        GameObject robot = (GameObject)PrefabUtility.InstantiatePrefab(model, offset);
        robot.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        robot.transform.localScale = Vector3.one;

        offset.localRotation = FaceForward(offset, robot);
        Bounds bounds = LocalBounds(player, robot, null);
        offset.localScale = Vector3.one * (TargetHeight / Mathf.Max(0.0001f, bounds.size.y));

        // Centre vertically on the whole model, and horizontally on the body so the arms and jets do not pull it aside.
        bounds = LocalBounds(player, robot, null);
        Vector3 axis = TryFindPart(robot, BodyPart, out Transform bodyPart)
            ? LocalBounds(player, robot, bodyPart).center
            : bounds.center;
        offset.localPosition = -new Vector3(axis.x, bounds.center.y, axis.z);

        FitCollider(controller.GetComponent<CapsuleCollider>(), LocalBounds(player, robot, null));

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("bodyVisual").objectReferenceValue = visual;
        serializedController.ApplyModifiedProperties();
        SetUpEffects(controller, visual, CreateNozzles(visual, robot));
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);

        Vector3 euler = offset.localEulerAngles;
        Debug.Log($"RobotMesh set up on {player.name}. Model Offset rotation ({euler.x:0.#}, {euler.y:0.#}, {euler.z:0.#}), " +
                  $"scale {offset.localScale.x:0.###}, position {offset.localPosition}. Save the scene to keep it.", visual);
    }

    /// <summary>
    /// One nozzle at the bottom of each jet, under Robot Visual so the exhaust bobs and leans with the model.
    /// Each faces down the robot's back: below it while hovering, behind it once it leans into a swim.
    /// </summary>
    private static Transform[] CreateNozzles(Transform visual, GameObject robot)
    {
        if (!TryFindPart(robot, JetsPart, out Transform jetsPart))
        {
            Debug.LogWarning($"RobotMesh has no part named {JetsPart}, so the jet effects will come from behind the body " +
                             "instead of the jets. Add nozzle transforms to RobotThrusterEffects by hand.");
            return new Transform[0];
        }

        Bounds jets = LocalBounds(visual, robot, jetsPart);
        // The jets mesh holds both cylinders side by side; each is as wide as the pack is deep.
        float radius = jets.size.z * 0.5f;
        float[] xs = jets.size.x > radius * 3f
            ? new[] { jets.min.x + radius, jets.max.x - radius }
            : new[] { jets.center.x };
        Transform[] nozzles = new Transform[xs.Length];
        for (int i = 0; i < xs.Length; i++)
        {
            nozzles[i] = new GameObject(xs.Length == 1 ? "Jet Nozzle" : i == 0 ? "Jet Nozzle L" : "Jet Nozzle R").transform;
            nozzles[i].SetParent(visual, false);
            nozzles[i].SetLocalPositionAndRotation(new Vector3(xs[i], jets.min.y, jets.center.z),
                Quaternion.LookRotation(Vector3.down, Vector3.back));
        }
        return nozzles;
    }

    private static void SetUpEffects(UnderwaterSwimController controller, Transform visual, Transform[] nozzles)
    {
        Material glow = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
        Material soft = AssetDatabase.LoadAssetAtPath<Material>(SoftMaterialPath);
        if (glow == null || soft == null)
            Debug.LogError($"Robot effects are missing their materials: expected {GlowMaterialPath} and {SoftMaterialPath}.");

        RobotThrusterEffects effects = controller.GetComponent<RobotThrusterEffects>();
        if (effects == null)
            effects = Undo.AddComponent<RobotThrusterEffects>(controller.gameObject);
        SerializedObject serializedEffects = new SerializedObject(effects);
        SerializedProperty nozzleList = serializedEffects.FindProperty("nozzles");
        nozzleList.arraySize = nozzles.Length;
        for (int i = 0; i < nozzles.Length; i++)
            nozzleList.GetArrayElementAtIndex(i).objectReferenceValue = nozzles[i];
        serializedEffects.FindProperty("body").objectReferenceValue = visual;
        serializedEffects.FindProperty("glowMaterial").objectReferenceValue = glow;
        serializedEffects.FindProperty("softMaterial").objectReferenceValue = soft;
        serializedEffects.ApplyModifiedProperties();
    }

    /// <summary>The offset rotation that turns the model's head up and its jets toward -Z.</summary>
    private static Quaternion FaceForward(Transform offset, GameObject robot)
    {
        if (!TryFindPart(robot, BodyPart, out Transform bodyPart) || !TryFindPart(robot, HeadPart, out Transform headPart)
            || !TryFindPart(robot, JetsPart, out Transform jetsPart))
        {
            Debug.LogWarning($"RobotMesh no longer has parts named {BodyPart}, {HeadPart} and {JetsPart}; " +
                             "leaving its orientation as imported. Rotate Model Offset by hand if it faces the wrong way.");
            return Quaternion.identity;
        }

        Vector3 bodyCenter = LocalBounds(offset, robot, bodyPart).center;
        Vector3 up = (LocalBounds(offset, robot, headPart).center - bodyCenter).normalized;
        Vector3 back = Vector3.ProjectOnPlane(LocalBounds(offset, robot, jetsPart).center - bodyCenter, up).normalized;
        Vector3 euler = Quaternion.Inverse(Quaternion.LookRotation(-back, up)).eulerAngles;
        // Blender exports are off by whole quarter turns; snap away tiny modelling tilts.
        return Quaternion.Euler(Snap(euler.x), Snap(euler.y), Snap(euler.z));
    }

    private static float Snap(float angle)
    {
        float quarter = Mathf.Round(angle / 90f) * 90f;
        return Mathf.Abs(Mathf.DeltaAngle(angle, quarter)) < 10f ? quarter : angle;
    }

    private static void FitCollider(CapsuleCollider capsule, Bounds bounds)
    {
        if (capsule == null)
            return;
        Undo.RecordObject(capsule, "Fit robot collider");
        // The capsule leans with the robot, so the same upright fit also covers the horizontal swim pose.
        // Slightly inside the arm and jet tips, so the robot slides past edges instead of snagging on them.
        capsule.direction = 1;
        capsule.height = bounds.size.y;
        capsule.radius = Mathf.Min(0.45f * Mathf.Max(bounds.size.x, bounds.size.z), bounds.size.y * 0.5f);
        capsule.center = bounds.center;
    }

    private static bool TryFindPart(GameObject robot, string partName, out Transform part)
    {
        foreach (Transform child in robot.GetComponentsInChildren<Transform>())
        {
            if (child.name == partName && HasMesh(child.GetComponent<Renderer>()))
            {
                part = child;
                return true;
            }
        }
        part = null;
        return false;
    }

    /// <summary>Bounds of the model's meshes (or one part) in the space of the given transform.</summary>
    private static Bounds LocalBounds(Transform space, GameObject robot, Transform onlyPart)
    {
        List<Vector3> points = new List<Vector3>();
        foreach (Renderer renderer in robot.GetComponentsInChildren<Renderer>())
        {
            if (onlyPart != null && renderer.transform != onlyPart)
                continue;
            if (!HasMesh(renderer))
                continue;
            Bounds meshBounds = MeshOf(renderer).bounds;
            Matrix4x4 toSpace = space.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                points.Add(toSpace.MultiplyPoint3x4(local));
            }
        }

        Bounds bounds = new Bounds(points.Count > 0 ? points[0] : Vector3.zero, Vector3.zero);
        foreach (Vector3 point in points)
            bounds.Encapsulate(point);
        return bounds;
    }

    private static bool HasAnyMesh(GameObject model)
    {
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            if (HasMesh(renderer))
                return true;
        return false;
    }

    // Empty meshes (such as an unconverted curve) would drag the bounds toward the origin.
    private static bool HasMesh(Renderer renderer)
    {
        Mesh mesh = MeshOf(renderer);
        return mesh != null && mesh.vertexCount > 0;
    }

    private static Mesh MeshOf(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
            return skinned.sharedMesh;
        MeshFilter filter = renderer != null ? renderer.GetComponent<MeshFilter>() : null;
        return filter != null ? filter.sharedMesh : null;
    }
}
