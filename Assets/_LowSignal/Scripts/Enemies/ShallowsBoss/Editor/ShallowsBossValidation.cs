using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ShallowsBossValidation
{
    static int stage;
    static double deadline;
    static ShallowsBossValidation()
    {
        EditorApplication.update += Tick;
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/_LowSignal/Scenes/Shallows.unity");
        SessionState.SetBool("GuardianValidation", true);
        EditorApplication.isPlaying = true;
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Tick()
    {
        if (!SessionState.GetBool("GuardianValidation", false) || !EditorApplication.isPlaying || Time.time < 0.2f) return;
        try
        {
            if (stage == 0)
            {
                var boss = UnityEngine.Object.FindFirstObjectByType<ShallowsFishBoss>();
                Require(boss != null && boss.treasures.Length == 4 && boss.requiredTreasure == 4, "Missing boss or treasures");
                CaptureFish(boss);
                Physics.SyncTransforms();
                Vector3 previous = boss.fish.position;
                foreach (var point in boss.swimRoute)
                {
                    Vector3 direction = (point.position - previous).normalized;
                    int steps = Mathf.CeilToInt(Vector3.Distance(previous, point.position) / 0.25f);
                    for (int step = 0; step <= steps; step++)
                    {
                        Vector3 center = Vector3.Lerp(previous, point.position, step / (float)steps);
                        foreach (var obstacle in Physics.OverlapCapsule(center - direction * 1.85f, center + direction * 1.85f,
                                     0.65f, ~0, QueryTriggerInteraction.Ignore))
                            Require(obstacle.transform.IsChildOf(boss.transform), "Fish route intersects " + obstacle.name + " at " + center);
                    }
                    previous = point.position;
                }
                var player = UnityEngine.Object.FindFirstObjectByType<PlayerPickup>();
                Require(player != null, "Missing pickup component");
                Require(!boss.PassageOpen && boss.passageBlocker.enabled, "Passage initially open");
                Require(!boss.TryAccept(boss.treasures[0]), "Distant treasure accepted");
                var unrelated = new GameObject("Unrelated item").AddComponent<PickupItem>();
                unrelated.transform.position = boss.offering.position;
                Require(!boss.TryAccept(unrelated), "Unrelated item accepted");
                UnityEngine.Object.Destroy(unrelated.gameObject);
                var first = boss.treasures[0];
                typeof(PickupItem).GetMethod("BeginCarry", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(first, new object[] { player });
                first.transform.position = boss.offering.position;
                Require(!boss.TryAccept(first), "Held treasure accepted");
                typeof(PickupItem).GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(first,
                    new object[] { boss.offering.position + Vector3.up * 0.3f, true });
                Require(boss.OfferedCount == 1 && !first.CanPickUp, "Placed event failed to accept and lock treasure");
                Require(!boss.TryAccept(first) && boss.OfferedCount == 1, "Duplicate treasure counted");
                for (int i = 1; i < boss.treasures.Length; i++)
                {
                    boss.treasures[i].transform.position = boss.offering.position + new Vector3(i * 0.7f, 0.3f, 0);
                    boss.treasures[i].OnPlaced.Invoke();
                }
                Require(boss.State == ShallowsFishBoss.Phase.Swimming && !boss.PassageOpen, "Fish failed to start swimming or opened early");
                boss.swimSpeed = 20;
                deadline = EditorApplication.timeSinceStartup + 30;
                stage = 1;
            }
            else if (stage == 1)
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Fish route timed out");
                var boss = UnityEngine.Object.FindFirstObjectByType<ShallowsFishBoss>();
                if (!boss.PassageOpen) return;
                Require(!boss.passageBlocker.enabled, "Blocker stayed enabled");
                var exit = UnityEngine.Object.FindFirstObjectByType<ShallowsCaveExit>();
                var player = UnityEngine.Object.FindFirstObjectByType<UnderwaterSwimController>();
                player.enabled = false;
                var body = player.GetComponent<Rigidbody>();
                body.linearVelocity = Vector3.zero;
                body.position = exit.transform.position;
                player.transform.position = exit.transform.position;
                Physics.SyncTransforms();
                deadline = EditorApplication.timeSinceStartup + 45;
                stage = 2;
            }
            else
            {
                Require(EditorApplication.timeSinceStartup < deadline, "Scene transition timed out");
                if (SceneManager.GetActiveScene().name != "UnderwaterCity") return;
                Debug.Log("GUARDIAN_PLAY_PASS: distant/held/unrelated/duplicate rejection; placed event; offering lock; route; passage; UnderwaterCity transition.");
                SessionState.SetBool("GuardianValidation", false);
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SessionState.SetBool("GuardianValidation", false);
            EditorApplication.Exit(1);
        }
    }

    static void CaptureFish(ShallowsFishBoss boss)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var camera = new GameObject("Fish validation camera").AddComponent<Camera>();
        camera.transform.position = boss.fish.position + boss.fish.right * 8 + Vector3.up;
        camera.transform.LookAt(boss.fish.position);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.15f, 0.25f, 0.3f);
        camera.orthographic = true; camera.orthographicSize = 3;
        camera.cullingMask = 1 << 30;
        var transforms = boss.fish.GetComponentsInChildren<Transform>();
        var layers = new int[transforms.Length];
        for (int i = 0; i < transforms.Length; i++) { layers[i] = transforms[i].gameObject.layer; transforms[i].gameObject.layer = 30; }
        var rt = new RenderTexture(900, 600, 24);
        camera.targetTexture = rt;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(900, 600, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 900, 600), 0, 0); texture.Apply();
        System.IO.File.WriteAllBytes("../fish-preview.png", texture.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        rt.Release();
        for (int i = 0; i < transforms.Length; i++) transforms[i].gameObject.layer = layers[i];
        UnityEngine.Object.Destroy(camera.gameObject);
        UnityEngine.Object.Destroy(rt);
        UnityEngine.Object.Destroy(texture);
    }
}
