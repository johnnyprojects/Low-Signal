using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Adds the shallow-water effects to the Shallows scene, under a "Water Effects" group:
///  - Water Surface: the underside of the surface overhead, a light aqua sheet with slowly drifting ripples.
///    It never casts shadows, so it cannot block the sun.
///  - Drifting Plankton: tiny pale specks hanging in the water around the camera.
///  - Light Shafts: soft god rays slanting down from the surface along the sun, turning to face the camera.
/// Runs by itself once, when the Shallows scene is open and has no Water Effects yet; afterwards use
/// Low Signal > Shallows > Rebuild water effects. Save the scene to keep the result.
/// </summary>
[InitializeOnLoad]
public static class ShallowsWaterEffects
{
    private const string ScenePath = "Assets/_LowSignal/Scenes/Shallows.unity";
    private const string Root = "Assets/_LowSignal/World/Shallows";
    private const string GroupName = "Water Effects";
    // Well above the reef, so the surface and its light shafts feel distant from the swimmer.
    private const float SurfaceY = 24f;
    // The terrain runs from -80 to 80 on both axes, centred on the origin.
    private const float AreaRadius = 70f;
    // Far wider than the terrain; fog fades the surface out well before its edge.
    private const float SurfaceWidth = 600f;
    // Without caustics to follow, one ripple tile covers this much water (the original 180m surface at 12 tiles).
    private const float RippleTileSize = 15f;

    static ShallowsWaterEffects()
    {
        EditorApplication.delayCall += () => AddIfMissing(SceneManager.GetActiveScene());
        EditorSceneManager.sceneOpened += (scene, mode) => AddIfMissing(scene);
    }

    private static string AddedKey => "LowSignal.ShallowsWaterEffectsAdded." + Application.dataPath;

