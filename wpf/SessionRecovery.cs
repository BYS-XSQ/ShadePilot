using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using DisplayPresetPrototype;

namespace ShadePilotWpf
{
    [DataContract]
    internal sealed class DisplayBaseline
    {
        [DataMember] public string DisplayId;
        [DataMember] public Snapshot State;

        public DisplayBaseline DeepClone()
        {
            return new DisplayBaseline { DisplayId = DisplayId, State = State?.DeepClone() };
        }
    }

    [DataContract]
    internal sealed class SystemBaseline
    {
        [DataMember] public List<DisplayBaseline> Displays = new List<DisplayBaseline>();
        [DataMember] public List<GammaBaseline> Gamma = new List<GammaBaseline>();

        public SystemBaseline DeepClone()
        {
            return new SystemBaseline {
                Displays = (Displays ?? new List<DisplayBaseline>()).Select(x => x?.DeepClone()).Where(x => x != null).ToList(),
                Gamma = (Gamma ?? new List<GammaBaseline>()).Select(x => x?.DeepClone()).Where(x => x != null).ToList()
            };
        }
    }

    [DataContract]
    internal sealed class RecoveryState
    {
        [DataMember] public int Version = 1;
        [DataMember] public bool SessionActive;
        [DataMember] public bool SoftwareColorMayBeActive;
        [DataMember] public SystemBaseline Baseline;
    }

    // The sole owner of recovery.json. It never receives presets or ordinary
    // settings, and it always copies snapshots before writing them.
    internal sealed class SessionRecoveryManager
    {
        private static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayPresetPrototype");
        private static string FilePath => Path.Combine(Folder, "recovery.json");
        private static string LogPath => Path.Combine(Folder, "logs", "session-lifecycle.log");
        private RecoveryState state;

        public SessionRecoveryManager()
        {
            state = Load();
        }

        public bool HasDirtySession => state?.SessionActive == true;
        public SystemBaseline Baseline => state?.Baseline?.DeepClone();

        public void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch { }
        }

        public void BeginSession(IEnumerable<PhysicalDisplay> displays, GammaRampController gamma)
        {
            var baseline = new SystemBaseline();
            foreach (var display in displays ?? Enumerable.Empty<PhysicalDisplay>())
            {
                if (display == null || string.IsNullOrWhiteSpace(display.Id)) continue;
                baseline.Displays.Add(new DisplayBaseline {
                    DisplayId = display.Id,
                    State = Snapshot.From(display).DeepClone()
                });
                if (!string.IsNullOrWhiteSpace(display.DisplayDeviceName))
                    baseline.Gamma.Add(gamma.CaptureSystemBaseline(display.DisplayDeviceName).DeepClone());
            }

            gamma.InstallSystemBaselines(baseline.Gamma);
            state = new RecoveryState {
                Version = 1,
                SessionActive = true,
                SoftwareColorMayBeActive = false,
                Baseline = baseline.DeepClone()
            };
            Save();
            Log("Session baseline captured");
        }

        public bool RestoreDirtySession(IEnumerable<PhysicalDisplay> displays,
            GammaRampController gamma, FullscreenColorController softwareColor, out string error)
        {
            error = null;
            if (!HasDirtySession) return true;
            if (state.Baseline == null)
            {
                error = "recovery.json 缺少上次会话基线；为防止覆盖残留状态，已拒绝建立新基线。";
                Log("Recovery baseline restore failed: " + error);
                return false;
            }
            Log("Recovery session detected");
            var baseline = state.Baseline.DeepClone();
            var current = (displays ?? Enumerable.Empty<PhysicalDisplay>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();

            foreach (var entry in baseline.Displays)
            {
                if (entry?.State == null || string.IsNullOrWhiteSpace(entry.DisplayId)) continue;
                if (!current.TryGetValue(entry.DisplayId, out var display))
                {
                    failures.Add("未找到原显示器 " + entry.DisplayId);
                    continue;
                }
                try { entry.State.DeepClone().Apply(display); }
                catch (Exception ex) { failures.Add(display.Name + "：" + ex.Message); }
            }

            try
            {
                gamma.InstallSystemBaselines(baseline.Gamma);
                gamma.RestoreSystemBaselines();
            }
            catch (Exception ex) { failures.Add("Gamma：" + ex.Message); }

            if (state.SoftwareColorMayBeActive)
            {
                try { softwareColor.Restore(); }
                catch (Exception ex) { failures.Add("软件色彩：" + ex.Message); }
            }

            if (failures.Count != 0)
            {
                error = string.Join("；", failures);
                Log("Recovery baseline restore failed: " + error);
                return false;
            }

            Log("Recovery baseline restored");
            MarkClean();
            return true;
        }

        public bool RestoreCurrentSession(IEnumerable<PhysicalDisplay> displays,
            GammaRampController gamma, FullscreenColorController softwareColor, out string error)
        {
            error = null;
            if (!HasDirtySession) return true;
            if (state.Baseline == null)
            {
                error = "recovery.json 缺少本会话基线。";
                return false;
            }
            var baseline = state.Baseline.DeepClone();
            var current = (displays ?? Enumerable.Empty<PhysicalDisplay>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Id))
                .ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var failures = new List<string>();
            foreach (var entry in baseline.Displays)
            {
                if (entry?.State == null || string.IsNullOrWhiteSpace(entry.DisplayId)) continue;
                if (!current.TryGetValue(entry.DisplayId, out var display))
                {
                    failures.Add("未找到原显示器 " + entry.DisplayId);
                    continue;
                }
                try { entry.State.DeepClone().Apply(display); }
                catch (Exception ex) { failures.Add(display.Name + "：" + ex.Message); }
            }
            try { gamma.RestoreSystemBaselines(); }
            catch (Exception ex) { failures.Add("Gamma：" + ex.Message); }
            try { softwareColor.Restore(); }
            catch (Exception ex) { failures.Add("软件色彩：" + ex.Message); }

            if (failures.Count != 0)
            {
                error = string.Join("；", failures);
                Log("System baseline restore failed: " + error);
                return false;
            }
            Log("System baseline restored");
            return true;
        }

        public void SetSoftwareColorMayBeActive(bool active)
        {
            if (!HasDirtySession || state.SoftwareColorMayBeActive == active) return;
            state.SoftwareColorMayBeActive = active;
            Save();
        }

        public void MarkClean()
        {
            if (state == null || !state.SessionActive) return;
            state.SessionActive = false;
            state.SoftwareColorMayBeActive = false;
            Save();
            Log("Session marked clean");
        }

        private static RecoveryState Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new RecoveryState();
                using (var stream = File.OpenRead(FilePath))
                {
                    var loaded = (RecoveryState)new DataContractJsonSerializer(typeof(RecoveryState)).ReadObject(stream);
                    if (loaded != null && loaded.Version == 1) return loaded;
                }
            }
            catch { }
            return new RecoveryState();
        }

        private void Save()
        {
            Directory.CreateDirectory(Folder);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporary))
                    new DataContractJsonSerializer(typeof(RecoveryState)).WriteObject(stream, state);
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            catch
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch { }
                throw;
            }
        }
    }
}
