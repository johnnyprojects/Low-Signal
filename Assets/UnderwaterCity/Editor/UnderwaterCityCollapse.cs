using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class UnderwaterCityBuilder
{
    const string CollapsedTowerName = "Collapsed west tower - swim above or around";
    const string TowerSupportName = "West tower support - crushed brick offices";
    const float SupportWidth = 24f;
    const float SupportDepth = 18f;
    const float SupportBaseHeight = 4f;
    static readonly Vector3 CollapsedTowerPosition = new Vector3(-125.5f, 0, 28f);

    // The leaning tower rests on a crushed low-rise whose roof follows its underside.
    // Reuse the existing materials and cube mesh so the scene needs no new art assets.
    static Transform AddCollapsedTowerSupport(Transform tower)
    {
        Check(tower != null, "Collapsed west tower missing");
        tower.position = CollapsedTowerPosition;
        var supportPosition = tower.TransformPoint(V(0, 48f, 0));
        supportPosition = V(supportPosition.x, 0, tower.position.z);
        var existing = buildings.Find(TowerSupportName);
        if (existing != null)
        {
            existing.position = supportPosition;
            return existing;
        }
        var towerMass = tower.Find("Sealed structural mass");
        Check(towerMass != null, "Tower structural mass missing");

        var support = Group(TowerSupportName, buildings);
        support.position = supportPosition;
        Box("Sealed structural mass", support, V(0, SupportBaseHeight / 2, 0),
            V(SupportWidth, SupportBaseHeight, SupportDepth), brick);
        Box("Algae tide stain", support, V(0, 1.1f, 0),
            V(SupportWidth + .05f, 2.2f, SupportDepth + .05f), algae, false);

        // Each wall strip extends into the inclined slab, closing the stepped upper floor.
        for (int i = 0; i < 12; i++)
        {
            float x = -SupportWidth / 2 + (i + .5f) * 2;
            float top = TowerUndersideHeight(towerMass, support.position.x + x, support.position.z) - .45f;
            Check(top > SupportBaseHeight, "Support upper floor would be below its base");
            Box("Crushed upper-floor masonry", support, V(x, (top + SupportBaseHeight) / 2, 0),
                V(2, top - SupportBaseHeight, SupportDepth), brick);
        }

        var roof = Group("Crushed roof - load-bearing contact", support);
        float roofHeight = TowerUndersideHeight(towerMass, support.position.x, support.position.z);
        float normalY = tower.right.y;
        roof.localPosition = V(0, roofHeight - .7f / (2 * normalY) + .03f, 0);
        roof.rotation = tower.rotation * Quaternion.Euler(0, 0, -90);
        float roofLength = SupportWidth / normalY;
        Box("Tower contact slab", roof, Vector3.zero, V(roofLength, .7f, SupportDepth), concrete);
        foreach (float side in new[] { -1f, 1f })
        {
            // Broken coping and exposed reinforcement frame the impact along the visible edges.
            for (int i = 0; i < 6; i++)
            {
                float x = -roofLength / 2 + (i + .5f) * roofLength / 6;
                if (i != 2 && i != 4)
                    Box("Fractured roof coping", roof, V(x, .42f, side * (SupportDepth / 2 - .25f)),
                        V(roofLength / 6 - .2f, .24f, .5f), pale, false);
                Box("Exposed roof reinforcement", roof, V(x, .41f, side * (SupportDepth / 2 - .8f)),
                    V(2.8f, .08f, .08f), rust, false);
            }
            for (float x = -SupportWidth / 2 + 3; x < SupportWidth / 2 - 1; x += 4.5f)
            {
                float top = TowerUndersideHeight(towerMass, support.position.x + x, support.position.z);
                for (float y = 1.7f; y + 1.15f < top - .8f; y += 3.5f)
                {
                    Box("Blocked office window", support, V(x, y, side * (SupportDepth / 2 + .025f)),
                        V(2.1f, 2.1f, .08f), glass, false);
                    Box("Weathered window sill", support, V(x, y - 1.1f, side * (SupportDepth / 2 + .04f)),
                        V(2.3f, .16f, .18f), pale, false);
                }
            }
            Box("Broken ground-floor cornice", support, V(0, 3.8f, side * (SupportDepth / 2 + .06f)),
                V(SupportWidth + .3f, .25f, .3f), pale, false);
            for (int i = 0; i < 4; i++)
            {
                float x = -8 + i * 4.7f;
                Box("Settled collapse debris", support, V(x, .25f, side * (SupportDepth / 2 + .4f)),
                    V(1.5f + i * .15f, .5f, 1.4f), i % 2 == 0 ? concrete : brick);
            }
        }
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * (SupportWidth / 2 + .025f);
            float top = TowerUndersideHeight(towerMass, support.position.x + x, support.position.z);
            for (float z = -SupportDepth / 2 + 3; z < SupportDepth / 2 - 1; z += 4.5f)
            for (float y = 1.7f; y + 1.15f < top - .8f; y += 3.5f)
                Box("Blocked end-wall window", support, V(x, y, z), V(.08f, 2.1f, 2.1f), glass, false);
        }
        return support;
    }

    static float TowerUndersideHeight(Transform mass, float x, float z)
    {
        Vector3 normal = mass.parent.right;
        Vector3 point = mass.TransformPoint(V(-.5f, 0, 0));
        Check(normal.y > .5f, "Unexpected collapsed tower tilt");
        return (Vector3.Dot(normal, point) - normal.x * x - normal.z * z) / normal.y;
    }

    static void LoadCollapseMaterials()
    {
        buildings = GameObject.Find("02 - City blocks (sealed exteriors)").transform;
        streets = GameObject.Find("01 - Streets and seabed").transform;
        brick = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Silted red brick.mat");
        concrete = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Weathered concrete.mat");
        pale = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Limestone trim.mat");
        glass = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Opaque dead windows.mat");
        rust = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Oxidized steel.mat");
        algae = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Dark algae.mat");
    }

    static void ValidateTowerSupport(Transform tower, Transform support)
    {
        Physics.SyncTransforms();
        var baseCollider = support.Find("Sealed structural mass").GetComponent<BoxCollider>();
        Check(Mathf.Abs(baseCollider.bounds.min.y) < .001f, "Support building is floating");
        var towerCollider = tower.Find("Sealed structural mass").GetComponent<BoxCollider>();
        Check(Mathf.Abs(tower.position.z - support.position.z) < .001f, "Tower is offset sideways from its support");
        Check(towerCollider.bounds.max.x < -122.25f, "Collapsed tower starts on the western road or sidewalk");
        var slab = support.Find("Crushed roof - load-bearing contact/Tower contact slab").GetComponent<BoxCollider>();
        Check(Physics.ComputePenetration(towerCollider, towerCollider.transform.position, towerCollider.transform.rotation,
            slab, slab.transform.position, slab.transform.rotation, out _, out float depth), "Tower misses supporting roof");
        Check(depth > .001f && depth < .1f, "Roof contact is excessively embedded: " + depth);
        foreach (float x in new[] { -9f, 0f, 9f })
        foreach (float z in new[] { -6f, 0f, 6f })
        {
            float worldX = support.position.x + x;
            float worldZ = support.position.z + z;
            float underside = TowerUndersideHeight(towerCollider.transform, worldX, worldZ);
            Check(slab.Raycast(new Ray(V(worldX, underside + .5f, worldZ), Vector3.down), out var hit, 1), "Missing roof contact");
            Check(Mathf.Abs(hit.point.y - underside) < .06f, "Gap beneath leaning tower");
        }
        ValidateCollapseClearance(tower, support);
        Debug.Log("CITY_COLLAPSE_PASS: aligned grounded support, nine roof-contact samples, clear western road; contact=" + depth);
    }

    static void ValidateCollapseClearance(Transform tower, Transform support)
    {
        var solidPieces = new System.Collections.Generic.List<Collider>();
        solidPieces.AddRange(tower.GetComponentsInChildren<Collider>());
        solidPieces.AddRange(support.GetComponentsInChildren<Collider>());
        var obstacles = new System.Collections.Generic.List<Collider>();
        foreach (var collider in streets.GetComponentsInChildren<Collider>())
            if (collider.name == "Connected carriageway" || collider.name == "Intersection asphalt" ||
                collider.name == "Sidewalk" || collider.name == "Sidewalk corner" || collider.name == "Curb")
                obstacles.Add(collider);
        foreach (Transform other in buildings)
        {
            if (other == tower || other == support) continue;
            var mass = other.Find("Sealed structural mass");
            if (mass != null) obstacles.Add(mass.GetComponent<Collider>());
        }
        foreach (var collider in UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            if (collider.name == "Collapsed perimeter masonry") obstacles.Add(collider);
        foreach (var piece in solidPieces)
        foreach (var obstacle in obstacles)
        {
            if (!piece.bounds.Intersects(obstacle.bounds)) continue;
            bool overlaps = Physics.ComputePenetration(piece, piece.transform.position, piece.transform.rotation,
                obstacle, obstacle.transform.position, obstacle.transform.rotation, out _, out float penetration);
            Check(!overlaps || penetration < .01f,
                "Collapse overlaps " + obstacle.name + " on " + obstacle.transform.parent.name + "; depth=" + penetration);
        }
    }

    [MenuItem("Low Signal/City/Align collapsed west tower and support")]
    public static void RepairCollapsedTower()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
        LoadCollapseMaterials();
        var tower = buildings.Find(CollapsedTowerName);
        bool existed = buildings.Find(TowerSupportName) != null;
        Undo.RecordObject(tower, "Align collapsed west tower");
        if (existed) Undo.RecordObject(buildings.Find(TowerSupportName), "Align tower support");
        var support = AddCollapsedTowerSupport(tower);
        if (!existed) Undo.RegisterCreatedObjectUndo(support.gameObject, "Support collapsed west tower");
        ValidateTowerSupport(tower, support);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
    }

    public static void RepairCollapsedTowerBatch()
    {
        try
        {
            EditorSceneManager.OpenScene(ScenePath);
            LoadCollapseMaterials();
            var tower = buildings.Find(CollapsedTowerName);
            var camera = Camera.main;
            Vector3 position = camera.transform.localPosition;
            Quaternion rotation = camera.transform.localRotation;
            float farClip = camera.farClipPlane;
            camera.farClipPlane = 800;
            Shot(camera, V(-160, 23, -8), V(-159, 9, 28), "CollapsedTowerBefore", true);
            var support = AddCollapsedTowerSupport(tower);
            ValidateTowerSupport(tower, support);
            Validate();
            // Capture uses the player's camera temporarily; restore its scene transform before saving.
            Shot(camera, V(-160, 23, -8), V(-159, 9, 28), "CollapsedTowerAfter", true);
            Shot(camera, V(-102, 26, -8), V(-157, 8, 28), "CollapsedTowerSide", true);
            camera.transform.localPosition = position;
            camera.transform.localRotation = rotation;
            camera.farClipPlane = farClip;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
