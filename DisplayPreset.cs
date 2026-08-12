using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("ShadePilot BETA 0.1.5")]
[assembly: System.Reflection.AssemblyProduct("ShadePilot")]
[assembly: System.Reflection.AssemblyDescription("ShadePilot display and color control beta")]
[assembly: System.Reflection.AssemblyVersion("0.1.5.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.1.5.0")]

namespace DisplayPresetPrototype
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var instance = new Mutex(true, @"Local\ShadePilot.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show(
                        "ShadePilot 已经在运行，请检查任务栏托盘。",
                        "ShadePilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                ThemeManager.Load();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private static Color Canvas { get { return ThemeManager.Current.Canvas; } }
        private static Color Card { get { return ThemeManager.Current.Surface; } }
        private static Color Ink { get { return ThemeManager.Current.Ink; } }
        private static Color Muted { get { return ThemeManager.Current.Muted; } }
        private static Color Accent { get { return ThemeManager.Current.Accent; } }
        private readonly ComboBox monitorBox = new ThemedComboBox();
        private readonly TrackBar brightness = NewSlider();
        private readonly TrackBar contrast = NewSlider();
        private readonly TrackBar saturation = NewSlider();
        private readonly Label saturationTitle = NewLabel("硬件饱和度", 42, 308);
        private readonly Label brightnessValue = new Label();
        private readonly Label contrastValue = new Label();
        private readonly Label saturationValue = new Label();
        private readonly Label saturationHint = new Label();
        private readonly Label status = new Label();
        private readonly CheckBox restoreOnExit = new CheckBox();
        private readonly CheckBox liveApply = new CheckBox();
        private readonly System.Windows.Forms.Timer liveApplyTimer = new System.Windows.Forms.Timer();
        private readonly GammaRampController gammaController = new GammaRampController();
        private readonly FullscreenColorController softwareSaturation =
            new FullscreenColorController();
        private readonly StyleSettingsStore styleStore = StyleSettingsStore.Load();
        private readonly AppSettingsStore appSettings = AppSettingsStore.Load();
        private readonly TrackBar styleShadows = NewStyleSlider(0, 200);
        private readonly TrackBar styleHighlights = NewStyleSlider(0, 200);
        private readonly TrackBar styleMidtones = NewStyleSlider(-100, 100);
        private readonly TrackBar styleBlackPoint = NewStyleSlider(-100, 100);
        private readonly TrackBar styleWhitePoint = NewStyleSlider(-100, 100);
        private readonly TrackBar styleContrastPivot = NewStyleSlider(-100, 100);
        private readonly TrackBar styleTemperature = NewStyleSlider(-100, 100);
        private readonly TrackBar styleTint = NewStyleSlider(-100, 100);
        private readonly TrackBar styleVibrance = NewStyleSlider(-100, 100);
        private readonly TrackBar styleExposure = NewStyleSlider(-100, 100);
        private readonly TrackBar styleShadowRange = NewStyleSlider(-100, 100);
        private readonly TrackBar styleHighlightRange = NewStyleSlider(-100, 100);
        private readonly TrackBar styleTransitionSoftness = NewStyleSlider(-100, 100);
        private readonly Label styleShadowValue = NewValuePill();
        private readonly Label styleHighlightValue = NewValuePill();
        private readonly Label styleMidtoneValue = NewValuePill();
        private readonly Label styleBlackPointValue = NewValuePill();
        private readonly Label styleWhitePointValue = NewValuePill();
        private readonly Label styleContrastPivotValue = NewValuePill();
        private readonly Label styleTemperatureValue = NewValuePill();
        private readonly Label styleTintValue = NewValuePill();
        private readonly Label styleVibranceValue = NewValuePill();
        private readonly Label styleExposureValue = NewValuePill();
        private readonly Label styleShadowRangeValue = NewValuePill();
        private readonly Label styleHighlightRangeValue = NewValuePill();
        private readonly Label styleTransitionSoftnessValue = NewValuePill();
        private readonly ComboBox stylePresetBox = new ThemedComboBox();
        private readonly ComboBox strengthBox = new ThemedComboBox();
        private readonly ComboBox themeBox = new ThemedComboBox();
        private readonly Label styleRiskStatus = new Label();
        private readonly System.Windows.Forms.Timer stylePreviewTimer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly ToolStripMenuItem trayStyleStatus = new ToolStripMenuItem();
        private readonly ToolStripMenuItem trayMonitorStatus = new ToolStripMenuItem();
        private readonly ToolStripMenuItem trayHotkeyStatus = new ToolStripMenuItem();
        private readonly System.Windows.Forms.Timer recoveryTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer gameRuleTimer = new System.Windows.Forms.Timer();
        private readonly ToolTip helpTip = new ToolTip();
        private readonly Dictionary<int, StylePresetData> hotkeyMap = new Dictionary<int, StylePresetData>();
        private readonly Dictionary<string, string> hotkeyRegistrationStatus = new Dictionary<string, string>();
        private readonly Dictionary<TrackBar, Label> styleValueLabels = new Dictionary<TrackBar, Label>();
        private readonly Dictionary<TrackBar, int> styleBaselineValues = new Dictionary<TrackBar, int>();
        private readonly Dictionary<TrackBar, string> styleSliderNames = new Dictionary<TrackBar, string>();
        private readonly List<PhysicalDisplay> displays = new List<PhysicalDisplay>();
        private readonly Dictionary<string, Snapshot> startup = new Dictionary<string, Snapshot>();
        private bool closing;
        private bool pendingBrightness;
        private bool pendingContrast;
        private bool pendingSaturation;
        private bool liveWorkerRunning;
        private bool displayResetRunning;
        private bool hotkeysRegistered;
        private bool trayHintShown;
        private bool settingStyleControls;
        private bool usingSoftwareSaturation;
        private StylePresetData activeStyle;
        private int cyclePresetIndex = -1;
        private int registeredHotkeyCount;
        private StylePresetData temporaryA;
        private StylePresetData temporaryB;
        private string lastAutoProcess;
        private StylePresetData styleBeforeAutoSwitch;
        private bool autoSwitchActive;
        private StylePresetData styleBeforeHold;
        private bool holdOriginalActive;
        private GlobalKeyWatcher holdWatcher;

        private const int WmHotkey = 0x0312;
        private const int HotkeyStyleBase = 2100;
        private const int HotkeyRestore = 2199;
        private const int HotkeyCycle = 2198;

        public MainForm()
        {
            Text = "ShadePilot BETA 0.1.5";
            ClientSize = new Size(700, 760);
            MinimumSize = new Size(700, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Canvas;
            // All coordinates below are authored at 96 DPI.  Without an
            // explicit design baseline WinForms can infer it from the machine
            // that first creates the form, then scale fonts and controls by
            // different factors on 125%-200% displays.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            AutoScrollMinSize = new Size(0, 1216);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var logo = new PictureBox {
                Location = new Point(28, 17), Size = new Size(42, 42),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Canvas,
                Image = Icon == null ? SystemIcons.Application.ToBitmap() : Icon.ToBitmap(),
                Tag = "CanvasLogo"
            };

            var title = new Label {
                Text = "ShadePilot",
                ForeColor = Ink,
                Font = new Font(Font.FontFamily, 19F, FontStyle.Bold),
                AutoSize = false, Location = new Point(80, 14),
                Size = new Size(325, 42), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var safety = new Label {
                Text = "明暗与色彩控制台",
                ForeColor = Muted,
                AutoSize = false, Location = new Point(83, 55),
                Size = new Size(322, 24), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            var themeLabel = new Label {
                Text = "界面主题", AutoSize = false, Location = new Point(540, 14),
                Size = new Size(130, 24), TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Muted, BackColor = Canvas,
                Font = new Font("Microsoft YaHei UI", 8F)
            };
            themeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            themeBox.FlatStyle = FlatStyle.Flat;
            themeBox.Location = new Point(540, 39);
            themeBox.Size = new Size(130, 27);
            foreach (var theme in ThemeManager.Themes) themeBox.Items.Add(theme);

            monitorBox.DropDownStyle = ComboBoxStyle.DropDownList;
            monitorBox.FlatStyle = FlatStyle.Flat;
            monitorBox.BackColor = Color.White;
            monitorBox.Location = new Point(42, 118);
            monitorBox.Size = new Size(400, 28);
            monitorBox.SelectedIndexChanged += delegate { LoadSelectedMonitor(); };
            var refresh = NewButton("重新检测", 454, 115, 92);
            refresh.Click += delegate { RefreshDisplays(); };

            ConfigureSlider(brightness, 42, 190, 570);
            ConfigureSlider(contrast, 42, 262, 570);
            ConfigureSlider(saturation, 42, 334, 570);
            brightness.Scroll += delegate {
                brightnessValue.Text = brightness.Value.ToString();
                QueueLiveApply(true, false);
            };
            contrast.Scroll += delegate {
                contrastValue.Text = contrast.Value.ToString();
                QueueLiveApply(false, true);
            };
            saturation.Scroll += delegate {
                saturationValue.Text = saturation.Value.ToString();
                QueueLiveApply(false, false, true);
            };
            brightness.MouseUp += delegate { FlushLiveApply(); };
            contrast.MouseUp += delegate { FlushLiveApply(); };
            saturation.MouseUp += delegate { FlushLiveApply(); };

            saturationHint.Text = "当前显示器不支持硬件饱和度";
            saturationHint.AutoSize = false;
            saturationHint.Location = new Point(255, 337);
            saturationHint.Size = new Size(300, 24);
            saturationHint.TextAlign = ContentAlignment.MiddleLeft;
            saturationHint.AutoEllipsis = true;
            saturationHint.ForeColor = Muted;
            saturationHint.BackColor = Color.White;
            saturationHint.Visible = false;

            liveApply.Text = "拖动时实时应用";
            liveApply.Checked = true;
            liveApply.AutoSize = false;
            liveApply.Size = new Size(240, 30);
            liveApply.ForeColor = Muted;
            liveApply.BackColor = Color.White;
            liveApply.Location = new Point(42, 405);
            liveApply.CheckedChanged += delegate {
                if (!liveApply.Checked) liveApplyTimer.Stop();
            };

            liveApplyTimer.Interval = 180;
            liveApplyTimer.Tick += delegate {
                liveApplyTimer.Stop();
                ApplyPendingSliders();
            };

            var apply = NewButton("应用全部", 538, 402, 112);
            apply.BackColor = Accent;
            apply.ForeColor = Color.White;
            apply.Click += delegate { ApplyCurrent(); };

            var restore = NewButton("重置参数", 558, 115, 92);
            restore.SurfaceColor = Color.White;
            restore.Click += delegate { RestoreStartup(); };
            restoreOnExit.Text = "退出时自动恢复";
            restoreOnExit.Checked = true;
            restoreOnExit.AutoSize = false;
            restoreOnExit.Size = new Size(260, 30);
            restoreOnExit.ForeColor = Muted;
            restoreOnExit.BackColor = Color.FromArgb(249, 250, 253);
            restoreOnExit.Location = new Point(42, 1028);

            var restoreStyle = NewButton("重置风格", 545, 459, 105);
            restoreStyle.SurfaceColor = Color.FromArgb(249, 248, 255);
            restoreStyle.BackColor = Color.FromArgb(240, 237, 255);
            restoreStyle.ForeColor = Accent;
            restoreStyle.Click += delegate { RestoreStyle(); };
            var curvePreview = NewButton("曲线预览", 435, 459, 100);
            curvePreview.SurfaceColor = Color.FromArgb(249, 248, 255);
            curvePreview.Click += delegate {
                using (var dialog = new CurvePreviewForm(
                    styleShadows.Value, styleHighlights.Value, styleMidtones.Value,
                    styleBlackPoint.Value, styleWhitePoint.Value, styleContrastPivot.Value,
                    styleVibrance.Value, styleExposure.Value, styleShadowRange.Value,
                    styleHighlightRange.Value, styleTransitionSoftness.Value))
                    dialog.ShowDialog(this);
            };
            var optimizeStyle = NewButton("一键优化", 545, 899, 105);
            optimizeStyle.SurfaceColor = ThemeManager.Current.TintSurface;
            optimizeStyle.BackColor = Color.FromArgb(240, 237, 255);
            optimizeStyle.ForeColor = Accent;
            optimizeStyle.Click += delegate { OptimizeCurrentStyle(); };
            styleRiskStatus.Location = new Point(42, 894);
            styleRiskStatus.Size = new Size(490, 48);
            styleRiskStatus.AutoEllipsis = true;
            styleRiskStatus.ForeColor = Muted;
            styleRiskStatus.BackColor = ThemeManager.Current.TintSurface;
            styleRiskStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            styleRiskStatus.Padding = new Padding(0, 3, 0, 0);

            status.Location = new Point(30, 1116);
            status.Size = new Size(640, 24);
            status.ForeColor = Color.FromArgb(45, 90, 55);
            var authorFooter = new Label {
                Text = "BY XD", AutoSize = true, Location = new Point(34, 1158),
                ForeColor = Muted, BackColor = Canvas,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                Tag = "CanvasMuted"
            };
            var sharingFooter = new Label {
                Text = "免费分享，倒卖必究", AutoSize = true, Location = new Point(535, 1158),
                ForeColor = Muted, BackColor = Canvas,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                Tag = "CanvasMuted"
            };

            var mainCard = NewCard(24, 91, 652, 1004);
            mainCard.CornerRadius = 22;
            var styleCard = NewCard(34, 447, 632, 554);
            styleCard.CornerRadius = 18;
            styleCard.FillColor = ThemeManager.Current.TintSurface;
            var actionCard = NewCard(34, 1013, 632, 72);
            actionCard.CornerRadius = 14;
            actionCard.FillColor = ThemeManager.Current.SoftSurface;

            stylePresetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            stylePresetBox.FlatStyle = FlatStyle.Flat;
            stylePresetBox.BackColor = Color.White;
            stylePresetBox.Location = new Point(42, 519);
            stylePresetBox.Size = new Size(410, 28);
            foreach (var style in styleStore.Items) stylePresetBox.Items.Add(style);
            stylePresetBox.SelectedIndex = 0;
            stylePresetBox.Size = new Size(230, 28);
            strengthBox.DropDownStyle = ComboBoxStyle.DropDownList;
            strengthBox.FlatStyle = FlatStyle.Flat;
            strengthBox.BackColor = Color.White;
            strengthBox.Location = new Point(300, 459);
            strengthBox.Size = new Size(120, 27);
            strengthBox.Items.AddRange(new object[] { "柔和", "标准", "极限" });
            strengthBox.SelectedIndex = Clamp(appSettings.StrengthMode, 0, 2);
            ApplyStrengthMode(strengthBox.SelectedIndex);
            strengthBox.SelectedIndexChanged += delegate {
                ApplyStrengthMode(strengthBox.SelectedIndex);
                appSettings.StrengthMode = strengthBox.SelectedIndex;
                appSettings.Save();
            };
            var applyStyle = NewButton("应用", 282, 516, 72);
            applyStyle.SurfaceColor = Color.FromArgb(249, 248, 255);
            applyStyle.BackColor = Accent;
            applyStyle.ForeColor = Color.White;
            applyStyle.Click += delegate { ApplySelectedStyle(); };
            var saveStyle = NewButton("保存当前", 364, 516, 86);
            saveStyle.SurfaceColor = Color.FromArgb(249, 248, 255);
            saveStyle.Click += delegate { SaveCurrentStyle(); };
            var manageStyles = NewButton("管理", 460, 516, 86);
            manageStyles.SurfaceColor = ThemeManager.Current.TintSurface;
            manageStyles.Click += delegate { ManageStylePresets(); };
            var configureHotkeys = NewButton("快捷键", 556, 516, 94);
            configureHotkeys.SurfaceColor = Color.FromArgb(249, 248, 255);
            configureHotkeys.Click += delegate { ConfigureHotkeys(); };
            var toolsCenter = NewButton("工具中心", 420, 37, 105);
            toolsCenter.SurfaceColor = Canvas;
            toolsCenter.BackColor = Accent;
            toolsCenter.ForeColor = Color.White;
            toolsCenter.Click += delegate { OpenToolsCenter(); };

            ConfigureGridSlider(styleShadows, 42, 586);
            ConfigureGridSlider(styleHighlights, 250, 586);
            ConfigureGridSlider(styleMidtones, 458, 586);
            ConfigureGridSlider(styleBlackPoint, 42, 652);
            ConfigureGridSlider(styleWhitePoint, 250, 652);
            ConfigureGridSlider(styleContrastPivot, 458, 652);
            ConfigureGridSlider(styleTemperature, 42, 718);
            ConfigureGridSlider(styleTint, 250, 718);
            ConfigureGridSlider(styleVibrance, 458, 718);
            ConfigureCompactSlider(styleExposure, 42, 784);
            ConfigureCompactSlider(styleShadowRange, 356, 784);
            ConfigureCompactSlider(styleHighlightRange, 42, 850);
            ConfigureCompactSlider(styleTransitionSoftness, 356, 850);
            PositionGridValuePill(styleShadowValue, 187, 561);
            PositionGridValuePill(styleHighlightValue, 395,…45045 tokens truncated…e.Flat;
            gamePreset.Location = new Point(254, 251);
            gamePreset.Size = new Size(220, 30);
            foreach (var style in styles.Items) gamePreset.Items.Add(style);
            if (gamePreset.Items.Count > 0) gamePreset.SelectedIndex = 0;
            var addRule = Button("添加规则", 486, 249, 100, true);
            addRule.Click += delegate { AddGameRule(); };
            gameRules.Location = new Point(34, 291);
            gameRules.Size = new Size(552, 83);
            gameRules.BorderStyle = BorderStyle.FixedSingle;
            RefreshRules();
            var deleteRule = Button("删除选中", 598, 291, 112, false);
            deleteRule.Click += delegate {
                var rule = gameRules.SelectedItem as GamePresetRule;
                if (rule == null) return;
                settings.GameRules.Remove(rule); settings.Save();
                RefreshRules(); Changed();
            };

            AddTitle("预设分享代码", 34, 398);
            shareCode.Location = new Point(34, 431);
            shareCode.Size = new Size(552, 66);
            shareCode.Multiline = true;
            shareCode.ScrollBars = ScrollBars.Vertical;
            shareCode.BorderStyle = BorderStyle.FixedSingle;
            var exportCode = Button("导出当前", 598, 431, 112, false);
            exportCode.Click += delegate {
                var preset = styles.Items.Count == 0 ? null :
                    styles.Items[Math.Max(0, Math.Min(styles.Items.Count - 1,
                        gamePreset.SelectedIndex))];
                if (preset != null) shareCode.Text = PresetCode.Export(preset);
            };
            var importCode = Button("导入代码", 598, 471, 112, true);
            importCode.Click += delegate { ImportShareCode(); };

            AddTitle("环境诊断中心", 34, 519);
            diagnosis.Location = new Point(34, 552);
            diagnosis.Size = new Size(552, 92);
            diagnosis.Multiline = true; diagnosis.ReadOnly = true;
            diagnosis.ScrollBars = ScrollBars.Vertical;
            diagnosis.BorderStyle = BorderStyle.FixedSingle;
            diagnosis.Text = diagnose();
            var refreshDiag = Button("重新诊断", 598, 552, 112, false);
            refreshDiag.Click += delegate { diagnosis.Text = diagnose(); };
            var copyDiag = Button("复制结果", 598, 592, 112, false);
            copyDiag.Click += delegate { Clipboard.SetText(diagnosis.Text); };
            var done = Button("完成", 614, 638, 96, true);
            done.DialogResult = DialogResult.OK;

            Controls.AddRange(new Control[] {
                holdInput, saveHold, capA, capB, showA, showB, enabled, restoreAfter,
                processName, gamePreset, addRule, gameRules, deleteRule,
                shareCode, exportCode, importCode, diagnosis, refreshDiag,
                copyDiag, done
            });
            diagnosticCard.SendToBack();
            shareCard.SendToBack();
            gameCard.SendToBack();
            compareCard.SendToBack();
            SecondaryWindowTheme.Apply(this);
            AcceptButton = done;
        }

        private void CaptureHold(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true; e.Handled = true;
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            { holdKey = 0; holdModifiers = 0; holdInput.Text = "未设置"; return; }
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu ||
                e.KeyCode == Keys.ShiftKey) return;
            holdModifiers = (e.Alt ? 1u : 0u) | (e.Control ? 2u : 0u) |
                (e.Shift ? 4u : 0u);
            holdKey = (int)e.KeyCode;
            holdInput.Text = FormatHotkey(holdModifiers, holdKey);
        }

        private void AddGameRule()
        {
            var preset = gamePreset.SelectedItem as StylePresetData;
            string process = processName.Text.Trim();
            if (preset == null || process.Length == 0) return;
            var existing = settings.GameRules.Find(item =>
                string.Equals(item.ProcessName, process, StringComparison.OrdinalIgnoreCase));
            if (existing == null) {
                existing = new GamePresetRule { ProcessName = process };
                settings.GameRules.Add(existing);
            }
            existing.PresetName = preset.Name;
            settings.Save(); RefreshRules(); Changed();
        }

        private void RefreshRules()
        {
            gameRules.Items.Clear();
            foreach (var rule in settings.GameRules) gameRules.Items.Add(rule);
        }

        private void ImportShareCode()
        {
            try
            {
                var preset = PresetCode.Import(shareCode.Text.Trim());
                string baseName = preset.Name;
                int suffix = 2;
                while (styles.Items.Exists(item => string.Equals(
                    item.Name, preset.Name, StringComparison.OrdinalIgnoreCase)))
                    preset.Name = baseName + " (" + suffix++ + ")";
                styles.Items.Add(preset); styles.Save(); Changed();
                MessageBox.Show(this, "已导入预设：" + preset.Name,
                    "分享代码", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导入失败：" + ex.Message,
                    "分享代码", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Changed() {
            if (SettingsChanged != null) SettingsChanged(this, EventArgs.Empty);
        }
        private void AddTitle(string text, int x, int y) { Controls.Add(new Label {
            Text=text, AutoSize=false, Location=new Point(x,y), Size=new Size(500,28),
            Font=new Font(Font.FontFamily,11F,FontStyle.Bold),
            ForeColor=Color.FromArgb(29,36,50), TextAlign=ContentAlignment.MiddleLeft }); }
        private void AddNote(string text, int x, int y) { Controls.Add(new Label {
            Text=text, AutoSize=false, Location=new Point(x,y), Size=new Size(650,24),
            ForeColor=Color.FromArgb(104,113,130), TextAlign=ContentAlignment.MiddleLeft }); }
        private void AddLabel(string text, int x, int y) { Controls.Add(new Label {
            Text=text, AutoSize=false, Location=new Point(x,y), Size=new Size(90,28),
            ForeColor=Color.FromArgb(47,55,70), TextAlign=ContentAlignment.MiddleLeft }); }
        private void ConfigureInput(TextBox input,int x,int y,int width) {
            input.Location=new Point(x,y); input.Size=new Size(width,30);
            input.ReadOnly=true; input.TextAlign=HorizontalAlignment.Center;
            input.BorderStyle=BorderStyle.FixedSingle; input.BackColor=Color.White; }
        private Button Button(string text,int x,int y,int width,bool primary) {
            return new RoundedButton { Text=text,Location=new Point(x,y),Size=new Size(width,34),
                BackColor=primary?Color.FromArgb(76,99,220):Color.FromArgb(238,241,247),
                ForeColor=primary?Color.White:Color.FromArgb(29,36,50),
                SurfaceColor=BackColor,Cursor=Cursors.Hand }; }
        private static string FormatHotkey(uint modifiers,int key) {
            if(key==0)return "未设置"; string t="";
            if((modifiers&2)!=0)t+="Ctrl + "; if((modifiers&1)!=0)t+="Alt + ";
            if((modifiers&4)!=0)t+="Shift + "; return t+((Keys)key); }
    }

    internal static class PresetCode
    {
        private const string Prefix = "SP1-";
        public static string Export(StylePresetData p)
        {
            string payload = string.Join("|", new string[] {
                p.Name ?? "预设", p.Shadows.ToString(), p.Highlights.ToString(),
                p.Midtones.ToString(), p.BlackPoint.ToString(), p.WhitePoint.ToString(),
                p.ContrastPivot.ToString(), p.Temperature.ToString(), p.Tint.ToString(),
                p.Vibrance.ToString(), p.Exposure.ToString(), p.ShadowRange.ToString(),
                p.HighlightRange.ToString(), p.TransitionSoftness.ToString() });
            byte[] data = Encoding.UTF8.GetBytes(payload);
            string body = Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_');
            return Prefix + body + "-" + Checksum(data).ToString("X8");
        }
        public static StylePresetData Import(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(Prefix))
                throw new InvalidOperationException("不是 ShadePilot SP1 分享代码。");
            int split = code.LastIndexOf('-');
            if (split <= Prefix.Length) throw new InvalidOperationException("代码不完整。");
            string body = code.Substring(Prefix.Length, split-Prefix.Length)
                .Replace('-','+').Replace('_','/');
            while (body.Length % 4 != 0) body += "=";
            byte[] data = Convert.FromBase64String(body);
            uint expected;
            if (!uint.TryParse(code.Substring(split+1),
                System.Globalization.NumberStyles.HexNumber, null, out expected) ||
                expected != Checksum(data))
                throw new InvalidOperationException("校验失败，代码可能复制不完整。");
            string[] v = Encoding.UTF8.GetString(data).Split('|');
            if (v.Length != 14) throw new InvalidOperationException("参数数量不正确。");
            var preset = new StylePresetData { Name=v[0], Shadows=I(v[1]),Highlights=I(v[2]),
                Midtones=I(v[3]),BlackPoint=I(v[4]),WhitePoint=I(v[5]),
                ContrastPivot=I(v[6]),Temperature=I(v[7]),Tint=I(v[8]),
                Vibrance=I(v[9]),Exposure=I(v[10]),ShadowRange=I(v[11]),
                HighlightRange=I(v[12]),TransitionSoftness=I(v[13]) };
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 80 ||
                preset.Shadows < 0 || preset.Shadows > 200 ||
                preset.Highlights < 0 || preset.Highlights > 200 ||
                !Signed(preset.Midtones) || !Signed(preset.BlackPoint) ||
                !Signed(preset.WhitePoint) || !Signed(preset.ContrastPivot) ||
                !Signed(preset.Temperature) || !Signed(preset.Tint) ||
                !Signed(preset.Vibrance) || !Signed(preset.Exposure) ||
                !Signed(preset.ShadowRange) || !Signed(preset.HighlightRange) ||
                !Signed(preset.TransitionSoftness))
                throw new InvalidOperationException("分享代码包含超出允许范围的参数。");
            preset.Name = preset.Name.Trim();
            return preset;
        }
        private static bool Signed(int value) { return value >= -100 && value <= 100; }
        private static int I(string value) { int result; if(!int.TryParse(value,out result))
            throw new InvalidOperationException("代码中包含无效参数。"); return result; }
        private static uint Checksum(byte[] data) { uint hash=2166136261;
            foreach(byte b in data){hash^=b;hash*=16777619;} return hash; }
    }

    internal static class Prompt
    {
        public static string Show(string text, string caption)
        {
            return Show(text, caption, null);
        }

        public static string Show(string text, string caption, string defaultValue)
        {
            using (var form = new Form())
            using (var input = new TextBox())
            {
                Color canvas = Color.FromArgb(245, 247, 251);
                Color accent = Color.FromArgb(76, 99, 220);
                form.Text = caption;
                form.ClientSize = new Size(420, 185);
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.AutoScaleMode = AutoScaleMode.Dpi;
                form.BackColor = canvas;
                form.Font = new Font("Microsoft YaHei UI", 9F);

                var heading = new Label {
                    Text = caption, AutoSize = true, Location = new Point(24, 20),
                    ForeColor = Color.FromArgb(29, 36, 50),
                    Font = new Font(form.Font.FontFamily, 12F, FontStyle.Bold)
                };
                var label = new Label {
                    Text = text, AutoSize = true, Location = new Point(25, 57),
                    ForeColor = Color.FromArgb(104, 113, 130)
                };
                input.Location = new Point(26, 82);
                input.Size = new Size(368, 28);
                input.BorderStyle = BorderStyle.FixedSingle;
                input.BackColor = Color.White;
                input.Text = defaultValue ?? "";

                var ok = new RoundedButton {
                    Text = "保存", DialogResult = DialogResult.OK,
                    Location = new Point(218, 128), Size = new Size(84, 36),
                    BackColor = accent, ForeColor = Color.White,
                    SurfaceColor = canvas, Cursor = Cursors.Hand
                };
                var cancel = new RoundedButton {
                    Text = "取消", DialogResult = DialogResult.Cancel,
                    Location = new Point(312, 128), Size = new Size(84, 36),
                    BackColor = Color.FromArgb(232, 235, 242),
                    ForeColor = Color.FromArgb(29, 36, 50),
                    SurfaceColor = canvas, Cursor = Cursors.Hand
                };
                form.Controls.AddRange(new Control[] { heading, label, input, ok, cancel });
                form.AcceptButton = ok; form.CancelButton = cancel;
                form.Shown += delegate { input.Focus(); input.SelectAll(); };
                return form.ShowDialog() == DialogResult.OK ? input.Text : null;
            }
        }
    }

    internal static class MonitorApi
    {
        internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        internal enum MC_COLOR_TEMPERATURE
        {
            MC_COLOR_TEMPERATURE_UNKNOWN,
            MC_COLOR_TEMPERATURE_4000K,
            MC_COLOR_TEMPERATURE_5000K,
            MC_COLOR_TEMPERATURE_6500K,
            MC_COLOR_TEMPERATURE_7500K,
            MC_COLOR_TEMPERATURE_8200K,
            MC_COLOR_TEMPERATURE_9300K,
            MC_COLOR_TEMPERATURE_10000K,
            MC_COLOR_TEMPERATURE_11500K
        }

        internal enum MC_VCP_CODE_TYPE
        {
            MC_MOMENTARY,
            MC_SET_PARAMETER
        }

        [DllImport("user32.dll")]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(
            IntPtr window, out uint processId);
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
        [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint size, [Out] PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetMonitorCapabilities(IntPtr monitor, out uint caps, out uint temperatures);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetMonitorBrightness(IntPtr monitor, out uint min, out uint current, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool SetMonitorBrightness(IntPtr monitor, uint value);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetMonitorContrast(IntPtr monitor, out uint min, out uint current, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool SetMonitorContrast(IntPtr monitor, uint value);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetMonitorColorTemperature(IntPtr monitor, out MC_COLOR_TEMPERATURE value);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool SetMonitorColorTemperature(IntPtr monitor, MC_COLOR_TEMPERATURE value);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetVCPFeatureAndVCPFeatureReply(
            IntPtr monitor, byte vcpCode, out MC_VCP_CODE_TYPE codeType, out uint currentValue, out uint maximumValue);
        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool SetVCPFeature(IntPtr monitor, byte vcpCode, uint newValue);

        internal static bool IsAdvancedColorEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\VideoSettings"))
                    return key != null && Convert.ToInt32(
                        key.GetValue("EnableHDRForPlayback", 0)) != 0;
            }
            catch { return false; }
        }

        public static List<PhysicalDisplay> Enumerate()
        {
            var result = new List<PhysicalDisplay>();
            int logicalIndex = 0;
            MonitorEnumProc callback = delegate(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0) return true;
                var physical = new PHYSICAL_MONITOR[count];
                if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) return true;
                for (int i = 0; i < physical.Length; i++)
                {
                    string name = string.IsNullOrWhiteSpace(physical[i].szPhysicalMonitorDescription)
                        ? "显示器 " + (logicalIndex + 1) : physical[i].szPhysicalMonitorDescription;
                    result.Add(new PhysicalDisplay {
                        Handle = physical[i].hPhysicalMonitor,
                        Name = name,
                        Id = name + "|" + rect.Left + "," + rect.Top + "|" + logicalIndex + ":" + i
                    });
                }
                logicalIndex++;
                return true;
            };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                throw Error("枚举显示器");
            return result;
        }

        public static Exception Error(string operation)
        {
            int code = Marshal.GetLastWin32Error();
            return new InvalidOperationException(operation + "失败（Windows 错误 " + code + "）");
        }
    }
}

