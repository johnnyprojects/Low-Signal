using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Stores generated surface meshes once per geometry in a shared asset.</summary>
public static class CitySurfaceMeshLibrary
{
    public const string AssetPath = "Assets/UnderwaterCity/Meshes/CitySurfaceMeshes.asset";
    private static Dictionary<string, Mesh> meshes;

    public static string Fingerprint(Mesh mesh)
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
        if (File.Exists(AssetPath))
            AssetDatabase.AddObjectToAsset(mesh, AssetPath);
        else
            AssetDatabase.CreateAsset(mesh, AssetPath);
        meshes.Add(fingerprint, mesh);
        return mesh;
    }
}
