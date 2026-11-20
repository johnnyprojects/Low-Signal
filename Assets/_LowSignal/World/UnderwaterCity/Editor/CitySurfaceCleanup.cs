using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Bake away overlapping coplanar triangles, without moving objects or changing colliders.
public static class CitySurfaceCleanup
{
    const float PlaneTolerance = .0005f;
    const float AreaTolerance = .0001f;
    class Face
    {
        public int id, submesh;
        public MeshFilter owner;
        public Vector3 a, b, c, normal;
        public Vector3 la, lb, lc;
        public Vector3 na, nb, nc;
        public Vector2 ua, ub, uc;
        public Bounds bounds;
        public List<List<Vector3>> pieces;
    }

    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
    // Visible trim owns a shared surface; floors yield to walls, sidewalks to curbs.
    static int Priority(MeshFilter f)
    {
        string n = f.name.ToLowerInvariant();
        if (n.Contains("curb") || n.Contains("coping")) return 0;
        if (n.Contains("wall") || n.Contains("lintel")) return 1;
        return 2;
    }
    static Vector3 Canonical(Vector3 n)
    {
        if (Mathf.Abs(n.x) > .0001f ? n.x < 0 : Mathf.Abs(n.y) > .0001f ? n.y < 0 : n.z < 0) n = -n;
        return n;
    }
    static string Key(Vector3 n, int distance) => Mathf.RoundToInt(n.x * 10000) + "," +
        Mathf.RoundToInt(n.y * 10000) + "," + Mathf.RoundToInt(n.z * 10000) + ":" + distance;
    static float Area(List<Vector3> p)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 1; i + 1 < p.Count; i++) sum += Vector3.Cross(p[i] - p[0], p[i + 1] - p[0]);
        return sum.magnitude * .5f;
    }
    static List<Vector3> Half(List<Vector3> p, Vector3 origin, Vector3 inward, bool inside)
    {
        var result = new List<Vector3>();
        for (int i = 0; i < p.Count; i++)
        {
            Vector3 a = p[i], b = p[(i + 1) % p.Count];
            float da = Vector3.Dot(a - origin, inward), db = Vector3.Dot(b - origin, inward);
            bool ina = inside ? da >= 0 : da <= 0, inb = inside ? db >= 0 : db <= 0;
            if (ina) result.Add(a);
            if (ina != inb) result.Add(Vector3.LerpUnclamped(a, b, da / (da - db)));
        }
        return result;
    }
    static List<List<Vector3>> Subtract(List<Vector3> subject, Face clip)
    {
        var result = new List<List<Vector3>>();
        var rest = subject;
        var v = new[] { clip.a, clip.b, clip.c };
        for (int e = 0; e < 3 && rest.Count >= 3; e++)
        {
            Vector3 inward = Vector3.Cross(clip.normal, v[(e + 1) % 3] - v[e]).normalized;
            var outside = Half(rest, v[e], inward, false);
            if (outside.Count >= 3 && Area(outside) > AreaTolerance) result.Add(outside);
            rest = Half(rest, v[e], inward, true);
        }
        // Avoid splitting merely touching triangles (also prevents repeated-run fragmentation).
        if (rest.Count < 3 || Area(rest) <= AreaTolerance) return new List<List<Vector3>> { subject };
        return result;
    }

    public static int Repair(bool undo = false)
    {
        var filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
            .Where(f => f.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene() &&
                !PrefabUtility.IsPartOfPrefabInstance(f) &&
                f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null && f.GetComponent<MeshRenderer>().enabled &&
                (f.transform.root.name.StartsWith("01 -") || f.transform.root.name.StartsWith("02 -") ||
                 f.transform.root.name.StartsWith("03 -") || f.transform.root.name.StartsWith("04 -") ||
                 f.transform.root.name.StartsWith("05 -")))
            .OrderBy(Priority).ThenBy(f => PathOf(f.transform), StringComparer.Ordinal)
            .ThenBy(f => GlobalObjectId.GetGlobalObjectIdSlow(f).ToString(), StringComparer.Ordinal).ToArray();
        var faces = new List<Face>();
        var buckets = new Dictionary<string, List<Face>>();
        foreach (var f in filters)
        {
            var mesh = f.sharedMesh;
            if (!mesh.isReadable) throw new Exception("Unreadable city mesh: " + PathOf(f.transform));
            var verts = mesh.vertices; var uv = mesh.uv; var meshNormals = mesh.normals;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var indices = mesh.GetTriangles(s);
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                    var face = new Face { id = faces.Count, owner = f, submesh = s,
                        la = verts[ia], lb = verts[ib], lc = verts[ic],
                        a = f.transform.TransformPoint(verts[ia]), b = f.transform.TransformPoint(verts[ib]), c = f.transform.TransformPoint(verts[ic]),
                        ua = uv.Length > ia ? uv[ia] : Vector2.zero, ub = uv.Length > ib ? uv[ib] : Vector2.zero, uc = uv.Length > ic ? uv[ic] : Vector2.zero };
                    Vector3 cross = Vector3.Cross(face.b - face.a, face.c - face.a);
                    if (cross.magnitude < AreaTolerance * 2) continue;
                    face.normal = cross.normalized; face.bounds = new Bounds(face.a, Vector3.zero);
                    Vector3 fallback = Vector3.Cross(face.lb - face.la, face.lc - face.la).normalized;
                    face.na = meshNormals.Length > ia ? meshNormals[ia] : fallback;
                    face.nb = meshNormals.Length > ib ? meshNormals[ib] : fallback;
                    face.nc = meshNormals.Length > ic ? meshNormals[ic] : fallback;
                    face.bounds.Encapsulate(face.b); face.bounds.Encapsulate(face.c); face.bounds.Expand(PlaneTolerance * 2);
                    faces.Add(face);
                    Vector3 n = Canonical(face.normal);
                    string key = Key(n, Mathf.RoundToInt(Vector3.Dot(n, face.a) / .002f));
                    if (!buckets.TryGetValue(key, out var bucket)) buckets[key] = bucket = new List<Face>();
                    bucket.Add(face);
                }
            }
        }
        int removed = 0;
        var changed = new HashSet<MeshFilter>();
        foreach (var face in faces)
        {
            Vector3 n = Canonical(face.normal);
            int distance = Mathf.RoundToInt(Vector3.Dot(n, face.a) / .002f);
            for (int offset = -1; offset <= 1; offset++)
            {
                if (!buckets.TryGetValue(Key(n, distance + offset), out var bucket)) continue;
                foreach (var other in bucket)
                {
                    if (other.owner == face.owner || !face.bounds.Intersects(other.bounds)) continue;
                    float dot = Vector3.Dot(face.normal, other.normal);
                    if (Mathf.Abs(dot) < .999999f || (dot > 0 && other.id > face.id)) continue;
                    if (Mathf.Abs(Vector3.Dot(face.normal, other.a - face.a)) > PlaneTolerance ||
                        Mathf.Abs(Vector3.Dot(face.normal, other.b - face.a)) > PlaneTolerance ||
                        Mathf.Abs(Vector3.Dot(face.normal, other.c - face.a)) > PlaneTolerance) continue;
                    var pieces = face.pieces ?? new List<List<Vector3>> { new List<Vector3> { face.a, face.b, face.c } };
                    var next = new List<List<Vector3>>(); bool cut = false;
                    foreach (var piece in pieces)
                    {
                        var remainder = Subtract(piece, other);
                        if (remainder.Count != 1 || !ReferenceEquals(remainder[0], piece)) cut = true;
                        next.AddRange(remainder);
                    }
                    if (cut) { face.pieces = next; changed.Add(face.owner); removed++; }
                }
            }
        }
        var report = new List<string> { "Coplanar overlap cuts: " + removed, "Changed render meshes: " + changed.Count };
        AssetDatabase.StartAssetEditing();
        try
        {
        foreach (var group in faces.Where(f => changed.Contains(f.owner)).GroupBy(f => f.owner))
        {
            var filter = group.Key;
            var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var normals = new List<Vector3>();
            var indices = Enumerable.Range(0, filter.sharedMesh.subMeshCount).Select(_ => new List<int>()).ToArray();
            foreach (var f in group)
            {
                var pieces = f.pieces ?? new List<List<Vector3>> { new List<Vector3> { f.a, f.b, f.c } };
                Vector3 ab = f.b - f.a, ac = f.c - f.a;
                double aa = Vector3.Dot(ab, ab), bb = Vector3.Dot(ab, ac), cc = Vector3.Dot(ac, ac), denom = aa * cc - bb * bb;
                foreach (var piece in pieces)
                {
                    int start = vertices.Count;
                    foreach (var p in piece)
                    {
                        Vector3 ap = p - f.a;
                        double pa = Vector3.Dot(ap, ab), pc = Vector3.Dot(ap, ac);
                        float b = (float)((pa * cc - pc * bb) / denom), c = (float)((pc * aa - pa * bb) / denom);
                        vertices.Add(f.la + b * (f.lb - f.la) + c * (f.lc - f.la));
                        uvs.Add(f.ua + b * (f.ub - f.ua) + c * (f.uc - f.ua));
                        normals.Add((f.na + b * (f.nb - f.na) + c * (f.nc - f.na)).normalized);
                    }
                    for (int i = 1; i + 1 < piece.Count; i++) indices[f.submesh].AddRange(new[] { start, start + i, start + i + 1 });
                }
            }
            var mesh = new Mesh { name = filter.name + " - unique surfaces", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetNormals(normals); mesh.subMeshCount = indices.Length;
            for (int s = 0; s < indices.Length; s++) mesh.SetTriangles(indices[s], s);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            mesh = MeshLibrary.Store(mesh);
            if (undo) Undo.RecordObject(filter, "Remove overlapping city surfaces");
            filter.sharedMesh = mesh; EditorUtility.SetDirty(filter);
            report.Add(PathOf(filter.transform));
        }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        if (changed.Count > 0) EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Directory.CreateDirectory("Logs"); File.WriteAllLines("Logs/city-surface-cleanup.txt", report);
        Debug.Log("CITY_SURFACE_CLEANUP: " + removed + " overlap cuts across " + changed.Count + " meshes; transforms and colliders preserved.");
        return changed.Count;
    }

    [MenuItem("Low Signal/City/Remove overlapping surfaces in current scene")]
    public static void RepairCurrent() => Repair(true);

    public static void Batch()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/_LowSignal/Scenes/UnderwaterCity.unity");
            Repair();
            File.Copy("Logs/city-surface-cleanup.txt", "Logs/city-surface-changes.txt", true);
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            // Re-scan the baked triangles; reject a result with unresolved overlaps.
            int residual = Repair();
            if (residual != 0) throw new Exception("Residual overlaps remain: " + residual);
            Debug.Log("CITY_SURFACE_CLEANUP_PASS: no remaining coplanar overlap above area tolerance.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // This asset cache is only used by the cleanup pass, so keep it beside the pass.
    static class MeshLibrary
    {
        const string AssetPath = "Assets/_LowSignal/World/UnderwaterCity/Meshes/CitySurfaceMeshes.asset";
        static Dictionary<string, Mesh> meshes;

        static string Fingerprint(Mesh mesh)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((int)mesh.indexFormat);
                var vertices = mesh.vertices;
                writer.Write(vertices.Length);
                foreach (var v in vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                var normals = mesh.normals;
                writer.Write(normals.Length);
                foreach (var v in normals) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
                var tangents = mesh.tangents;
                writer.Write(tangents.Length);
                foreach (var v in tangents) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); writer.Write(v.w); }
                var uv = mesh.uv;
                writer.Write(uv.Length);
                foreach (var v in uv) { writer.Write(v.x); writer.Write(v.y); }
                writer.Write(mesh.subMeshCount);
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    writer.Write((int)mesh.GetTopology(i));
                    var indices = mesh.GetIndices(i);
                    writer.Write(indices.Length);
                    foreach (int index in indices) writer.Write(index);
                }
                var bounds = mesh.bounds;
                writer.Write(bounds.center.x); writer.Write(bounds.center.y); writer.Write(bounds.center.z);
                writer.Write(bounds.extents.x); writer.Write(bounds.extents.y); writer.Write(bounds.extents.z);
                writer.Flush();
                using (var hash = SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }

        public static Mesh Store(Mesh mesh)
        {
            if (meshes == null)
                meshes = AssetDatabase.LoadAllAssetsAtPath(AssetPath).OfType<Mesh>()
                    .GroupBy(Fingerprint).ToDictionary(group => group.Key, group => group.First());

            string fingerprint = Fingerprint(mesh);
            if (meshes.TryGetValue(fingerprint, out var existing))
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                return existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            mesh.name = "Surface_" + fingerprint;
            if (File.Exists(AssetPath)) AssetDatabase.AddObjectToAsset(mesh, AssetPath);
            else AssetDatabase.CreateAsset(mesh, AssetPath);
            meshes.Add(fingerprint, mesh);
            return mesh;
        }
    }
}
