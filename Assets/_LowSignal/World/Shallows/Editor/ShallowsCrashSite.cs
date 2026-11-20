using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// The robot's crashed scout ship, ahead and to the left of where the player starts in the Shallows, so it is in view
/// as the game begins without being in the way. A white delta-winged ship about six metres long, it lies nose-down in
/// a mound of shoved-up sand, rolled so its top faces the start: dented and scorched, a gash torn in its flank, one
/// wing snapped off (it lies along the skid trail), the other bent, the dorsal hatch open where the robot climbed out
/// (the hatch lies nearby), a blinking beacon on the tail fin, one engine still sputtering and the other leaking
/// bubbles. Runs by itself once per version when the Shallows is open; afterwards use Low Signal > Shallows >
/// Rebuild crashed ship. Meshes and materials are kept as assets.
/// </summary>
[InitializeOnLoad]
public static class ShallowsCrashSite
{
    const string ScenePath = "Assets/_LowSignal/Scenes/Shallows.unity";
    const string AssetRoot = "Assets/_LowSignal/World/Shallows/Dressing/Crash site";
    const string SandLayerPath = "Assets/_LowSignal/World/Shallows/Textures/Sand/Sand.terrainlayer";
    const string BubbleMaterialPath = "Assets/_LowSignal/World/Shallows/Materials/Plankton.mat";
    const string RootName = "Crash site - scout ship";
    // Earlier versions of the crash site, replaced when this one is built.
    static readonly string[] OldRootNames = { "Crash site - escape pod" };
    static readonly string[] OldAssets =
    {
        "Escape pod hull.asset", "Escape pod scorched.asset", "Escape pod livery.asset", "Escape pod viewport.asset",
        "Escape pod beacon.asset", "Escape pod engine glow.asset",
        "Pod hull.mat", "Pod scorched.mat", "Pod livery.mat", "Pod viewport.mat", "Pod beacon.mat", "Pod engine glow.mat",
    };
    // How steeply the ship dives into the sand, how deep its nose is buried, and its length from tail to nose.
    const float Pitch = 16f, NoseDepth = 0.35f, Length = 6f;

