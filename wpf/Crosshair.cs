using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShapeRectangle = System.Windows.Shapes.Rectangle;
using Forms = System.Windows.Forms;
using DisplayPresetPrototype;

namespace ShadePilotWpf
{
    internal enum CrosshairElementKind { Line, Dot, Ring, Rectangle, Image }
    internal enum CrosshairDotShape { Circle, Square }

    [DataContract]
    [KnownType(typeof(CrosshairLine))]
    [KnownType(typeof(CrosshairDot))]
    [KnownType(typeof(CrosshairRing))]
    [KnownType(typeof(CrosshairRectangle))]
    [KnownType(typeof(CrosshairImage))]
    internal abstract class CrosshairElement
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Name { get; set; } = "图元";
        [DataMember] public bool Enabled { get; set; } = true;
        [DataMember] public double X { get; set; }
        [DataMember] public double Y { get; set; }
        [DataMember] public double Rotation { get; set; }
        [DataMember] public double Opacity { get; set; } = 1;
        [DataMember] public double Scale { get; set; } = 1;
        [DataMember] public bool MirrorX { get; set; }
        [DataMember] public bool MirrorY { get; set; }
        [DataMember] public int ZIndex { get; set; }
        public abstract CrosshairElementKind Kind { get; }
        public virtual void Normalize()
        {
            Rotation = Math.Clamp(Rotation, -180, 180); Opacity = Math.Clamp(Opacity, 0, 1); Scale = Math.Clamp(double.IsFinite(Scale) && Scale > 0 ? Scale : 1, .25, 4);
            Name = string.IsNullOrWhiteSpace(Name) ? "图元" : Name.Trim();
        }
    }

    [DataContract]
    internal sealed class CrosshairLine : CrosshairElement
    {
        [DataMember] public double StartX { get; set; } = -12;
        [DataMember] public double StartY { get; set; }
        [DataMember] public double EndX { get; set; } = 12;
        [DataMember] public double EndY { get; set; }
        [DataMember] public double Thickness { get; set; } = 2;
        [DataMember] public string ColorHex { get; set; } = "#FF63E6BE";
        [DataMember] public bool OutlineEnabled { get; set; }
        [DataMember] public string OutlineColorHex { get; set; } = "#FF000000";
        [DataMember] public double OutlineThickness { get; set; } = 1;
        public override CrosshairElementKind Kind => CrosshairElementKind.Line;
        public override void Normalize() { base.Normalize(); Thickness = Math.Clamp(Thickness, 1, 64); OutlineThickness = Math.Clamp(OutlineThickness, 0, 32); }
    }

    [DataContract]
    internal sealed class CrosshairDot : CrosshairElement
    {
        [DataMember] public double Diameter { get; set; } = 6;
        [DataMember] public CrosshairDotShape Shape { get; set; } = CrosshairDotShape.Circle;
        [DataMember] public string ColorHex { get; set; } = "#FFFFD166";
        [DataMember] public bool OutlineEnabled { get; set; }
        [DataMember] public string OutlineColorHex { get; set; } = "#FF000000";
        [DataMember] public double OutlineThickness { get; set; } = 1;
        public override CrosshairElementKind Kind => CrosshairElementKind.Dot;
        public override void Normalize() { base.Normalize(); Diameter = Math.Clamp(Diameter, 1, 256); OutlineThickness = Math.Clamp(OutlineThickness, 0, 32); }
    }

    [DataContract]
    internal sealed class CrosshairRing : CrosshairElement
    {
        [DataMember] public double Diameter { get; set; } = 28;
        [DataMember] public double Thickness { get; set; } = 2;
        [DataMember] public string ColorHex { get; set; } = "#FF63E6BE";
        public override CrosshairElementKind Kind => CrosshairElementKind.Ring;
        public override void Normalize() { base.Normalize(); Diameter = Math.Clamp(Diameter, 1, 512); Thickness = Math.Clamp(Thickness, 1, 64); }
    }

    [DataContract]
    internal sealed class CrosshairRectangle : CrosshairElement
    {
        [DataMember] public double Width { get; set; } = 12;
        [DataMember] public double Height { get; set; } = 12;
        [DataMember] public double CornerRadius { get; set; }
        [DataMember] public bool FillEnabled { get; set; } = true;
        [DataMember] public string FillColorHex { get; set; } = "#FFFFD166";
        [DataMember] public bool StrokeEnabled { get; set; }
        [DataMember] public string StrokeColorHex { get; set; } = "#FF000000";
        [DataMember] public double StrokeThickness { get; set; } = 1;
        public override CrosshairElementKind Kind => CrosshairElementKind.Rectangle;
        public override void Normalize() { base.Normalize(); Width = Math.Clamp(Width, 1, 512); Height = Math.Clamp(Height, 1, 512); CornerRadius = Math.Clamp(CornerRadius, 0, Math.Min(Width, Height) / 2); StrokeThickness = Math.Clamp(StrokeThickness, 0, 32); }
    }

    [DataContract]
    internal sealed class CrosshairImage : CrosshairElement
    {
        [DataMember] public string AssetId { get; set; } = "";
        [DataMember] public double Width { get; set; } = 32;
        [DataMember] public double Height { get; set; } = 32;
        public override CrosshairElementKind Kind => CrosshairElementKind.Image;
        public override void Normalize() { base.Normalize(); Width = Math.Clamp(Width, 1, 1024); Height = Math.Clamp(Height, 1, 1024); }
    }

    [DataContract]
    internal sealed class CrosshairDocument
    {
        [DataMember] public int SchemaVersion { get; set; } = 1;
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Name { get; set; } = "自定义准星";
        // GlobalScale is kept as a serialized compatibility field for presets
        // created before the explicit GlobalCrosshairScale name was introduced.
        [DataMember] public double GlobalScale { get; set; } = 1;
        [DataMember] public double GlobalCrosshairScale { get; set; } = 1;
        [DataMember] public double GlobalRotation { get; set; }
        [DataMember] public double OffsetX { get; set; }
        [DataMember] public double OffsetY { get; set; }
        [DataMember] public double GlobalOpacity { get; set; } = 1;
        // Set after an advanced structural edit so the basic semantic editor
        // can keep safe global edits without rebuilding the user's structure.
        [DataMember] public bool IsCustomStructure { get; set; }
        [DataMember] public List<CrosshairElement> Elements { get; set; } = new();
        public void Normalize()
        {
            var legacyScale = GlobalScale;
            var scale = GlobalCrosshairScale;
            // A legacy document has no GlobalCrosshairScale member, so the
            // property initializer leaves it at 1. Preserve its old
            // non-default GlobalScale value during migration.
            if (!double.IsFinite(scale) || scale <= 0 || (Math.Abs(scale - 1) < .0001 && double.IsFinite(legacyScale) && legacyScale > 0 && Math.Abs(legacyScale - 1) >= .0001))
                scale = double.IsFinite(legacyScale) && legacyScale > 0 ? legacyScale : 1;
            GlobalCrosshairScale = Math.Clamp(scale, .25, 4);
            GlobalScale = GlobalCrosshairScale;
            GlobalRotation = Math.Clamp(GlobalRotation, -180, 180); GlobalOpacity = Math.Clamp(GlobalOpacity, 0, 1);
            Elements ??= new List<CrosshairElement>(); foreach (var e in Elements) e?.Normalize(); Elements = Elements.Where(e => e != null).OrderBy(e => e.ZIndex).ToList();
        }
        public CrosshairDocument DeepClone() { Normalize(); return Json.Clone(this); }
    }

    [DataContract]
    internal sealed class CrosshairPreset
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Name { get; set; } = "准星预设";
        [DataMember] public CrosshairDocument Document { get; set; } = new();
        [DataMember] public bool IncludeInCycle { get; set; } = true;
        [DataMember] public int SortOrder { get; set; }
        public CrosshairPreset DeepClone() => new() { Id = Id, Name = Name, IncludeInCycle = IncludeInCycle, SortOrder = SortOrder, Document = Document?.DeepClone() ?? new() };
    }

    [DataContract]
    internal sealed class CrosshairHotkey
    {
        [DataMember] public uint Modifiers { get; set; }
        [DataMember] public int Key { get; set; }
        public bool IsSet => Key != 0;
        public CrosshairHotkey Clone() => new() { Modifiers = Modifiers, Key = Key };
    }

    [DataContract]
    internal sealed class CrosshairHotkeyBinding
    {
        [DataMember] public string PresetId { get; set; } = "";
        [DataMember] public CrosshairHotkey Hotkey { get; set; } = new();
    }

    [DataContract]
    internal sealed class CrosshairState
    {
        [DataMember] public bool Enabled { get; set; }
        [DataMember] public string ActivePresetId { get; set; } = "";
        [DataMember] public string SelectedMonitorId { get; set; } = "";
        [DataMember] public CrosshairDocument CurrentDraft { get; set; } = Templates.Cross();
        [DataMember] public CrosshairHotkey ToggleHotkey { get; set; } = new();
        [DataMember] public CrosshairHotkey NextPresetHotkey { get; set; } = new();
        [DataMember] public CrosshairHotkey PreviousPresetHotkey { get; set; } = new();
        [DataMember] public List<CrosshairHotkeyBinding> PresetBindings { get; set; } = new();
        [DataMember] public bool SwitchPresetEnablesCrosshair { get; set; } = true;
        public void Normalize() { CurrentDraft ??= Templates.Cross(); CurrentDraft.Normalize(); PresetBindings ??= new(); ToggleHotkey ??= new(); NextPresetHotkey ??= new(); PreviousPresetHotkey ??= new(); }
    }

    internal static class Json
    {
        static readonly DataContractJsonSerializerSettings Settings = new() { UseSimpleDictionaryFormat = true };
        public static string Serialize<T>(T value) { using var ms = new MemoryStream(); new DataContractJsonSerializer(typeof(T), Settings).WriteObject(ms, value); return Encoding.UTF8.GetString(ms.ToArray()); }
        public static T Clone<T>(T value) { using var ms = new MemoryStream(); new DataContractJsonSerializer(typeof(T), Settings).WriteObject(ms, value); ms.Position = 0; return (T)new DataContractJsonSerializer(typeof(T), Settings).ReadObject(ms); }
        public static T Load<T>(string path, T fallback) { try { if (!File.Exists(path)) return fallback; using var fs = File.OpenRead(path); return (T)new DataContractJsonSerializer(typeof(T), Settings).ReadObject(fs); } catch { return fallback; } }
        public static void SaveAtomic<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temp = path + ".tmp"; using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) { new DataContractJsonSerializer(typeof(T), Settings).WriteObject(fs, value); fs.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true); else File.Move(temp, path);
        }
    }

    internal static class Templates
    {
        public static CrosshairDocument Cross()
        {
            var d = new CrosshairDocument { Name = "Classic Green" }; d.Elements.Add(new CrosshairLine { Name = "横线", StartX = -22, EndX = 22, Thickness = 2, ColorHex = "#FF63E6BE" }); d.Elements.Add(new CrosshairLine { Name = "竖线", StartX = 0, StartY = -22, EndX = 0, EndY = 22, Thickness = 2, ColorHex = "#FF63E6BE", ZIndex = 1 }); return d;
        }
        public static CrosshairDocument Dot() { var d = new CrosshairDocument { Name = "Small White Dot" }; d.Elements.Add(new CrosshairDot { Name = "中心点", Diameter = 6, ColorHex = "#FFFFFFFF" }); return d; }
        public static CrosshairDocument CrossDot() { var d = Cross(); d.Name = "Cross + Dot"; d.Elements.Add(new CrosshairDot { Name = "中心点", Diameter = 5, ColorHex = "#FFFFD166", ZIndex = 2 }); return d; }
        public static CrosshairDocument Ring() { var d = new CrosshairDocument { Name = "Ring Dot" }; d.Elements.Add(new CrosshairRing { Name = "圆环", Diameter = 30, Thickness = 2, ColorHex = "#FF63E6BE" }); d.Elements.Add(new CrosshairDot { Name = "中心点", Diameter = 4, ColorHex = "#FFFFD166", ZIndex = 1 }); return d; }
        public static List<CrosshairPreset> Defaults() => new() { New("Classic Green", Cross()), New("Small White Dot", Dot()), New("Cross + Dot", CrossDot()), New("Ring Dot", Ring()), New("T Shape", T()), New("Chevron", Chevron()) };
        static CrosshairPreset New(string n, CrosshairDocument d) => new() { Name = n, Document = d, SortOrder = 0 };
        public static CrosshairDocument T() { var d = new CrosshairDocument { Name = "T Shape" }; d.Elements.Add(new CrosshairLine { Name = "顶部", StartX = -20, EndX = 20, Thickness = 2, ColorHex = "#FF63E6BE" }); d.Elements.Add(new CrosshairLine { Name = "中线", StartY = 0, EndY = 22, Thickness = 2, ColorHex = "#FF63E6BE", ZIndex = 1 }); return d; }
        public static CrosshairDocument Chevron() { var d = new CrosshairDocument { Name = "Chevron" }; d.Elements.Add(new CrosshairLine { Name = "左斜线", StartX = -20, StartY = -12, EndX = 0, EndY = 10, Thickness = 2, ColorHex = "#FF63E6BE" }); d.Elements.Add(new CrosshairLine { Name = "右斜线", StartX = 0, StartY = 10, EndX = 20, EndY = -12, Thickness = 2, ColorHex = "#FF63E6BE", ZIndex = 1 }); return d; }
    }

    [DataContract]
    internal sealed class CrosshairPresetStore
    {
        [DataMember] public List<CrosshairPreset> Items { get; set; } = new();
        static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShadePilot", "Crosshair");
        static string PresetsPath => Path.Combine(Root, "crosshair-presets.json");
        public static CrosshairPresetStore Load()
        {
            var store = Json.Load(PresetsPath, new CrosshairPresetStore()); if (store.Items == null || store.Items.Count == 0) store.Items = Templates.Defaults();
            foreach (var p in store.Items) { p.Document ??= Templates.Cross(); p.Document.Normalize(); p.Name = string.IsNullOrWhiteSpace(p.Name) ? p.Document.Name : p.Name; }
            // Preserve the persisted list order when SortOrder values are tied
            // (for example, on the first load of the built-in presets). A
            // name-based tie-breaker would make renaming change cycle order.
            store.Items = store.Items.OrderBy(p => p.SortOrder).ToList(); return store;
        }
        public void Save() { for (int i = 0; i < Items.Count; i++) Items[i].SortOrder = i; Json.SaveAtomic(PresetsPath, this); }
        public CrosshairPreset Find(string id) => Items.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    internal sealed class CrosshairAssetStore
    {
        readonly string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShadePilot", "Crosshair", "assets");
        public string ImportPng(string source)
        {
            if (!string.Equals(Path.GetExtension(source), ".png", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("仅支持 PNG 图片。");
            var info = new FileInfo(source); if (info.Length > 5 * 1024 * 1024) throw new InvalidDataException("PNG 文件不能超过 5 MB。");
            using var stream = File.OpenRead(source); var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); var frame = decoder.Frames[0]; if (frame.PixelWidth > 1024 || frame.PixelHeight > 1024) throw new InvalidDataException("PNG 尺寸不能超过 1024×1024。");
            Directory.CreateDirectory(root); string id = Guid.NewGuid().ToString("N") + ".png"; File.Copy(source, Path.Combine(root, id)); return Path.GetFileNameWithoutExtension(id);
        }
        public string PathFor(string assetId) => System.IO.Path.Combine(root, (assetId ?? "") + ".png");
    }

    internal sealed class CrosshairManager : IDisposable
    {
        const int ToggleId = 4200, NextId = 4201, PreviousId = 4202, PresetBase = 4300;
        readonly CrosshairPresetStore store; readonly CrosshairAssetStore assets = new(); readonly CrosshairState state; readonly string statePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShadePilot", "Crosshair", "crosshair-state.json"); readonly string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShadePilot", "Crosshair", "crosshair.log");
        readonly Dictionary<int, Action> actions = new(); readonly List<int> registered = new();
        // EditorDraft is kept in CrosshairState; this private copy is the only
        // document sent to the live overlay.
        CrosshairDocument runtimeSnapshot;
        // IMPORTANT: Screen Position Drag and Structural Element Drag are intentionally isolated.
        // Screen Position Drag edits screen offsets in physical pixels; it must not share
        // drag state, coordinate conversion, handlers, interaction mode, or position properties
        // with the WPF structural editor Thumb drag.
        CrosshairOverlayWindow overlay; IntPtr hwnd; bool disposed; bool screenPositionEditing; bool screenPositionStartEnabled; bool screenPositionDragging; bool screenPositionDragChanged; bool advancedPreviewSuspended; bool overlayVisibleBeforeAdvancedPreview; double screenPositionStartX, screenPositionStartY, screenPositionDragStartX, screenPositionDragStartY;
        public event EventHandler Changed;
        public IReadOnlyList<CrosshairPreset> Presets => store.Items;
        public CrosshairDocument Draft => state.CurrentDraft.DeepClone();
        public CrosshairDocument RuntimeSnapshot => Json.Clone(runtimeSnapshot ?? state.CurrentDraft);
        public string ActivePresetId => state.ActivePresetId;
        public bool DraftDirty => store.Find(state.ActivePresetId) is { } p && Json.Serialize(p.Document) != Json.Serialize(state.CurrentDraft);
        public bool Enabled => state.Enabled;
        public string SelectedMonitorId => state.SelectedMonitorId;
        public CrosshairState Snapshot => Json.Clone(state);
        public CrosshairManager() { store = CrosshairPresetStore.Load(); state = Json.Load(statePath, new CrosshairState()); state.Normalize(); var active = store.Find(state.ActivePresetId); if (active == null) { active = store.Items[0]; state.ActivePresetId = active.Id; } state.CurrentDraft = active.Document.DeepClone(); runtimeSnapshot = state.CurrentDraft.DeepClone(); SaveState(); }
        public void Attach(IntPtr window)
        {
            hwnd = window; Log("Crosshair initialized"); RegisterHotkeys(); ReconcileOverlayState();
        }
        public void RegisterHotkeys()
        {
            UnregisterHotkeys(); if (hwnd == IntPtr.Zero) return;
            TryRegister(ToggleId, state.ToggleHotkey, () => Toggle()); TryRegister(NextId, state.NextPresetHotkey, ActivateNextPreset); TryRegister(PreviousId, state.PreviousPresetHotkey, ActivatePreviousPreset);
            int id = PresetBase; foreach (var p in store.Items.OrderBy(x => x.SortOrder)) { var b = state.PresetBindings.FirstOrDefault(x => x.PresetId == p.Id); if (b?.Hotkey?.IsSet == true) TryRegister(id, b.Hotkey, () => ActivatePreset(p.Id)); id++; }
        }
        void TryRegister(int id, CrosshairHotkey key, Action action) { if (key == null || !key.IsSet) return; if (MonitorApi.RegisterHotKey(hwnd, id, key.Modifiers | 0x4000u, (uint)key.Key)) { registered.Add(id); actions[id] = action; Log("Hotkey registered: " + id); } else Log("Hotkey registration failed: " + id); }
        public void UnregisterHotkeys() { foreach (var id in registered) MonitorApi.UnregisterHotKey(hwnd, id); registered.Clear(); actions.Clear(); }
        public bool HandleHotkey(int id) { if (!actions.TryGetValue(id, out var action)) return false; action(); return true; }
        public void Toggle() => SetEnabled(!state.Enabled);
        public void Enable() => SetEnabled(true);
        public void Disable() => SetEnabled(false);
        public void SetEnabled(bool enabled)
        {
            if (state.Enabled == enabled) { ReconcileOverlayState(); return; }
            state.Enabled = enabled; ReconcileOverlayState(); SaveState(); Log(enabled ? "Crosshair enabled" : "Crosshair disabled"); Changed?.Invoke(this, EventArgs.Empty);
        }
        public void SelectPreset(string id)
        {
            var p = store.Find(id); if (p == null) return; state.ActivePresetId = p.Id; state.CurrentDraft = p.Document.DeepClone(); runtimeSnapshot = state.CurrentDraft.DeepClone(); ReconcileOverlayState(); SaveState(); Changed?.Invoke(this, EventArgs.Empty);
        }
        public void ActivatePreset(string id)
        {
            var p = store.Find(id); if (p == null) return; state.ActivePresetId = p.Id; state.CurrentDraft = p.Document.DeepClone(); runtimeSnapshot = state.CurrentDraft.DeepClone(); if (state.SwitchPresetEnablesCrosshair) state.Enabled = true; ReconcileOverlayState(); SaveState(); Log("Crosshair preset loaded: " + p.Name); Changed?.Invoke(this, EventArgs.Empty);
        }
        public void ActivateNextPreset() => ActivateRelative(1);
        public void ActivatePreviousPreset() => ActivateRelative(-1);
        public IReadOnlyList<CrosshairPreset> CyclePresets => store.Items.Where(p => p.IncludeInCycle).OrderBy(p => p.SortOrder).ToList();
        void ActivateRelative(int direction)
        {
            var list = CyclePresets; if (list.Count <= 1) return; int i = list.ToList().FindIndex(p => p.Id == state.ActivePresetId); if (i < 0) i = direction > 0 ? 0 : list.Count - 1; else i = (i + direction + list.Count) % list.Count; ActivatePreset(list[i].Id);
        }
        public void UpdateDraft(Action<CrosshairDocument> mutate) { var draft = state.CurrentDraft.DeepClone(); mutate(draft); draft.Normalize(); state.CurrentDraft = draft; runtimeSnapshot = draft.DeepClone(); ReconcileOverlayState(); Changed?.Invoke(this, EventArgs.Empty); }
        public void UpdateStructuralDraft(Action<CrosshairDocument> mutate)
        {
            if (mutate == null) return;
            var draft = state.CurrentDraft.DeepClone(); mutate(draft); draft.IsCustomStructure = true; draft.Normalize(); state.CurrentDraft = draft; runtimeSnapshot = draft.DeepClone(); ReconcileOverlayState(); Changed?.Invoke(this, EventArgs.Empty);
        }
        public bool MoveElementBy(string id, double dx, double dy)
        {
            if (string.IsNullOrWhiteSpace(id) || (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)) return false;
            var draft = Json.Clone(state.CurrentDraft); var element = draft.Elements.FirstOrDefault(x => x.Id == id); if (element == null) return false;
            switch (element)
            {
                case CrosshairLine line:
                    line.X += dx; line.Y += dy; break;
                case CrosshairDot dot:
                    dot.X += dx; dot.Y += dy; break;
                case CrosshairRing ring:
                    ring.X += dx; ring.Y += dy; break;
                case CrosshairRectangle rectangle:
                    rectangle.X += dx; rectangle.Y += dy; break;
                case CrosshairImage image:
                    image.X += dx; image.Y += dy; break;
                default: return false;
            }
            draft.IsCustomStructure = true; state.CurrentDraft = draft; runtimeSnapshot = Json.Clone(draft); return true;
        }
        public void NotifyDraftChanged() => Changed?.Invoke(this, EventArgs.Empty);
        public void ReplaceDraft(CrosshairDocument document) { state.CurrentDraft = document?.DeepClone() ?? Templates.Cross(); runtimeSnapshot = state.CurrentDraft.DeepClone(); ReconcileOverlayState(); Changed?.Invoke(this, EventArgs.Empty); }
        public string AddElement(CrosshairElement element) { if (element == null) return null; element.ZIndex = state.CurrentDraft.Elements.Count; UpdateStructuralDraft(d => d.Elements.Add(element)); return element.Id; }
        public void DeleteElement(string id) => UpdateStructuralDraft(d => d.Elements.RemoveAll(e => e.Id == id));
        public string DuplicateElement(string id) { var e = state.CurrentDraft.Elements.FirstOrDefault(x => x.Id == id); if (e == null) return null; var copy = Json.Clone(e); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " 副本"; AddElement(copy); return copy.Id; }
        public void SaveDraftAs(string name)
        {
            var p = new CrosshairPreset { Name = name.Trim(), Document = state.CurrentDraft.DeepClone(), IncludeInCycle = true, SortOrder = store.Items.Count }; store.Items.Add(p); state.ActivePresetId = p.Id; store.Save(); SaveState(); Log("Crosshair preset saved: " + p.Name); Changed?.Invoke(this, EventArgs.Empty);
        }
        public void CreatePreset(string name, CrosshairDocument document)
        {
            if (string.IsNullOrWhiteSpace(name) || document == null) return;
            var draft = document.DeepClone(); draft.Name = name.Trim(); draft.Normalize();
            var preset = new CrosshairPreset { Name = draft.Name, Document = draft.DeepClone(), IncludeInCycle = true, SortOrder = store.Items.Count };
            store.Items.Add(preset); state.ActivePresetId = preset.Id; state.CurrentDraft = draft; runtimeSnapshot = draft.DeepClone();
            ReconcileOverlayState(); store.Save(); SaveState(); Log("Crosshair preset created: " + preset.Name); Changed?.Invoke(this, EventArgs.Empty);
        }
        public void SaveDraftToActive() { var p = store.Find(state.ActivePresetId); if (p == null) return; p.Document = state.CurrentDraft.DeepClone(); store.Save(); state.CurrentDraft = p.Document.DeepClone(); runtimeSnapshot = state.CurrentDraft.DeepClone(); SaveState(); Log("Crosshair preset saved: " + p.Name); Changed?.Invoke(this, EventArgs.Empty); }
        public void DuplicateActive()
        {
            var p = store.Find(state.ActivePresetId); if (p == null) return; var copy = p.DeepClone(); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " Copy"; copy.SortOrder = store.Items.Count; store.Items.Add(copy); state.ActivePresetId = copy.Id; state.CurrentDraft = copy.Document.DeepClone(); store.Save(); SaveState(); Changed?.Invoke(this, EventArgs.Empty);
        }
        public void DeleteActive()
        {
            var p = store.Find(state.ActivePresetId); if (p == null || store.Items.Count <= 1) return; store.Items.Remove(p); state.PresetBindings.RemoveAll(x => x.PresetId == p.Id); var next = store.Items.OrderBy(x => x.SortOrder).First(); state.ActivePresetId = next.Id; state.CurrentDraft = next.Document.DeepClone(); store.Save(); RegisterHotkeys(); SaveState(); Changed?.Invoke(this, EventArgs.Empty);
        }
        public void RenameActive(string name) { var p = store.Find(state.ActivePresetId); if (p == null || string.IsNullOrWhiteSpace(name)) return; p.Name = name.Trim(); store.Save(); SaveState(); Log("Crosshair preset renamed: " + p.Name); Changed?.Invoke(this, EventArgs.Empty); }
        // ScreenPositionDragController equivalent: this manager block owns only
        // screen offsets and the real overlay's physical-pixel fast path.
        public bool PositionEditMode => screenPositionEditing;
        public void BeginPositionEdit() { if (screenPositionEditing) return; screenPositionStartEnabled = state.Enabled; if (!state.Enabled) state.Enabled = true; screenPositionStartX = state.CurrentDraft.OffsetX; screenPositionStartY = state.CurrentDraft.OffsetY; screenPositionEditing = true; ReconcileOverlayState(); SaveState(); Log("Crosshair position edit started"); Changed?.Invoke(this, EventArgs.Empty); }
        public void BeginPositionDrag() { if (!screenPositionEditing) return; screenPositionDragging = true; screenPositionDragChanged = false; screenPositionDragStartX = state.CurrentDraft.OffsetX; screenPositionDragStartY = state.CurrentDraft.OffsetY; }
        // ScreenPositionDrag fast path: incoming values are physical-pixel
        // cursor deltas from the independent overlay handler. No runtime rebuild,
        // preset load, or template operation is allowed here.
        void UpdateScreenPositionOnly(double dx, double dy) { if (!screenPositionEditing) return; var startX = screenPositionDragging ? screenPositionDragStartX : state.CurrentDraft.OffsetX; var startY = screenPositionDragging ? screenPositionDragStartY : state.CurrentDraft.OffsetY; UpdatePositionOffset(startX + dx, startY + dy); if (screenPositionDragging) screenPositionDragChanged = true; }
        public void EndPositionDrag() { var changed = screenPositionDragChanged; screenPositionDragging = false; screenPositionDragChanged = false; if (changed) Changed?.Invoke(this, EventArgs.Empty); }
        public void NudgePosition(double dx, double dy) { if (!screenPositionEditing) return; var current = state.CurrentDraft; UpdatePositionOffset(current.OffsetX + dx, current.OffsetY + dy); Changed?.Invoke(this, EventArgs.Empty); }
        public void ResetPosition() { if (screenPositionEditing) { UpdatePositionOffset(0, 0); Changed?.Invoke(this, EventArgs.Empty); } else UpdateDraft(d => { d.OffsetX = 0; d.OffsetY = 0; }); }
        void UpdatePositionOffset(double offsetX, double offsetY) { if (!screenPositionEditing) return; var draft = state.CurrentDraft.DeepClone(); draft.OffsetX = offsetX; draft.OffsetY = offsetY; draft.Normalize(); state.CurrentDraft = draft; runtimeSnapshot = draft.DeepClone(); overlay?.UpdateScreenPositionOnly(offsetX, offsetY); }
        public void EndPositionEdit(bool commit) { if (!screenPositionEditing) return; screenPositionDragging = false; screenPositionEditing = false; if (!commit) { var draft = state.CurrentDraft.DeepClone(); draft.OffsetX = screenPositionStartX; draft.OffsetY = screenPositionStartY; draft.Normalize(); state.CurrentDraft = draft; runtimeSnapshot = draft.DeepClone(); if (!screenPositionStartEnabled) state.Enabled = false; } ReconcileOverlayState(); SaveState(); Log(commit ? "Crosshair position edit committed" : "Crosshair position edit cancelled"); Changed?.Invoke(this, EventArgs.Empty); }
        public void SetIncludeInCycle(bool value) => SetPresetIncludeInCycle(state.ActivePresetId, value);
        public void SetPresetIncludeInCycle(string presetId, bool value) { var p = store.Find(presetId); if (p == null || p.IncludeInCycle == value) return; p.IncludeInCycle = value; store.Save(); Changed?.Invoke(this, EventArgs.Empty); }
        public bool SetHotkey(string action, uint modifiers, int key)
        {
            if (key == 0 || HasHotkeyConflict(action, modifiers, key)) return false; var old = Json.Clone(state); var k = new CrosshairHotkey { Modifiers = modifiers, Key = key }; if (action == "toggle") state.ToggleHotkey = k; else if (action == "next") state.NextPresetHotkey = k; else if (action == "previous") state.PreviousPresetHotkey = k; else { var b = state.PresetBindings.FirstOrDefault(x => x.PresetId == action) ?? new CrosshairHotkeyBinding { PresetId = action }; b.Hotkey = k; if (!state.PresetBindings.Contains(b)) state.PresetBindings.Add(b); } RegisterHotkeys(); if (!IsRegisteredFor(action)) { CopyState(old); RegisterHotkeys(); return false; } SaveState(); Changed?.Invoke(this, EventArgs.Empty); return true;
        }
        public void ClearHotkey(string action) { if (action == "toggle") state.ToggleHotkey = new(); else if (action == "next") state.NextPresetHotkey = new(); else if (action == "previous") state.PreviousPresetHotkey = new(); else state.PresetBindings.RemoveAll(x => x.PresetId == action); RegisterHotkeys(); SaveState(); Changed?.Invoke(this, EventArgs.Empty); }
        bool HasHotkeyConflict(string action, uint modifiers, int key)
        {
            bool Same(CrosshairHotkey x) => x?.IsSet == true && x.Modifiers == modifiers && x.Key == key;
            if (action != "toggle" && Same(state.ToggleHotkey) || action != "next" && Same(state.NextPresetHotkey) || action != "previous" && Same(state.PreviousPresetHotkey)) return true;
            return state.PresetBindings.Any(x => x.PresetId != action && Same(x.Hotkey));
        }
        bool IsRegisteredFor(string action)
        {
            int id = action == "toggle" ? ToggleId : action == "next" ? NextId : action == "previous" ? PreviousId : PresetBase + store.Items.OrderBy(x => x.SortOrder).ToList().FindIndex(x => x.Id == action); return id >= 0 && registered.Contains(id);
        }
        void CopyState(CrosshairState other) { state.Enabled = other.Enabled; state.ActivePresetId = other.ActivePresetId; state.SelectedMonitorId = other.SelectedMonitorId; state.CurrentDraft = other.CurrentDraft?.DeepClone() ?? Templates.Cross(); state.ToggleHotkey = other.ToggleHotkey?.Clone() ?? new(); state.NextPresetHotkey = other.NextPresetHotkey?.Clone() ?? new(); state.PreviousPresetHotkey = other.PreviousPresetHotkey?.Clone() ?? new(); state.PresetBindings = other.PresetBindings == null ? new() : Json.Clone(other.PresetBindings); state.SwitchPresetEnablesCrosshair = other.SwitchPresetEnablesCrosshair; runtimeSnapshot = state.CurrentDraft.DeepClone(); }
        public string HotkeyText(string action)
        {
            var k = action == "toggle" ? state.ToggleHotkey : action == "next" ? state.NextPresetHotkey : action == "previous" ? state.PreviousPresetHotkey : state.PresetBindings.FirstOrDefault(x => x.PresetId == action)?.Hotkey;
            if (k == null || !k.IsSet) return "未设置"; var s = ((k.Modifiers & 2) != 0 ? "Ctrl + " : "") + ((k.Modifiers & 1) != 0 ? "Alt + " : "") + ((k.Modifiers & 4) != 0 ? "Shift + " : "") + ((k.Modifiers & 8) != 0 ? "Win + " : ""); return s + ((Forms.Keys)k.Key);
        }
        public string ImportPng(string source) { var id = assets.ImportPng(source); AddElement(new CrosshairImage { Name = "PNG 图片", AssetId = id, Width = 32, Height = 32 }); return id; }
        public IEnumerable<string> MonitorIds() => Forms.Screen.AllScreens.Select(s => s.DeviceName);
        public void SelectMonitor(string id) { state.SelectedMonitorId = id ?? ""; ReconcileOverlayState(); SaveState(); Changed?.Invoke(this, EventArgs.Empty); }
        public void Reposition() => ReconcileOverlayState();
        public void BeginAdvancedPreview()
        {
            if (advancedPreviewSuspended) return;
            advancedPreviewSuspended = true;
            overlayVisibleBeforeAdvancedPreview = state.Enabled && overlay?.IsVisible == true;
            overlay?.SetScreenPositionEditMode(false);
            overlay?.Hide();
        }
        public void EndAdvancedPreview()
        {
            if (!advancedPreviewSuspended) return;
            advancedPreviewSuspended = false;
            if (state.Enabled && overlayVisibleBeforeAdvancedPreview) ReconcileOverlayState();
            else overlay?.Hide();
            overlayVisibleBeforeAdvancedPreview = false;
        }
        void EnsureOverlay()
        {
            if (overlay == null)
            {
                overlay = new CrosshairOverlayWindow(assets.PathFor);
                overlay.ScreenPositionDragStarted += () => BeginPositionDrag();
                overlay.ScreenPositionDragged += (dx, dy) => UpdateScreenPositionOnly(dx, dy);
                overlay.ScreenPositionDragEnded += () => EndPositionDrag();
                overlay.ClosedUnexpectedly += (_,__) => { if (!disposed) { overlay = null; Application.Current?.Dispatcher.BeginInvoke(new Action(ReconcileOverlayState)); } };
                Log("Crosshair overlay created");
            }
            overlay.SetScreenPositionEditMode(screenPositionEditing); overlay.Apply(RuntimeSnapshot); overlay.SetMonitor(state.SelectedMonitorId); overlay.ShowOverlay();
        }
        public void ReconcileOverlayState()
        {
            if (advancedPreviewSuspended) { overlay?.SetScreenPositionEditMode(false); overlay?.Hide(); return; }
            if (!state.Enabled) { overlay?.SetScreenPositionEditMode(false); overlay?.Hide(); return; }
            EnsureOverlay(); overlay.SetScreenPositionEditMode(screenPositionEditing); overlay.SetMonitor(state.SelectedMonitorId); overlay.Reposition(); overlay.ShowOverlay();
        }
        void SaveState() { state.Normalize(); Json.SaveAtomic(statePath, state); }
        void Log(string message) { try { Directory.CreateDirectory(Path.GetDirectoryName(logPath)!); File.AppendAllText(logPath, DateTime.Now.ToString("s") + " " + message + Environment.NewLine, Encoding.UTF8); } catch { } }
        public void Dispose() { if (disposed) return; disposed = true; state.Enabled = false; SaveState(); UnregisterHotkeys(); overlay?.Dispose(); overlay = null; Log("Crosshair disposed"); }
    }

    internal sealed class CrosshairVisual : FrameworkElement
    {
        CrosshairDocument document = Templates.Cross(); readonly Func<string, string> resolveAsset; Rect bounds = new(-32, -32, 64, 64); double pixelToDip = 1; string selectedElementId = ""; bool fixedDocumentOrigin; double viewportWidth = 640; double viewportHeight = 350;
        public CrosshairVisual(Func<string, string> assetResolver) { resolveAsset = assetResolver; SnapsToDevicePixels = true; UseLayoutRounding = true; }
        public string SelectedElementId { get => selectedElementId; set { selectedElementId = value ?? ""; InvalidateVisual(); } }
        public bool FixedDocumentOrigin { get => fixedDocumentOrigin; set { if (fixedDocumentOrigin == value) return; fixedDocumentOrigin = value; RebuildLayoutBounds(); InvalidateVisual(); } }
        public Rect DocumentBounds => bounds;
        public double PixelToDip => pixelToDip;
        public void SetViewportSize(double width, double height) { if (width > 0) viewportWidth = width; if (height > 0) viewportHeight = height; if (fixedDocumentOrigin) { RebuildLayoutBounds(); InvalidateVisual(); } }
        public void SetDocument(CrosshairDocument d, double dpi) { document = d == null ? Templates.Cross() : Json.Clone(d); pixelToDip = 96 / Math.Max(96, dpi); RebuildLayoutBounds(); InvalidateVisual(); }
        void RebuildLayoutBounds()
        {
            if (fixedDocumentOrigin)
            {
                // Advanced editing uses a stable viewport coordinate system:
                // document (0,0) is always the visual center. Content bounds
                // never recenter sibling elements after one element moves.
                var documentWidth = Math.Max(1, viewportWidth / pixelToDip);
                var documentHeight = Math.Max(1, viewportHeight / pixelToDip);
                bounds = new Rect(8 - documentWidth / 2, 8 - documentHeight / 2, documentWidth, documentHeight);
                Width = Math.Max(1, viewportWidth);
                Height = Math.Max(1, viewportHeight);
                return;
            }
            bounds = CrosshairRenderer.Bounds(document);
            Width = Math.Max(1, bounds.Width * pixelToDip + 16 * pixelToDip);
            Height = Math.Max(1, bounds.Height * pixelToDip + 16 * pixelToDip);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (fixedDocumentOrigin) CrosshairRenderer.RenderAtDocumentOrigin(dc, document, pixelToDip, resolveAsset);
            else CrosshairRenderer.RenderCenteredAtDocumentOrigin(dc, document, pixelToDip, ActualWidth, ActualHeight, resolveAsset);
            var selected = CrosshairRenderer.SelectedBounds(document, selectedElementId);
            if (!selected.IsEmpty)
            {
                var penBrush = new SolidColorBrush(Color.FromArgb(220, 255, 170, 72)); penBrush.Freeze();
                var pen = new Pen(penBrush, Math.Max(1, pixelToDip)); pen.Freeze();
                var rect = fixedDocumentOrigin
                    ? new Rect(selected.Left * pixelToDip, selected.Top * pixelToDip, selected.Width * pixelToDip, selected.Height * pixelToDip)
                    : new Rect((selected.Left - bounds.Left + 8) * pixelToDip, (selected.Top - bounds.Top + 8) * pixelToDip, selected.Width * pixelToDip, selected.Height * pixelToDip);
                dc.DrawRectangle(null, pen, rect);
            }
        }
    }

    internal static class CrosshairRenderer
    {
        public static Rect GetElementDocumentBounds(CrosshairDocument d, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return Rect.Empty; var element = d.Elements.FirstOrDefault(e => e.Id == id && e.Enabled); if (element == null) return Rect.Empty; var rect = Rect.Empty; foreach (var mirror in Mirrors(element)) rect.Union(TransformRect(ElementBounds(element), ElementToDocumentMatrix(d, element, mirror))); return rect;
        }
        public static Rect SelectedBounds(CrosshairDocument d, string id) => GetElementDocumentBounds(d, id);
        public static Rect Bounds(CrosshairDocument d)
        {
            var rect = Rect.Empty;
            if (d == null) return new Rect(-16, -16, 32, 32);
            foreach (var e in d.Elements?.Where(e => e?.Enabled == true) ?? Enumerable.Empty<CrosshairElement>())
                foreach (var mirror in Mirrors(e)) rect.Union(TransformRect(ElementBounds(e), ElementToDocumentMatrix(d, e, mirror)));
            if (rect.IsEmpty) rect = new Rect(-8, -8, 16, 16);
            rect.Inflate(8, 8);
            // Runtime overlays are centered on the document origin. Keep the
            // window symmetric even when an element is intentionally offset.
            var halfWidth = Math.Max(Math.Abs(rect.Left), Math.Abs(rect.Right));
            var halfHeight = Math.Max(Math.Abs(rect.Top), Math.Abs(rect.Bottom));
            return new Rect(-halfWidth, -halfHeight, halfWidth * 2, halfHeight * 2);
        }
        // Document origin is permanently (0,0). This order mirrors Render:
        // element-local transform -> element position -> mirror -> document rotation -> global scale.
        // Bounds are window sizing data only and never become a transform anchor.
        static Matrix ElementToDocumentMatrix(CrosshairDocument d, CrosshairElement e, (bool x, bool y) mirror)
        {
            var matrix = new Matrix();
            matrix.Scale(e.Scale, e.Scale);
            matrix.Rotate(e.Rotation);
            matrix.Translate(e.X, e.Y);
            matrix.Scale(mirror.x ? -1 : 1, mirror.y ? -1 : 1);
            matrix.Rotate(d.GlobalRotation);
            matrix.Scale(d.GlobalCrosshairScale, d.GlobalCrosshairScale);
            return matrix;
        }
        static Rect ElementBounds(CrosshairElement e) => e switch { CrosshairLine l => RectFromPoints(new Point(l.StartX, l.StartY), new Point(l.EndX, l.EndY), l.Thickness / 2 + (l.OutlineEnabled ? l.OutlineThickness : 0)), CrosshairDot x => new Rect(-x.Diameter / 2 - x.OutlineThickness, -x.Diameter / 2 - x.OutlineThickness, x.Diameter + x.OutlineThickness * 2, x.Diameter + x.OutlineThickness * 2), CrosshairRing x => new Rect(-x.Diameter / 2 - x.Thickness / 2, -x.Diameter / 2 - x.Thickness / 2, x.Diameter + x.Thickness, x.Diameter + x.Thickness), CrosshairRectangle x => new Rect(-x.Width / 2 - x.StrokeThickness, -x.Height / 2 - x.StrokeThickness, x.Width + x.StrokeThickness * 2, x.Height + x.StrokeThickness * 2), CrosshairImage x => new Rect(-x.Width / 2, -x.Height / 2, x.Width, x.Height), _ => new Rect(-1, -1, 2, 2) };
        static Rect RectFromPoints(Point a, Point b, double pad) { var r = new Rect(a, b); r.Inflate(pad, pad); return r; }
        static Rect TransformRect(Rect r, Matrix m) { var p1 = m.Transform(new Point(r.Left, r.Top)); var p2 = m.Transform(new Point(r.Right, r.Top)); var p3 = m.Transform(new Point(r.Right, r.Bottom)); var p4 = m.Transform(new Point(r.Left, r.Bottom)); return new Rect(new[] { p1.X, p2.X, p3.X, p4.X }.Min(), new[] { p1.Y, p2.Y, p3.Y, p4.Y }.Min(), new[] { p1.X, p2.X, p3.X, p4.X }.Max() - new[] { p1.X, p2.X, p3.X, p4.X }.Min(), new[] { p1.Y, p2.Y, p3.Y, p4.Y }.Max() - new[] { p1.Y, p2.Y, p3.Y, p4.Y }.Min()); }
        static IEnumerable<(bool x, bool y)> Mirrors(CrosshairElement e) { yield return (false, false); if (e.MirrorX) yield return (true, false); if (e.MirrorY) yield return (false, true); if (e.MirrorX && e.MirrorY) yield return (true, true); }
        public static void Render(DrawingContext dc, CrosshairDocument d, Rect bounds, double p2d, Func<string, string> resolveAsset)
        {
            dc.PushTransform(new TranslateTransform((-bounds.Left + 8) * p2d, (-bounds.Top + 8) * p2d)); dc.PushTransform(new ScaleTransform(d.GlobalCrosshairScale, d.GlobalCrosshairScale)); dc.PushTransform(new RotateTransform(d.GlobalRotation));
            foreach (var e in d.Elements.OrderBy(e => e.ZIndex).Where(e => e.Enabled)) foreach (var mirror in Mirrors(e)) { dc.PushTransform(new ScaleTransform(mirror.x ? -1 : 1, mirror.y ? -1 : 1)); dc.PushTransform(new TranslateTransform(e.X, e.Y)); dc.PushTransform(new RotateTransform(e.Rotation)); dc.PushTransform(new ScaleTransform(e.Scale, e.Scale)); DrawElement(dc, e, p2d, d.GlobalOpacity, resolveAsset); dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop(); }
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        public static void RenderAtDocumentOrigin(DrawingContext dc, CrosshairDocument d, double p2d, Func<string, string> resolveAsset)
        {
            dc.PushTransform(new ScaleTransform(d.GlobalCrosshairScale, d.GlobalCrosshairScale)); dc.PushTransform(new RotateTransform(d.GlobalRotation));
            foreach (var e in d.Elements.OrderBy(e => e.ZIndex).Where(e => e.Enabled))
                foreach (var mirror in Mirrors(e))
                {
                    dc.PushTransform(new ScaleTransform(mirror.x ? -1 : 1, mirror.y ? -1 : 1)); dc.PushTransform(new TranslateTransform(e.X, e.Y)); dc.PushTransform(new RotateTransform(e.Rotation)); dc.PushTransform(new ScaleTransform(e.Scale, e.Scale));
                    DrawElement(dc, e, p2d, d.GlobalOpacity, resolveAsset); dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
                }
            dc.Pop(); dc.Pop();
        }
        public static void RenderCenteredAtDocumentOrigin(DrawingContext dc, CrosshairDocument d, double p2d, double widthDip, double heightDip, Func<string, string> resolveAsset)
        {
            dc.PushTransform(new TranslateTransform(widthDip / 2, heightDip / 2));
            RenderAtDocumentOrigin(dc, d, p2d, resolveAsset);
            dc.Pop();
        }
        static Pen Pen(string hex, double width, double opacity, double p2d) { var b = new SolidColorBrush(Parse(hex)); b.Opacity = opacity; b.Freeze(); var p = new Pen(b, Math.Max(.5, width * p2d)); p.Freeze(); return p; }
        static void DrawElement(DrawingContext dc, CrosshairElement e, double p2d, double globalOpacity, Func<string, string> resolveAsset)
        {
            double op = Math.Clamp(e.Opacity * globalOpacity, 0, 1); switch (e)
            {
                case CrosshairLine l: if (l.OutlineEnabled) dc.DrawLine(Pen(l.OutlineColorHex, l.Thickness + l.OutlineThickness * 2, op, p2d), new Point(l.StartX * p2d, l.StartY * p2d), new Point(l.EndX * p2d, l.EndY * p2d)); dc.DrawLine(Pen(l.ColorHex, l.Thickness, op, p2d), new Point(l.StartX * p2d, l.StartY * p2d), new Point(l.EndX * p2d, l.EndY * p2d)); break;
                case CrosshairDot x: var r = x.Diameter * p2d / 2; var brush = new SolidColorBrush(Parse(x.ColorHex)); brush.Opacity = op; brush.Freeze(); if (x.Shape == CrosshairDotShape.Circle) dc.DrawEllipse(brush, x.OutlineEnabled ? Pen(x.OutlineColorHex, x.OutlineThickness, op, p2d) : null, new Point(0, 0), r, r); else dc.DrawRectangle(brush, x.OutlineEnabled ? Pen(x.OutlineColorHex, x.OutlineThickness, op, p2d) : null, new Rect(-r, -r, r * 2, r * 2)); break;
                case CrosshairRing x: dc.DrawEllipse(null, Pen(x.ColorHex, x.Thickness, op, p2d), new Point(0, 0), x.Diameter * p2d / 2, x.Diameter * p2d / 2); break;
                case CrosshairRectangle x: var fill = x.FillEnabled ? new SolidColorBrush(Parse(x.FillColorHex)) : null; if (fill != null) { fill.Opacity = op; fill.Freeze(); } dc.DrawRoundedRectangle(fill, x.StrokeEnabled ? Pen(x.StrokeColorHex, x.StrokeThickness, op, p2d) : null, new Rect(-x.Width * p2d / 2, -x.Height * p2d / 2, x.Width * p2d, x.Height * p2d), x.CornerRadius * p2d, x.CornerRadius * p2d); break;
                case CrosshairImage x: var path = resolveAsset(x.AssetId); if (File.Exists(path)) { var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.UriSource = new Uri(path); bmp.EndInit(); bmp.Freeze(); dc.DrawImage(bmp, new Rect(-x.Width * p2d / 2, -x.Height * p2d / 2, x.Width * p2d, x.Height * p2d)); } break;
            }
        }
        static System.Windows.Media.Color Parse(string value) { try { return (System.Windows.Media.Color)ColorConverter.ConvertFromString(value ?? "#FFFFFFFF"); } catch { return Colors.White; } }
    }

    internal sealed class CrosshairOverlayWindow : Window, IDisposable
    {
        const int GWL_EXSTYLE = -20; const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOOLWINDOW = 0x80; const int HWND_TOPMOST = -1; const int PositionHitAreaPixels = 96; const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_FRAMECHANGED = 0x20; const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0; readonly Func<string, string> resolveAsset; readonly CrosshairVisual visual; readonly Grid screenPositionDragSurface; readonly ShapeRectangle screenPositionHitSurface; readonly ShapeRectangle editOutline; readonly WinEventDelegate foregroundDelegate; HwndSource hwndSource; IntPtr hook; bool windowHooked, disposing, screenPositionDragging; string monitorId = ""; CrosshairDocument document = Templates.Cross(); CrosshairOverlayInteractionMode interactionMode; double dpiScale = 1; int windowWidthPixels, windowHeightPixels; Point screenDragStartCursor;
        enum CrosshairOverlayInteractionMode { NormalClickThrough, ScreenPositionEditing }
        public event Action ScreenPositionDragStarted;
        public event Action<double, double> ScreenPositionDragged;
        public event Action ScreenPositionDragEnded;
        public event EventHandler ClosedUnexpectedly;
        public CrosshairOverlayWindow(Func<string, string> resolver)
        {
            resolveAsset = resolver; WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight; ShowActivated = false; Focusable = false; Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual; Opacity = 1; interactionMode = CrosshairOverlayInteractionMode.NormalClickThrough; visual = new CrosshairVisual(resolveAsset) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }; screenPositionDragSurface = new Grid { Background = Brushes.Transparent, Cursor = Cursors.Arrow, IsHitTestVisible = true, Focusable = false }; screenPositionHitSurface = new ShapeRectangle { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Opacity = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = true, Cursor = Cursors.SizeAll, Visibility = Visibility.Collapsed }; editOutline = new ShapeRectangle { Stroke = new SolidColorBrush(Color.FromArgb(170, 255, 170, 72)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, Visibility = Visibility.Collapsed }; Panel.SetZIndex(visual, 1); Panel.SetZIndex(editOutline, 2); Panel.SetZIndex(screenPositionHitSurface, 100); screenPositionDragSurface.Children.Add(visual); screenPositionDragSurface.Children.Add(editOutline); screenPositionDragSurface.Children.Add(screenPositionHitSurface); Content = screenPositionDragSurface; screenPositionHitSurface.PreviewMouseLeftButtonDown += OnScreenPositionMouseDown; screenPositionHitSurface.PreviewMouseMove += OnScreenPositionMouseMove; screenPositionHitSurface.PreviewMouseLeftButtonUp += OnScreenPositionMouseUp; SourceInitialized += (_,__) => ConfigureWindow(); Closed += (_,__) => { if (!disposing) ClosedUnexpectedly?.Invoke(this, EventArgs.Empty); }; foregroundDelegate = (_,__,___,____,_____,______,_______) => Dispatcher.BeginInvoke(new Action(EnsureTopmost)); hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, foregroundDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        }
        public void Apply(CrosshairDocument d) { document = d?.DeepClone() ?? Templates.Cross(); var dpi = 96 * (PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1); visual.SetDocument(document, dpi); UpdateHitArea(dpi); if (IsVisible) Reposition(); }
        // Fast path for ScreenPositionDrag: the crosshair visual and hit surface
        // stay unchanged; only the top-level overlay HWND position is updated.
        public void UpdateScreenPositionOnly(double offsetX, double offsetY) { document.OffsetX = offsetX; document.OffsetY = offsetY; if (IsVisible) SetScreenPositionWindowPosition(offsetX, offsetY); }
        public void SetMonitor(string id) { monitorId = id ?? ""; }
        public void ShowOverlay() { if (!IsVisible) Show(); ConfigureWindow(); Reposition(); EnsureTopmost(); }
        public void SetScreenPositionEditMode(bool enabled)
        {
            var nextMode = enabled ? CrosshairOverlayInteractionMode.ScreenPositionEditing : CrosshairOverlayInteractionMode.NormalClickThrough;
            if (interactionMode == nextMode) return;
            interactionMode = nextMode; screenPositionHitSurface.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; editOutline.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed; screenPositionDragSurface.Cursor = enabled ? Cursors.SizeAll : Cursors.Arrow;
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) { var style = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64(); style = enabled ? style & ~WS_EX_TRANSPARENT : style | WS_EX_TRANSPARENT; SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(style)); SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED); }
            if (!enabled) { var wasDragging = screenPositionDragging; screenPositionDragging = false; screenPositionHitSurface.ReleaseMouseCapture(); if (wasDragging) ScreenPositionDragEnded?.Invoke(); }
        }
        // IMPORTANT: Screen Position Drag and Structural Element Drag are intentionally isolated.
        // These handlers operate only on the real overlay HWND and physical screen pixels.
        void OnScreenPositionMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (interactionMode != CrosshairOverlayInteractionMode.ScreenPositionEditing || screenPositionDragging || e.ChangedButton != MouseButton.Left || !GetCursorPos(out var p)) return;
            screenDragStartCursor = new Point(p.X, p.Y);
            screenPositionDragging = true;
            if (!screenPositionHitSurface.CaptureMouse()) { screenPositionDragging = false; return; }
            ScreenPositionDragStarted?.Invoke();
            e.Handled = true;
        }
        void OnScreenPositionMouseMove(object sender, MouseEventArgs e)
        {
            if (interactionMode != CrosshairOverlayInteractionMode.ScreenPositionEditing || !screenPositionDragging || !GetCursorPos(out var p)) return;
            ScreenPositionDragged?.Invoke(p.X - screenDragStartCursor.X, p.Y - screenDragStartCursor.Y);
        }
        void OnScreenPositionMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (interactionMode != CrosshairOverlayInteractionMode.ScreenPositionEditing || !screenPositionDragging || e.ChangedButton != MouseButton.Left) return;
            screenPositionDragging = false;
            screenPositionHitSurface.ReleaseMouseCapture();
            ScreenPositionDragEnded?.Invoke();
            e.Handled = true;
        }
        public void Reposition()
        {
            var screen = GetTargetScreen(); if (screen == null || !IsVisible) return;
            var dpi = 96 * (PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1); dpiScale = Math.Max(1, dpi / 96);
            var widthDip = Math.Max(visual.Width, interactionMode == CrosshairOverlayInteractionMode.ScreenPositionEditing ? screenPositionHitSurface.Width : 0); var heightDip = Math.Max(visual.Height, interactionMode == CrosshairOverlayInteractionMode.ScreenPositionEditing ? screenPositionHitSurface.Height : 0);
            windowWidthPixels = Math.Max(1, (int)Math.Ceiling(widthDip * dpiScale)); windowHeightPixels = Math.Max(1, (int)Math.Ceiling(heightDip * dpiScale));
            SetScreenPositionWindowPosition(document.OffsetX, document.OffsetY);
        }
        Forms.Screen GetTargetScreen() => Forms.Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, monitorId, StringComparison.OrdinalIgnoreCase)) ?? Forms.Screen.PrimaryScreen;
        void SetScreenPositionWindowPosition(double offsetX, double offsetY)
        {
            var screen = GetTargetScreen(); var handle = new WindowInteropHelper(this).Handle;
            if (screen == null || handle == IntPtr.Zero || !IsVisible || windowWidthPixels <= 0 || windowHeightPixels <= 0) return;
            var x = screen.Bounds.Left + screen.Bounds.Width / 2 - windowWidthPixels / 2 + (int)Math.Round(offsetX);
            var y = screen.Bounds.Top + screen.Bounds.Height / 2 - windowHeightPixels / 2 + (int)Math.Round(offsetY);
            SetWindowPos(handle, new IntPtr(HWND_TOPMOST), x, y, windowWidthPixels, windowHeightPixels, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
        void ConfigureWindow() { var h = new WindowInteropHelper(this).Handle; if (h == IntPtr.Zero) return; var style = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64(); style |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW; style = interactionMode == CrosshairOverlayInteractionMode.ScreenPositionEditing ? style & ~WS_EX_TRANSPARENT : style | WS_EX_TRANSPARENT; SetWindowLongPtr(h, GWL_EXSTYLE, new IntPtr(style)); if (!windowHooked) { hwndSource = HwndSource.FromHwnd(h); hwndSource?.AddHook(WindowHook); windowHooked = true; } }
        IntPtr WindowHook(IntPtr h, int message, IntPtr wParam, IntPtr lParam, ref bool handled) { if (message == 0x02E0 || message == 0x007E) { var dpi = 96 * (PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1); visual.SetDocument(document, dpi); UpdateHitArea(dpi); Reposition(); } return IntPtr.Zero; }
        void UpdateHitArea(double dpi) { dpiScale = Math.Max(1, dpi / 96); var hitDip = PositionHitAreaPixels / dpiScale; screenPositionHitSurface.Width = hitDip; screenPositionHitSurface.Height = hitDip; editOutline.Width = hitDip; editOutline.Height = hitDip; }
        public void EnsureTopmost() { if (IsVisible) SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(HWND_TOPMOST), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); }
        public void Dispose() { disposing = true; screenPositionDragging = false; screenPositionHitSurface.ReleaseMouseCapture(); if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; } if (hwndSource != null) { hwndSource.RemoveHook(WindowHook); hwndSource = null; } if (IsLoaded) Close(); }
        [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X; public int Y; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong); [DllImport("user32.dll", SetLastError = true)] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex); [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags); delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId, uint threadId, uint time); [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint process, uint thread, uint flags); [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    }
}
