using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ShallowsBossSetup
{
    const string ScenePath = "Assets/_LowSignal/Scenes/Shallows.unity";
    const string AssetRoot = "Assets/_LowSignal/Characters/Arapaima";
    const string RootName = "Shallows Treasure Guardian";
    static Terrain terrain;
    static float Ground(Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;
    static Transform Node(string name, Transform parent, Vector3 position)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.position = position;
        return t;
    }
    static Material Material(string name, Color color)
    {
        string path = AssetRoot + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.color = color;
        material.SetFloat("_Metallic", 0.65f);
        material.SetFloat("_Smoothness", 0.65f);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 0.25f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    [MenuItem("Low Signal/Shallows/Set up treasure guardian")]
    public static void Build()
    {
        if (SceneManager.GetActiveScene().path != ScenePath) throw new Exception("Open Shallows first.");
        if (GameObject.Find(RootName) != null) throw new Exception("Guardian already exists; edit its Inspector settings.");
        terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var cave = GameObject.Find("NE cave - entrance faces southwest").transform;
        var root = Node(RootName, null, cave.position);
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Set up treasure guardian");
        var boss = root.gameObject.AddComponent<ShallowsFishBoss>();
        Vector3 Local(float x, float z, float height)
        {
            Vector3 p = cave.TransformPoint(new Vector3(x, 0, z));
            p.y = Ground(p) + height;
            return p;
        }
        var fish = Node("Arapaima guardian", root, Local(0, -2, 2.4f));
        fish.rotation = cave.rotation * Quaternion.Euler(0, 180, 0);
        boss.fish = fish;
        var visual = Node("Model orientation", fish, fish.position);
        visual.localRotation = Quaternion.identity;
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AssetRoot + "/arapima.fbx"), visual);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        // Imported mesh bounds determine its longitudinal axis before rotation/scale.
        var renderers = model.GetComponentsInChildren<Renderer>();
        Bounds bounds = new Bounds();
        bool hasBounds = false;
        foreach (var renderer in renderers)
            for (int corner = 0; corner < 8; corner++)
            {
                Bounds b = renderer.localBounds;
                Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((corner & 1) == 0 ? -1 : 1,
                    (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                p = fish.InverseTransformPoint(renderer.transform.TransformPoint(p));
                if (!hasBounds) { bounds = new Bounds(p, Vector3.zero); hasBounds = true; }
                else bounds.Encapsulate(p);
            }
        Vector3 size = bounds.size;
        visual.localRotation = size.x > size.z && size.x > size.y ? Quaternion.Euler(0, 90, 0) :
            size.y > size.z ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
        float scale = 5f / Mathf.Max(size.x, size.y, size.z);
        visual.localScale = Vector3.one * scale;
        model.transform.localPosition = -bounds.center;
        var fishMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Fish Texture.mat");
        fishMaterial.shader = Shader.Find("Universal Render Pipeline/Lit");
        fishMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AssetRoot + "/Material.001 Base Color.png"));
        fishMaterial.SetColor("_BaseColor", Color.white);
        foreach (var renderer in renderers) renderer.sharedMaterial = fishMaterial;
        EditorUtility.SetDirty(fishMaterial);
        Debug.Log("FISH_ASSET: mesh bounds " + size + "; animation clips " + AnimationUtility.GetAnimationClips(model).Length);
        var body = fish.gameObject.AddComponent<CapsuleCollider>();
        body.direction = 2; body.height = 5; body.radius = 0.65f;
        var fishBody = fish.gameObject.AddComponent<Rigidbody>();
        fishBody.isKinematic = true; fishBody.useGravity = false;

        boss.offering = Node("Offering area - place three treasures in ring", root, Local(7, -21, 0.08f));
        boss.swimRoute = new[] {
            Node("Swim 1 - leave cave", root, Local(0, -12, 2.4f)),
            Node("Swim 2 - clear entrance", root, Local(0, -21, 2.4f)),
            Node("Swim 3 - treasure", root, boss.offering.position + Vector3.up * 2.3f)
        };
        var gold = Material("Offering gold", new Color(1, 0.62f, 0.08f));
        var ring = boss.offering.gameObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = gold; ring.useWorldSpace = false; ring.loop = true;
        ring.widthMultiplier = 0.07f; ring.positionCount = 64;
        for (int i = 0; i < ring.positionCount; i++)
        {
            float a = i * Mathf.PI * 2 / ring.positionCount;
            Vector3 p = boss.offering.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * boss.offeringRadius;
            p.y = Ground(p) + 0.08f;
            ring.SetPosition(i, p - boss.offering.position);
        }
        var blocker = Node("Guarded passage", root, Local(0, 0, 4));
        blocker.rotation = cave.rotation;
        boss.passageBlocker = blocker.gameObject.AddComponent<BoxCollider>();
        ((BoxCollider)boss.passageBlocker).size = new Vector3(15, 12, 0.5f);
        var exit = Node("Cave exit to UnderwaterCity", root, Local(0, 5, 2.5f));
        exit.rotation = cave.rotation;
        var trigger = exit.gameObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true; trigger.size = new Vector3(10, 7, 2);
        var rb = exit.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
        var portal = exit.gameObject.AddComponent<ShallowsCaveExit>(); portal.boss = boss;

        Physics.SyncTransforms();
        boss.treasures = new PickupItem[3];
        Vector3[] locations = { new Vector3(-22, 0, -12), new Vector3(22, 0, -24), new Vector3(-16, 0, 27) };
        for (int i = 0; i < locations.Length; i++)
        {
            Vector3 p = FindClearGround(locations[i]);
            var treasure = Node("Guardian treasure " + (i + 1), root, p);
            var chest = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chest.name = "Gold relic"; chest.transform.SetParent(treasure, false);
            chest.transform.localScale = new Vector3(0.65f, 0.4f, 0.45f);
            chest.GetComponent<Renderer>().sharedMaterial = gold;
            var gem = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gem.name = "Pearl"; gem.transform.SetParent(treasure, false);
            gem.transform.localPosition = new Vector3(0, 0.27f, 0);
            gem.transform.localScale = Vector3.one * 0.24f;
            UnityEngine.Object.DestroyImmediate(gem.GetComponent<Collider>());
            gem.GetComponent<Renderer>().sharedMaterial = Material("Relic pearl", new Color(0.3f, 0.95f, 1));
            var rigidbody = treasure.gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true; rigidbody.useGravity = false;
            var item = treasure.gameObject.AddComponent<PickupItem>();
            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = "shallows_guardian_treasure";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            boss.treasures[i] = item;
        }
        var player = UnityEngine.Object.FindFirstObjectByType<UnderwaterSwimController>();
        if (player == null) throw new Exception("Shallows player missing.");
        if (player.GetComponent<PlayerPickup>() == null) player.gameObject.AddComponent<PlayerPickup>();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root.gameObject;
    }

    static Vector3 FindClearGround(Vector3 desired)
    {
        for (int i = 0; i < 300; i++)
        {
            float a = i * 2.4f, radius = Mathf.Sqrt(i) * 0.7f;
            Vector3 p = desired + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
            p.y = Ground(p) + 0.24f;
            if (!Physics.CheckBox(p + Vector3.up * 0.5f, new Vector3(0.8f, 0.7f, 0.8f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) return p;
        }
        throw new Exception("No accessible treasure position near " + desired);
    }

    public static void BuildBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            Build();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("GUARDIAN_BUILD_PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void BuildAndValidate()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            Build();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("GUARDIAN_BUILD_PASS");
            ShallowsBossValidation.Run();
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