    static System.Random random;
    static Terrain terrain;
    static float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
    static float Ground(Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;

    static ShallowsCrashSite()
    {
        EditorApplication.delayCall += () => AddIfMissing(SceneManager.GetActiveScene());
        EditorSceneManager.sceneOpened += (scene, mode) => AddIfMissing(scene);
    }

    // Bump when the ship changes, so an older crash site in the scene is replaced automatically once.
    const int BuildVersion = 6;
    static string AddedKey => "LowSignal.ShallowsCrashSiteAdded.v" + BuildVersion + "." + Application.dataPath;

    static void AddIfMissing(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || scene.path != ScenePath)
            return;
        // Only ever add (or update) it automatically once per version, so deleting it by hand sticks.
        if (EditorPrefs.GetBool(AddedKey, false))
            return;
        try
        {
            RemoveOld(scene);
            Build(scene);
            EditorPrefs.SetBool(AddedKey, true);
            Debug.Log("Crashed scout ship added to the Shallows, ahead and left of the player's start. Save the scene to keep it.");
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    [MenuItem("Low Signal/Shallows/Rebuild crashed ship")]
    static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("Exit Play mode first."); return; }
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) { Debug.LogError("Open the Shallows scene first."); return; }
        RemoveOld(scene);
        Build(scene);
    }

    static void RemoveOld(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == RootName || Array.IndexOf(OldRootNames, root.name) >= 0)
                Undo.DestroyObjectImmediate(root);
        foreach (string asset in OldAssets)
            if (AssetDatabase.LoadMainAssetAtPath(AssetRoot + "/" + asset) != null)
                AssetDatabase.DeleteAsset(AssetRoot + "/" + asset);
    }

    // ---------- Placing ----------

    static void Build(Scene scene)
    {
        terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null) throw new Exception("The Shallows has no terrain to crash into.");
        Transform player = FindPlayer();
        if (player == null) throw new Exception("Couldn't find the Player in the Shallows.");
        random = new System.Random(4207);
        if (!AssetDatabase.IsValidFolder(AssetRoot)) AssetDatabase.CreateFolder("Assets/_LowSignal/World/Shallows/Dressing", "Crash site");
        Physics.SyncTransforms();

        Vector3 spawn = player.position;
        Vector3 forward = Flat(player.forward), right = Flat(player.right);
        Vector3 entry = FindClearSpot(spawn, forward, right, player);
        entry.y = Ground(entry);
        Vector3 travel = Travel(spawn, forward, entry);

        var root = new GameObject(RootName).transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Build crash site");
        root.position = entry;

        Material hull = LitMaterial("Ship hull", new Color(0.9f, 0.9f, 0.88f), 0.25f, 0.55f, Color.black);
        Material scorched = LitMaterial("Ship scorched", new Color(0.075f, 0.07f, 0.065f), 0.4f, 0.25f, Color.black);
        Material livery = LitMaterial("Ship livery", new Color(0.4f, 0.42f, 0.44f), 0.45f, 0.4f, Color.black);
        Material canopy = LitMaterial("Ship canopy", new Color(0.025f, 0.05f, 0.06f), 0f, 0.72f, new Color(0.05f, 0.22f, 0.28f) * 0.5f);
        Material beacon = LitMaterial("Ship beacon", new Color(0.5f, 0.05f, 0.04f), 0f, 0.8f, new Color(1f, 0.08f, 0.04f) * 6f);
        Material engine = LitMaterial("Ship engine glow", new Color(0.3f, 0.12f, 0.02f), 0f, 0.3f, new Color(1f, 0.45f, 0.12f) * 4f);

        Material skin = LitMaterial("Ship skin", Color.white, 0.25f, 0.55f, Color.black);
        skin.SetTexture("_BaseMap", SkinTexture());
        EditorUtility.SetDirty(skin);

        Transform ship = BuildShip(root, entry, travel, spawn, skin, hull, scorched, livery, canopy, beacon, engine);
        BuildSand(root, entry, travel);
        BuildDebris(root, entry, travel, spawn, hull, scorched, livery);
        BuildBubbles(ship, new Vector3(-0.1f, -0.65f, -EngineOffset));

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
    }

    static Transform FindPlayer()
    {
        var swimmer = Object.FindFirstObjectByType<UnderwaterSwimController>();
        if (swimmer != null) return swimmer.transform;
        var named = GameObject.Find("Player");
        return named != null ? named.transform : null;
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0;
        return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized;
    }

    // The ship skidded in crossing the view from the start, heading right and slightly away, so from the spawn its
    // whole length shows and its skid trail runs off to the left, away from the player's path.
    static Vector3 Travel(Vector3 spawn, Vector3 forward, Vector3 spot)
    {
        Vector3 toSpot = Flat(spot - spawn);
        Vector3 across = Vector3.Cross(Vector3.up, toSpot);
        if (Vector3.Dot(across, forward) < 0) across = -across;
        return (across + toSpot * 0.25f).normalized;
    }

    // Ahead and to the left of the start, clear of rocks: inside the opening camera's view, but off the player's path.
    // The mirror-image spots on the right are only a fallback.
    static Vector3 FindClearSpot(Vector3 spawn, Vector3 forward, Vector3 right, Transform player)
    {
        var spots = new List<Vector2>
        {
            new Vector2(10f, -5.5f), new Vector2(11f, -6.5f), new Vector2(9f, -6f), new Vector2(12f, -4.5f),
            new Vector2(13f, -7.5f), new Vector2(8f, -7f), new Vector2(14f, -5f),
        };
        int left = spots.Count;
        for (int i = 0; i < left; i++) spots.Add(new Vector2(spots[i].x, -spots[i].y));
        foreach (var offset in spots)
        {
            Vector3 spot = spawn + forward * offset.x + right * offset.y;
            spot.y = Ground(spot);
            if (IsClear(spot, Travel(spawn, forward, spot), player)) return spot;
        }
        Debug.LogWarning("No rock-free spot ahead of the start; the crashed ship may overlap scenery.");
        Vector3 fallback = spawn + forward * 10f - right * 5.5f;
        fallback.y = Ground(fallback);
        return fallback;
    }

    // Checks round the nose and round the tail and wings, which reach several metres back along the skid.
    static bool IsClear(Vector3 spot, Vector3 travel, Transform player)
    {
        foreach (Vector3 centre in new[] { spot + Vector3.up * 1.2f, spot - travel * 3.5f + Vector3.up * 1.5f })
            foreach (Collider hit in Physics.OverlapSphere(centre, 3.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit is TerrainCollider || hit.transform.IsChildOf(player)) continue;
                // Invisible limits and ceilings don't count as scenery.
                if (hit.GetComponent<Renderer>() == null && hit.GetComponentInChildren<Renderer>() == null) continue;
                return false;
            }
        return true;
    }

    // ---------- The ship ----------

    // The ship's own frame: +Y runs from tail to nose, +X is its top, the wings reach out along Z.
    // Fuselage cross-sections from tail to nose: (distance along the ship, half width, half height, how far the
    // centre drops so the nose droops).
    static readonly Vector4[] Sections =
    {
        new Vector4(0f, 0.72f, 0.48f, 0f), new Vector4(0.35f, 0.92f, 0.62f, 0f), new Vector4(1.4f, 1.0f, 0.7f, 0f),
        new Vector4(2.8f, 0.98f, 0.68f, 0f), new Vector4(3.9f, 0.78f, 0.55f, -0.05f), new Vector4(4.9f, 0.48f, 0.36f, -0.12f),
        new Vector4(5.6f, 0.2f, 0.16f, -0.18f), new Vector4(Length, 0f, 0f, -0.2f),
    };
    const float EngineOffset = 0.62f;

    static Vector4 SectionAt(float y)
    {
        if (y <= Sections[0].x) return Sections[0];
        for (int i = 1; i < Sections.Length; i++)
            if (y <= Sections[i].x)
                return Vector4.Lerp(Sections[i - 1], Sections[i], Mathf.InverseLerp(Sections[i - 1].x, Sections[i].x, y));
        return Sections[Sections.Length - 1];
    }

    // A point on the fuselage's skin, a rounded-box cross-section: angle 0 is the top, 90 degrees the +Z side.
    static Vector3 Skin(float y, float degrees, float scale = 1)
    {
        Vector4 s = SectionAt(y);
        float a = degrees * Mathf.Deg2Rad;
        float F(float t) => Mathf.Sign(t) * Mathf.Pow(Mathf.Abs(t), 0.77f);
        return new Vector3(s.w + s.z * scale * F(Mathf.Cos(a)), y, s.y * scale * F(Mathf.Sin(a)));
    }

    // Straight out from the fuselage's centre line at a point: near enough the skin's normal for decals.
    static Vector3 Outward(Vector3 p) => new Vector3(p.x - SectionAt(p.y).w, 0, p.z).normalized;

    // The canopy's shape along the ship: (distance along the ship, half width, height above its base). It rises
    // steeply at the back, runs level over the seat and slopes down to the nose as a windscreen.
    static readonly Vector3[] CanopyProfile =
    {
        new Vector3(3.3f, 0.12f, 0f), new Vector3(3.5f, 0.32f, 0.17f), new Vector3(3.8f, 0.4f, 0.27f), new Vector3(4.15f, 0.38f, 0.27f),
        new Vector3(4.45f, 0.32f, 0.2f), new Vector3(4.75f, 0.2f, 0.09f), new Vector3(4.98f, 0.06f, 0f),
    };
    // Where the panes meet: rows along the ship and columns round the canopy, in degrees from the top.
    static readonly float[] CanopyRows = { 3.3f, 3.5f, 3.8f, 4.15f, 4.45f, 4.75f, 4.98f };
    static readonly float[] CanopyColumns = { -90f, -50f, -15f, 15f, 50f, 90f };

    // A point on the canopy, lifted off its surface by s. Its base sits a little under the hull's top so the edges
    // are sunk into the skin.
    static Vector3 Canopy(float y, float degrees, float s)
    {
        Vector3 shape = CanopyProfile[CanopyProfile.Length - 1];
        for (int i = 1; i < CanopyProfile.Length; i++)
            if (y <= CanopyProfile[i].x)
            {
                shape = Vector3.Lerp(CanopyProfile[i - 1], CanopyProfile[i], Mathf.InverseLerp(CanopyProfile[i - 1].x, CanopyProfile[i].x, y));
                break;
            }
        float a = degrees * Mathf.Deg2Rad, floor = Skin(y, 0).x - 0.07f;
        return new Vector3(floor + (shape.z + s) * Mathf.Cos(a), y, (shape.y + s) * Mathf.Sin(a));
    }

    // A raised frame bar on the canopy, covering a patch from one distance to another and one angle to another.
    // Built from small blocks that follow the curve, each standing just proud of the glass.
    static void CanopyFrame(MeshBuilder builder, float y0, float y1, float a0, float a1)
    {
        int rows = Mathf.Max(1, Mathf.CeilToInt((y1 - y0) / 0.06f)), columns = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / 10f));
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < columns; j++)
            {
                float ya = Mathf.Lerp(y0, y1, i / (float)rows), yb = Mathf.Lerp(y0, y1, (i + 1) / (float)rows);
                float aa = Mathf.Lerp(a0, a1, j / (float)columns), ab = Mathf.Lerp(a0, a1, (j + 1) / (float)columns);
                // Corners numbered (out from the glass) + 2 (along the ship) + 4 (round toward +Z), matching X, Y, Z.
                var c = new Vector3[8];
                for (int k = 0; k < 8; k++)
                    c[k] = Canopy((k & 2) == 0 ? ya : yb, (k & 4) == 0 ? aa : ab, (k & 1) == 0 ? -0.01f : 0.022f);
                builder.Hexa(c);
            }
    }

    static Transform BuildShip(Transform root, Vector3 entry, Vector3 travel, Vector3 spawn, Material skinMaterial,
        Material hull, Material scorched, Material livery, Material canopyMaterial, Material beacon, Material engine)
    {
        // Rolled so the top, with its open hatch and canopy, faces up and toward the start.
        Vector3 axis = (travel * Mathf.Cos(Pitch * Mathf.Deg2Rad) - Vector3.up * Mathf.Sin(Pitch * Mathf.Deg2Rad)).normalized;
        Vector3 toward = (Flat(spawn - entry) + Vector3.up * 2f).normalized;
        Vector3 top = (toward - axis * Vector3.Dot(toward, axis)).normalized;
        Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(top, axis), axis);
        float buried = NoseDepth / Mathf.Sin(Pitch * Mathf.Deg2Rad);
        Vector3 nose = entry + axis * buried;

        var ship = new GameObject("Scout ship").transform;
        ship.SetParent(root, false);
        ship.SetPositionAndRotation(nose - axis * Length, rotation);

        var fuselage = new MeshBuilder();
        var body = new MeshBuilder();
        var dark = new MeshBuilder();
        var stripe = new MeshBuilder();
        var glass = new MeshBuilder();

        // Fuselage. Its stripes, blackened nose and scorch streaks are painted into its texture (see SkinTexture),
        // so they follow the curved skin smoothly.
        fuselage.Loft(0, Length, 1, 72, true);

        // Delta wings, low on the fuselage. The +Z wing snapped off halfway out (the rest lies along the trail);
        // the other is bent down at the tip.
        var wingRoot = new WingEnd(0.8f, 0.25f, 3.4f, -0.25f, 0.14f);
        var wingTip = new WingEnd(2.9f, 0.1f, 1.0f, -0.75f, 0.05f);
        body.Wing(-1, wingRoot, wingTip);
        stripe.Wing(-1, WingEnd.Lerp(wingRoot, wingTip, 0.86f, 0.012f), WingEnd.Lerp(wingTip, wingTip, 0, 0.012f));
        var snapped = WingEnd.Lerp(wingRoot, new WingEnd(2.9f, 0.1f, 1.0f, -0.4f, 0.05f), 0.48f, 0);
        body.Wing(1, wingRoot, snapped);
        dark.Box(new Vector3(snapped.x, (snapped.trail + snapped.lead) / 2, snapped.z + 0.01f), Quaternion.identity,
            new Vector3(snapped.thick + 0.03f, snapped.lead - snapped.trail, 0.04f));

        // Tail fin on top, carrying the beacon, with a grey tip.
        body.Hexa(new[]
        {
            new Vector3(0.4f, 0.05f, -0.05f), new Vector3(1.55f, 0.2f, -0.03f), new Vector3(0.4f, 1.8f, -0.05f), new Vector3(1.55f, 0.8f, -0.03f),
            new Vector3(0.4f, 0.05f, 0.05f), new Vector3(1.55f, 0.2f, 0.03f), new Vector3(0.4f, 1.8f, 0.05f), new Vector3(1.55f, 0.8f, 0.03f),
        });
        stripe.Hexa(new[]
        {
            new Vector3(1.3f, 0.17f, -0.036f), new Vector3(1.56f, 0.2f, -0.034f), new Vector3(1.3f, 1.0f, -0.036f), new Vector3(1.56f, 0.8f, -0.034f),
            new Vector3(1.3f, 0.17f, 0.036f), new Vector3(1.56f, 0.2f, 0.034f), new Vector3(1.3f, 1.0f, 0.036f), new Vector3(1.56f, 0.8f, 0.034f),
        });
        Vector3 beaconAt = new Vector3(1.62f, 0.45f, 0);

        // Twin engines under the tail. The +Z one still sputters; the other is dead and leaking.
        foreach (float side in new[] { 1f, -1f })
        {
            Vector3 centre = new Vector3(-0.1f, 0, side * EngineOffset);
            body.Lathe(centre, new[] { new Vector2(0, -0.25f), new Vector2(0.3f, -0.25f), new Vector2(0.34f, -0.05f), new Vector2(0.34f, 1.4f), new Vector2(0.26f, 1.75f), new Vector2(0, 1.85f) }, 28);
            dark.Lathe(centre, new[] { new Vector2(0.24f, -0.6f), new Vector2(0.3f, -0.6f), new Vector2(0.26f, -0.35f), new Vector2(0.2f, -0.22f), new Vector2(0.16f, -0.22f), new Vector2(0.2f, -0.35f), new Vector2(0.24f, -0.6f) }, 28);
            stripe.Lathe(centre, new[] { new Vector2(0.345f, 0.15f), new Vector2(0.345f, 0.32f) }, 28);
            if (side < 0) dark.Lathe(centre, new[] { new Vector2(0, -0.28f), new Vector2(0.2f, -0.28f) }, 20);
        }

        // The open hatch on top the robot climbed out of: a dark hole in a grey frame, with torn hinges.
        const float hatchY = 2.55f, hatchHalfLength = 0.42f, hatchHalfWidth = 0.32f;
        float hatchX = Skin(hatchY, 0).x;
        dark.Box(new Vector3(hatchX, hatchY, 0), Quaternion.identity, new Vector3(0.05f, hatchHalfLength * 2, hatchHalfWidth * 2));
        stripe.Box(new Vector3(hatchX + 0.012f, hatchY + hatchHalfLength + 0.03f, 0), Quaternion.identity, new Vector3(0.05f, 0.06f, hatchHalfWidth * 2 + 0.12f));
        stripe.Box(new Vector3(hatchX + 0.012f, hatchY - hatchHalfLength - 0.03f, 0), Quaternion.identity, new Vector3(0.05f, 0.06f, hatchHalfWidth * 2 + 0.12f));
        stripe.Box(new Vector3(hatchX, hatchY, hatchHalfWidth + 0.03f), Quaternion.identity, new Vector3(0.08f, hatchHalfLength * 2, 0.06f));
        stripe.Box(new Vector3(hatchX, hatchY, -hatchHalfWidth - 0.03f), Quaternion.identity, new Vector3(0.08f, hatchHalfLength * 2, 0.06f));
        dark.Box(new Vector3(hatchX + 0.05f, hatchY + 0.25f, hatchHalfWidth + 0.09f), Quaternion.Euler(20, 0, 0), new Vector3(0.1f, 0.1f, 0.05f));
        dark.Box(new Vector3(hatchX + 0.05f, hatchY - 0.25f, hatchHalfWidth + 0.09f), Quaternion.Euler(-15, 0, 0), new Vector3(0.1f, 0.1f, 0.05f));

        // The cockpit canopy: flat tinted panes faceted over the cockpit, a raised dark frame (two rails along the
        // top, two bows across it and a sill where it meets the hull), and one pane shattered out.
        for (int i = 0; i < CanopyRows.Length - 1; i++)
            for (int j = 0; j < CanopyColumns.Length - 1; j++)
            {
                bool shattered = i == 3 && j == 3;
                float s = shattered ? -0.05f : 0;
                float y0 = CanopyRows[i], y1 = CanopyRows[i + 1], a0 = CanopyColumns[j], a1 = CanopyColumns[j + 1];
                (shattered ? dark : glass).FlatQuad(Canopy(y1, a0, s), Canopy(y1, a1, s), Canopy(y0, a1, s), Canopy(y0, a0, s));
            }
        const float rail = 4.5f;
        foreach (float a in new[] { -15f, 15f })
            CanopyFrame(dark, CanopyRows[0], CanopyRows[CanopyRows.Length - 1], a - rail, a + rail);
        CanopyFrame(dark, CanopyRows[0], CanopyRows[CanopyRows.Length - 1], 80f, 93f);
        CanopyFrame(dark, CanopyRows[0], CanopyRows[CanopyRows.Length - 1], -93f, -80f);
        foreach (float y in new[] { CanopyRows[2], CanopyRows[4] })
            CanopyFrame(dark, y - 0.028f, y + 0.028f, -90f, 90f);

        // Snapped landing struts under the belly.
        dark.Box(Skin(3.6f, 180) + new Vector3(-0.2f, 0.1f, 0.15f), Quaternion.Euler(0, 0, 35) * Quaternion.Euler(20, 0, 0), new Vector3(0.45f, 0.08f, 0.08f));
        dark.Box(Skin(1.1f, 150) + new Vector3(-0.12f, 0, 0), Quaternion.Euler(0, 0, 10), new Vector3(0.28f, 0.1f, 0.1f));
        dark.Box(Skin(1.1f, 210) + new Vector3(-0.12f, 0, 0), Quaternion.Euler(0, 0, -15), new Vector3(0.22f, 0.1f, 0.1f));

        // Dents: the shell is pushed in smoothly at a few impact points (angle, distance along the ship, radius), and
        // everything on it moves with it so nothing floats.
        var dents = new List<Vector4>();
        foreach (var dent in new[] { new Vector3(-30f, 0.6f, 0.4f), new Vector3(70f, 2.0f, 0.45f), new Vector3(-100f, 2.6f, 0.5f), new Vector3(160f, 1.2f, 0.45f), new Vector3(-62f, 1.6f, 0.5f), new Vector3(120f, 3.4f, 0.4f) })
        {
            Vector3 at = Skin(dent.y, dent.x);
            dents.Add(new Vector4(at.x, at.y, at.z, dent.z));
        }
        Vector3 Dent(Vector3 p)
        {
            Vector3 radial = new Vector3(p.x - SectionAt(p.y).w, 0, p.z);
            if (radial.sqrMagnitude < 1e-4f) return p;
            float push = 0;
            foreach (var d in dents)
            {
                float t = (p - new Vector3(d.x, d.y, d.z)).magnitude / d.w;
                if (t < 1) push += d.w * 0.3f * (1 - t * t) * (1 - t * t);
            }
            return p - radial.normalized * push;
        }
        fuselage.Deform(Dent);
        body.Deform(Dent);
        dark.Deform(Dent);
        stripe.Deform(Dent);
        glass.Deform(Dent);

        var skinPart = Part(ship, "Fuselage", fuselage.ToMesh("Scout ship fuselage", true), skinMaterial);
        skinPart.AddComponent<MeshCollider>().sharedMesh = skinPart.GetComponent<MeshFilter>().sharedMesh;
        var hullPart = Part(ship, "Wings, fin and engines", body.ToMesh("Scout ship hull"), hull);
        hullPart.AddComponent<MeshCollider>().sharedMesh = hullPart.GetComponent<MeshFilter>().sharedMesh;
        Part(ship, "Scorched plating", dark.ToMesh("Scout ship scorched"), scorched);
        Part(ship, "Livery", stripe.ToMesh("Scout ship livery"), livery);
        Part(ship, "Canopy", glass.ToMesh("Scout ship canopy"), canopyMaterial);

        // The beacon: a red lamp on top of the tail fin and a light that blinks with it.
        var lamp = new MeshBuilder();
        lamp.Sphere(Vector3.zero, 0.09f);
        var beaconPart = Part(ship, "Distress beacon", lamp.ToMesh("Scout ship beacon"), beacon);
        beaconPart.isStatic = false;
        beaconPart.transform.localPosition = beaconAt;
        var beaconLight = new GameObject("Beacon light").AddComponent<Light>();
        beaconLight.transform.SetParent(beaconPart.transform, false);
        beaconLight.transform.localPosition = Vector3.right * 0.2f;
        beaconLight.type = LightType.Point;
        beaconLight.color = new Color(1f, 0.12f, 0.06f);
        beaconLight.range = 7f;
        beaconLight.intensity = 3.5f;
        beaconLight.shadows = LightShadows.None;
        Pulse(beaconPart, beaconLight, beaconPart.GetComponent<Renderer>(), new Color(1f, 0.08f, 0.04f) * 6f, true);

        // The live engine, sputtering inside its bell.
        var disc = new MeshBuilder();
        disc.Lathe(new Vector3(-0.1f, 0, EngineOffset), new[] { new Vector2(0f, -0.28f), new Vector2(0.2f, -0.28f) }, 20);
        var enginePart = Part(ship, "Engine glow", disc.ToMesh("Scout ship engine glow"), engine);
        enginePart.isStatic = false;
        var engineLight = new GameObject("Engine light").AddComponent<Light>();
        engineLight.transform.SetParent(enginePart.transform, false);
        engineLight.transform.localPosition = new Vector3(-0.1f, -0.9f, EngineOffset);
        engineLight.type = LightType.Point;
        engineLight.color = new Color(1f, 0.5f, 0.18f);
        engineLight.range = 4f;
        engineLight.intensity = 1.8f;
        engineLight.shadows = LightShadows.None;
        Pulse(enginePart, engineLight, enginePart.GetComponent<Renderer>(), new Color(1f, 0.45f, 0.12f) * 4f, false);

        return ship;
    }

    // The fuselage's paint, wrapped round it: across is the angle round the ship (0 and 1 on top), up is the distance
    // from tail to nose. White, with two grey stripes along the top, a nose blackened by the fall with a ragged
    // edge, soot streaks swept back from it, and a few soft scorch blotches.
    // 0 below edge0, 1 above edge1, easing between (a shader's smoothstep; Mathf.SmoothStep blends values instead).
    static float Smooth(float edge0, float edge1, float x) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(edge0, edge1, x));

    static Texture2D SkinTexture()
    {
        const int size = 512;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Ship skin paint", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
        var white = new Color(0.92f, 0.92f, 0.9f);
        var trim = new Color(0.42f, 0.44f, 0.47f);
        var soot = new Color(0.07f, 0.065f, 0.06f);
        var pixels = new Color[size * size];
        // Noise that wraps seamlessly round the ship, by sampling round a circle.
        float Wrapped(float u, float v, float around, float along, float seed)
        {
            float a = u * Mathf.PI * 2;
            return 0.5f * (Mathf.PerlinNoise(Mathf.Cos(a) * around + seed, v * along + seed * 0.7f)
                + Mathf.PerlinNoise(Mathf.Sin(a) * around + seed * 1.3f, v * along - seed));
        }
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                float u = (i + 0.5f) / size, v = (j + 0.5f) / size, y = v * Length;
                float degrees = u * 360f;
                Color c = white;
                // Stripes 28 degrees either side of the top, from just ahead of the tail to the cockpit.
                float off = Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(degrees, 28f)), Mathf.Abs(Mathf.DeltaAngle(degrees, -28f)));
                float stripe = (1 - Smooth(3.2f, 4.2f, off)) * Smooth(0.35f, 0.45f, y) * (1 - Smooth(3.8f, 3.95f, y));
                c = Color.Lerp(c, trim, stripe);
                // The nose, burnt black back to a ragged line.
                float edge = 4.75f + (Wrapped(u, v, 3f, 2f, 11f) - 0.5f) * 0.9f;
                float nose = Smooth(edge - 0.15f, edge + 0.1f, y);
                // Soot streaks dragged back from the nose, fading toward the tail.
                float streak = Smooth(0.56f, 0.7f, Wrapped(u, v, 9f, 1.2f, 37f)) * Smooth(2.2f, 4.6f, y) * 0.85f;
                // Soft scorch blotches anywhere.
                float blotch = Smooth(0.64f, 0.72f, Wrapped(u, v, 2.5f, 5f, 73f)) * 0.7f;
                c = Color.Lerp(c, soot, Mathf.Max(nose, Mathf.Max(streak, blotch)));
                pixels[j * size + i] = c;
            }
        texture.SetPixels(pixels);
        texture.Apply(true);
        string path = AssetRoot + "/Ship skin paint.asset";
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(texture, path);
        return texture;
    }

    // One end of a wing: how far out it is, where its trailing and leading edges are along the ship, how high it
    // sits, and how thick it is.
    struct WingEnd
    {
        public float z, trail, lead, x, thick;
        public WingEnd(float z, float trail, float lead, float x, float thick) { this.z = z; this.trail = trail; this.lead = lead; this.x = x; this.thick = thick; }
        public static WingEnd Lerp(WingEnd a, WingEnd b, float t, float extraThickness) => new WingEnd(
            Mathf.Lerp(a.z, b.z, t), Mathf.Lerp(a.trail, b.trail, t), Mathf.Lerp(a.lead, b.lead, t), Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.thick, b.thick, t) + extraThickness);
    }

    static void Pulse(GameObject host, Light light, Renderer glow, Color emission, bool blink)
    {
        var pulse = new SerializedObject(host.AddComponent<CrashBeacon>());
        pulse.FindProperty("pulsedLight").objectReferenceValue = light;
        pulse.FindProperty("glow").objectReferenceValue = glow;
        pulse.FindProperty("emission").colorValue = emission;
        pulse.FindProperty("blink").boolValue = blink;
        pulse.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------- Sand ----------

    // How far the sand mesh rises above the terrain at a point in the crash frame (z along the travel, x across it).
    // Below zero it sinks out of sight, so only the shoved-up mound and the skid's two ridges show.
    static float SandHeight(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float dome = 0.42f * (1 - Mathf.SmoothStep(0, 1, r / 2.4f));
        // A crescent of sand piled up ahead of the nose.
        float ahead = Mathf.Max(0, z / (r + 1e-3f));
        float crescent = 0.42f * Mathf.Exp(-(r - 1.3f) * (r - 1.3f) / 0.2f) * (0.25f + 0.75f * ahead);
        // Two low ridges either side of the skid behind the ship, fading out with distance.
        float fade = z < -0.6f ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-11f, -2f, z)) : 0;
        float ridges = 0.27f * Mathf.Exp(-(Mathf.Abs(x) - 1.2f) * (Mathf.Abs(x) - 1.2f) / 0.06f) * fade;
        float ripple = (Mathf.PerlinNoise(x * 1.7f + 40, z * 1.7f + 9) - 0.5f) * 0.06f;
        return Mathf.Max(dome + crescent, ridges) + ripple - 0.12f;
    }

    static void BuildSand(Transform root, Vector3 entry, Vector3 travel)
    {
        Vector3 across = Vector3.Cross(Vector3.up, travel);
        const float step = 0.15f, minX = -3f, maxX = 3f, minZ = -11.5f, maxZ = 3.2f;
        int columns = Mathf.RoundToInt((maxX - minX) / step) + 1, rows = Mathf.RoundToInt((maxZ - minZ) / step) + 1;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        Vector2 tile = Vector2.one * 4;
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(SandLayerPath);
        if (layer != null) tile = layer.tileSize;
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < columns; i++)
            {
                float x = minX + i * step, z = minZ + j * step;
                Vector3 world = entry + across * x + travel * z;
                world.y = Ground(world) + SandHeight(x, z);
                vertices.Add(world - entry);
                // Same tiling as the terrain's sand, in world space, so the mound blends into the floor.
                Vector3 local = world - terrain.transform.position;
                uvs.Add(new Vector2(local.x / tile.x, local.z / tile.y));
            }
        for (int j = 0; j < rows - 1; j++)
            for (int i = 0; i < columns - 1; i++)
            {
                int a = j * columns + i, b = a + 1, c = a + columns, d = c + 1;
                triangles.AddRange(new[] { a, c, d, a, d, b });
            }
        var mesh = new Mesh { name = "Crash site sand", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        var sand = Part(root, "Shoved-up sand", mesh, SandMaterial(layer));
        sand.AddComponent<MeshCollider>().sharedMesh = sand.GetComponent<MeshFilter>().sharedMesh;
    }

    static Material SandMaterial(TerrainLayer layer)
    {
        Material material = LitMaterial("Crash site sand", Color.white, 0f, 0.15f, Color.black);
        if (layer != null)
        {
            material.SetTexture("_BaseMap", layer.diffuseTexture);
            if (layer.normalMapTexture != null)
            {
                material.SetTexture("_BumpMap", layer.normalMapTexture);
                material.EnableKeyword("_NORMALMAP");
            }
        }
        else material.SetColor("_BaseColor", new Color(0.62f, 0.57f, 0.44f));
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------- Debris ----------

    static void BuildDebris(Transform root, Vector3 entry, Vector3 travel, Vector3 spawn, Material hull, Material scorched, Material livery)
    {
        Vector3 across = Vector3.Cross(Vector3.up, travel);
        var plates = new[] { new MeshBuilder(), new MeshBuilder(), new MeshBuilder() };
        Vector3 OnSand(float x, float z, float lift)
        {
            Vector3 p = entry + across * x + travel * z;
            p.y = Ground(p) + Mathf.Max(SandHeight(x, z), 0) + lift;
            return p - entry;
        }

        // Torn hull panels strewn along the skid, thicker near the ship.
        for (int i = 0; i < 22; i++)
        {
            float z = -Mathf.Lerp(1.8f, 13f, Mathf.Pow(R(0, 1), 1.4f));
            float x = R(-1, 1) * (0.8f + -z * 0.18f);
            Vector3 size = new Vector3(R(0.18f, 0.6f), R(0.02f, 0.035f), R(0.15f, 0.45f));
            Quaternion lie = Quaternion.Euler(R(-28, 28), R(0, 360), R(-28, 28));
            // Mostly white plating, some scorched, a few grey trim.
            int kind = random.Next(10);
            plates[kind < 6 ? 0 : kind < 9 ? 1 : 2].Box(OnSand(x, z, R(-0.04f, 0.05f)), lie, size);
        }
        // Small bent strips and bolts.
        for (int i = 0; i < 14; i++)
        {
            float z = -R(1.5f, 9f), x = R(-2f, 2f);
            plates[1].Box(OnSand(x, z, 0.01f), Quaternion.Euler(R(-15, 15), R(0, 360), R(-15, 15)), new Vector3(R(0.04f, 0.1f), 0.03f, R(0.1f, 0.35f)));
        }

        // The snapped-off half of the wing, nosed into the sand a few metres back, with its grey tip.
        Quaternion wingLie = Quaternion.Euler(R(14, 24), R(0, 360), R(-10, 10));
        Vector3 wingAt = OnSand(-2.1f, -4.8f, 0.1f);
        plates[0].Box(wingAt, wingLie, new Vector3(1.1f, 0.08f, 1.5f));
        plates[2].Box(wingAt + wingLie * new Vector3(0.6f, 0, -0.1f), wingLie, new Vector3(0.14f, 0.085f, 1.0f));
        plates[1].Box(wingAt + wingLie * new Vector3(-0.56f, 0, 0), wingLie * Quaternion.Euler(0, R(-10, 10), 0), new Vector3(0.05f, 0.1f, 1.45f));
        // A pair of spilled canisters.
        for (int i = 0; i < 2; i++)
        {
            var can = new MeshBuilder();
            can.Lathe(Vector3.zero, new[] { new Vector2(0, -0.2f), new Vector2(0.11f, -0.2f), new Vector2(0.12f, -0.17f), new Vector2(0.12f, 0.17f), new Vector2(0.06f, 0.2f), new Vector2(0, 0.21f) }, 16);
            Vector3 at = OnSand(i == 0 ? 1.7f : -2.1f, i == 0 ? -2.4f : -6.2f, 0.09f);
            (i == 0 ? plates[0] : plates[2]).Append(can, Matrix4x4.TRS(at, Quaternion.Euler(90, R(0, 360), 0), Vector3.one));
        }

        // The hatch the robot burst out of, lying on the sand between the ship and the start.
        Vector3 toStart = Flat(spawn - entry);
        float hx = Vector3.Dot(toStart, across) * 2.2f, hz = Vector3.Dot(toStart, travel) * 2.2f - 0.6f;
        Quaternion hatchLie = Quaternion.LookRotation(toStart) * Quaternion.Euler(-12, 25, 4);
        Vector3 hatchAt = OnSand(hx, hz, 0.06f);
        plates[0].Box(hatchAt, hatchLie, new Vector3(0.66f, 0.05f, 0.86f));
        plates[2].Box(hatchAt + hatchLie * new Vector3(0, 0.035f, 0), hatchLie, new Vector3(0.54f, 0.02f, 0.74f));
        plates[1].Box(hatchAt + hatchLie * new Vector3(0, 0.055f, 0.1f), hatchLie, new Vector3(0.12f, 0.03f, 0.25f));

        var debris = new GameObject("Debris").transform;
        debris.SetParent(root, false);
        Part(debris, "Hull panels", plates[0].ToMesh("Crash debris hull"), hull);
        Part(debris, "Scorched scraps", plates[1].ToMesh("Crash debris scorched"), scorched);
        Part(debris, "Livery scraps", plates[2].ToMesh("Crash debris livery"), livery);
    }

    // ---------- Bubbles ----------

    // A slow stream of bubbles leaking from the dead engine and rising toward the surface.
    static void BuildBubbles(Transform ship, Vector3 from)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(BubbleMaterialPath);
        var bubbles = new GameObject("Leaking bubbles");
        bubbles.transform.SetParent(ship, false);
        bubbles.transform.localPosition = from;
        bubbles.transform.rotation = Quaternion.LookRotation(Vector3.up);
        var system = bubbles.AddComponent<ParticleSystem>();
        var main = system.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.13f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.97f, 1f, 0.7f), new Color(1f, 1f, 1f, 0.45f));
        main.gravityModifier = -0.06f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var emission = system.emission;
        emission.rateOverTime = 9f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 12, 0, 3.5f) });
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.25f;
        var noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.6f;
        var fade = system.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var renderer = bubbles.GetComponent<ParticleSystemRenderer>();
        if (material != null) renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ---------- Assets ----------

    static GameObject Part(Transform parent, string name, Mesh mesh, Material material)
    {
        var part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.AddComponent<MeshFilter>().sharedMesh = SaveMesh(mesh);
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
        part.isStatic = true;
        return part;
    }

    static Material LitMaterial(string name, Color color, float metallic, float smoothness, Color emission)
    {
        string path = AssetRoot + "/" + name + ".mat";
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
        if (emission.maxColorComponent > 0)
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", emission);
        EditorUtility.SetDirty(material);
        return material;
    }

    // Overwrites an existing mesh asset in place so scene references keep pointing at it.
    static Mesh SaveMesh(Mesh mesh)
    {
        string path = AssetRoot + "/" + mesh.name + ".asset";
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
        if (mesh.uv.Length > 0) existing.SetUVs(0, mesh.uv);
        if (mesh.tangents.Length > 0) existing.SetTangents(mesh.tangents);
        existing.SetTriangles(mesh.triangles, 0);
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        return existing;
    }

    // ---------- Mesh building ----------

    class MeshBuilder
    {
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<int> triangles = new List<int>();

        readonly List<Vector2> uvs = new List<Vector2>();

        int Vertex(Vector3 p, Vector2 uv = default) { vertices.Add(p); uvs.Add(uv); return vertices.Count - 1; }

        public void Deform(Func<Vector3, Vector3> move)
        {
            for (int i = 0; i < vertices.Count; i++) vertices[i] = move(vertices[i]);
        }

        // The fuselage skin between two distances along the ship, rings every 6cm, optionally closed at the back.
        // Its rings run the same way round as a lathe's, so the faces point outward.
        public void Loft(float from, float to, float scale, int segments, bool closeBack)
        {
            int first = vertices.Count, rings = 0;
            if (closeBack)
            {
                Vector3 centre = new Vector3(SectionAt(from).w, from, 0);
                for (int j = 0; j <= segments; j++) Vertex(centre, new Vector2(j / (float)segments, from / Length));
                rings++;
            }
            int steps = Mathf.CeilToInt((to - from) / 0.06f);
            for (int i = 0; i <= steps; i++, rings++)
            {
                float y = Mathf.Lerp(from, to, i / (float)steps);
                for (int j = 0; j <= segments; j++) Vertex(Skin(y, j * 360f / segments, scale), new Vector2(j / (float)segments, y / Length));
            }
            for (int i = 0; i < rings - 1; i++)
                for (int j = 0; j < segments; j++)
                {
                    int a = first + i * (segments + 1) + j, b = a + segments + 1;
                    Quad(b, b + 1, a + 1, a);
                }
        }

        // A four-cornered panel with its own vertices, so it shades flat like a pane of glass. Corners clockwise as
        // seen from the front.
        public void FlatQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int first = Vertex(a);
            Vertex(b);
            Vertex(c);
            Vertex(d);
            Quad(first, first + 1, first + 2, first + 3);
        }

        // A box bent to fit eight corners, numbered x + 2y + 4z (each 0 or 1 for the low or high side of that axis).
        // Keep each corner pair in increasing order along its axis so the faces still point outward.
        public void Hexa(Vector3[] c)
        {
            var unit = new MeshBuilder();
            unit.Box(Vector3.zero, Quaternion.identity, Vector3.one * 2);
            int start = vertices.Count;
            foreach (var v in unit.vertices)
            {
                float fx = (v.x + 1) / 2, fy = (v.y + 1) / 2, fz = (v.z + 1) / 2;
                Vector3 near = Vector3.Lerp(Vector3.Lerp(c[0], c[1], fx), Vector3.Lerp(c[2], c[3], fx), fy);
                Vector3 far = Vector3.Lerp(Vector3.Lerp(c[4], c[5], fx), Vector3.Lerp(c[6], c[7], fx), fy);
                Vertex(Vector3.Lerp(near, far, fz));
            }
            foreach (int index in unit.triangles) triangles.Add(start + index);
        }

        // A wing from its root to its tip, on the +Z (side 1) or -Z (side -1) side of the ship.
        public void Wing(int side, WingEnd root, WingEnd tip)
        {
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                int ix = i & 1, iy = (i >> 1) & 1, iz = (i >> 2) & 1;
                // Z must increase with iz: on the -Z side the tip comes first.
                WingEnd end = (side > 0) == (iz == 0) ? root : tip;
                corners[i] = new Vector3(end.x + (ix == 0 ? -0.5f : 0.5f) * end.thick, iy == 0 ? end.trail : end.lead, side * end.z);
            }
            Hexa(corners);
        }

        // Corners in clockwise order as seen from the front, Unity's front-face winding.
        void Quad(int a, int b, int c, int d) { triangles.AddRange(new[] { a, b, c, a, c, d }); }

        // Turns a profile of (radius, height) points round the vertical axis. Trace it anticlockwise round the solid
        // (up the outside, back down any inside) and the faces point outward.
        public void Lathe(Vector3 centre, Vector2[] profile, int segments)
        {
            // Rings at least every 6cm along the profile, so dents pressed in later have vertices to move.
            var dense = new List<Vector2> { profile[0] };
            for (int i = 1; i < profile.Length; i++)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(profile[i - 1], profile[i]) / 0.06f));
                for (int k = 1; k <= steps; k++) dense.Add(Vector2.Lerp(profile[i - 1], profile[i], k / (float)steps));
            }
            profile = dense.ToArray();
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

        // A ring lying flat before rotation.
        public void Torus(Vector3 centre, Quaternion rotation, float major, float minor)
        {
            const int segments = 24, sides = 8;
            int first = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float u = i * Mathf.PI * 2 / segments;
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
            const int rings = 8, segments = 12;
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

        public void Append(MeshBuilder other, Matrix4x4 place)
        {
            int start = vertices.Count;
            foreach (var p in other.vertices) Vertex(place.MultiplyPoint3x4(p));
            foreach (int index in other.triangles) triangles.Add(start + index);
        }

        // With smoothSeams, vertices that sit in the same place share one normal, so a texture seam or a closed nose
        // shows no crease. Leave it off for boxes, whose hard edges need their own normals.
        public Mesh ToMesh(string name, bool smoothSeams = false)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            if (smoothSeams)
            {
                Vector3[] normals = mesh.normals;
                var shared = new Dictionary<Vector3Int, Vector3>();
                Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 10000f);
                for (int i = 0; i < vertices.Count; i++)
                {
                    shared.TryGetValue(Key(vertices[i]), out Vector3 sum);
                    shared[Key(vertices[i])] = sum + normals[i];
                }
                for (int i = 0; i < vertices.Count; i++) normals[i] = shared[Key(vertices[i])].normalized;
                mesh.normals = normals;
            }
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
