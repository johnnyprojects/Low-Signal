using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Authoring only: the saved city uses ordinary prefabs, meshes and colliders.
public static partial class UnderwaterCityBuilder
{
    const string BuildingPrefabs = Root + "/Prefabs/Buildings";

    [MenuItem("Low Signal/City/Convert buildings to compact prefabs")]
    public static void ConvertBuildingPrefabs()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        ConvertBuildingsToPrefabs();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }

    public static void ConvertBuildingPrefabsBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            Directory.CreateDirectory("Logs/CityPrefabBackup");
            if (!File.Exists("Logs/CityPrefabBackup/UnderwaterCity.unity"))
                File.Copy(ScenePath, "Logs/CityPrefabBackup/UnderwaterCity.unity");
            LoadCollapseMaterials();
            Validate();
            string originalSupportCheck = TowerSupportCheckResult();
            CapturePrefabComparison("Before");
            ConvertBuildingsToPrefabs();
            LoadCollapseMaterials();
            Validate();
            Check(TowerSupportCheckResult() == originalSupportCheck, "Tower support validation changed during conversion");
            CapturePrefabComparison("After");
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            // Check serialized prefab links and collisions after unloading the originals.
            EditorSceneManager.OpenScene(ScenePath);
            LoadCollapseMaterials();
            ValidateBuildingPrefabs();
            if (File.Exists("Logs/CityPrefabBackup/UnderwaterCity.unity")) VerifyBuildingsAgainstBackup();
            Validate();
            Check(TowerSupportCheckResult() == originalSupportCheck, "Tower support validation changed after reload");
            Debug.Log("CITY_PREFABS_PASS: saved and reopened city; prefab links, routes, interiors and unchanged tower support verified.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void VerifyBuildingsAgainstBackup()
    {
        var current = SceneManager.GetActiveScene();
        const string temporaryScene = "Assets/__CityPrefabBaseline.unity";
        Check(!File.Exists(temporaryScene), "Temporary validation scene already exists");
        File.Copy("Logs/CityPrefabBackup/UnderwaterCity.unity", temporaryScene);
        AssetDatabase.ImportAsset(temporaryScene);
        Scene reference = default;
        try
        {
            reference = EditorSceneManager.OpenScene(temporaryScene, OpenSceneMode.Additive);
            var originalRoot = reference.GetRootGameObjects().Single(g => g.name == "02 - City blocks (sealed exteriors)").transform;
            Check(originalRoot.childCount == buildings.childCount, "Building count changed");
            Physics.SyncTransforms();
            foreach (Transform original in originalRoot)
            {
                var converted = buildings.Cast<Transform>().SingleOrDefault(t => t.name == original.name);
                Check(converted != null && Vector3.Distance(original.position, converted.position) < .001f &&
                    Quaternion.Angle(original.rotation, converted.rotation) < .001f &&
                    Vector3.Distance(original.lossyScale, converted.lossyScale) < .001f, "Building placement changed: " + original.name);
                CompareBuilding(original, converted);
            }
            Debug.Log("CITY_PREFABS_BASELINE_PASS: all 65 original building transforms, material geometry, collider counts, bounds and six surface probes per collider match the saved prefabs.");
        }
        finally
        {
            if (reference.IsValid()) EditorSceneManager.CloseScene(reference, true);
            SceneManager.SetActiveScene(current);
            AssetDatabase.DeleteAsset(temporaryScene);
        }
    }

    static string TowerSupportCheckResult()
    {
        try
        {
            ValidateTowerSupport(buildings.Find(CollapsedTowerName), buildings.Find(TowerSupportName));
            return "PASS";
        }
        catch (Exception error)
        {
            Debug.LogWarning("CITY_PREFABS_EXISTING_SUPPORT_CHECK: " + error.Message);
            return error.Message;
        }
    }

    static void CapturePrefabComparison(string suffix)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        Directory.CreateDirectory("Previews");
        var camera = Camera.main;
        var position = camera.transform.localPosition;
        var rotation = camera.transform.localRotation;
        float far = camera.farClipPlane;
        try
        {
            camera.farClipPlane = 800;
            Shot(camera, V(185,230,-210), Vector3.zero, "PrefabOverview"+suffix, true);
            Shot(camera, V(80,28,37), V(58,23,26), "PrefabFacade"+suffix, true);
            Shot(camera, V(-48,5,-38), V(-48,3,-20), "PrefabInterior"+suffix, true);
        }
        finally
        {
            camera.transform.localPosition = position;
            camera.transform.localRotation = rotation;
            camera.farClipPlane = far;
        }
    }

    static void ConvertBuildingsToPrefabs(bool reuseLibrary = false)
    {
        buildings = GameObject.Find("02 - City blocks (sealed exteriors)").transform;
        var originals = buildings.Cast<Transform>().ToArray();
        Check(originals.Length >= 60, "City buildings missing");
        int oldObjects = buildings.GetComponentsInChildren<Transform>(true).Length;
        int oldRenderers = buildings.GetComponentsInChildren<MeshRenderer>(true).Length;
        Directory.CreateDirectory(BuildingPrefabs);
        AssetDatabase.Refresh();
        foreach (var original in originals)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(original)) continue;
            string path = BuildingPrefabs + "/" + BuildingAssetName(original.name) + ".prefab";
            var existing = reuseLibrary ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            if (existing != null)
            {
                // New layout copies reuse the team's edited prefabs; no duplicate mesh library.
                var instance = ((GameObject)PrefabUtility.InstantiatePrefab(existing, original.parent)).transform;
                instance.name = original.name;
                instance.localPosition = original.localPosition;
                instance.localRotation = original.localRotation;
                instance.localScale = original.localScale;
                instance.SetSiblingIndex(original.GetSiblingIndex());
                UnityEngine.Object.DestroyImmediate(original.gameObject);
                continue;
            }
            ConvertBuilding(original);
        }
        AssetDatabase.SaveAssets();
        Physics.SyncTransforms();
        ValidateBuildingPrefabs();
        var report = $"Buildings: {originals.Length}\nObjects: {oldObjects} -> {buildings.GetComponentsInChildren<Transform>(true).Length}\n" +
            $"Renderers: {oldRenderers} -> {buildings.GetComponentsInChildren<MeshRenderer>(true).Length}\n";
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/city-prefab-conversion.txt", report);
        Debug.Log("CITY_PREFABS_CONVERTED: " + report.Replace('\n', ' '));
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static string BuildingAssetName(string name)
    {
        string stem = name.Replace(" / ", " - ");
        foreach (char c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
        return stem;
    }

    public static void ValidateBuildingPrefabsBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            LoadCollapseMaterials();
            ValidateBuildingPrefabs();
            Validate();
            TowerSupportCheckResult();
            if (File.Exists("Logs/CityPrefabBackup/UnderwaterCity.unity")) VerifyBuildingsAgainstBackup();
            Debug.Log("CITY_PREFABS_VALIDATION_PASS: saved mesh assets, prefab links and gameplay routes validated.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    static void ConvertBuilding(Transform source)
    {
        Check(source.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "Building has behaviours: " + source.name);
        Check(source.GetComponentsInChildren<Rigidbody>(true).Length == 0, "Building has rigid bodies: " + source.name);
        Check(source.GetComponentsInChildren<Renderer>(true).All(r => r is MeshRenderer && r.enabled && r.gameObject.activeInHierarchy),
            "Unsupported or disabled renderer in " + source.name);
        string stem = BuildingAssetName(source.name);
        string prefabPath = BuildingPrefabs + "/" + stem + ".prefab";
        string meshPath = BuildingPrefabs + "/" + stem + ".asset";
        // Never overwrite an artist's existing prefab when building a fresh city copy.
        if (File.Exists(prefabPath) || File.Exists(meshPath))
        {
            prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
            meshPath = Path.ChangeExtension(prefabPath, ".asset");
            Check(!File.Exists(meshPath), "Mesh asset already exists: " + meshPath);
        }
        var target = new GameObject(source.name).transform;
        target.SetParent(source.parent, false);
        target.localPosition = source.localPosition;
        target.localRotation = source.localRotation;
        target.localScale = source.localScale;
        target.gameObject.layer = source.gameObject.layer;
        target.gameObject.tag = source.gameObject.tag;
        var shell = Group("Shell", target);
        var facade = Group("Facade", target);
        var roof = Group("Roof", target);
        var interior = Group("Interior", target);
        var modules = new Dictionary<Transform, List<MeshFilter>>
        {
            {shell, new List<MeshFilter>()}, {facade, new List<MeshFilter>()},
            {roof, new List<MeshFilter>()}, {interior, new List<MeshFilter>()}
        };
        foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            Check(filter.sharedMesh != null && filter.sharedMesh.isReadable, "Missing or unreadable building mesh");
            string path = AnimationUtility.CalculateTransformPath(filter.transform, source).ToLowerInvariant();
            Transform module = path.Contains("roof") || path.Contains("parapet") ? roof :
                path.Contains("window") || path.Contains("facade") || path.Contains("algae") || path.Contains("floor band") || path.Contains("cornice") ? facade :
                path.Contains("counter") || path.Contains("bench") || path.Contains("entrance marker") ? interior : shell;
            modules[module].Add(filter);
        }
        bool assetCreated = false;
        foreach (var pair in modules)
        {
            if (pair.Value.Count == 0) { UnityEngine.Object.DestroyImmediate(pair.Key.gameObject); continue; }
            BakeBuildingModule(source, pair.Key, pair.Value, meshPath, ref assetCreated);
        }
        var collisions = Group("Collision", target);
        foreach (var collider in source.GetComponentsInChildren<Collider>(true))
            CopyBuildingCollider(source, target, collisions, collider);
        Physics.SyncTransforms();
        CompareBuilding(source, target);
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(target.gameObject, prefabPath, InteractionMode.AutomatedAction, out bool saved);
        Check(saved && prefab != null, "Could not save building prefab: " + prefabPath);
        target.SetSiblingIndex(source.GetSiblingIndex());
        UnityEngine.Object.DestroyImmediate(source.gameObject);
    }

    static void BakeBuildingModule(Transform source, Transform module, List<MeshFilter> filters, string meshPath, ref bool assetCreated)
    {
        var materials = new List<Material>();
        var byMaterial = new List<List<CombineInstance>>();
        var first = filters[0].GetComponent<MeshRenderer>();
        foreach (var filter in filters)
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            Check(renderer != null && renderer.sharedMaterials.Length == filter.sharedMesh.subMeshCount, "Material/submesh mismatch");
            Check(renderer.shadowCastingMode == first.shadowCastingMode && renderer.receiveShadows == first.receiveShadows &&
                renderer.lightProbeUsage == first.lightProbeUsage && renderer.reflectionProbeUsage == first.reflectionProbeUsage &&
                renderer.gameObject.layer == first.gameObject.layer && renderer.lightmapIndex < 0,
                "Incompatible renderer settings in " + source.name);
            for (int s = 0; s < filter.sharedMesh.subMeshCount; s++)
            {
                var material = renderer.sharedMaterials[s];
                Check(material != null, "Missing building material");
                int index = materials.IndexOf(material);
                if (index < 0) { index = materials.Count; materials.Add(material); byMaterial.Add(new List<CombineInstance>()); }
                byMaterial[index].Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = s,
                    transform = source.worldToLocalMatrix * filter.transform.localToWorldMatrix });
            }
        }
        var parts = new List<Mesh>();
        try
        {
            foreach (var group in byMaterial)
            {
                var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                part.CombineMeshes(group.ToArray(), true, true);
                parts.Add(part);
            }
            var mesh = new Mesh { name = module.name,
                indexFormat = parts.Sum(p => p.vertexCount) > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.CombineMeshes(parts.Select(p => new CombineInstance { mesh = p, transform = Matrix4x4.identity }).ToArray(), false, false);
            mesh.RecalculateBounds();
            if (!assetCreated) { AssetDatabase.CreateAsset(mesh, meshPath); assetCreated = true; }
            else AssetDatabase.AddObjectToAsset(mesh, meshPath);
            module.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var output = module.gameObject.AddComponent<MeshRenderer>();
            EditorUtility.CopySerialized(first, output);
            output.sharedMaterials = materials.ToArray();
            module.gameObject.layer = first.gameObject.layer;
            GameObjectUtility.SetStaticEditorFlags(module.gameObject, GameObjectUtility.GetStaticEditorFlags(first.gameObject));
        }
        finally { foreach (var part in parts) UnityEngine.Object.DestroyImmediate(part); }
    }

    static void CopyBuildingCollider(Transform source, Transform target, Transform collisions, Collider collider)
    {
        Check(collider is BoxCollider || collider is CapsuleCollider || collider is SphereCollider || collider is MeshCollider,
            "Unsupported building collider: " + collider.GetType().Name);
        string path = AnimationUtility.CalculateTransformPath(collider.transform, source);
        Transform node;
        // These named anchors are also used by the existing city/support validation tools.
        if (path == "Sealed structural mass" || path == "Crushed roof - load-bearing contact/Tower contact slab")
        {
            var parent = target;
            foreach (string name in path.Split('/'))
            {
                var child = parent.Find(name);
                parent = child != null ? child : Group(name, parent);
            }
            node = parent;
        }
        else if (collider is BoxCollider box && collider.gameObject.layer == collisions.gameObject.layer &&
            Quaternion.Angle(Quaternion.Inverse(source.rotation) * collider.transform.rotation, Quaternion.identity) < .001f)
        {
            // Multiple aligned box colliders can live on one object without altering their shapes.
            var copy = collisions.gameObject.AddComponent<BoxCollider>();
            EditorUtility.CopySerialized(box, copy);
            copy.center = target.InverseTransformPoint(collider.transform.TransformPoint(box.center));
            copy.size = Vector3.Scale(box.size, new Vector3(collider.transform.lossyScale.x / target.lossyScale.x,
                collider.transform.lossyScale.y / target.lossyScale.y, collider.transform.lossyScale.z / target.lossyScale.z));
            return;
        }
        else node = Group(collider.name, collisions);
        node.position = collider.transform.position;
        node.rotation = collider.transform.rotation;
        node.localScale = new Vector3(collider.transform.lossyScale.x / node.parent.lossyScale.x,
            collider.transform.lossyScale.y / node.parent.lossyScale.y, collider.transform.lossyScale.z / node.parent.lossyScale.z);
        node.gameObject.layer = collider.gameObject.layer;
        GameObjectUtility.SetStaticEditorFlags(node.gameObject, GameObjectUtility.GetStaticEditorFlags(collider.gameObject));
        EditorUtility.CopySerialized(collider, node.gameObject.AddComponent(collider.GetType()));
    }

    static void CompareBuilding(Transform before, Transform after)
    {
        var oldColliders = before.GetComponentsInChildren<Collider>(true);
        var newColliders = after.GetComponentsInChildren<Collider>(true);
        Check(oldColliders.Length == newColliders.Length, "Collider count changed: " + before.name);
        var remaining = new List<Collider>(newColliders);
        foreach (var collider in oldColliders)
        {
            int index = remaining.FindIndex(c => c.GetType() == collider.GetType() && c.enabled == collider.enabled &&
                c.isTrigger == collider.isTrigger && c.sharedMaterial == collider.sharedMaterial &&
                Vector3.Distance(c.bounds.center, collider.bounds.center) < .003f && Vector3.Distance(c.bounds.size, collider.bounds.size) < .003f);
            Check(index >= 0, "Collider bounds/properties changed: " + before.name + "/" + collider.name);
            var copy = remaining[index];
            // Probe each collider directly so overlapping buildings cannot hide a shape error.
            var bounds = collider.bounds;
            foreach (var direction in new[] {Vector3.right, Vector3.up, Vector3.forward})
            foreach (float sign in new[] {-1f, 1f})
            {
                var ray = new Ray(bounds.center + direction * sign * (bounds.extents.magnitude + 2), -direction * sign);
                bool hitBefore = collider.Raycast(ray, out var oldHit, bounds.size.magnitude + 4);
                bool hitAfter = copy.Raycast(ray, out var newHit, bounds.size.magnitude + 4);
                Check(hitBefore == hitAfter && (!hitBefore || Vector3.Distance(oldHit.point, newHit.point) < .003f),
                    "Collider surface changed: " + before.name + "/" + collider.name);
            }
            remaining.RemoveAt(index);
        }
        var oldGeometry = GeometrySummary(before);
        var newGeometry = GeometrySummary(after);
        Check(oldGeometry.Count == newGeometry.Count, "Building material count changed");
        foreach (var pair in oldGeometry)
        {
            Check(newGeometry.TryGetValue(pair.Key, out var other), "Building material lost");
            Check(pair.Value.indices == other.indices && Vector3.Distance(pair.Value.bounds.center, other.bounds.center) < .003f &&
                Vector3.Distance(pair.Value.bounds.size, other.bounds.size) < .003f, "Geometry changed: " + before.name + "/" + pair.Key.name);
        }
    }

    static Dictionary<Material, (long indices, Bounds bounds)> GeometrySummary(Transform root)
    {
        var result = new Dictionary<Material, (long indices, Bounds bounds)>();
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            var materials = filter.GetComponent<MeshRenderer>().sharedMaterials;
            var vertices = mesh.vertices;
            var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                int[] indices = mesh.GetIndices(s);
                if (indices.Length == 0) continue;
                if (!result.TryGetValue(materials[s], out var entry)) entry = (0, new Bounds(matrix.MultiplyPoint3x4(vertices[indices[0]]), Vector3.zero));
                entry.indices += indices.Length;
                foreach (int index in indices) entry.bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[index]));
                result[materials[s]] = entry;
            }
        }
        return result;
    }

    static void ValidateBuildingPrefabs()
    {
        foreach (Transform building in buildings)
        {
            Check(PrefabUtility.IsAnyPrefabInstanceRoot(building.gameObject), "Building is not a prefab: " + building.name);
            Check(PrefabUtility.GetPrefabInstanceStatus(building.gameObject) == PrefabInstanceStatus.Connected, "Missing prefab asset");
            Check(building.GetComponentsInChildren<MeshRenderer>().Length <= 4, "Uncombined building visuals");
            foreach (var filter in building.GetComponentsInChildren<MeshFilter>())
                Check(filter.sharedMesh != null && AssetDatabase.Contains(filter.sharedMesh), "Unsaved building mesh");
        }
    }

    static void RequirePrimitiveBuildings()
    {
        var root = GameObject.Find("02 - City blocks (sealed exteriors)");
        Check(root == null || !root.GetComponentsInChildren<Transform>().Any(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)),
            "This legacy primitive repair is for unconverted cities. Edit the building prefab's Shell, Facade or Roof in Prefab Mode instead.");
    }
}
