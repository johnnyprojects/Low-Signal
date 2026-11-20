using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Versioned JSON with atomic replacement and recovery from the previous good save.</summary>
public sealed class SaveGameStore
{
    public const string Format = "LowSignalSave";
    public const int Version = 1;
    public string FilePath { get; }
    public string BackupPath => FilePath + ".bak";

    public SaveGameStore(string directory) => FilePath = Path.Combine(directory, "savegame.json");

    public bool TryRead(out SaveGameData data, out string error)
    {
        if (ReadFile(FilePath, out data, out error)) return true;
        string primaryError = error;
        if (ReadFile(BackupPath, out data, out _))
        {
            error = "Recovered the previous save from backup.";
            return true;
        }
        error = File.Exists(FilePath) || File.Exists(BackupPath)
            ? "The save could not be read. " + primaryError : "No saved game yet.";
        return false;
    }

    public bool TryWrite(SaveGameData data, out string error)
    {
        error = null;
        string temporary = FilePath + ".tmp";
        try
        {
            data.format = Format;
            data.version = Version;
            data.savedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            if (!Validate(data)) throw new InvalidDataException("Save state failed validation.");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(data, true));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(FilePath))
            {
                // Never replace a good backup with a corrupt primary.
                bool validPrimary = ReadFile(FilePath, out _, out _);
                File.Replace(temporary, FilePath, validPrimary ? BackupPath : null);
            }
            else File.Move(temporary, FilePath);
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
        {
            error = "Could not save the game: " + exception.Message;
            return false;
        }
    }

    private static bool ReadFile(string path, out SaveGameData data, out string error)
    {
        data = null;
        error = "Invalid or unsupported save file.";
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return false;
            var candidate = JsonUtility.FromJson<SaveGameData>(File.ReadAllText(path));
            if (!Validate(candidate)) return false;
            data = candidate;
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
        {
            error = exception.Message;
            return false;
        }
    }

    public static bool Validate(SaveGameData data)
    {
        if (data == null || data.format != Format || data.version != Version || string.IsNullOrEmpty(data.savedUtc)
            || string.IsNullOrEmpty(data.currentScene) || data.scenes == null || data.scenes.Count == 0 || data.scenes.Count > 64) return false;
        var scenePaths = new HashSet<string>();
        bool hasCurrent = false;
        foreach (var scene in data.scenes)
        {
            if (scene == null || string.IsNullOrEmpty(scene.scenePath) || !scenePaths.Add(scene.scenePath)
                || scene.player == null || scene.items == null || scene.guardians == null || scene.items.Count > 1024) return false;
            hasCurrent |= scene.scenePath == data.currentScene;
            var player = scene.player;
            if (!Finite(player.position) || !Finite(player.heading) || !Finite(player.look.x) || !Finite(player.look.y)
                || !Finite(player.cameraDistance) || player.cameraDistance < 0 || !Finite(player.energy) || player.energy < 0
                || !Enum.IsDefined(typeof(AbilityId), player.selectedAbility) || player.unlockedAbilities == null) return false;
            foreach (var ability in player.unlockedAbilities)
                if (!Enum.IsDefined(typeof(AbilityId), ability)) return false;
            // Saves from before the inventory have no list. Stored items are hidden, so they are saved inactive.
            var stored = new HashSet<string>();
            if (player.inventory != null)
            {
                if (player.inventory.Count > 64) return false;
                foreach (string item in player.inventory)
                    if (string.IsNullOrEmpty(item) || !stored.Add(item) || !scene.items.Exists(i => i.id == item && !i.held && !i.active)) return false;
            }
            var ids = new HashSet<string>();
            int heldCount = 0;
            foreach (var item in scene.items)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || !ids.Add(item.id) || !Finite(item.position) || !Valid(item.rotation)) return false;
                if (item.held && (++heldCount > 1 || !item.active || !item.canPickUp)) return false;
            }
            ids.Clear();
            foreach (var boss in scene.guardians)
            {
                if (boss == null || string.IsNullOrEmpty(boss.id) || !ids.Add(boss.id) || !Enum.IsDefined(typeof(ShallowsFishBoss.Phase), boss.phase)
                    || boss.waypoint < 0 || !Finite(boss.fishPosition) || !Valid(boss.fishRotation) || boss.acceptedItems == null) return false;
                var accepted = new HashSet<string>();
                foreach (string item in boss.acceptedItems)
                    if (string.IsNullOrEmpty(item) || !accepted.Add(item) || stored.Contains(item) || !scene.items.Exists(i => i.id == item && !i.held)) return false;
            }
        }
        return hasCurrent;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Valid(Quaternion q)
    {
        float length = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
        return Finite(length) && Mathf.Abs(length - 1f) < 0.02f;
    }
}