    private static void AddIfMissing(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || scene.path != ScenePath)
            return;
        // Only ever add them automatically once, so deleting the group by hand sticks.
        if (FindGroup(scene) != null || FindSurface(scene) != null || EditorPrefs.GetBool(AddedKey, false))
            return;
        Build(scene);
        EditorPrefs.SetBool(AddedKey, true);
        Debug.Log("Water effects added to the Shallows (surface, plankton, light shafts). Save the scene to keep them.");
    }

    [MenuItem("Low Signal/Shallows/Rebuild water effects")]
    private static void Rebuild()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError("Open the Shallows scene first.");
            return;
        }
        GameObject old = FindGroup(scene);
        // Effects moved out of their group would otherwise be left in place and doubled up.
        if (old == null && FindSurface(scene) != null)
        {
            Debug.LogError("The water effects have been moved out of the " + GroupName + " group. Delete them, or move them back into it, before rebuilding.");
            return;
        }
        if (old != null)
            Undo.DestroyObjectImmediate(old);
        Build(scene);
    }

    [MenuItem("Low Signal/Shallows/Move water surface to its set height")]
    private static void MoveSurfaceMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            Debug.LogError("Open the Shallows scene first.");
            return;
        }
        MoveSurface(scene);
    }

    public static void MoveSurfaceBatch()
    {
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            MoveSurface(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("SURFACE_PASS: water surface at " + SurfaceY);
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Lifts the surface to SurfaceY wherever the effects sit, lengthening each light shaft so it still reaches as far
    // down, and keeps an invisible ceiling just under it so the robot and camera stay in the water.
    private static void MoveSurface(Scene scene)
    {
        GameObject surface = FindSurface(scene);
        if (surface == null)
            throw new Exception("The Shallows has no water surface.");
        float delta = SurfaceY - surface.transform.position.y;
        Undo.RecordObject(surface.transform, "Move water surface");
        surface.transform.position += Vector3.up * delta;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (LightShafts shafts in root.GetComponentsInChildren<LightShafts>(true))
                foreach (Transform shaft in shafts.transform)
                {
                    Vector3 axis = shaft.up;
                    float length = shaft.localScale.y, longer = length + delta / Mathf.Max(0.2f, axis.y);
                    Vector3 top = shaft.position + axis * (length * 0.5f) + Vector3.up * delta;
                    Undo.RecordObject(shaft, "Move water surface");
                    shaft.localScale = new Vector3(shaft.localScale.x, longer, shaft.localScale.z);
                    shaft.position = top - axis * (longer * 0.5f);
                }
        Transform parent = surface.transform.parent;
        Transform ceiling = parent != null ? parent.Find("Surface limit (invisible)") : null;
        if (ceiling == null)
        {
            ceiling = new GameObject("Surface limit (invisible)").transform;
            ceiling.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(ceiling.gameObject, "Move water surface");
            ceiling.gameObject.AddComponent<BoxCollider>().size = new Vector3(SurfaceWidth, 1f, SurfaceWidth);
            ceiling.gameObject.isStatic = true;
        }
        Undo.RecordObject(ceiling, "Move water surface");
        ceiling.position = new Vector3(surface.transform.position.x, SurfaceY - 0.5f, surface.transform.position.z);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static GameObject FindGroup(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == GroupName)
                return root;
        return null;
    }

    // The surface wherever it sits in the hierarchy, so effects moved into another group still count as present.
    private static GameObject FindSurface(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Water Surface")
                    return t.gameObject;
        return null;
    }

    private static void Build(Scene scene)
    {
        ImportTextures();
        Texture2D ripples = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Water/WaterSurface_Ripples.png");
        Texture2D shaft = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Water/LightShaft.png");
        if (ripples == null || shaft == null)
        {
            Debug.LogError("Water effect textures are missing from " + Root + "/Textures/Water.");
            return;
        }
        Material surfaceMaterial = TransparentMaterial("Water Surface", "Universal Render Pipeline/Unlit",
            new Color(0.62f, 0.94f, 1f, 0.55f), ripples, false);
        Material shaftMaterial = TransparentMaterial("God Ray", "Universal Render Pipeline/Particles/Unlit",
            new Color(0.8f, 0.96f, 1f, 0.14f), shaft, true);
        // Unity's built-in soft dot; its name differs between versions.
        Texture2D speck = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-ParticleSystem.psd");
        if (speck == null)
            speck = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        Material planktonMaterial = TransparentMaterial("Plankton", "Universal Render Pipeline/Particles/Unlit",
            Color.white, speck, false);

        GameObject group = new GameObject(GroupName);
        SceneManager.MoveGameObjectToScene(group, scene);
        Undo.RegisterCreatedObjectUndo(group, "Add water effects");

        BuildSurface(group.transform, surfaceMaterial);
        BuildPlankton(group.transform, planktonMaterial);
        BuildLightShafts(group.transform, shaftMaterial);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    // ---------- Water surface ----------

    private static void BuildSurface(Transform group, Material material)
    {
        GameObject surface = GameObject.CreatePrimitive(PrimitiveType.Plane);
        surface.name = "Water Surface";
        Object.DestroyImmediate(surface.GetComponent<Collider>());
        surface.transform.SetParent(group, false);
        // Flipped to face down, toward the reef below.
        surface.transform.SetPositionAndRotation(new Vector3(0f, SurfaceY, 0f), Quaternion.Euler(180f, 0f, 0f));
        MeshRenderer renderer = surface.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        // The surface must never shade the reef from the sun.
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        surface.AddComponent<ScrollingTexture>();
        SizeSurface(surface);
    }

    [MenuItem("Low Signal/Shallows/Extend water surface")]
    private static void ExtendSurface()
    {
        GameObject surface = GameObject.Find("Water Surface");
        if (surface == null)
        {
            Debug.LogError("Open the Shallows scene with its water effects first.");
            return;
        }
        Undo.RecordObject(surface.transform, "Extend water surface");
        SizeSurface(surface);
        EditorSceneManager.MarkSceneDirty(surface.scene);
    }

    // Stretches the surface far past the reef so the fog hides its rim, keeping the ripples the same size.
    private static void SizeSurface(GameObject surface)
    {
        // The plane primitive is 10m across before scaling.
        surface.transform.localScale = new Vector3(SurfaceWidth / 10f, 1f, SurfaceWidth / 10f);
        ScrollingTexture scroller = surface.GetComponent<ScrollingTexture>();
        if (scroller == null)
            return;
        SerializedObject settings = new SerializedObject(scroller);
        settings.FindProperty("tiling").vector2Value = Vector2.one * (SurfaceWidth / RippleTileSize);
        settings.ApplyModifiedProperties();
    }

    // ---------- Plankton ----------

    private static void BuildPlankton(Transform group, Material material)
    {
        GameObject plankton = new GameObject("Drifting Plankton");
        plankton.transform.SetParent(group, false);
        plankton.transform.position = new Vector3(0f, 6f, 0f);
        plankton.AddComponent<FollowCamera>();
        ParticleSystem system = plankton.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 1f, 0.96f, 0.55f), new Color(1f, 0.97f, 0.88f, 0.4f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1400;
        // A whisper of buoyancy, so the specks hang rather than sink.
        main.gravityModifier = -0.002f;
        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 110f;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(36f, 18f, 36f);
        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 0.15f;
        noise.scrollSpeed = 0.05f;
        ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
        fade.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        ParticleSystemRenderer particles = plankton.GetComponent<ParticleSystemRenderer>();
        particles.sharedMaterial = material;
        particles.shadowCastingMode = ShadowCastingMode.Off;
        particles.receiveShadows = false;
    }

    // ---------- God rays ----------

    private static void BuildLightShafts(Transform group, Material material)
    {
        Light sun = LightShafts.FindSun();
        if (sun == null)
            Debug.LogWarning("No directional light in the Shallows; the light shafts will hang straight down until there is one.");
        Vector3 axis = sun != null ? -sun.transform.forward : Vector3.up;
        Transform shafts = new GameObject("Light Shafts").transform;
        shafts.SetParent(group, false);
        System.Random random = new System.Random(16);
        float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
        for (int i = 0; i < 10; i++)
        {
            float angle = R(0f, Mathf.PI * 2f);
            float distance = Mathf.Sqrt(R(0f, 1f)) * (AreaRadius - 15f);
            float length = R(18f, 28f);
            GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Quad);
            shaft.name = "Light Shaft " + (i + 1);
            Object.DestroyImmediate(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(shafts, false);
            // Hang from just under the surface, slanting down and away from the sun.
            Vector3 top = new Vector3(Mathf.Cos(angle) * distance, SurfaceY - 0.5f, Mathf.Sin(angle) * distance);
            shaft.transform.position = top - axis * (length * 0.5f);
            shaft.transform.rotation = Quaternion.LookRotation(Vector3.Cross(axis, Vector3.right).normalized, axis);
            shaft.transform.localScale = new Vector3(R(2.5f, 6f), length, 1f);
            MeshRenderer renderer = shaft.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        shafts.gameObject.AddComponent<LightShafts>();
    }

    // ---------- Assets ----------

    private static void ImportTextures()
    {
        AssetDatabase.Refresh();
        foreach (string name in new[] { "WaterSurface_Ripples", "LightShaft" })
        {
            string path = $"{Root}/Textures/Water/{name}.png";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                continue;
            TextureWrapMode wrap = name == "LightShaft" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (importer.textureType == TextureImporterType.Default && importer.wrapMode == wrap && importer.alphaIsTransparency)
                continue;
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = wrap;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }

    /// <summary>A transparent material, unlit surface or particles, alpha-blended or additive; reused if it already exists.</summary>
    private static Material TransparentMaterial(string name, string shaderName, Color color, Texture texture, bool additive)
    {
        string path = $"{Root}/Materials/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
            throw new Exception("Shader not found: " + shaderName);
        Material material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        if (texture != null)
            material.SetTexture("_BaseMap", texture);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", additive ? 2f : 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");
        System.IO.Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
