using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OneBeam.Domain;

namespace OneBeam.Persistence;

public static class CheckpointStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static bool TrySave(string path, WorldSnapshot snapshot, out string error)
    {
        string? pendingFile = null;
        try
        {
            if (snapshot is null || snapshot.Status == RunStatus.Failed || !WorldState.TryRestore(snapshot, out _))
            {
                error = "当前生态数据无法保存为有效的检查点。";
                return false;
            }

            var destination = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(Path.GetFileName(destination)))
                throw new ArgumentException("请选择一个检查点文件。", nameof(path));

            Directory.CreateDirectory(directory);
            var candidate = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                pendingFile = candidate;
                JsonSerializer.Serialize(stream, snapshot, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            // Validate the actual bytes on disk before replacing the last playable checkpoint.
            if (!TryLoad(candidate, out _, out error)) return false;
            File.Move(candidate, destination, overwrite: true);
            pendingFile = null;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (IsFileOrFormatError(ex))
        {
            error = $"保存检查点失败。{ex.Message}";
            return false;
        }
        finally
        {
            if (pendingFile != null)
            {
                try { File.Delete(pendingFile); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public static bool TryLoad(string path, out WorldState? state, out string error)
    {
        state = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var snapshot = JsonSerializer.Deserialize<WorldSnapshot>(stream, JsonOptions);
            if (snapshot is null || snapshot.Status == RunStatus.Failed || !WorldState.TryRestore(snapshot, out state))
            {
                error = "检查点版本或生态数据无效。";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (FileNotFoundException)
        {
            error = "尚无已保存的检查点。";
            return false;
        }
        catch (Exception ex) when (IsFileOrFormatError(ex))
        {
            error = $"读取检查点失败。{ex.Message}";
            return false;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        // In .NET 8 a missing constructor parameter silently receives its default.
        // Require each stored property, including both public fields of Vector2.
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            if (type.Kind != JsonTypeInfoKind.Object) return;
            foreach (var property in type.Properties)
            {
                if (property.Set != null) property.IsRequired = true;
            }
        });
        return new JsonSerializerOptions { IncludeFields = true, IgnoreReadOnlyProperties = true, TypeInfoResolver = resolver };
    }

    private static bool IsFileOrFormatError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException;
}
