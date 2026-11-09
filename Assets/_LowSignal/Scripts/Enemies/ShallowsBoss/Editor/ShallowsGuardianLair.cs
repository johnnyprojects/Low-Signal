using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Dresses the guardian's lair under the guardian root: a hoard of gold beneath the fish, darkness filling the cave
// behind it, the four treasures' models, the stone circle treasure is placed in, and the intro cutscene.
// Rerunning replaces them all; the meshes are kept as assets so the scene stays small.
public static class ShallowsGuardianLair
{
    const string ScenePath = "Assets/_LowSignal/Scenes/Shallows.unity";
    const string AssetRoot = "Assets/_LowSignal/World/Shallows/Dressing";
    const string GoldName = "Guardian gold pile", DarknessName = "Cave darkness - fades to black", CutsceneName = "Guardian intro cutscene";
    static System.Random random;
    static Terrain terrain;
    static float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
    static float Ground(Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;

    [MenuItem("Low Signal/Shallows/Build guardian gold, treasures, cave darkness and intro cutscene")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play mode first.");
        if (SceneManager.GetActiveScene().path != ScenePath) throw new Exception("Open Shallows first.");
        terrain = UnityEngine.Object.FindFirstObjectByType<Terrain>();
        var boss = UnityEngine.Object.FindFirstObjectByType<ShallowsFishBoss>();
        var cave = GameObject.Find("NE cave - entrance faces southwest");
        if (boss == null || cave == null) throw new Exception("The guardian and its cave must be set up first.");
        random = new System.Random(301001);
        foreach (string name in new[] { GoldName, DarknessName, CutsceneName })
        {
            Transform old = boss.transform.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
        Undo.RecordObject(boss, "Build guardian lair");
        BuildStoneCircle(boss, cave.transform);
        BuildGold(boss, cave.transform);
        BuildDarkness(boss, cave.transform);
        Physics.SyncTransforms();
        BuildTreasures(boss);
        Physics.SyncTransforms();
        BuildCutscene(boss, cave.transform);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
    }

    public static void BuildBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            Build();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Capture();
            Debug.Log("GUARDIAN_LAIR_PASS: gold, darkness and cutscene built; previews captured.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static Transform Node(string name, Transform parent)
    {
        var node = new GameObject(name).transform;
        node.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(node.gameObject, "Build guardian lair");
        return node;
    }

    // ---------- Gold ----------

    // The hoard is several overlapping heaps, (x, z, radius, height) in the pile's frame, the tallest under the fish.
    static readonly Vector4[] Heaps =
    {
        new Vector4(0, 0, 2.6f, 1.0f), new Vector4(1.2f, 1.4f, 1.6f, 0.7f), new Vector4(-1.1f, -1.5f, 1.7f, 0.75f),
        new Vector4(-1.5f, 0.9f, 1.3f, 0.55f), new Vector4(1.4f, -1.1f, 1.2f, 0.5f), new Vector4(0.2f, 2.4f, 1.0f, 0.4f),
    };
    const float PileReach = 3.8f;

    // Height of the heaped gold above the sand, or below zero where there is none.
    static float Mound(float x, float z)
    {
        float height = -0.08f;
        foreach (var heap in Heaps)
        {
            float d = new Vector2(x - heap.x, z - heap.y).magnitude / heap.z;
            if (d < 1) height = Mathf.Max(height, heap.w * Mathf.Pow(1 - d * d, 0.8f) - 0.06f * d);
        }
        return height <= 0 ? height : height + (Mathf.PerlinNoise(x * 2.3f + 30, z * 2.3f + 12) - 0.5f) * 0.1f;
    }

    static Vector3 Slope(float x, float z) =>
        new Vector3(Mound(x - 0.08f, z) - Mound(x + 0.08f, z), 0.16f, Mound(x, z - 0.08f) - Mound(x, z + 0.08f)).normalized;

    // The middle of the fish as drawn. The model sits off its transform, so placing by the transform misses the body.
    static Vector3 FishCentre(ShallowsFishBoss boss)
    {
        var renderers = boss.fish.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return boss.fish.position;
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        return bounds.center;
    }

    static void BuildGold(ShallowsFishBoss boss, Transform cave)
    {
        var root = Node(GoldName, boss.transform);
        Vector3 under = FishCentre(boss);
        under.y = Ground(under);
        root.SetPositionAndRotation(under, cave.rotation);
        float SandAt(float x, float z) => Ground(root.TransformPoint(new Vector3(x, 0, z))) - under.y;
        Vector3 OnPile(float x, float z, float lift) => new Vector3(x, SandAt(x, z) + Mathf.Max(Mound(x, z), 0) + lift, z);
        // A random point on the gold (or, when off is true, on the sand around it) no higher than the given height,
        // so nothing reaches up into the fish.
        Vector2 Spot(bool off, float maxHeight = 2)
        {
            for (int tries = 0; tries < 200; tries++)
            {
                float a = R(0, Mathf.PI * 2), r = off ? R(2.6f, 4.6f) : Mathf.Sqrt(R(0, 1)) * PileReach;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r, m = Mound(x, z);
                if (off ? m <= 0 : m > 0.05f && m <= maxHeight) return new Vector2(x, z);
            }
            return Vector2.zero;
        }

        // The heap's body, so no sand shows between the coins; it is also the pile's collider.
        var dome = new MeshBuilder();
        const int rings = 24, segments = 56;
        for (int i = 0; i <= rings; i++)
            for (int j = 0; j < segments; j++)
            {
                float u = i / (float)rings, a = j * Mathf.PI * 2 / segments;
                float x = Mathf.Cos(a) * PileReach * u, z = Mathf.Sin(a) * PileReach * u;
                dome.Vertex(new Vector3(x, SandAt(x, z) + Mound(x, z), z));
            }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < segments; j++)
            {
                int a = i * segments + j, b = i * segments + (j + 1) % segments;
                dome.Quad(a, b, b + segments, a + segments);
            }

        var coins = new MeshBuilder();
        var cyan = new MeshBuilder();
        var red = new MeshBuilder();
        var green = new MeshBuilder();
        // Small coins heaped every which way on the gold, and a scatter spilled onto the sand.
        for (int i = 0; i < 2600; i++)
        {
            Vector2 p = Spot(false);
            // Mostly lying with the slope, a few kicked up on edge.
            float kick = i % 9 == 0 ? 50 : 18;
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, Slope(p.x, p.y)) * Quaternion.Euler(R(-kick, kick), R(0, 360), R(-kick, kick));
            coins.Coin(OnPile(p.x, p.y, R(0.004f, 0.02f)), tilt, R(0.06f, 0.085f));
        }
        for (int i = 0; i < 260; i++)
        {
            Vector2 p = Spot(true);
            coins.Coin(OnPile(p.x, p.y, 0.012f), Quaternion.Euler(R(-8, 8), R(0, 360), R(-8, 8)), R(0.07f, 0.1f));
        }
        // Neat stacks of coins standing round the foot of the heap.
        for (int s = 0; s < 8; s++)
        {
            Vector2 p = Spot(true);
            int count = random.Next(3, 10);
            for (int c = 0; c < count; c++)
                coins.Coin(OnPile(p.x + R(-0.012f, 0.012f), p.y + R(-0.012f, 0.012f), 0.0125f + c * 0.026f), Quaternion.Euler(R(-3, 3), R(0, 360), R(-3, 3)), 0.085f);
        }
        // Small pyramids of gold bars on the lower slopes.
        for (int s = 0; s < 3; s++)
        {
            Vector2 p = Spot(false, 0.5f);
            Quaternion turn = Quaternion.FromToRotation(Vector3.up, Slope(p.x, p.y)) * Quaternion.Euler(0, R(0, 360), 0);
            Vector3 foot = OnPile(p.x, p.y, 0.03f);
            for (int layer = 0; layer < 2; layer++)
                for (int b = 0; b < 3 - layer; b++)
                    coins.Box(foot + turn * new Vector3((b - (2 - layer) * 0.5f) * 0.16f, 0.045f + layer * 0.09f, 0), turn, new Vector3(0.14f, 0.09f, 0.3f));
        }
        // Goblets, upright and tipped over, and a small crown, all on the lower heaps.
        for (int g = 0; g < 4; g++)
        {
            var goblet = new MeshBuilder();
            var gobletGems = new MeshBuilder();
            Chalice(goblet, gobletGems);
            Vector2 p = Spot(false, 0.6f);
            Quaternion lie = g % 2 == 0 ? Quaternion.Euler(0, R(0, 360), 0) : Quaternion.Euler(R(70, 85), R(0, 360), 0);
            var place = Matrix4x4.TRS(OnPile(p.x, p.y, g % 2 == 0 ? -0.02f : 0.12f), lie, Vector3.one * 0.45f);
            coins.Append(goblet, place);
            red.Append(gobletGems, place);
        }
        {
            var crown = new MeshBuilder();
            var crownCyan = new MeshBuilder();
            var crownRed = new MeshBuilder();
            Crown(crown, crownCyan, crownRed);
            Vector2 p = Spot(false, 0.6f);
            var place = Matrix4x4.TRS(OnPile(p.x, p.y, -0.02f), Quaternion.FromToRotation(Vector3.up, Slope(p.x, p.y)) * Quaternion.Euler(R(-15, 15), R(0, 360), 0), Vector3.one * 0.7f);
            coins.Append(crown, place);
            cyan.Append(crownCyan, place);
            red.Append(crownRed, place);
        }
        // Strings of gold beads draped over the heap.
        for (int s = 0; s < 4; s++)
        {
            Vector2 from = Spot(false), to = Spot(false);
            for (int b = 0; b <= 36; b++)
            {
                Vector2 p = Vector2.Lerp(from, to, b / 36f) + Vector2.Perpendicular(to - from).normalized * Mathf.Sin(b / 36f * Mathf.PI) * 0.4f;
                coins.Sphere(OnPile(p.x, p.y, 0.02f), b % 6 == 0 ? 0.035f : 0.024f);
            }
        }
        // Gems half sunk into the gold.
        for (int i = 0; i < 36; i++)
        {
            Vector2 p = Spot(false);
            (i % 3 == 0 ? cyan : i % 3 == 1 ? red : green).Sphere(OnPile(p.x, p.y, 0.02f), R(0.05f, 0.09f));
        }

        var bodyMaterial = TreasureMaterial("Hoard gold", new Color(0.85f, 0.6f, 0.16f), 0.8f, 0.55f, new Color(0.22f, 0.13f, 0.02f));
        var coinMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_LowSignal/Characters/Arapaima/Offering gold.mat");
        var body = new GameObject("Heap").transform;
        body.SetParent(root, false);
        root.gameObject.AddComponent<MeshFilter>().sharedMesh = SaveMesh(coins.ToMesh("Guardian gold pile"));
        root.gameObject.AddComponent<MeshRenderer>().sharedMaterial = coinMaterial;
        Mesh heap = SaveMesh(dome.ToMesh("Guardian gold pile collider"));
        root.gameObject.AddComponent<MeshCollider>().sharedMesh = heap;
        body.gameObject.AddComponent<MeshFilter>().sharedMesh = heap;
        body.gameObject.AddComponent<MeshRenderer>().sharedMaterial = bodyMaterial;
        PileGems(root, cyan, "Guardian gold pile gems", AssetDatabase.LoadAssetAtPath<Material>("Assets/_LowSignal/Characters/Arapaima/Relic pearl.mat"));
        PileGems(root, red, "Guardian gold pile rubies", GemMaterial("Relic ruby", new Color(0.9f, 0.08f, 0.16f)));
        PileGems(root, green, "Guardian gold pile emeralds", GemMaterial("Relic emerald", new Color(0.1f, 0.85f, 0.4f)));
        foreach (Transform t in root.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;

        // A warm glow between the gold and the fish's belly makes the hoard catch the eye in the cave mouth.
        var glow = Node("Gold glow", root);
        glow.localPosition = new Vector3(0, 1.5f, 0);
        var light = glow.gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.72f, 0.3f);
        light.intensity = 1.6f;
        light.range = 7f;
        light.shadows = LightShadows.None;
    }

    static void PileGems(Transform root, MeshBuilder mesh, string name, Material material)
    {
        if (mesh.IsEmpty) return;
        var gems = Node(name.Substring("Guardian gold pile ".Length), root);
        gems.gameObject.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh.ToMesh(name));
        gems.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static Material TreasureMaterial(string name, Color color, float metallic, float smoothness, Color emission)
    {
        string path = TreasureRoot + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        // Set every time, so changing the colours here updates the existing asset.
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
        EditorUtility.SetDirty(material);
        return material;
    }

    // Overwrites an existing mesh asset in place so scene references keep pointing at it.
    static Mesh SaveMesh(Mesh mesh) => SaveMesh(mesh, AssetRoot);

    static Mesh SaveMesh(Mesh mesh, string folder)
    {
        string path = folder + "/" + mesh.name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        existing.Clear();
        existing.indexFormat = mesh.indexFormat;
        existing.SetVertices(mesh.vertices);
        existing.SetNormals(mesh.normals);
        existing.SetTriangles(mesh.triangles, 0);
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        return existing;
    }

    // ---------- Darkness ----------

    // Translucent black sheets across the cave, one behind another from just behind the fish to the back wall, so the
    // cave darkens with depth and is black at the back. Their edges sit inside the walls and roof.
    static void BuildDarkness(ShallowsFishBoss boss, Transform cave)
    {
        var root = Node(DarknessName, boss.transform);
        root.SetPositionAndRotation(cave.position, cave.rotation);
        // Many faint sheets rather than a few dark ones, so the fade is smooth where they cross the floor and walls.
        Material sheet = DarkMaterial("Cave darkness", 0.18f), solid = DarkMaterial("Cave darkness - back", 1f);
        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        const int sheets = 18;
        for (int i = 0; i <= sheets; i++)
        {
            float z = 0.4f + i * 0.4f;
            Vector3 centre = cave.TransformPoint(new Vector3(0, 0, z));
            var card = Node(i == sheets ? "Black back" : "Darkness sheet " + (i + 1), root);
            card.position = new Vector3(centre.x, Ground(centre) + 3.6f, centre.z);
            card.rotation = cave.rotation;
            card.localScale = new Vector3(11, 8.5f, 1);
            card.gameObject.AddComponent<MeshFilter>().sharedMesh = quad;
            var renderer = card.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = i == sheets ? solid : sheet;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    // Uses the fog-free darkness shader, so the sheets add up to true black rather than the water's fog colour.
    static Material DarkMaterial(string name, float alpha)
    {
        string path = AssetRoot + "/" + name + ".mat";
        var shader = Shader.Find("Low Signal/Cave Darkness");
        if (shader == null) throw new Exception("Cave darkness shader missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_Color", new Color(0, 0, 0, alpha));
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------- Stone circle ----------

    // A ring of weathered standing stones around the offering point, open on the guardian's side, with a kerb of flat
    // stones marking the edge and a low slab in the middle. Treasure set down inside it is accepted.
    static void BuildStoneCircle(ShallowsFishBoss boss, Transform cave)
    {
        var ring = boss.offering.GetComponent<LineRenderer>();
        if (ring != null) Undo.DestroyObjectImmediate(ring);
        Transform old = boss.offering.Find("Stone circle");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var circle = Node("Stone circle", boss.offering);
        circle.localPosition = Vector3.zero;
        Mesh rock = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_LowSignal/World/SeaFloor/Models/Boulder.obj").GetComponentInChildren<MeshFilter>().sharedMesh;
        Material stone = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Algae limestone.mat");
        Material pale = AssetDatabase.LoadAssetAtPath<Material>(AssetRoot + "/Shell limestone.mat");
        Vector3 centre = boss.offering.position;
        Vector3 toFish = FishCentre(boss) - centre;
        float opening = Mathf.Atan2(toFish.z, toFish.x);
        GameObject Stone(string name, float angle, float radius, Vector3 scale, float sink, Material material, bool solid)
        {
            Vector3 p = centre + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            p.y = Ground(p) + scale.y * sink;
            var go = new GameObject(name);
            go.transform.SetParent(circle, false);
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(new Vector3(centre.x - p.x, 0, centre.z - p.z)) * Quaternion.Euler(R(-6, 6), R(-12, 12), R(-6, 6)));
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = rock;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (solid) go.AddComponent<MeshCollider>().sharedMesh = rock;
            go.isStatic = true;
            return go;
        }
        // Nine standing stones with a gap facing the guardian, so it reads as a gateway to the cave.
        for (int i = 0; i < 10; i++)
        {
            if (i == 0) continue;
            float angle = opening + i * Mathf.PI * 2 / 10 + R(-0.06f, 0.06f);
            Stone("Standing stone", angle, boss.offeringRadius + 0.55f, new Vector3(R(0.42f, 0.58f), R(0.85f, 1.25f), R(0.32f, 0.45f)), 0.7f, i % 3 == 0 ? pale : stone, true);
        }
        // A kerb of low flat stones tracing the edge of the circle.
        for (int i = 0; i < 30; i++)
            Stone("Kerb stone", i * Mathf.PI * 2 / 30 + R(-0.03f, 0.03f), boss.offeringRadius, new Vector3(R(0.3f, 0.4f), R(0.1f, 0.14f), R(0.22f, 0.3f)), 0.15f, pale, false);
        // A low slab at the heart of the circle to set the treasure on.
        var slab = Stone("Offering slab", 0, 0, new Vector3(0.95f, 0.22f, 0.95f), 0.25f, pale, true);
        slab.transform.rotation = Quaternion.Euler(R(-3, 3), R(0, 360), R(-3, 3));
    }

    // ---------- Treasures ----------

    const string TreasureRoot = "Assets/_LowSignal/Items/Treasure";
    static readonly string[] TreasureKinds = { "Necklace", "Chalice", "Gold pottery", "Crown" };

    // Gives each guardian treasure its model (a necklace, a chalice, a gold pot and a crown), adding the fourth
    // treasure inside a swim-through grotto if it is missing. Each keeps its object, so saves still find it.
    static void BuildTreasures(ShallowsFishBoss boss)
    {
        Directory.CreateDirectory(TreasureRoot + "/Models");
        var gold = AssetDatabase.LoadAssetAtPath<Material>("Assets/_LowSignal/Characters/Arapaima/Offering gold.mat");
        var pearl = AssetDatabase.LoadAssetAtPath<Material>("Assets/_LowSignal/Characters/Arapaima/Relic pearl.mat");
        var ruby = GemMaterial("Relic ruby", new Color(0.9f, 0.08f, 0.16f));
        var treasures = new List<PickupItem>(boss.treasures);
        while (treasures.Count < TreasureKinds.Length)
        {
            var root = Node("Guardian treasure " + (treasures.Count + 1), boss.transform);
            var grotto = GameObject.Find("Swim-through grotto 02") ?? GameObject.Find("Swim-through grotto 05");
            root.position = ClearSpot(grotto != null ? grotto.transform.position : new Vector3(-30, 0, -50));
            var body = root.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var item = root.gameObject.AddComponent<PickupItem>();
            var serialized = new SerializedObject(item);
            serialized.FindProperty("itemId").stringValue = "shallows_guardian_treasure";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            treasures.Add(item);
        }
        for (int i = 0; i < treasures.Count && i < TreasureKinds.Length; i++)
        {
            Transform root = treasures[i].transform;
            for (int c = root.childCount - 1; c >= 0; c--) Undo.DestroyObjectImmediate(root.GetChild(c).gameObject);
            var goldMesh = new MeshBuilder();
            var cyanMesh = new MeshBuilder();
            var redMesh = new MeshBuilder();
            switch (i)
            {
                case 0: Necklace(goldMesh, cyanMesh); break;
                case 1: Chalice(goldMesh, redMesh); break;
                case 2: Pottery(goldMesh, cyanMesh); break;
                default: Crown(goldMesh, cyanMesh, redMesh); break;
            }
            var model = Node(TreasureKinds[i], root);
            model.localPosition = Vector3.zero;
            var mesh = SaveMesh(goldMesh.ToMesh(TreasureKinds[i]), TreasureRoot + "/Models");
            model.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.gameObject.AddComponent<MeshRenderer>().sharedMaterial = gold;
            var box = model.gameObject.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.12f);
            Gems(model, cyanMesh, TreasureKinds[i] + " gems", pearl);
            Gems(model, redMesh, TreasureKinds[i] + " rubies", ruby);
            // The models stand on their base, so the treasure rests on the sand.
            root.position = new Vector3(root.position.x, Ground(root.position) + 0.02f, root.position.z);
        }
        Undo.RecordObject(boss, "Build guardian treasures");
        boss.treasures = treasures.ToArray();
        boss.requiredTreasure = boss.treasures.Length;
    }

    static void Gems(Transform model, MeshBuilder mesh, string name, Material material)
    {
        if (mesh.IsEmpty) return;
        var gems = Node(name, model);
        gems.gameObject.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh.ToMesh(name), TreasureRoot + "/Models");
        gems.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static Material GemMaterial(string name, Color color)
    {
        string path = TreasureRoot + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", 0.2f);
        material.SetFloat("_Smoothness", 0.9f);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 0.35f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static Vector3 ClearSpot(Vector3 desired)
    {
        for (int i = 0; i < 300; i++)
        {
            float a = i * 2.4f, radius = Mathf.Sqrt(i) * 0.7f;
            Vector3 p = desired + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
            p.y = Ground(p) + 0.02f;
            // Clear of rocks and props; the sand itself, which rises and falls under the box on dunes, does not count.
            bool blocked = false;
            foreach (var hit in Physics.OverlapBox(p + Vector3.up * 0.9f, new Vector3(0.8f, 0.6f, 0.8f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                blocked |= !(hit is TerrainCollider);
            if (!blocked) return p;
        }
        throw new Exception("No clear spot for a treasure near " + desired);
    }

    // A dropped string of gold beads with a cyan pendant, lying in a loose loop.
    static void Necklace(MeshBuilder gold, MeshBuilder gems)
    {
        const int beads = 34;
        for (int i = 0; i < beads; i++)
        {
            float t = i * Mathf.PI * 2 / beads, s = Mathf.Sin(t);
            float droop = Mathf.Pow(Mathf.Max(0, -s), 4) * 0.12f;
            gold.Sphere(new Vector3(Mathf.Cos(t) * 0.36f, 0.04f, s * 0.27f - droop), i % 2 == 0 ? 0.04f : 0.03f);
        }
        Vector3 pendant = new Vector3(0, 0.05f, -0.47f);
        gold.Torus(pendant, Quaternion.identity, 0.085f, 0.018f);
        gold.Torus(pendant + new Vector3(0, 0, 0.09f), Quaternion.Euler(0, 0, 90), 0.025f, 0.01f);
        gems.Sphere(pendant, 0.07f);
    }

    // A goblet on a knotted stem, its cup ringed with rubies.
    static void Chalice(MeshBuilder gold, MeshBuilder rubies)
    {
        const float k = 1.4f;
        var profile = new[]
        {
            new Vector2(0, 0), new Vector2(0.2f, 0), new Vector2(0.2f, 0.025f), new Vector2(0.12f, 0.06f), new Vector2(0.045f, 0.13f),
            new Vector2(0.07f, 0.2f), new Vector2(0.04f, 0.27f), new Vector2(0.1f, 0.32f), new Vector2(0.2f, 0.42f), new Vector2(0.235f, 0.56f),
            new Vector2(0.215f, 0.56f), new Vector2(0.18f, 0.44f), new Vector2(0.06f, 0.36f), new Vector2(0, 0.355f),
        };
        gold.Lathe(Vector3.zero, Scale(profile, k), 24);
        gold.Torus(new Vector3(0, 0.2f * k, 0), Quaternion.identity, 0.07f * k, 0.02f * k);
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3;
            rubies.Sphere(new Vector3(Mathf.Cos(a) * 0.2f * k, 0.46f * k, Mathf.Sin(a) * 0.2f * k), 0.028f * k);
        }
    }

    // A round-bellied gold urn with two handles and a band of cyan stones.
    static void Pottery(MeshBuilder gold, MeshBuilder gems)
    {
        var profile = new[]
        {
            new Vector2(0, 0), new Vector2(0.13f, 0), new Vector2(0.16f, 0.05f), new Vector2(0.26f, 0.2f), new Vector2(0.28f, 0.32f),
            new Vector2(0.24f, 0.46f), new Vector2(0.13f, 0.57f), new Vector2(0.1f, 0.66f), new Vector2(0.15f, 0.73f),
            new Vector2(0.125f, 0.74f), new Vector2(0.085f, 0.68f), new Vector2(0, 0.62f),
        };
        gold.Lathe(Vector3.zero, profile, 24);
        gold.Torus(new Vector3(0, 0.32f, 0), Quaternion.identity, 0.283f, 0.018f);
        for (int side = -1; side <= 1; side += 2)
            gold.Torus(new Vector3(side * 0.15f, 0.56f, 0), Quaternion.Euler(90, 0, 0), 0.1f, 0.02f,
                side > 0 ? -Mathf.PI * 0.55f : Mathf.PI * 0.45f, side > 0 ? Mathf.PI * 0.55f : Mathf.PI * 1.55f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            gems.Sphere(new Vector3(Mathf.Cos(a) * 0.29f, 0.32f, Mathf.Sin(a) * 0.29f), 0.03f);
        }
    }

    // A pointed crown with stones set round its band and pearls on its tips.
    static void Crown(MeshBuilder gold, MeshBuilder gems, MeshBuilder rubies)
    {
        const float k = 1.5f;
        var band = new[] { new Vector2(0.2f, 0), new Vector2(0.2f, 0.12f), new Vector2(0.185f, 0.12f), new Vector2(0.185f, 0), new Vector2(0.2f, 0) };
        gold.Lathe(Vector3.zero, Scale(band, k), 32);
        gold.Torus(new Vector3(0, 0.012f * k, 0), Quaternion.identity, 0.2f * k, 0.012f * k);
        var spike = new[] { new Vector2(0, 0), new Vector2(0.045f, 0), new Vector2(0, 0.15f) };
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4;
            Vector3 foot = new Vector3(Mathf.Cos(a) * 0.192f * k, 0.11f * k, Mathf.Sin(a) * 0.192f * k);
            gold.Lathe(foot, Scale(spike, k), 8);
            gems.Sphere(foot + Vector3.up * 0.155f * k, 0.022f * k);
            float b = a + Mathf.PI / 8;
            (i % 2 == 0 ? rubies : gems).Sphere(new Vector3(Mathf.Cos(b) * 0.203f * k, 0.06f * k, Mathf.Sin(b) * 0.203f * k), 0.022f * k);
        }
    }

    static Vector2[] Scale(Vector2[] profile, float k)
    {
        var scaled = new Vector2[profile.Length];
        for (int i = 0; i < profile.Length; i++) scaled[i] = profile[i] * k;
        return scaled;
    }

    // ---------- Cutscene ----------

    static void BuildCutscene(ShallowsFishBoss boss, Transform cave)
    {
        var root = Node(CutsceneName, boss.transform);
        var cutscene = root.gameObject.AddComponent<GuardianIntroCutscene>();
        cutscene.boss = boss;
        Vector3 fish = FishCentre(boss), offering = boss.offering.position;
        Vector3 into = cave.forward, side = cave.right;
        float sand = Ground(fish);
        Vector3 mouth = fish + into * 1 + Vector3.up * 0.4f;
        Vector3 hoard = new Vector3(fish.x, sand + 1.6f, fish.z);
        Vector3 away = offering - fish;
        away.y = 0;
        away.Normalize();
        Vector3 between = Vector3.Lerp(offering, fish, 0.5f) + Vector3.up * 1.5f;
        cutscene.shots = new[]
        {
            Shot(root, 1, 4.5f, fish - into * 30 + side * 10 + Vector3.up * 5, mouth, fish - into * 20 + side * 5 + Vector3.up * 2.5f, mouth,
                "Something vast stirs in the mouth of the cave."),
            Shot(root, 2, 4.5f, Flat(fish - into * 11 + side * 5, sand + 1.1f), hoard, Flat(fish - into * 8.5f + side * 3, sand + 1.5f), hoard,
                "An ancient arapaima, coiled over a hoard of gold. It lets nothing past."),
            Shot(root, 3, 5f, offering + away * 7 + Vector3.up * 3.5f, offering, offering + away * 10 + Vector3.up * 6, between,
                "It covets treasure. Bring four pieces to the stone circle."),
        };
        cutscene.closingTip = "Use your SCANNER to find shiny treasure: its sweep makes nearby treasure glint.";
    }

    static Vector3 Flat(Vector3 p, float y) => new Vector3(p.x, y, p.z);

    static GuardianIntroCutscene.Shot Shot(Transform root, int number, float seconds, Vector3 from, Vector3 fromLook, Vector3 to, Vector3 toLook, string caption)
    {
        return new GuardianIntroCutscene.Shot
        {
            from = Marker(root, "Shot " + number + " from", from, fromLook),
            to = Marker(root, "Shot " + number + " to", to, toLook),
            seconds = seconds,
            caption = caption,
        };
    }

    // A camera marker kept above the sand and clear of rocks, aimed at its target.
    static Transform Marker(Transform root, string name, Vector3 position, Vector3 lookAt)
    {
        position.y = Mathf.Max(position.y, Ground(position) + 1.2f);
        for (int i = 0; i < 12 && Physics.CheckSphere(position, 0.8f, ~0, QueryTriggerInteraction.Ignore); i++)
            position.y += 0.5f;
        var marker = Node(name, root);
        marker.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position));
        return marker;
    }

    // ---------- Previews ----------

    static void Capture()
    {
        var camera = Camera.main;
        if (camera == null) return;
        var boss = UnityEngine.Object.FindFirstObjectByType<ShallowsFishBoss>();
        var cave = GameObject.Find("NE cave - entrance faces southwest").transform;
        Directory.CreateDirectory("Previews");
        var cutscene = boss.GetComponentInChildren<GuardianIntroCutscene>();
        for (int i = 0; i < cutscene.shots.Length; i++)
        {
            Shot(camera, cutscene.shots[i].from, "Cutscene " + (i + 1) + " start");
            Shot(camera, cutscene.shots[i].to, "Cutscene " + (i + 1) + " end");
        }
        Vector3 fish = FishCentre(boss);
        var look = new GameObject("Preview camera").transform;
        look.position = new Vector3(fish.x, Ground(fish) + 2.2f, fish.z) - cave.forward * 9 + cave.right * 1.5f;
        look.LookAt(cave.TransformPoint(new Vector3(0, 2.4f, 3)));
        Shot(camera, look, "Cave mouth - gold and darkness");
        look.position = new Vector3(fish.x, Ground(fish) + 1.1f, fish.z) - cave.forward * 3 - cave.right * 4.5f;
        look.LookAt(new Vector3(fish.x, Ground(fish) + 0.8f, fish.z));
        Shot(camera, look, "Gold pile close");
        foreach (var treasure in boss.treasures)
        {
            Vector3 at = treasure.transform.position + Vector3.up * 0.3f;
            look.position = at + new Vector3(1.4f, 0.9f, -1.4f);
            look.LookAt(at);
            Shot(camera, look, "Treasure - " + treasure.transform.GetChild(0).name);
        }
        UnityEngine.Object.DestroyImmediate(look.gameObject);
    }

    static void Shot(Camera camera, Transform pose, string name)
    {
        camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
        var texture = new RenderTexture(1280, 720, 24);
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes("Previews/" + name + ".png", image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(texture);
    }

    // ---------- Mesh building ----------

    class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int> triangles = new List<int>();

        public bool IsEmpty => vertices.Count == 0;

        public int Vertex(Vector3 p) { vertices.Add(p); return vertices.Count - 1; }

        // Turns a profile of (radius, height) points round the vertical axis. Trace it anticlockwise round the solid
        // (up the outside, back down any inside) and the faces point outward.
        public void Lathe(Vector3 centre, Vector2[] profile, int segments)
        {
            int first = vertices.Count;
            foreach (var point in profile)
                for (int j = 0; j <= segments; j++)
                {
                    float a = j * Mathf.PI * 2 / segments;
                    Vertex(centre + new Vector3(Mathf.Cos(a) * point.x, point.y, Mathf.Sin(a) * point.x));
                }
            for (int i = 0; i < profile.Length - 1; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a = first + i * (segments + 1) + j, b = a + segments + 1;
                    Quad(b, b + 1, a + 1, a);
                }
        }

        // A ring, or an arc of one between two angles, lying flat before rotation.
        public void Torus(Vector3 centre, Quaternion rotation, float major, float minor, float from = 0, float to = Mathf.PI * 2)
        {
            const int segments = 20, sides = 8;
            int first = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float u = Mathf.Lerp(from, to, i / (float)segments);
                for (int j = 0; j <= sides; j++)
                {
                    float v = j * Mathf.PI * 2 / sides;
                    var outward = new Vector3(Mathf.Cos(u) * Mathf.Cos(v), Mathf.Sin(v), Mathf.Sin(u) * Mathf.Cos(v));
                    Vertex(centre + rotation * (new Vector3(Mathf.Cos(u), 0, Mathf.Sin(u)) * major + outward * minor));
                }
            }
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = first + i * (sides + 1) + j, b = a + sides + 1;
                    Quad(a + 1, b + 1, b, a);
                }
        }

        // Corners in clockwise order as seen from the front, Unity's front-face winding.
        public void Quad(int a, int b, int c, int d) { triangles.AddRange(new[] { a, b, c, a, c, d }); }

        public void Append(MeshBuilder other, Matrix4x4 place)
        {
            int start = vertices.Count;
            foreach (var p in other.vertices) vertices.Add(place.MultiplyPoint3x4(p));
            foreach (int index in other.triangles) triangles.Add(start + index);
        }

        public void Append(MeshBuilder other)
        {
            int start = vertices.Count;
            vertices.AddRange(other.vertices);
            foreach (int index in other.triangles) triangles.Add(start + index);
        }

        // A flat ten-sided disc; caps and rim get their own vertices so the coin keeps crisp edges.
        public void Coin(Vector3 centre, Quaternion rotation, float radius)
        {
            const int sides = 10;
            const float half = 0.0125f;
            for (int cap = -1; cap <= 1; cap += 2)
            {
                int middle = Vertex(centre + rotation * new Vector3(0, cap * half, 0));
                int first = vertices.Count;
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides;
                    Vertex(centre + rotation * new Vector3(Mathf.Cos(a) * radius, cap * half, Mathf.Sin(a) * radius));
                }
                for (int i = 0; i < sides; i++)
                {
                    int a = first + i, b = first + (i + 1) % sides;
                    triangles.AddRange(cap > 0 ? new[] { middle, b, a } : new[] { middle, a, b });
                }
            }
            int rim = vertices.Count;
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                Vertex(centre + rotation * new Vector3(Mathf.Cos(a) * radius, -half, Mathf.Sin(a) * radius));
                Vertex(centre + rotation * new Vector3(Mathf.Cos(a) * radius, half, Mathf.Sin(a) * radius));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = rim + i * 2, b = rim + (i + 1) % sides * 2;
                Quad(a, a + 1, b + 1, b);
            }
        }

        public void Box(Vector3 centre, Quaternion rotation, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int axis = 0; axis < 3; axis++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 n = axes[axis] * sign, u = axes[(axis + 1) % 3], v = axes[(axis + 2) % 3];
                    int first = vertices.Count;
                    foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
                        Vertex(centre + rotation * Vector3.Scale(n + u * corner.x + v * corner.y, h));
                    if (sign > 0) Quad(first, first + 1, first + 2, first + 3);
                    else Quad(first, first + 3, first + 2, first + 1);
                }
        }

        public void Sphere(Vector3 centre, float radius)
        {
            const int rings = 6, segments = 8;
            int first = vertices.Count;
            for (int i = 0; i <= rings; i++)
                for (int j = 0; j <= segments; j++)
                {
                    float a = Mathf.PI * i / rings, b = Mathf.PI * 2 * j / segments;
                    Vertex(centre + new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)) * radius);
                }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a = first + i * (segments + 1) + j, b = a + segments + 1;
                    Quad(a, a + 1, b + 1, b);
                }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
