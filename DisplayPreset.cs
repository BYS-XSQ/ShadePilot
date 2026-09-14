using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("ShadePilot BETA 0.4.5")]
[assembly: System.Reflection.AssemblyProduct("ShadePilot")]
[assembly: System.Reflection.AssemblyDescription("ShadePilot display and color control beta")]
[assembly: System.Reflection.AssemblyVersion("0.4.5.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.4.5.0")]

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
        private readonly Label saturationTitle = NewLabel("饱和度", 42, 308);
        private readonly ComboBox saturationBackend = new ThemedComboBox();
        private readonly Label brightnessValue = new Label();
        private readonly Label contrastValue = new Label();
        private readonly Label saturationValue = new Label();
        private readonly Label saturationHint = new Label();
        private readonly Label status = new Label();
        private readonly CheckBox restoreOnExit = new CheckBox();
        private readonly CheckBox liveApply = new CheckBox();
        private readonly CheckBox syncAllDisplays = new CheckBox();
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
        private readonly ToneCurveGraph liveToneCurve = new ToneCurveGraph();
        private readonly System.Windows.Forms.Timer stylePreviewTimer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon trayIcon = new NotifyIcon();
        private readonly ToolStripMenuItem trayStyleStatus = new ToolStripMenuItem();
        private readonly ToolStripMenuItem trayMonitorStatus = new ToolStripMenuItem();
        private readonly ToolStripMenuItem trayHotkeyStatus = new ToolStripMenuItem();
        private readonly System.Windows.Forms.Timer recoveryTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer gameRuleTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer resizeUiTimer = new System.Windows.Forms.Timer();
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
        private bool softwareSaturationSuppressedForGame;
        private StylePresetData styleBeforeOriginalToggle;
        private bool originalToggleActive;
        private bool displayRefreshRunning;
        private bool displayRefreshPending;
        private bool rebuildingDisplayList;
        private bool reapplyStyleAfterRefresh;
        private bool performanceProtectionActive;
        private bool displayRecoveryDeferredForGame;
        private int hotkeyReceivedCount;
        private DateTime lastHotkeyReceivedUtc;
        private string lastHotkeyAction = "尚未收到";
        private string lastStyleApplyError = "无";
        private readonly int currentProcessId;

        private const int WmHotkey = 0x0312;
        private const int HotkeyStyleBase = 2100;
        private const int HotkeyRestore = 2199;
        private const int HotkeyCycle = 2198;
        private const int HotkeyOriginalToggle = 2197;
        private const int WmNcHitTest = 0x0084;
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 0x0002;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attribute, ref int value, int valueSize);

        public MainForm()
        {
            using (var currentProcess = Process.GetCurrentProcess())
                currentProcessId = currentProcess.Id;
            Text = "ShadePilot BETA 0.4.5";
            ClientSize = new Size(1180, 840);
            MinimumSize = new Size(1000, 700);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(1);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Canvas;
            // All coordinates below are authored at 96 DPI.  Without an
            // explicit design baseline WinForms can infer it from the machine
            // that first creates the form, then scale fonts and controls by
            // different factors on 125%-200% displays.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            // The adaptive page below is the single scroll owner. Enabling
            // Form.AutoScroll as well creates nested vertical scrollbars and
            // makes each wheel step move only the inner viewport.
            AutoScroll = false;
            AutoScrollMinSize = Size.Empty;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
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
                ForeColor = Ink,
                AutoSize = false, Location = new Point(83, 55),
                Size = new Size(322, 24), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular)
            };
            var brandEnglish = new Label {
                Text = "DISPLAY INTELLIGENCE · COLOR CONTROL",
                ForeColor = Muted,
                AutoSize = false, Location = new Point(83, 72),
                Size = new Size(322, 18), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 7.5F, FontStyle.Regular)
            };
            title.Tag = "CanvasText";
            safety.Tag = "CanvasText";
            brandEnglish.Tag = "CanvasMuted";
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
            monitorBox.SelectedIndexChanged += delegate {
                if (!rebuildingDisplayList) LoadSelectedMonitor(false);
            };
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

            saturationBackend.DropDownStyle = ComboBoxStyle.DropDownList;
            saturationBackend.FlatStyle = FlatStyle.Flat;
            saturationTitle.Size = new Size(72, 22);
            saturationBackend.Location = new Point(122, 306);
            saturationBackend.Size = new Size(210, 28);
            saturationBackend.Items.AddRange(new object[] {
                "自动选择", "显示器 DDC/CI", "Windows 软件色彩"
            });
            saturationBackend.SelectedIndex = Math.Max(0,
                Math.Min(2, appSettings.SaturationBackend));
            saturationBackend.SelectedIndexChanged += delegate {
                appSettings.SaturationBackend = saturationBackend.SelectedIndex;
                appSettings.Save();
                LoadSelectedMonitor(false);
            };

            saturationHint.Text = "当前显示器不支持硬件饱和度";
            saturationHint.AutoSize = false;
            saturationHint.Location = new Point(42, 337);
            saturationHint.Size = new Size(608, 24);
            saturationHint.TextAlign = ContentAlignment.MiddleLeft;
            saturationHint.AutoEllipsis = true;
            saturationHint.ForeColor = Muted;
            saturationHint.BackColor = Color.White;
            saturationHint.Visible = false;
            saturationHint.Margin = new Padding(4, -2, 4, 6);

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
            syncAllDisplays.Text = "同步所有显示器画面风格";
            syncAllDisplays.Checked = appSettings.SyncAllDisplays;
            syncAllDisplays.AutoSize = true;
            syncAllDisplays.ForeColor = Muted;
            syncAllDisplays.CheckedChanged += delegate {
                appSettings.SyncAllDisplays = syncAllDisplays.Checked;
                appSettings.Save();
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
            var exportLut = NewButton("导出 OBS LUT", 435, 459, 112);
            exportLut.SurfaceColor = Color.FromArgb(249, 248, 255);
            exportLut.Click += delegate { ExportCurrentLut(); };
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
            PositionGridValuePill(styleHighlightValue, 395, 561);
            PositionGridValuePill(styleMidtoneValue, 603, 561);
            PositionGridValuePill(styleBlackPointValue, 187, 627);
            PositionGridValuePill(styleWhitePointValue, 395, 627);
            PositionGridValuePill(styleContrastPivotValue, 603, 627);
            PositionGridValuePill(styleTemperatureValue, 187, 693);
            PositionGridValuePill(styleTintValue, 395, 693);
            PositionGridValuePill(styleVibranceValue, 603, 693);
            PositionGridValuePill(styleExposureValue, 272, 759);
            PositionGridValuePill(styleShadowRangeValue, 586, 759);
            PositionGridValuePill(styleHighlightRangeValue, 272, 825);
            PositionGridValuePill(styleTransitionSoftnessValue, 586, 825);
            WireStyleSlider(styleShadows, styleShadowValue, true, "提亮暗部");
            WireStyleSlider(styleHighlights, styleHighlightValue, true, "压低高光");
            WireStyleSlider(styleMidtones, styleMidtoneValue, false, "中间调");
            WireStyleSlider(styleBlackPoint, styleBlackPointValue, false, "黑位");
            WireStyleSlider(styleWhitePoint, styleWhitePointValue, false, "白位");
            WireStyleSlider(styleContrastPivot, styleContrastPivotValue, false, "中间对比");
            WireStyleSlider(styleTemperature, styleTemperatureValue, false, "色温");
            WireStyleSlider(styleTint, styleTintValue, false, "色调");
            WireStyleSlider(styleVibrance, styleVibranceValue, false, "自然色彩");
            WireStyleSlider(styleExposure, styleExposureValue, false, "整体曝光");
            WireStyleSlider(styleShadowRange, styleShadowRangeValue, false, "暗部范围");
            WireStyleSlider(styleHighlightRange, styleHighlightRangeValue, false, "高光范围");
            WireStyleSlider(styleTransitionSoftness, styleTransitionSoftnessValue, false, "过渡柔和");
            stylePreviewTimer.Interval = 120;
            stylePreviewTimer.Tick += delegate { stylePreviewTimer.Stop(); ApplyStylePreview(); };

            // Adaptive layout: rows are sized from their content instead of
            // sharing fixed Y coordinates. This prevents text/slider overlap
            // at fractional DPI and allows the usable width to grow naturally.
            var page = NewAdaptivePage();
            page.Tag = "CanvasContainer";
            var brand = NewAdaptiveFlow(FlowDirection.TopDown);
            brand.Controls.Add(title); brand.Controls.Add(safety); brand.Controls.Add(brandEnglish);
            title.AutoSize = true; safety.AutoSize = true; brandEnglish.AutoSize = true;
            var themeArea = NewAdaptiveFlow(FlowDirection.TopDown);
            themeLabel.AutoSize = true; themeBox.Width = 130;
            themeArea.Controls.Add(themeLabel); themeArea.Controls.Add(themeBox);

            var hardwareCard = NewAdaptiveCard(Card);
            hardwareCard.Tag = "SurfaceContainer";
            var monitorRow = NewAdaptiveGrid(4, 38);
            monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            monitorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            monitorRow.Controls.Add(NewAdaptiveText("显示器", true), 0, 0);
            monitorBox.Dock = DockStyle.Fill;
            monitorRow.Controls.Add(monitorBox, 1, 0);
            monitorRow.Controls.Add(refresh, 2, 0); monitorRow.Controls.Add(restore, 3, 0);
            hardwareCard.Controls.Add(monitorRow);
            hardwareCard.Controls.Add(NewAdaptiveSliderRow("亮度", null, brightness, brightnessValue));
            hardwareCard.Controls.Add(NewAdaptiveSliderRow("对比度", null, contrast, contrastValue));
            hardwareCard.Controls.Add(NewAdaptiveSliderRow("饱和度", saturationBackend, saturation, saturationValue));
            saturationHint.Dock = DockStyle.Top; saturationHint.Height = 46;
            hardwareCard.Controls.Add(saturationHint);
            var hardwareActions = NewAdaptiveGrid(3, 40);
            hardwareActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            hardwareActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            hardwareActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            liveApply.AutoSize = true; hardwareActions.Controls.Add(liveApply, 0, 0);
            hardwareActions.Controls.Add(syncAllDisplays, 1, 0);
            hardwareActions.Controls.Add(apply, 2, 0);
            hardwareCard.Controls.Add(hardwareActions);
            page.Controls.Add(hardwareCard);

            var styleAdaptiveCard = NewAdaptiveCard(ThemeManager.Current.TintSurface);
            styleAdaptiveCard.Tag = "TintContainer";
            var styleHead = NewAdaptiveGrid(4, 38);
            styleHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            styleHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            styleHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            styleHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            styleHead.Controls.Add(NewAdaptiveText("画面风格", true, 12F), 0, 0);
            styleHead.Controls.Add(NewAdaptiveText("强度档位", true), 1, 0);
            styleHead.Controls.Add(strengthBox, 2, 0);
            styleHead.Controls.Add(exportLut, 3, 0);
            styleAdaptiveCard.Controls.Add(styleHead);
            styleAdaptiveCard.Controls.Add(NewAdaptiveText("选择预设，或直接拖动参数微调", false));
            var presetRow = NewAdaptiveGrid(5, 40);
            presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 1; i < 5; i++) presetRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            stylePresetBox.Dock = DockStyle.Fill; presetRow.Controls.Add(stylePresetBox, 0, 0);
            presetRow.Controls.Add(applyStyle, 1, 0); presetRow.Controls.Add(saveStyle, 2, 0);
            presetRow.Controls.Add(manageStyles, 3, 0); presetRow.Controls.Add(configureHotkeys, 4, 0);
            styleAdaptiveCard.Controls.Add(presetRow);

            var styleGrid = NewAdaptiveGrid(3, 0);
            for (int i = 0; i < 3; i++) styleGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            AddStyleCell(styleGrid, 0, 0, "提亮暗部", styleShadows, styleShadowValue);
            AddStyleCell(styleGrid, 1, 0, "压低高光", styleHighlights, styleHighlightValue);
            AddStyleCell(styleGrid, 2, 0, "中间调", styleMidtones, styleMidtoneValue);
            AddStyleCell(styleGrid, 0, 1, "黑位", styleBlackPoint, styleBlackPointValue);
            AddStyleCell(styleGrid, 1, 1, "白位", styleWhitePoint, styleWhitePointValue);
            AddStyleCell(styleGrid, 2, 1, "中间对比", styleContrastPivot, styleContrastPivotValue);
            AddStyleCell(styleGrid, 0, 2, "色温", styleTemperature, styleTemperatureValue);
            AddStyleCell(styleGrid, 1, 2, "色调", styleTint, styleTintValue);
            AddStyleCell(styleGrid, 2, 2, "自然色彩", styleVibrance, styleVibranceValue);
            AddStyleCell(styleGrid, 0, 3, "整体曝光", styleExposure, styleExposureValue);
            AddStyleCell(styleGrid, 1, 3, "暗部范围", styleShadowRange, styleShadowRangeValue);
            AddStyleCell(styleGrid, 2, 3, "高光范围", styleHighlightRange, styleHighlightRangeValue);
            AddStyleCell(styleGrid, 0, 4, "过渡柔和", styleTransitionSoftness, styleTransitionSoftnessValue);
            styleAdaptiveCard.Controls.Add(styleGrid);
            var curveHeader = NewAdaptiveGrid(2, 28);
            curveHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            curveHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            curveHeader.Controls.Add(NewAdaptiveText("实时明暗曲线", true), 0, 0);
            curveHeader.Controls.Add(NewAdaptiveText("虚线：原始 · 实线：当前", false), 1, 0);
            styleAdaptiveCard.Controls.Add(curveHeader);
            liveToneCurve.Size = new Size(600, 190);
            liveToneCurve.Margin = new Padding(0, 0, 0, 6);
            liveToneCurve.Tag = "TintControl";
            styleAdaptiveCard.Controls.Add(liveToneCurve);
            UpdateLiveToneCurve();
            var riskRow = NewAdaptiveGrid(3, 54);
            riskRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            riskRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            riskRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            styleRiskStatus.Dock = DockStyle.Fill; riskRow.Controls.Add(styleRiskStatus, 0, 0);
            riskRow.Controls.Add(optimizeStyle, 1, 0); riskRow.Controls.Add(restoreStyle, 2, 0);
            styleAdaptiveCard.Controls.Add(riskRow); page.Controls.Add(styleAdaptiveCard);

            var bottom = NewAdaptiveCard(ThemeManager.Current.SoftSurface);
            bottom.Tag = "SoftContainer";
            restoreOnExit.AutoSize = true; bottom.Controls.Add(restoreOnExit);
            bottom.Controls.Add(NewAdaptiveText("后台快捷键可自由分配；最小化后仍可切换画面风格", false));
            status.AutoSize = true; status.MaximumSize = new Size(740, 0); page.Controls.Add(status);
            var footer = NewAdaptiveGrid(2, 30);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(authorFooter, 0, 0); footer.Controls.Add(sharingFooter, 1, 0);
            // Footer labels are hosted by the fixed application footer below.

            // LP-style application shell: custom chrome, a stable navigation
            // rail and one scroll owner for all functional cards.
            var shell = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
                Margin = new Padding(0), Padding = new Padding(0),
                BackColor = Canvas, Tag = "CanvasContainer"
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214F));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

            var topBar = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1,
                Margin = new Padding(0), Padding = new Padding(25, 12, 18, 12),
                BackColor = Canvas, Tag = "CanvasContainer"
            };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 4F));

            var brandRow = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0), Padding = new Padding(0), BackColor = Canvas,
                Tag = "CanvasContainer"
            };
            brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
            brandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            logo.Location = Point.Empty; logo.Size = new Size(46, 46);
            logo.Anchor = AnchorStyles.Left;
            brandRow.Controls.Add(logo, 0, 0);
            brand.Dock = DockStyle.Fill; brandRow.Controls.Add(brand, 1, 0);
            topBar.Controls.Add(brandRow, 0, 0);
            toolsCenter.Width = 105; toolsCenter.Height = 36;
            toolsCenter.Anchor = AnchorStyles.None;
            toolsCenter.Margin = new Padding(8, 0, 8, 0);
            toolsCenter.Tag = "CanvasPrimaryButton";
            topBar.Controls.Add(toolsCenter, 1, 0);
            themeArea.Anchor = AnchorStyles.None;
            themeArea.Margin = new Padding(8, 0, 18, 0);
            topBar.Controls.Add(themeArea, 2, 0);

            var minimizeButton = NewChromeButton("—");
            var maximizeButton = NewChromeButton("□");
            var closeButton = NewChromeButton("×");
            minimizeButton.Click += delegate { WindowState = FormWindowState.Minimized; };
            maximizeButton.Click += delegate { ToggleMaximize(); };
            closeButton.Click += delegate { Close(); };
            topBar.Controls.Add(minimizeButton, 3, 0);
            topBar.Controls.Add(maximizeButton, 4, 0);
            topBar.Controls.Add(closeButton, 5, 0);
            shell.Controls.Add(topBar, 0, 0);
            shell.SetColumnSpan(topBar, 2);

            var navigation = new FlowLayoutPanel {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, Padding = new Padding(18, 22, 18, 14),
                Margin = new Padding(0), BackColor = ThemeManager.Current.SoftSurface,
                Tag = "SoftContainer"
            };
            var navTitle = NewAdaptiveText("功能板块", false, 8.5F);
            navTitle.Margin = new Padding(5, 0, 0, 8);
            navigation.Controls.Add(navTitle);
            var hardwareNav = NewNavigationButton("显示控制", true);
            var styleNav = NewNavigationButton("画面风格", false);
            var settingsNav = NewNavigationButton("工具中心", false);
            navigation.Controls.Add(hardwareNav);
            navigation.Controls.Add(styleNav);
            navigation.Controls.Add(settingsNav);
            var boundary = NewAdaptiveText("外部显示控制\n不注入游戏 · 不叠加画面", false, 8F);
            boundary.Margin = new Padding(5, 24, 0, 0);
            navigation.Controls.Add(boundary);
            shell.Controls.Add(navigation, 0, 1);

            var displayHeading = NewPageHeading("显示控制", "显示器硬件参数与多屏同步设置");
            page.Controls.Add(displayHeading);
            page.Controls.SetChildIndex(displayHeading, 0);
            page.Padding = new Padding(26, 22, 24, 20);

            var stylePage = NewAdaptivePage();
            stylePage.Tag = "CanvasContainer";
            stylePage.Padding = new Padding(26, 22, 24, 20);
            stylePage.Controls.Add(NewPageHeading("画面风格", "预设、明暗曲线与色彩参数微调"));
            stylePage.Controls.Add(styleAdaptiveCard);

            var toolsPage = NewAdaptivePage();
            toolsPage.Tag = "CanvasContainer";
            toolsPage.Padding = new Padding(26, 22, 24, 20);
            toolsPage.Controls.Add(NewPageHeading("工具中心", "原画对比、自动切换、分享代码与环境诊断"));
            var embeddedTools = new ToolsCenterForm(
                styleStore, appSettings,
                delegate { temporaryA = CaptureCurrentStyle("临时 A"); },
                delegate { temporaryB = CaptureCurrentStyle("临时 B"); },
                delegate(bool useA) {
                    var preset = useA ? temporaryA : temporaryB;
                    if (preset != null) ApplyStylePreset(preset);
                },
                BuildEnvironmentDiagnosis, restoreOnExit);
            embeddedTools.SettingsChanged += delegate {
                styleStore.Save(); appSettings.Save(); lastAutoProcess = "";
                RegisterGlobalHotkeys(); UpdateGameRuleTimer();
            };
            embeddedTools.ConfigureEmbedded();
            var toolsHost = new Panel {
                Height = 680, MinimumSize = new Size(620, 680),
                Margin = new Padding(0), BackColor = Canvas,
                Tag = "EmbeddedToolsHost"
            };
            toolsHost.Controls.Add(embeddedTools);
            toolsPage.Controls.Add(toolsHost);
            embeddedTools.Show();

            // Each navigation entry owns a real page. Only one page participates
            // in layout and painting at a time.
            stylePage.Visible = false;
            toolsPage.Visible = false;
            var contentHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), Tag = "CanvasContainer" };
            contentHost.Controls.Add(toolsPage);
            contentHost.Controls.Add(stylePage);
            contentHost.Controls.Add(page);
            shell.Controls.Add(contentHost, 1, 1);
            hardwareNav.Click += delegate { SelectPage(page, hardwareNav, styleNav, settingsNav); };
            styleNav.Click += delegate { SelectPage(stylePage, styleNav, hardwareNav, settingsNav); };
            settingsNav.Click += delegate { SelectPage(toolsPage, settingsNav, hardwareNav, styleNav); };
            toolsCenter.Click += delegate { SelectPage(toolsPage, settingsNav, hardwareNav, styleNav); };

            var appFooter = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0), Padding = new Padding(24, 6, 24, 4),
                BackColor = ThemeManager.Current.SoftSurface, Tag = "SoftContainer"
            };
            appFooter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            appFooter.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            authorFooter.Text = "BY XD · 免费分享，倒卖必究";
            sharingFooter.Text = "ShadePilot BETA 0.4.5";
            authorFooter.Tag = "SoftMuted"; sharingFooter.Tag = "SoftMuted";
            appFooter.Controls.Add(authorFooter, 0, 0);
            appFooter.Controls.Add(sharingFooter, 1, 0);
            shell.Controls.Add(appFooter, 0, 2);
            shell.SetColumnSpan(appFooter, 2);
            Controls.Add(shell);
            WireWindowDrag(topBar);
            topBar.DoubleClick += delegate { ToggleMaximize(); };
            brandRow.DoubleClick += delegate { ToggleMaximize(); };
            resizeUiTimer.Interval = 100;
            resizeUiTimer.Tick += delegate {
                resizeUiTimer.Stop();
                FitAdaptiveContent(page);
                FitAdaptiveContent(stylePage);
                FitAdaptiveContent(toolsPage);
                UpdateWindowShape();
                Invalidate(true);
            };
            ResizeBegin += delegate { Region = null; };
            ResizeEnd += delegate { QueueResizeLayout(); };
            Resize += delegate {
                maximizeButton.Text = WindowState == FormWindowState.Maximized ? "❐" : "□";
                QueueResizeLayout();
            };
            brightnessValue.Location = new Point(620, 194);
            brightnessValue.ForeColor = Accent;
            brightnessValue.AutoSize = true;
            contrastValue.Location = new Point(620, 266);
            contrastValue.ForeColor = Accent;
            contrastValue.AutoSize = true;
            saturationValue.Location = new Point(620, 338);
            saturationValue.ForeColor = Accent;
            saturationValue.AutoSize = true;

            AssignThemeRoles(mainCard, styleCard, null, actionCard,
                themeLabel, refresh, apply, restore, applyStyle, saveStyle,
                manageStyles, configureHotkeys, restoreStyle, optimizeStyle,
                exportLut,
                toolsCenter);
            ConfigureToolTips(
                refresh, apply, restore, restoreStyle,
                applyStyle, saveStyle, manageStyles, configureHotkeys, exportLut, optimizeStyle);
            SetHelp(exportLut, "将当前画面风格导出为 .cube 3D LUT，供 OBS 的“应用 LUT”滤镜使用。");
            toolsCenter.BringToFront();
            SetHelp(toolsCenter, "打开原画与 A/B 对比、游戏自动切换、预设分享代码和环境诊断。");
            ApplyTheme(ThemeManager.Current);
            UpdateStyleRiskStatus();
            themeBox.SelectedItem = ThemeManager.Current;
            themeBox.SelectedIndexChanged += delegate {
                var selectedTheme = themeBox.SelectedItem as AppTheme;
                if (selectedTheme == null) return;
                ThemeManager.Set(selectedTheme);
                ApplyTheme(selectedTheme);
                SecondaryWindowTheme.Apply(embeddedTools);
                UpdateStyleRiskStatus();
            };

            FormClosing += OnFormClosing;
            Resize += OnWindowResize;
            Shown += delegate {
                UpdateWindowShape();
                FitAdaptiveContent(page);
                FitAdaptiveContent(stylePage);
                FitAdaptiveContent(toolsPage);
                RegisterGlobalHotkeys();
                if (appSettings.StartMinimized)
                    BeginInvoke((MethodInvoker)delegate { WindowState = FormWindowState.Minimized; });
            };
            HandleCreated += delegate {
                if (hotkeysRegistered)
                    BeginInvoke((MethodInvoker)delegate { RegisterGlobalHotkeys(); });
            };
            ConfigureTrayIcon();
            recoveryTimer.Interval = 2200;
            recoveryTimer.Tick += delegate {
                recoveryTimer.Stop();
                if (closing || !appSettings.AutoReapply) return;
                if (performanceProtectionActive)
                {
                    displayRecoveryDeferredForGame = true;
                    return;
                }
                reapplyStyleAfterRefresh = gammaController.IsCaptured;
                RefreshDisplays();
                UpdateGameRuleTimer();
            };
            SystemEvents.DisplaySettingsChanged += OnDisplayEnvironmentChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            gameRuleTimer.Interval = 2000;
            gameRuleTimer.Tick += delegate { CheckGameAutoSwitch(); };
            UpdateGameRuleTimer();
            RefreshDisplays();
        }

        private void ConfigureToolTips(
            Button refresh, Button apply, Button restore, Button restoreStyle,
            Button applyStyle, Button saveStyle, Button manageStyles,
            Button configureHotkeys, Button exportLut, Button optimizeStyle)
        {
            helpTip.IsBalloon = true;
            helpTip.AutoPopDelay = 9000;
            helpTip.InitialDelay = 550;
            helpTip.ReshowDelay = 150;
            helpTip.ShowAlways = true;

            SetHelp(monitorBox, "选择要调节的物理显示器。亮度、对比度等硬件参数只作用于这里选择的显示器。");
            SetHelp(refresh, "重新扫描显示器并读取它支持的 DDC/CI 调节项目。");
            SetHelp(brightness, "调节显示器背光亮度。它会改变屏幕整体发光强度，不等同于提亮暗部。");
            SetHelp(contrast, "调节显示器硬件对比度。过高可能造成亮部或暗部细节丢失。");
            SetHelp(saturation, "显示器支持时调节 DDC/CI 硬件饱和度；不支持时自动改用 Windows 全屏软件饱和度。");
            SetHelp(liveApply, "开启后，拖动硬件滑块会在短暂防抖后自动写入显示器。");
            SetHelp(syncAllDisplays, "开启后，画面风格、预设快捷键和重置会同步到所有显示器；硬件亮度与对比度仍只调整当前显示器。");
            SetHelp(apply, "立即写入当前亮度、对比度、硬件饱和度和硬件色温。");
            SetHelp(themeBox, "只改变 ShadePilot 自身的界面配色，不影响游戏画面。");

            SetHelp(stylePresetBox, "选择一套画面风格。点击“应用”或使用分配的全局快捷键切换。");
            SetHelp(strengthBox, "限制画面参数的可调范围：柔和适合日常，标准兼顾效果，极限开放完整范围。");
            SetHelp(applyStyle, "应用当前选中的画面风格预设。");
            SetHelp(saveStyle, "将当前九项画面参数保存为新预设，或覆盖同名预设。");
            SetHelp(manageStyles, "重命名、修改或删除画面风格预设。");
            SetHelp(configureHotkeys, "为每个画面风格和恢复原始画面录入全局快捷键。");
            SetHelp(exportLut, "将当前画面风格导出为 .cube 3D LUT，供 OBS 的“应用 LUT”滤镜使用。");
            SetHelp(optimizeStyle, "只修正当前参数中明显的灰雾、重复压缩、裁切和互相抵消，不会更换预设。");
            SetHelp(styleRiskStatus, "实时检查灰雾、端点裁切、重复压缩和参数互相抵消。");

            SetHelp(styleShadows, "提升阴影和较暗的中间区域，同时尽量保留纯黑与亮部。适合查看暗处。");
            SetHelp(styleHighlights, "压低画面中高亮区域，减少灯光、天空或白色物体刺眼。");
            SetHelp(styleMidtones, "调整画面主体所在的中间亮度范围；正值提亮，负值压暗。");
            SetHelp(styleBlackPoint, "调整黑场端点。正值抬起黑位、显露最暗细节；负值让黑色更深。");
            SetHelp(styleWhitePoint, "调整白场端点。正值增强最亮部分；负值收回白场并保留亮部余量。");
            SetHelp(styleContrastPivot, "围绕中间亮度增加或降低曲线对比度，不会改动显示器硬件对比度。");
            SetHelp(styleTemperature, "连续改变红蓝通道比例。负值偏冷蓝，正值偏暖黄。");
            SetHelp(styleTint, "连续改变绿与洋红方向的色偏。负值偏绿，正值偏洋红。");
            SetHelp(styleVibrance, "在 Gamma 曲线能力范围内近似增强较柔和的颜色。它不是逐像素滤镜。");
            SetHelp(styleExposure, "在线性曲线起点整体增减曝光，范围约为 -2 到 +2 档；不会改变显示器背光。");
            SetHelp(styleShadowRange, "只改变暗部补偿覆盖范围。负值集中在最黑区域，正值延伸到较暗中间调。");
            SetHelp(styleHighlightRange, "只改变高光压制覆盖范围。负值集中在最亮区域，正值更早进入中高亮。");
            SetHelp(styleTransitionSoftness, "只改变明暗调整边缘的衔接。负值更集中，正值更柔和宽缓。");

            SetHelp(restore, "恢复当前显示器在 ShadePilot 启动时记录的硬件参数。");
            SetHelp(restoreStyle, "恢复程序启动前的原始 Gamma 曲线，并将九项画面参数归零。");
            SetHelp(restoreOnExit, "退出 ShadePilot 时自动恢复启动前的显示器参数和 Gamma 曲线。");
        }

        private void SetHelp(Control control, string text)
        {
            helpTip.SetToolTip(control, text);
        }

        private void ApplyStrengthMode(int mode)
        {
            int tonalMaximum = mode == 0 ? 80 : mode == 1 ? 150 : 200;
            int signedMaximum = mode == 0 ? 40 : mode == 1 ? 70 : 100;
            styleShadows.Maximum = styleHighlights.Maximum = tonalMaximum;
            TrackBar[] signed = {
                styleMidtones, styleBlackPoint, styleWhitePoint, styleContrastPivot,
                styleTemperature, styleTint, styleVibrance, styleExposure,
                styleShadowRange, styleHighlightRange, styleTransitionSoftness
            };
            foreach (var slider in signed)
            {
                slider.Minimum = -signedMaximum;
                slider.Maximum = signedMaximum;
            }
        }

        private static TrackBar NewSlider()
        {
            return new NoWheelTrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, LargeChange = 5 };
        }

        private static TrackBar NewStyleSlider(int minimum, int maximum)
        {
            return new NoWheelTrackBar {
                Minimum = minimum, Maximum = maximum, Value = 0,
                TickStyle = TickStyle.None, LargeChange = 10, SmallChange = 2
            };
        }

        private static Label NewValuePill()
        {
            return new RoundedPillLabel {
                Text = "0", AutoSize = false, Size = new Size(54, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent,
                FillColor = Color.FromArgb(239, 236, 255),
                ForeColor = Accent,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold)
            };
        }

        private static void ConfigureSlider(TrackBar slider, int x, int y, int width)
        {
            slider.Location = new Point(x, y);
            slider.Size = new Size(width, 34);
            slider.TickStyle = TickStyle.None;
            slider.BackColor = Color.White;
        }

        private static void ConfigureCompactSlider(TrackBar slider, int x, int y)
        {
            slider.Location = new Point(x, y);
            slider.Size = new Size(286, 34);
            slider.BackColor = Color.FromArgb(249, 248, 255);
        }

        private static void ConfigureGridSlider(TrackBar slider, int x, int y)
        {
            slider.Location = new Point(x, y);
            slider.Size = new Size(190, 30);
            slider.BackColor = Color.FromArgb(249, 248, 255);
        }

        private static void PositionValuePill(Label label, int x, int y)
        {
            label.Location = new Point(x, y - 3);
        }

        private static void PositionGridValuePill(Label label, int x, int y)
        {
            label.Location = new Point(x, y);
            label.Size = new Size(42, 22);
        }

        private static Label NewLabel(string text, int x, int y)
        {
            return new Label {
                // Keep the label's painted bounds clear of the TrackBar below.
                // The old 27 px label started only 25 px above the slider, so
                // the later-added TrackBar covered the lower half of glyphs
                // after WinForms rounded coordinates at 150%-200% DPI.
                Text = text, AutoSize = false, Location = new Point(x, y - 4),
                Size = new Size(140, 22), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = Ink, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                BackColor = Color.White
            };
        }

        private static Label NewSectionTitle(string text, int x, int y)
        {
            return new Label {
                Text = text, AutoSize = false, Location = new Point(x, y - 4),
                Size = new Size(180, 28), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = Ink, BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold)
            };
        }

        private static Label NewTintedSectionTitle(string text, int x, int y)
        {
            var label = NewSectionTitle(text, x, y);
            label.BackColor = Color.FromArgb(249, 248, 255);
            return label;
        }

        private static Label NewMutedLabel(string text, int x, int y)
        {
            return new Label {
                Text = text, AutoSize = false, Location = new Point(x, y - 2),
                Size = new Size(360, 22), TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                ForeColor = Muted, BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 8F)
            };
        }

        private static Label NewTintedMutedLabel(string text, int x, int y)
        {
            var label = NewMutedLabel(text, x, y);
            label.BackColor = Color.FromArgb(249, 248, 255);
            return label;
        }

        private static Label NewTintedLabel(string text, int x, int y)
        {
            var label = NewLabel(text, x, y);
            label.BackColor = Color.FromArgb(249, 248, 255);
            return label;
        }

        private static Label NewTintedCompactLabel(
            string text, int x, int y, int width)
        {
            var label = NewTintedLabel(text, x, y);
            label.Size = new Size(width, label.Height);
            return label;
        }

        private static Label NewSoftLabel(string text, int x, int y)
        {
            var label = NewLabel(text, x, y);
            label.BackColor = Color.FromArgb(249, 250, 253);
            return label;
        }

        private static Label NewSoftMutedLabel(string text, int x, int y)
        {
            var label = NewMutedLabel(text, x, y);
            label.BackColor = Color.FromArgb(249, 250, 253);
            return label;
        }

        private static RoundedButton NewButton(string text, int x, int y, int width)
        {
            var button = new RoundedButton {
                Text = text, Location = new Point(x, y), Size = new Size(width, 32),
                BackColor = Color.FromArgb(238, 241, 247), ForeColor = Ink,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
            };
            button.SurfaceColor = Color.FromArgb(245, 247, 251);
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static RoundedPanel NewCard(int x, int y, int width, int height)
        {
            return new RoundedPanel {
                Location = new Point(x, y), Size = new Size(width, height),
                BackColor = Color.Transparent, FillColor = Card,
                CornerRadius = 18, BorderStyle = BorderStyle.None
            };
        }

        private static FlowLayoutPanel NewAdaptivePage()
        {
            return new FlowLayoutPanel {
                Dock = DockStyle.Fill, AutoScroll = true,
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = new Padding(24, 18, 24, 28), BackColor = Canvas
            };
        }

        private static RoundedFlowLayoutPanel NewAdaptiveCard(Color color)
        {
            return new RoundedFlowLayoutPanel {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(632, 0), MaximumSize = new Size(1100, 0),
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = new Padding(14), Margin = new Padding(0, 0, 0, 12),
                FillColor = color, CornerRadius = 12
            };
        }

        private static FlowLayoutPanel NewAdaptiveFlow(FlowDirection direction)
        {
            return new FlowLayoutPanel {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = direction, WrapContents = false,
                Margin = new Padding(0), Padding = new Padding(0)
            };
        }

        private static TableLayoutPanel NewAdaptiveGrid(int columns, int height)
        {
            return new TableLayoutPanel {
                ColumnCount = columns, RowCount = 1,
                AutoSize = height == 0, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Height = height, Width = 604, Margin = new Padding(0, 0, 0, 8),
                GrowStyle = TableLayoutPanelGrowStyle.AddRows
            };
        }

        private static Label NewAdaptiveText(string text, bool bold, float size = 9F)
        {
            return new Label {
                Text = text, AutoSize = true, MaximumSize = new Size(560, 0),
                Margin = new Padding(4, 5, 4, 4), ForeColor = bold ? Ink : Muted,
                Font = new Font("Microsoft YaHei UI", size,
                    bold ? FontStyle.Bold : FontStyle.Regular)
            };
        }

        private static RoundedButton NewChromeButton(string text)
        {
            var button = NewButton(text, 0, 0, 36);
            button.Size = new Size(38, 32);
            button.Anchor = AnchorStyles.None;
            button.Margin = new Padding(3);
            button.CornerRadius = 8;
            button.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            button.Tag = "ChromeButton";
            return button;
        }

        private static FlowLayoutPanel NewPageHeading(string title, string subtitle)
        {
            var heading = NewAdaptiveFlow(FlowDirection.TopDown);
            heading.Controls.Add(NewAdaptiveText(title, true, 20F));
            heading.Controls.Add(NewAdaptiveText(subtitle, false, 9F));
            heading.Margin = new Padding(0, 0, 0, 12);
            return heading;
        }

        private static RoundedButton NewNavigationButton(string text, bool selected)
        {
            var button = NewButton((selected ? "•  " : "   ") + text, 0, 0, 178);
            button.Width = 178;
            button.Height = 42;
            button.Margin = new Padding(0, 0, 0, 6);
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(14, 0, 0, 0);
            button.Tag = selected ? "NavSelectedButton" : "NavButton";
            return button;
        }

        private void SelectPage(FlowLayoutPanel selectedPage,
            RoundedButton selected, params RoundedButton[] others)
        {
            selected.Tag = "NavSelectedButton";
            selected.Text = "•  " + selected.Text.TrimStart('•', ' ');
            foreach (var button in others)
            {
                button.Tag = "NavButton";
                button.Text = "   " + button.Text.TrimStart('•', ' ');
            }
            ApplyTheme(ThemeManager.Current);
            Control parent = selectedPage.Parent;
            if (parent != null)
            {
                foreach (Control page in parent.Controls) page.Visible = page == selectedPage;
                selectedPage.BringToFront();
            }
            selectedPage.AutoScrollPosition = Point.Empty;
            FitAdaptiveContent(selectedPage);
        }

        private void QueueResizeLayout()
        {
            resizeUiTimer.Stop();
            resizeUiTimer.Start();
        }

        private void WireWindowDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized) return;
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
            };
            foreach (Control child in control.Controls)
            {
                if (child is Button || child is ComboBox) continue;
                WireWindowDrag(child);
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        private void FitAdaptiveContent(FlowLayoutPanel page)
        {
            int scrollbar = page.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            int width = Math.Max(632, page.ClientSize.Width - page.Padding.Horizontal - scrollbar - 2);
            foreach (Control control in page.Controls)
            {
                var card = control as RoundedFlowLayoutPanel;
                if (card != null)
                {
                    card.MinimumSize = new Size(width, 0);
                    card.MaximumSize = new Size(width, 0);
                    card.Width = width;
                    FitAdaptiveChildren(card, Math.Max(560, width - card.Padding.Horizontal));
                }
                else if (control is TableLayoutPanel || control is FlowLayoutPanel)
                    control.Width = width;
                else if (Equals(control.Tag, "EmbeddedToolsHost"))
                    control.Width = width;
            }
        }

        private static void FitAdaptiveChildren(Control parent, int width)
        {
            foreach (Control child in parent.Controls)
            {
                var table = child as TableLayoutPanel;
                if (table != null) table.Width = width;
                var curve = child as ToneCurveGraph;
                if (curve != null) curve.Width = width;
                if (child.HasChildren && !(child is ComboBox))
                    FitAdaptiveChildren(child, Math.Max(120, child.ClientSize.Width - child.Padding.Horizontal));
            }
        }

        private void UpdateWindowShape()
        {
            Region previous = Region;
            Region = null;
            if (previous != null) previous.Dispose();
            // Windows 11 can round the native top-level surface in the DWM
            // compositor. Unlike a managed Region this does not rebuild and
            // clip every child control during maximize/restore animation.
            int preference = WindowState == FormWindowState.Maximized ? 1 : 2;
            try
            {
                DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            }
            catch { }
        }

        private static TableLayoutPanel NewAdaptiveSliderRow(
            string caption, ComboBox backend, TrackBar slider, Label value)
        {
            var row = NewAdaptiveGrid(3, 70);
            row.RowCount = 2;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            var label = NewAdaptiveText(caption, true);
            row.Controls.Add(label, 0, 0);
            if (backend != null)
            {
                backend.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                backend.Margin = new Padding(8, 1, 4, 1);
                row.Controls.Add(backend, 1, 0);
            }
            value.AutoSize = false; value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.MiddleCenter;
            row.Controls.Add(value, 2, 0);
            slider.Dock = DockStyle.Fill; slider.Margin = new Padding(4, 2, 4, 2);
            row.Controls.Add(slider, 0, 1); row.SetColumnSpan(slider, 3);
            return row;
        }

        private static void AddStyleCell(TableLayoutPanel grid, int column, int row,
            string caption, TrackBar slider, Label value)
        {
            var cell = new TableLayoutPanel {
                ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill,
                AutoSize = true, Margin = new Padding(4), Padding = new Padding(0, 2, 0, 4)
            };
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            cell.Controls.Add(NewAdaptiveText(caption, true), 0, 0);
            value.AutoSize = false; value.Dock = DockStyle.Fill;
            value.TextAlign = ContentAlignment.MiddleCenter;
            cell.Controls.Add(value, 1, 0);
            slider.Dock = DockStyle.Fill; slider.Margin = new Padding(0, 2, 0, 0);
            cell.Controls.Add(slider, 0, 1); cell.SetColumnSpan(slider, 2);
            grid.Controls.Add(cell, column, row);
        }

        private void AssignThemeRoles(
            RoundedPanel main, RoundedPanel tint, RoundedPanel preset, RoundedPanel action,
            Label themeLabel, params RoundedButton[] buttons)
        {
            main.Tag = "MainSurface";
            tint.Tag = "TintSurface";
            if (preset != null) preset.Tag = "SoftSurface";
            action.Tag = "SoftSurface";
            themeLabel.Tag = "CanvasMuted";
            titleRole(Controls);

            foreach (var button in buttons)
            {
                bool primary = button.ForeColor == Color.White;
                button.Tag = button.Location.Y < 80
                    ? (primary ? "CanvasPrimaryButton" : "CanvasSecondaryButton")
                    : (primary ? "PrimaryButton" : "SecondaryButton");
            }
            themeBox.Tag = "CanvasCombo";
            stylePresetBox.Tag = "TintCombo";
            strengthBox.Tag = "TintCombo";
            monitorBox.Tag = "SurfaceCombo";
            saturationBackend.Tag = "SurfaceCombo";
            liveApply.Tag = "SurfaceMuted";
            syncAllDisplays.Tag = "SurfaceMuted";
            restoreOnExit.Tag = "SoftMuted";
            styleShadows.Tag = styleHighlights.Tag = styleMidtones.Tag =
                styleBlackPoint.Tag = styleWhitePoint.Tag = styleContrastPivot.Tag =
                styleTemperature.Tag = styleTint.Tag = styleVibrance.Tag = "TintControl";
            styleExposure.Tag = styleShadowRange.Tag = styleHighlightRange.Tag =
                styleTransitionSoftness.Tag = "TintControl";
            brightness.Tag = contrast.Tag = saturation.Tag = "SurfaceControl";
            styleShadowValue.Tag = styleHighlightValue.Tag = styleMidtoneValue.Tag =
                styleBlackPointValue.Tag = styleWhitePointValue.Tag =
                styleContrastPivotValue.Tag = styleTemperatureValue.Tag =
                styleTintValue.Tag = styleVibranceValue.Tag = "Pill";
            styleExposureValue.Tag = styleShadowRangeValue.Tag =
                styleHighlightRangeValue.Tag = styleTransitionSoftnessValue.Tag = "Pill";
        }

        private void titleRole(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                if (control.HasChildren) titleRole(control.Controls);
                if (control.Tag != null || control == status) continue;
                var label = control as Label;
                if (label != null)
                {
                    bool isMuted = label.ForeColor == Muted;
                    if (label.Location.Y < 80)
                        label.Tag = isMuted ? "CanvasMuted" : "CanvasText";
                    else if (label.Location.Y >= 447 && label.Location.Y < 1001)
                        label.Tag = isMuted ? "TintMuted" : "TintText";
                    else if (label.Location.Y >= 1013)
                        label.Tag = isMuted ? "SoftMuted" : "SoftText";
                    else
                        label.Tag = isMuted ? "SurfaceMuted" : "SurfaceText";
                }
            }
        }

        private void ApplyTheme(AppTheme theme)
        {
            BackColor = theme.Canvas;
            ApplyThemeToControls(Controls, theme, "Canvas");
            status.ForeColor = theme.Muted;
            Invalidate(true);
        }

        private void ApplyThemeToControls(Control.ControlCollection controls, AppTheme theme,
            string inheritedSurface)
        {
            foreach (Control control in controls)
            {
                string role = control.Tag as string;
                string surface = role == "TintContainer" ? "Tint" :
                    role == "SoftContainer" ? "Soft" :
                    role == "SurfaceContainer" ? "Surface" :
                    role == "CanvasContainer" ? "Canvas" : inheritedSurface;
                Color surfaceColor = surface == "Tint" ? theme.TintSurface :
                    surface == "Soft" ? theme.SoftSurface :
                    surface == "Surface" ? theme.Surface : theme.Canvas;
                var roundedFlow = control as RoundedFlowLayoutPanel;
                if (roundedFlow != null)
                {
                    roundedFlow.FillColor = surfaceColor;
                    roundedFlow.BorderColor = BlendColor(surfaceColor, theme.Muted, 0.28F);
                    roundedFlow.Invalidate();
                }
                var layout = control as Panel;
                if (layout != null && !(layout is RoundedPanel) && roundedFlow == null)
                    layout.BackColor = surfaceColor;
                var panel = control as RoundedPanel;
                if (panel != null)
                {
                    panel.FillColor = role == "TintSurface" ? theme.TintSurface :
                                      role == "SoftSurface" ? theme.SoftSurface : theme.Surface;
                    panel.Invalidate();
                    continue;
                }
                var button = control as RoundedButton;
                if (button != null)
                {
                    bool primary = role == "PrimaryButton" || role == "CanvasPrimaryButton";
                    bool navSelected = role == "NavSelectedButton";
                    bool nav = role == "NavButton";
                    bool chrome = role == "ChromeButton";
                    button.BackColor = primary ? theme.Accent :
                        navSelected ? theme.AccentSoft :
                        nav ? theme.SoftSurface :
                        chrome ? theme.TintSurface : theme.Button;
                    button.ForeColor = primary ? theme.OnAccent :
                        navSelected ? theme.Ink :
                        (nav || chrome) ? theme.Muted : theme.Ink;
                    button.SurfaceColor = (nav || navSelected) ? theme.SoftSurface : surfaceColor;
                    button.Invalidate();
                    continue;
                }
                var pill = control as RoundedPillLabel;
                if (pill != null)
                {
                    pill.FillColor = theme.AccentSoft;
                    pill.ForeColor = theme.Accent;
                    pill.Invalidate();
                    continue;
                }
                bool muted = role != null && role.EndsWith("Muted", StringComparison.Ordinal);
                control.ForeColor = muted ? theme.Muted : theme.Ink;
                control.BackColor = surfaceColor;
                var themedCombo = control as ThemedComboBox;
                if (themedCombo != null)
                    themedCombo.BorderColor = BlendColor(
                        surfaceColor, theme.Accent, 0.28F);
                control.Invalidate();
                if (control.HasChildren)
                    ApplyThemeToControls(control.Controls, theme, surface);
            }
        }

        private static Color BlendColor(Color background, Color foreground, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                255,
                (int)Math.Round(background.R + (foreground.R - background.R) * amount),
                (int)Math.Round(background.G + (foreground.G - background.G) * amount),
                (int)Math.Round(background.B + (foreground.B - background.B) * amount));
        }

        private PhysicalDisplay Selected
        {
            get {
                int index = monitorBox.SelectedIndex;
                return index >= 0 && index < displays.Count ? displays[index] : null;
            }
        }

        private async void RefreshDisplays()
        {
            if (displayRefreshRunning)
            {
                displayRefreshPending = true;
                return;
            }
            displayRefreshRunning = true;
            displayRefreshPending = false;
            var oldDisplays = new List<PhysicalDisplay>(displays);
            displays.Clear();
            rebuildingDisplayList = true;
            monitorBox.Items.Clear();
            rebuildingDisplayList = false;
            try
            {
                var detected = await System.Threading.Tasks.Task.Run(delegate {
                    foreach (var oldDisplay in oldDisplays)
                    {
                        try { oldDisplay.Dispose(); } catch { }
                    }
                    var found = MonitorApi.Enumerate();
                    foreach (var display in found)
                    {
                        try { display.Refresh(); }
                        catch { display.MarkProbeFailed(); }
                    }
                    return found;
                });
                if (closing)
                {
                    foreach (var display in detected) display.Dispose();
                    return;
                }
                displays.AddRange(detected);
                rebuildingDisplayList = true;
                foreach (var d in displays) monitorBox.Items.Add(d.Name);
                if (displays.Count > 0)
                {
                    monitorBox.SelectedIndex = 0;
                    rebuildingDisplayList = false;
                    LoadSelectedMonitor(false);
                    SetStatus("已检测到 " + displays.Count + " 个物理显示器。", false);
                    if (reapplyStyleAfterRefresh)
                    {
                        reapplyStyleAfterRefresh = false;
                        ApplyStylePreview();
                    }
                }
                else
                {
                    rebuildingDisplayList = false;
                    DisableControls();
                    SetStatus("没有发现可通过 Windows DDC/CI 访问的物理显示器。", true);
                }
            }
            catch (Exception ex)
            {
                rebuildingDisplayList = false;
                DisableControls();
                SetStatus("检测失败：" + ex.Message, true);
            }
            finally
            {
                rebuildingDisplayList = false;
                displayRefreshRunning = false;
                if (displayRefreshPending && !closing)
                    BeginInvoke((MethodInvoker)RefreshDisplays);
            }
        }

        private void LoadSelectedMonitor(bool refreshHardware = true)
        {
            var d = Selected;
            if (d == null) return;
            try
            {
                gammaController.SetTarget(d.DisplayDeviceName);
                if (refreshHardware) d.Refresh();
                brightness.Enabled = d.SupportsBrightness;
                contrast.Enabled = d.SupportsContrast;
                brightness.Minimum = (int)Math.Min(d.BrightnessMin, 100);
                brightness.Maximum = (int)Math.Max(d.BrightnessMax, d.BrightnessMin + 1);
                brightness.Value = Clamp((int)d.BrightnessCurrent, brightness.Minimum, brightness.Maximum);
                brightnessValue.Text = d.SupportsBrightness ? brightness.Value.ToString() : "不支持";
                contrast.Minimum = (int)Math.Min(d.ContrastMin, 100);
                contrast.Maximum = (int)Math.Max(d.ContrastMax, d.ContrastMin + 1);
                contrast.Value = Clamp((int)d.ContrastCurrent, contrast.Minimum, contrast.Maximum);
                contrastValue.Text = d.SupportsContrast ? contrast.Value.ToString() : "不支持";
                int requestedBackend = appSettings.SaturationBackend;
                bool hardwareSelected = requestedBackend == 1;
                bool softwareSelected = requestedBackend == 2;
                bool useHardware = !softwareSelected && d.SupportsSaturation;
                bool useSoftware = softwareSelected ||
                    (requestedBackend == 0 && !d.SupportsSaturation);
                usingSoftwareSaturation = useSoftware;
                if (useHardware)
                {
                    if (softwareSaturation.Value != 100)
                        softwareSaturation.Restore();
                    saturationTitle.Text = "饱和度";
                    saturation.Minimum = (int)Math.Min(d.SaturationMin, 100);
                    saturation.Maximum = (int)Math.Max(d.SaturationMax, d.SaturationMin + 1);
                    saturation.Value = Clamp((int)d.SaturationCurrent,
                        saturation.Minimum, saturation.Maximum);
                    saturationHint.Visible = false;
                }
                else if (useSoftware)
                {
                    saturationTitle.Text = "饱和度";
                    saturation.Minimum = 0;
                    saturation.Maximum = 200;
                    saturation.Value = softwareSaturation.Value;
                    saturationHint.Text = softwareSaturation.IsAvailable
                        ? "⚠ 软件饱和度可能与 DLSS FG、AFMF 或第三方插帧冲突。\r\n开启游戏性能保护后，全屏游戏期间会自动暂停。"
                        : "当前 Windows 环境不支持全屏颜色矩阵";
                    saturationHint.Visible = !softwareSaturation.IsAvailable;
                }
                else
                {
                    saturationTitle.Text = "饱和度";
                    saturation.Minimum = 0;
                    saturation.Maximum = 200;
                    saturation.Value = 100;
                    saturationHint.Text = hardwareSelected
                        ? "此显示器不支持 DDC/CI 饱和度，请改用自动或 Windows 软件色彩"
                        : "当前饱和度后端不可用";
                    saturationHint.Visible = true;
                }
                bool saturationAvailable = useHardware ||
                    (useSoftware && softwareSaturation.IsAvailable);
                saturation.Enabled = saturationAvailable;
                // Preserve the row geometry when a backend is unavailable.
                // Hiding controls makes TableLayoutPanel collapse/reflow the
                // row and moves the selector and following content.
                saturation.Visible = true;
                saturationValue.Text = saturationAvailable
                    ? saturation.Value.ToString() : "不可用";
                saturationValue.Visible = true;
                saturationHint.Visible = useSoftware || !saturationAvailable;
                saturationHint.ForeColor = useSoftware && softwareSaturation.IsAvailable
                    ? Color.FromArgb(194, 104, 42) : Muted;
                saturationHint.BringToFront();

                if (!startup.ContainsKey(d.Id))
                    startup[d.Id] = Snapshot.From(d);
                SetStatus(CapabilitySummary(d), false);
            }
            catch (Exception ex)
            {
                DisableControls();
                SetStatus("读取显示器失败：" + ex.Message, true);
            }
        }

        private void ApplyCurrent()
        {
            var d = Selected;
            if (d == null) return;
            var results = new List<string>();
            Cursor = Cursors.WaitCursor;
            try
            {
                if (d.SupportsBrightness)
                {
                    d.SetBrightness((uint)brightness.Value);
                    results.Add("亮度");
                    Thread.Sleep(80);
                }
                if (d.SupportsContrast)
                {
                    d.SetContrast((uint)contrast.Value);
                    results.Add("对比度");
                    Thread.Sleep(80);
                }
                if (!usingSoftwareSaturation && d.SupportsSaturation)
                {
                    d.SetSaturation((uint)saturation.Value);
                    results.Add("饱和度");
                    Thread.Sleep(80);
                }
                else if (softwareSaturation.IsAvailable)
                {
                    if (ApplySoftwareSaturationSafely(saturation.Value))
                        results.Add("软件饱和度");
                    else results.Add("软件饱和度（游戏性能保护已拦截）");
                }
                d.Refresh();
                SetStatus(results.Count == 0 ? "当前显示器没有可调项目。" : "已应用：" + string.Join("、", results), results.Count == 0);
            }
            catch (Exception ex)
            {
                SetStatus("部分参数可能未应用：" + ex.Message, true);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void QueueLiveApply(bool applyBrightness, bool applyContrast, bool applySaturation = false)
        {
            if (!liveApply.Checked) return;
            pendingBrightness |= applyBrightness;
            pendingContrast |= applyContrast;
            pendingSaturation |= applySaturation;
            liveApplyTimer.Stop();
            liveApplyTimer.Start();
            SetStatus("正在预览…", false);
        }

        private void FlushLiveApply()
        {
            if (!liveApply.Checked) return;
            liveApplyTimer.Stop();
            ApplyPendingSliders();
        }

        private async void ApplyPendingSliders()
        {
            if (liveWorkerRunning || closing) return;
            liveWorkerRunning = true;
            try
            {
                while (!closing && (pendingBrightness || pendingContrast || pendingSaturation))
                {
                    var d = Selected;
                    if (d == null) break;
                    bool doBrightness = pendingBrightness;
                    bool doContrast = pendingContrast;
                    bool doSaturation = pendingSaturation;
                    uint requestedBrightness = (uint)brightness.Value;
                    uint requestedContrast = (uint)contrast.Value;
                    uint requestedSaturation = (uint)saturation.Value;
                    bool applySoftwareSaturation = doSaturation &&
                        usingSoftwareSaturation && softwareSaturation.IsAvailable;
                    pendingBrightness = false;
                    pendingContrast = false;
                    pendingSaturation = false;

                    string result = null;
                    Exception failure = null;
                    await Task.Run(delegate
                    {
                        try
                        {
                            var changed = new List<string>();
                            if (doBrightness && d.SupportsBrightness)
                            {
                                d.SetBrightness(requestedBrightness);
                                changed.Add("亮度 " + d.BrightnessCurrent);
                            }
                            if (doContrast && d.SupportsContrast)
                            {
                                d.SetContrast(requestedContrast);
                                changed.Add("对比度 " + d.ContrastCurrent);
                            }
                            if (doSaturation && !usingSoftwareSaturation && d.SupportsSaturation)
                            {
                                d.SetSaturation(requestedSaturation);
                                changed.Add("饱和度 " + d.SaturationCurrent);
                            }
                            result = changed.Count == 0
                                ? "当前项目不受显示器支持。"
                                : "实时应用：" + string.Join("，", changed);
                        }
                        catch (Exception ex) { failure = ex; }
                    });

                    if (closing) break;
                    if (failure == null && applySoftwareSaturation)
                    {
                        try
                        {
                            bool applied = ApplySoftwareSaturationSafely(
                                (int)requestedSaturation);
                            result = applied
                                ? "实时应用：软件饱和度 " + requestedSaturation
                                : "游戏性能保护：全屏期间不启用软件饱和度。";
                        }
                        catch (Exception ex) { failure = ex; }
                    }
                    if (failure != null)
                        SetStatus("实时应用失败：" + failure.Message, true);
                    else
                        SetStatus(result, result == "当前项目不受显示器支持。");
                }
            }
            finally
            {
                liveWorkerRunning = false;
                // A slider event may have arrived between the last loop check
                // and clearing the worker flag.
                if (!closing && (pendingBrightness || pendingContrast || pendingSaturation))
                    BeginInvoke((MethodInvoker)delegate { ApplyPendingSliders(); });
            }
        }

        private async void RestoreStartup()
        {
            if (displayResetRunning) return;
            var d = Selected;
            if (d == null || !startup.ContainsKey(d.Id)) return;
            var snapshot = startup[d.Id];
            displayResetRunning = true;
            liveApplyTimer.Stop();
            pendingBrightness = false;
            pendingContrast = false;
            pendingSaturation = false;
            // Give immediate visual feedback. Hardware DDC/CI restoration and
            // verification continue in the background and can take seconds.
            if (usingSoftwareSaturation)
            {
                try { softwareSaturation.Restore(); } catch { }
                saturation.Value = 100;
                saturationValue.Text = "100";
            }
            SyncControlsToSnapshot(snapshot);
            SetStatus("滑块已重置，正在后台恢复显示器硬件参数…", false);
            try
            {
                while (liveWorkerRunning && !closing)
                    await Task.Delay(30);
                await Task.Run(delegate {
                    snapshot.Apply(d);
                    d.Refresh();
                });
                if (closing) return;
                if (Selected != d)
                {
                    SetStatus("原显示器参数已恢复；当前选择已发生变化。", false);
                    return;
                }
                LoadSelectedMonitor(false);
                SyncControlsToSnapshot(snapshot);
                SetStatus("已恢复该显示器启动前的状态。", false);
            }
            catch (Exception ex) { SetStatus("恢复失败：" + ex.Message, true); }
            finally { displayResetRunning = false; }
        }

        private void SyncControlsToSnapshot(Snapshot snapshot)
        {
            if (snapshot.HasBrightness && brightness.Enabled)
            {
                brightness.Value = Clamp((int)snapshot.Brightness, brightness.Minimum, brightness.Maximum);
                brightnessValue.Text = brightness.Value.ToString();
            }
            if (snapshot.HasContrast && contrast.Enabled)
            {
                contrast.Value = Clamp((int)snapshot.Contrast, contrast.Minimum, contrast.Maximum);
                contrastValue.Text = contrast.Value.ToString();
            }
            if (snapshot.HasSaturation && saturation.Enabled)
            {
                saturation.Value = Clamp((int)snapshot.Saturation, saturation.Minimum, saturation.Maximum);
                saturationValue.Text = saturation.Value.ToString();
            }
            else if (usingSoftwareSaturation && saturation.Enabled)
            {
                saturation.Value = 100;
                saturationValue.Text = "100";
            }
        }

        private void OpenToneCurve()
        {
            try
            {
                if (!gammaController.IsCaptured)
                    gammaController.CaptureOriginal();
                using (var form = new ToneCurveForm(gammaController))
                    form.ShowDialog(this);
            }
            catch (Exception ex)
            {
                SetStatus("无法打开色调曲线：" + ex.Message, true);
            }
        }

        private void WireStyleSlider(
            TrackBar slider, Label valueLabel, bool percent, string parameterName)
        {
            styleValueLabels[slider] = valueLabel;
            styleBaselineValues[slider] = slider.Value;
            styleSliderNames[slider] = parameterName;
            valueLabel.Cursor = Cursors.Hand;
            UpdateStyleValueLabel(slider, percent);

            slider.Scroll += delegate { OnStyleSliderChanged(slider, percent, false); };
            slider.MouseUp += delegate {
                stylePreviewTimer.Stop();
                ApplyStylePreview();
            };
            slider.DoubleClick += delegate {
                SetStyleSliderValue(slider, 0, percent, true);
            };
            slider.KeyDown += delegate(object sender, KeyEventArgs e) {
                int direction = e.KeyCode == Keys.Left || e.KeyCode == Keys.Down ? -1 :
                                e.KeyCode == Keys.Right || e.KeyCode == Keys.Up ? 1 : 0;
                if (direction == 0) return;
                int step = e.Shift ? 10 : 1;
                SetStyleSliderValue(slider, slider.Value + direction * step, percent, false);
                e.Handled = true;
                e.SuppressKeyPress = true;
            };
            valueLabel.Click += delegate {
                string input = Prompt.Show(
                    parameterName + "（" + slider.Minimum + " 至 " + slider.Maximum + "）",
                    "输入参数数值", slider.Value.ToString());
                int value;
                if (string.IsNullOrWhiteSpace(input)) return;
                if (!int.TryParse(input.Trim(), out value))
                {
                    MessageBox.Show(this, "请输入整数。", "参数无效",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SetStyleSliderValue(slider, value, percent, true);
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("复制数值", null, delegate {
                try { Clipboard.SetText(slider.Value.ToString()); }
                catch (Exception ex) { SetStatus("复制失败：" + ex.Message, true); }
            });
            menu.Items.Add("粘贴数值", null, delegate {
                try
                {
                    int value;
                    if (!int.TryParse(Clipboard.GetText().Trim(), out value))
                    {
                        SetStatus("剪贴板中没有可用的整数参数。", true);
                        return;
                    }
                    SetStyleSliderValue(slider, value, percent, true);
                }
                catch (Exception ex) { SetStatus("粘贴失败：" + ex.Message, true); }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("重置此项", null, delegate {
                SetStyleSliderValue(slider, 0, percent, true);
            });
            slider.ContextMenuStrip = menu;
            valueLabel.ContextMenuStrip = menu;
        }

        private void SetStyleSliderValue(
            TrackBar slider, int value, bool percent, bool applyImmediately)
        {
            slider.Value = Clamp(value, slider.Minimum, slider.Maximum);
            OnStyleSliderChanged(slider, percent, applyImmediately);
        }

        private void OnStyleSliderChanged(
            TrackBar slider, bool percent, bool applyImmediately)
        {
            UpdateStyleValueLabel(slider, percent);
            UpdateStyleRiskStatus();
            UpdateLiveToneCurve();
            if (settingStyleControls) return;
            stylePreviewTimer.Stop();
            if (applyImmediately) ApplyStylePreview();
            else stylePreviewTimer.Start();

            int baseline;
            styleBaselineValues.TryGetValue(slider, out baseline);
            int delta = slider.Value - baseline;
            string name;
            if (!styleSliderNames.TryGetValue(slider, out name)) name = "参数";
            SetStatus(
                name + "：基准 " + FormatStyleValue(baseline, percent) +
                " → 当前 " + FormatStyleValue(slider.Value, percent) +
                "（差值 " + FormatStyleValue(delta, percent) + "）", false);
        }

        private void UpdateStyleValueLabel(TrackBar slider, bool percent)
        {
            Label label;
            if (!styleValueLabels.TryGetValue(slider, out label)) return;
            label.Text = FormatStyleValue(slider.Value, percent);
            int baseline;
            styleBaselineValues.TryGetValue(slider, out baseline);
            int delta = slider.Value - baseline;
            string name;
            styleSliderNames.TryGetValue(slider, out name);
            helpTip.SetToolTip(label,
                (name ?? "参数") + "：基准 " + FormatStyleValue(baseline, percent) +
                "，当前 " + FormatStyleValue(slider.Value, percent) +
                "，差值 " + FormatStyleValue(delta, percent) +
                "。点击可输入，右键可复制、粘贴或重置。");
        }

        private void UpdateStyleRiskStatus()
        {
            var risks = AnalyzeStyleRisks();
            if (risks.Count == 0)
            {
                styleRiskStatus.Text = "曲线状态良好 · 未发现明显灰雾、裁切或参数冲突";
                styleRiskStatus.ForeColor = ThemeManager.Current.Muted;
                return;
            }
            styleRiskStatus.Text = "调整建议 · " + string.Join("；", risks.ToArray());
            styleRiskStatus.ForeColor = Color.FromArgb(218, 126, 62);
        }

        private List<string> AnalyzeStyleRisks()
        {
            var risks = new List<string>();
            if (styleShadows.Value >= 55 &&
                styleBlackPoint.Value >= -3 &&
                styleContrastPivot.Value <= 10)
                risks.Add("暗部抬升较高但黑位与反差不足，可能灰蒙");

            if (styleShadows.Value >= 80 && styleMidtones.Value >= 25)
                risks.Add("暗部和中间调重复提亮");

            if (styleHighlights.Value >= 70 && styleWhitePoint.Value <= -18)
                risks.Add("高光与白位重复压缩，亮部层次可能变平");

            if (styleExposure.Value >= 18 && styleHighlights.Value >= 50)
                risks.Add("曝光提升与高光压制互相抵消");

            if (styleExposure.Value <= -18 && styleShadows.Value >= 55)
                risks.Add("负曝光与暗部补偿互相抵消");

            if (Math.Abs(styleContrastPivot.Value) >= 45 &&
                (styleShadows.Value >= 45 || styleHighlights.Value >= 45))
                risks.Add("中间对比过强，可能覆盖明暗塑形");

            if (styleVibrance.Value >= 55 && styleContrastPivot.Value >= 30)
                risks.Add("自然色彩与高对比叠加，通道可能提前裁切");

            if (styleTransitionSoftness.Value >= 45 &&
                (styleShadows.Value >= 55 || styleHighlights.Value >= 55))
                risks.Add("过渡范围过宽，容易降低局部反差");

            if (Math.Abs(styleShadowRange.Value) >= 25 && styleShadows.Value < 10)
                risks.Add("暗部范围已调整，但暗部补偿接近零");

            if (Math.Abs(styleHighlightRange.Value) >= 25 && styleHighlights.Value < 10)
                risks.Add("高光范围已调整，但高光压制接近零");

            return risks;
        }

        private void OptimizeCurrentStyle()
        {
            var before = AnalyzeStyleRisks();
            if (before.Count == 0)
            {
                SetStatus("当前参数没有发现需要自动修正的明显冲突。", false);
                return;
            }

            settingStyleControls = true;
            try
            {
                if (styleShadows.Value >= 55 &&
                    styleBlackPoint.Value >= -3 &&
                    styleContrastPivot.Value <= 10)
                {
                    styleBlackPoint.Value = Clamp(
                        -Math.Max(6, Math.Min(18, styleShadows.Value / 9)),
                        styleBlackPoint.Minimum, styleBlackPoint.Maximum);
                    styleContrastPivot.Value = Clamp(
                        Math.Max(10, styleContrastPivot.Value),
                        styleContrastPivot.Minimum, styleContrastPivot.Maximum);
                }
                if (styleShadows.Value >= 80 && styleMidtones.Value >= 25)
                    styleMidtones.Value = Clamp(20, styleMidtones.Minimum, styleMidtones.Maximum);

                if (styleHighlights.Value >= 70 && styleWhitePoint.Value <= -18)
                    styleWhitePoint.Value = Clamp(-12, styleWhitePoint.Minimum, styleWhitePoint.Maximum);

                if (styleExposure.Value >= 18 && styleHighlights.Value >= 50)
                    styleExposure.Value = Clamp(14, styleExposure.Minimum, styleExposure.Maximum);

                if (styleExposure.Value <= -18 && styleShadows.Value >= 55)
                    styleExposure.Value = Clamp(-12, styleExposure.Minimum, styleExposure.Maximum);

                if (Math.Abs(styleContrastPivot.Value) >= 45 &&
                    (styleShadows.Value >= 45 || styleHighlights.Value >= 45))
                    styleContrastPivot.Value = Clamp(
                        Math.Sign(styleContrastPivot.Value) * 32,
                        styleContrastPivot.Minimum, styleContrastPivot.Maximum);

                if (styleVibrance.Value >= 55 && styleContrastPivot.Value >= 30)
                    styleVibrance.Value = Clamp(45, styleVibrance.Minimum, styleVibrance.Maximum);

                if (styleTransitionSoftness.Value >= 45 &&
                    (styleShadows.Value >= 55 || styleHighlights.Value >= 55))
                    styleTransitionSoftness.Value = Clamp(
                        30, styleTransitionSoftness.Minimum, styleTransitionSoftness.Maximum);

                if (Math.Abs(styleShadowRange.Value) >= 25 && styleShadows.Value < 10)
                    styleShadowRange.Value = 0;
                if (Math.Abs(styleHighlightRange.Value) >= 25 && styleHighlights.Value < 10)
                    styleHighlightRange.Value = 0;

                RefreshAllStyleValueLabels();
            }
            finally { settingStyleControls = false; }

            UpdateStyleRiskStatus();
            ApplyStylePreview();
            SetStatus("已修正 " + before.Count + " 项参数冲突；整体风格方向保持不变。", false);
        }

        private void RefreshAllStyleValueLabels()
        {
            foreach (var pair in styleValueLabels)
                UpdateStyleValueLabel(pair.Key,
                    pair.Key == styleShadows || pair.Key == styleHighlights);
            UpdateLiveToneCurve();
        }

        private void UpdateLiveToneCurve()
        {
            liveToneCurve.Shadows = GammaRampController.ScalePositive(styleShadows.Value, 200);
            liveToneCurve.Highlights = GammaRampController.ScalePositive(styleHighlights.Value, 200);
            liveToneCurve.Midtones = GammaRampController.ScaleSigned(styleMidtones.Value);
            liveToneCurve.BlackPoint = GammaRampController.ScaleSigned(styleBlackPoint.Value);
            liveToneCurve.WhitePoint = GammaRampController.ScaleSigned(styleWhitePoint.Value);
            liveToneCurve.ContrastPivot = GammaRampController.ScaleSigned(styleContrastPivot.Value);
            liveToneCurve.Vibrance = GammaRampController.ScaleSigned(styleVibrance.Value);
            liveToneCurve.Exposure = GammaRampController.ScaleSigned(styleExposure.Value);
            liveToneCurve.ShadowRange = GammaRampController.ScaleSigned(styleShadowRange.Value);
            liveToneCurve.HighlightRange = GammaRampController.ScaleSigned(styleHighlightRange.Value);
            liveToneCurve.TransitionSoftness =
                GammaRampController.ScaleSigned(styleTransitionSoftness.Value);
            liveToneCurve.Invalidate();
        }

        private void ExportCurrentLut()
        {
            string presetName = activeStyle == null ? "ShadePilot-current" : activeStyle.Name;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                presetName = presetName.Replace(invalid, '_');
            using (var dialog = new SaveFileDialog {
                Title = "导出供 OBS 使用的 3D LUT",
                Filter = "Adobe Cube 3D LUT (*.cube)|*.cube",
                FileName = presetName + ".cube",
                AddExtension = true,
                DefaultExt = "cube"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    CubeLutExporter.Export(dialog.FileName, 33,
                        styleShadows.Value, styleHighlights.Value, styleMidtones.Value,
                        styleBlackPoint.Value, styleWhitePoint.Value,
                        styleContrastPivot.Value, styleTemperature.Value,
                        styleTint.Value, styleVibrance.Value, saturation.Value,
                        styleExposure.Value, styleShadowRange.Value,
                        styleHighlightRange.Value, styleTransitionSoftness.Value);
                    SetStatus("OBS LUT 已导出。请在来源或场景的“应用 LUT”滤镜中选择该文件。", false);
                }
                catch (Exception ex)
                {
                    SetStatus("LUT 导出失败：" + ex.Message, true);
                }
            }
        }

        private static string FormatStyleValue(int value, bool percent)
        {
            string number = value > 0 ? "+" + value : value.ToString();
            return percent ? number + "%" : number;
        }

        private void ApplySelectedStyle()
        {
            var preset = stylePresetBox.SelectedItem as StylePresetData;
            if (preset == null) return;
            ApplyStylePreset(preset);
        }

        private void SaveCurrentStyle()
        {
            string name = Prompt.Show("输入风格预设名称", "保存当前画面风格");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            var existing = styleStore.Items.Find(
                p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new StylePresetData { Name = name };
                styleStore.Items.Add(existing);
            }
            existing.Shadows = styleShadows.Value;
            existing.Highlights = styleHighlights.Value;
            existing.Midtones = styleMidtones.Value;
            existing.BlackPoint = styleBlackPoint.Value;
            existing.WhitePoint = styleWhitePoint.Value;
            existing.ContrastPivot = styleContrastPivot.Value;
            existing.Temperature = styleTemperature.Value;
            existing.Tint = styleTint.Value;
            existing.Vibrance = styleVibrance.Value;
            existing.Saturation = saturation.Value;
            existing.Exposure = styleExposure.Value;
            existing.ShadowRange = styleShadowRange.Value;
            existing.HighlightRange = styleHighlightRange.Value;
            existing.TransitionSoftness = styleTransitionSoftness.Value;
            styleStore.Save();
            ReloadStylePresets(existing);
            RegisterGlobalHotkeys();
            SetStatus("已保存画面风格“" + name + "”，可在“快捷键”中为它分配按键。", false);
        }

        private void ReloadStylePresets(StylePresetData selected)
        {
            stylePresetBox.Items.Clear();
            foreach (var style in styleStore.Items) stylePresetBox.Items.Add(style);
            if (selected != null) stylePresetBox.SelectedItem = selected;
            if (stylePresetBox.SelectedIndex < 0 && stylePresetBox.Items.Count > 0)
                stylePresetBox.SelectedIndex = 0;
        }

        private void ConfigureHotkeys()
        {
            using (var dialog = new HotkeySettingsForm(styleStore, hotkeyRegistrationStatus))
            {
                dialog.BindingSaved += delegate {
                    styleStore.Save();
                    RegisterGlobalHotkeys();
                    dialog.RefreshRegistrationStatus();
                };
                dialog.ShowDialog(this);
            }
            styleStore.Save();
            ReloadStylePresets(stylePresetBox.SelectedItem as StylePresetData);
            RegisterGlobalHotkeys();
            SetStatus("快捷键设置已保存并重新注册。", false);
        }

        private void OpenToolsCenter()
        {
            using (var dialog = new ToolsCenterForm(
                styleStore, appSettings,
                delegate { temporaryA = CaptureCurrentStyle("临时 A"); },
                delegate { temporaryB = CaptureCurrentStyle("临时 B"); },
                delegate(bool useA) {
                    var preset = useA ? temporaryA : temporaryB;
                    if (preset != null) ApplyStylePreset(preset);
                },
                BuildEnvironmentDiagnosis))
            {
                dialog.SettingsChanged += delegate {
                    styleStore.Save();
                    appSettings.Save();
                    lastAutoProcess = "";
                    RegisterGlobalHotkeys();
                    UpdateGameRuleTimer();
                };
                dialog.ShowDialog(this);
            }
            ReloadStylePresets(stylePresetBox.SelectedItem as StylePresetData);
            RegisterGlobalHotkeys();
        }

        private StylePresetData CaptureCurrentStyle(string name)
        {
            return new StylePresetData {
                Name = name,
                Shadows = styleShadows.Value,
                Highlights = styleHighlights.Value,
                Midtones = styleMidtones.Value,
                BlackPoint = styleBlackPoint.Value,
                WhitePoint = styleWhitePoint.Value,
                ContrastPivot = styleContrastPivot.Value,
                Temperature = styleTemperature.Value,
                Tint = styleTint.Value,
                Vibrance = styleVibrance.Value,
                Saturation = saturation.Value,
                Exposure = styleExposure.Value,
                ShadowRange = styleShadowRange.Value,
                HighlightRange = styleHighlightRange.Value,
                TransitionSoftness = styleTransitionSoftness.Value
            };
        }

        private void ToggleOriginalView()
        {
            if (!originalToggleActive)
            {
                styleBeforeOriginalToggle = CaptureCurrentStyle("切换原画前");
                try { gammaController.RestoreAll(); } catch { }
                try { softwareSaturation.Restore(); } catch { }
                originalToggleActive = true;
                SetStatus("当前为原画；再次按快捷键恢复滤镜。", false);
            }
            else
            {
                originalToggleActive = false;
                if (styleBeforeOriginalToggle != null)
                    ApplyStylePreset(styleBeforeOriginalToggle);
                SetStatus("已恢复切换原画前的画面。", false);
            }
        }

        private void CheckGameAutoSwitch()
        {
            string processName = "";
            IntPtr foreground = IntPtr.Zero;
            uint processId = 0;
            try
            {
                foreground = MonitorApi.GetForegroundWindow();
                MonitorApi.GetWindowThreadProcessId(foreground, out processId);
                using (var process = Process.GetProcessById((int)processId))
                    processName = process.ProcessName;
            }
            catch { return; }
            bool protect = appSettings.FrameGenerationCompatibility &&
                MonitorApi.IsFullscreenWindow(foreground) &&
                processId != (uint)currentProcessId &&
                !string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(processName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
            UpdatePerformanceProtection(protect);
            if (!appSettings.GameAutoSwitchEnabled ||
                appSettings.GameRules == null || originalToggleActive) return;
            if (string.Equals(processName, lastAutoProcess,
                StringComparison.OrdinalIgnoreCase)) return;
            lastAutoProcess = processName;
            var rule = appSettings.GameRules.Find(item =>
                string.Equals(NormalizeProcessName(item.ProcessName),
                    NormalizeProcessName(processName), StringComparison.OrdinalIgnoreCase));
            if (rule == null)
            {
                if (autoSwitchActive)
                {
                    softwareSaturationSuppressedForGame = performanceProtectionActive;
                    if (appSettings.RestoreAfterGame)
                    {
                        if (styleBeforeAutoSwitch != null)
                            ApplyStylePreset(styleBeforeAutoSwitch);
                        else RestoreStyle();
                    }
                    else ApplyPresetSaturation();
                    autoSwitchActive = false;
                    styleBeforeAutoSwitch = null;
                    SetStatus(appSettings.RestoreAfterGame
                        ? "游戏离开前台，已恢复进入前的画面。"
                        : "游戏离开前台，已恢复软件饱和度。", false);
                }
                return;
            }
            var preset = styleStore.Items.Find(item =>
                string.Equals(item.Name, rule.PresetName, StringComparison.OrdinalIgnoreCase));
            if (preset == null) return;
            if (!autoSwitchActive)
                styleBeforeAutoSwitch = activeStyle == null
                    ? CaptureCurrentStyle("自动切换前") : activeStyle;
            softwareSaturationSuppressedForGame = performanceProtectionActive;
            ApplyStylePreset(preset);
            autoSwitchActive = true;
            SetStatus("自动切换：" + processName + " → " + preset.Name +
                (softwareSaturationSuppressedForGame ? " · 已暂停软件饱和度" : ""), false);
        }

        private void UpdateGameRuleTimer()
        {
            gameRuleTimer.Stop();
            if (!closing && (appSettings.GameAutoSwitchEnabled ||
                appSettings.FrameGenerationCompatibility))
                gameRuleTimer.Start();
            else if (autoSwitchActive)
            {
                softwareSaturationSuppressedForGame = false;
                if (appSettings.RestoreAfterGame && styleBeforeAutoSwitch != null)
                    ApplyStylePreset(styleBeforeAutoSwitch);
                else ApplyPresetSaturation();
                autoSwitchActive = false;
                styleBeforeAutoSwitch = null;
            }
            if (!appSettings.FrameGenerationCompatibility)
                UpdatePerformanceProtection(false);
        }

        private void UpdatePerformanceProtection(bool shouldProtect)
        {
            if (shouldProtect == performanceProtectionActive) return;
            performanceProtectionActive = shouldProtect;
            softwareSaturationSuppressedForGame = shouldProtect;
            if (shouldProtect)
            {
                recoveryTimer.Stop();
                try { softwareSaturation.Restore(); } catch { }
                SetStatus("游戏性能保护已启用：软件色彩矩阵与显示器重检已暂停。", false);
                return;
            }

            try { ApplyPresetSaturation(); } catch { }
            if (displayRecoveryDeferredForGame)
            {
                displayRecoveryDeferredForGame = false;
                recoveryTimer.Stop();
                recoveryTimer.Start();
            }
            SetStatus("已离开全屏程序，ShadePilot 后台功能已恢复。", false);
        }

        private static string NormalizeProcessName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            name = name.Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name.Substring(0, name.Length - 4) : name;
        }

        private string BuildEnvironmentDiagnosis()
        {
            var d = Selected;
            var lines = new List<string>();
            lines.Add("系统：" + Environment.OSVersion.VersionString);
            lines.Add("HDR / 高级颜色：" + (MonitorApi.IsAdvancedColorEnabled()
                ? "已开启，Gamma 或颜色矩阵可能受限" : "未检测到开启"));
            lines.Add("Gamma Ramp：" + (gammaController.IsCaptured
                ? "已捕获且可写入" : "尚未写入测试"));
            lines.Add("Gamma 基线：" + (gammaController.IsUsingCompatibilityBaseline
                ? "驱动拒绝回读，正在使用标准兼容基线" : "驱动回读基线"));
            lines.Add("软件颜色矩阵：" + (softwareSaturation.IsAvailable
                ? "可用" : "不可用或被系统放大镜占用"));
            lines.Add("软件颜色运行时：" + (softwareSaturation.IsActive
                ? "活动（可能与帧生成冲突）" : "未启动"));
            lines.Add("游戏性能保护：" + (performanceProtectionActive
                ? "活动，已暂停软件矩阵与显示器重检"
                : (appSettings.FrameGenerationCompatibility ? "待命" : "已关闭")));
            lines.Add("游戏期间延迟的显示恢复：" +
                (displayRecoveryDeferredForGame ? "有" : "无"));
            lines.Add("当前显示器：" + (d == null ? "未检测到" : d.Name));
            if (d != null)
            {
                lines.Add("DDC/CI 亮度：" + (d.SupportsBrightness ? "支持" : "不支持"));
                lines.Add("DDC/CI 对比度：" + (d.SupportsContrast ? "支持" : "不支持"));
                lines.Add("DDC/CI 饱和度：" + (d.SupportsSaturation ? "支持" : "不支持"));
                lines.Add("画面风格目标：" +
                    (string.IsNullOrWhiteSpace(d.DisplayDeviceName)
                        ? "默认桌面（未取得设备名）" : d.DisplayDeviceName));
                lines.Add("输出显卡：" + (string.IsNullOrWhiteSpace(d.DisplayAdapterName)
                    ? "未识别" : d.DisplayAdapterName));
            }
            lines.Add("已注册全局快捷键：" + registeredHotkeyCount);
            lines.Add("已收到快捷键事件：" + hotkeyReceivedCount);
            lines.Add("最近快捷键：" + lastHotkeyAction +
                (lastHotkeyReceivedUtc == DateTime.MinValue ? "" :
                    " · " + lastHotkeyReceivedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
            lines.Add("最近画面风格错误：" + lastStyleApplyError);
            lines.Add("当前预设：" + (activeStyle == null ? "手动参数 / 原画" : activeStyle.Name));
            lines.Add("自动切换：" + (appSettings.GameAutoSwitchEnabled ? "已开启" : "已关闭"));
            lines.Add("快捷键切换原画：" +
                (appSettings.HoldOriginalKey == 0 ? "未设置" : "已设置"));
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private void ManageStylePresets()
        {
            var selected = stylePresetBox.SelectedItem as StylePresetData;
            using (var dialog = new StylePresetManagerForm(styleStore, selected))
            {
                dialog.ShowDialog(this);
            }
            styleStore.Save();
            ReloadStylePresets(styleStore.Items.Count > 0 ? styleStore.Items[0] : null);
            RegisterGlobalHotkeys();
            SetStatus("画面风格预设已更新。", false);
        }

        private void SetStyleControls(StylePresetData preset)
        {
            SetStyleControls(
                preset.Shadows, preset.Highlights, preset.Midtones,
                preset.BlackPoint, preset.WhitePoint, preset.ContrastPivot,
                preset.Temperature, preset.Tint, preset.Vibrance,
                preset.Exposure, preset.ShadowRange, preset.HighlightRange,
                preset.TransitionSoftness);
            saturation.Value = Clamp(preset.Saturation <= 0 ? 100 : preset.Saturation,
                saturation.Minimum, saturation.Maximum);
            saturationValue.Text = saturation.Value.ToString();
        }

        private void SetStyleControls(
            int shadows, int highlights, int midtones,
            int blackPoint, int whitePoint, int contrastPivot,
            int temperature, int tint, int vibrance,
            int exposure, int shadowRange, int highlightRange, int transitionSoftness)
        {
            TrackBar[] sliders = {
                styleShadows, styleHighlights, styleMidtones,
                styleBlackPoint, styleWhitePoint, styleContrastPivot,
                styleTemperature, styleTint, styleVibrance, styleExposure,
                styleShadowRange, styleHighlightRange, styleTransitionSoftness
            };
            int[] values = {
                shadows, highlights, midtones, blackPoint, whitePoint,
                contrastPivot, temperature, tint, vibrance, exposure,
                shadowRange, highlightRange, transitionSoftness
            };
            Label[] labels = {
                styleShadowValue, styleHighlightValue, styleMidtoneValue,
                styleBlackPointValue, styleWhitePointValue, styleContrastPivotValue,
                styleTemperatureValue, styleTintValue, styleVibranceValue,
                styleExposureValue, styleShadowRangeValue, styleHighlightRangeValue,
                styleTransitionSoftnessValue
            };
            settingStyleControls = true;
            try
            {
                for (int i = 0; i < sliders.Length; i++)
                {
                    sliders[i].Value = Clamp(values[i], sliders[i].Minimum, sliders[i].Maximum);
                    styleBaselineValues[sliders[i]] = sliders[i].Value;
                    labels[i].Text = FormatStyleValue(sliders[i].Value, i < 2);
                }
                RefreshAllStyleValueLabels();
                UpdateStyleRiskStatus();
            }
            finally { settingStyleControls = false; }
        }

        private void ApplyStylePreview()
        {
            try
            {
                activeStyle = null;
                var targets = GetStyleTargetDeviceNames();
                var failures = new List<string>();
                foreach (string target in targets)
                {
                    try
                    {
                        gammaController.SetTarget(target);
                        gammaController.Apply(
                            styleShadows.Value, styleHighlights.Value,
                            styleMidtones.Value, styleBlackPoint.Value, styleWhitePoint.Value,
                            styleContrastPivot.Value, styleTemperature.Value,
                            styleTint.Value, styleVibrance.Value, styleExposure.Value,
                            styleShadowRange.Value, styleHighlightRange.Value,
                            styleTransitionSoftness.Value);
                    }
                    catch (Exception ex) { failures.Add((target ?? "默认桌面") + "：" + ex.Message); }
                }
                RestoreGammaTargetToSelection();
                if (failures.Count == targets.Count)
                    throw new InvalidOperationException(string.Join("；", failures.ToArray()));
                ApplyPresetSaturation();
                lastStyleApplyError = "无";
                SetStatus(
                    (syncAllDisplays.Checked ? "画面风格已同步" : "画面风格已应用") +
                    (failures.Count > 0 ? "（部分屏幕失败）" : "") + " · 暗部 " + FormatStyleValue(styleShadows.Value, true) +
                    " · 高光 " + FormatStyleValue(styleHighlights.Value, true) +
                    " · 色温 " + FormatStyleValue(styleTemperature.Value, false) +
                    " · 色调 " + FormatStyleValue(styleTint.Value, false), false);
            }
            catch (Exception ex)
            {
                lastStyleApplyError = ex.Message;
                SetStatus("画面风格应用失败：" + ex.Message, true);
            }
        }

        private void ApplyPresetSaturation()
        {
            var d = Selected;
            if (d != null && !usingSoftwareSaturation && d.SupportsSaturation)
                d.SetSaturation((uint)saturation.Value);
            else if (usingSoftwareSaturation && softwareSaturation.IsAvailable)
            {
                if (softwareSaturationSuppressedForGame)
                    softwareSaturation.Restore();
                else ApplySoftwareSaturationSafely(saturation.Value);
            }
        }

        private bool ApplySoftwareSaturationSafely(int value)
        {
            if (appSettings.FrameGenerationCompatibility &&
                MonitorApi.IsFullscreenWindow(MonitorApi.GetForegroundWindow()))
            {
                UpdatePerformanceProtection(true);
                try { softwareSaturation.Restore(); } catch { }
                return false;
            }
            softwareSaturation.Apply(value);
            return true;
        }

        private void RestoreStyle()
        {
            try
            {
                stylePreviewTimer.Stop();
                if (syncAllDisplays.Checked) gammaController.RestoreAll();
                else gammaController.Restore();
                RestoreGammaTargetToSelection();
                activeStyle = null;
                SetStyleControls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                saturation.Value = Clamp(100, saturation.Minimum, saturation.Maximum);
                saturationValue.Text = saturation.Value.ToString();
                ApplyPresetSaturation();
                SetStatus("已恢复程序启动前的原始画面。", false);
                trayIcon.Text = "ShadePilot · 原始画面";
            }
            catch (Exception ex)
            {
                SetStatus("恢复原始画面失败：" + ex.Message, true);
            }
        }

        private List<string> GetStyleTargetDeviceNames()
        {
            var result = new List<string>();
            if (!syncAllDisplays.Checked)
            {
                result.Add(Selected == null ? null : Selected.DisplayDeviceName);
                return result;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var display in displays)
            {
                string target = display.DisplayDeviceName;
                string key = string.IsNullOrWhiteSpace(target) ? "__desktop__" : target;
                if (seen.Add(key)) result.Add(target);
            }
            if (result.Count == 0) result.Add(null);
            return result;
        }

        private void RestoreGammaTargetToSelection()
        {
            var selected = Selected;
            gammaController.SetTarget(selected == null ? null : selected.DisplayDeviceName);
        }

        private void RegisterGlobalHotkeys()
        {
            UnregisterGlobalHotkeys();
            var failed = new List<string>();
            registeredHotkeyCount = 0;
            hotkeyRegistrationStatus.Clear();
            hotkeyMap.Clear();
            int id = HotkeyStyleBase;
            foreach (var preset in styleStore.Items)
            {
                if (preset.HotkeyKey == 0) {
                    hotkeyRegistrationStatus[preset.Name] = "未设置";
                    continue;
                }
                uint modifiers = preset.HotkeyModifiers | 0x4000u;
                id++;
                if (!MonitorApi.RegisterHotKey(Handle, id, modifiers, (uint)preset.HotkeyKey))
                {
                    failed.Add(preset.Name);
                    hotkeyRegistrationStatus[preset.Name] = "注册失败，Win32 错误 " +
                        Marshal.GetLastWin32Error();
                }
                else
                {
                    hotkeysRegistered = true;
                    registeredHotkeyCount++;
                    hotkeyMap[id] = preset;
                    hotkeyRegistrationStatus[preset.Name] = "已注册，可在后台使用";
                }
            }

            if (styleStore.RestoreHotkeyKey != 0)
            {
                uint restoreModifiers = styleStore.RestoreHotkeyModifiers | 0x4000u;
                if (!MonitorApi.RegisterHotKey(
                    Handle, HotkeyRestore, restoreModifiers, (uint)styleStore.RestoreHotkeyKey))
                {
                    failed.Add("恢复原始");
                    hotkeyRegistrationStatus["恢复原始画面"] = "注册失败，Win32 错误 " +
                        Marshal.GetLastWin32Error();
                }
                else {
                    hotkeysRegistered = true;
                    registeredHotkeyCount++;
                    hotkeyRegistrationStatus["恢复原始画面"] = "已注册，可在后台使用";
                }
            }
            else hotkeyRegistrationStatus["恢复原始画面"] = "未设置";

            if (styleStore.CycleHotkeyKey != 0)
            {
                uint cycleModifiers = styleStore.CycleHotkeyModifiers | 0x4000u;
                if (!MonitorApi.RegisterHotKey(
                    Handle, HotkeyCycle, cycleModifiers, (uint)styleStore.CycleHotkeyKey))
                {
                    failed.Add("循环切换");
                    hotkeyRegistrationStatus["循环切换所选预设"] =
                        "注册失败，Win32 错误 " + Marshal.GetLastWin32Error();
                }
                else
                {
                    hotkeysRegistered = true;
                    registeredHotkeyCount++;
                    hotkeyRegistrationStatus["循环切换所选预设"] =
                        "已注册，可在后台使用";
                }
            }
            else hotkeyRegistrationStatus["循环切换所选预设"] = "未设置";

            if (appSettings.HoldOriginalKey != 0)
            {
                uint toggleModifiers = appSettings.HoldOriginalModifiers | 0x4000u;
                if (!MonitorApi.RegisterHotKey(Handle, HotkeyOriginalToggle,
                    toggleModifiers, (uint)appSettings.HoldOriginalKey))
                {
                    failed.Add("切换原画");
                    hotkeyRegistrationStatus["切换原画"] = "注册失败，Win32 错误 " +
                        Marshal.GetLastWin32Error();
                }
                else
                {
                    hotkeysRegistered = true;
                    registeredHotkeyCount++;
                    hotkeyRegistrationStatus["切换原画"] = "已注册，可在后台使用";
                }
            }
            else hotkeyRegistrationStatus["切换原画"] = "未设置";

            if (failed.Count == 0)
                SetStatus("自定义后台快捷键已启用。", false);
            else
                SetStatus("以下快捷键被其他程序占用：" + string.Join("、", failed) + "。", true);
        }

        private void UnregisterGlobalHotkeys()
        {
            foreach (int id in new List<int>(hotkeyMap.Keys))
                MonitorApi.UnregisterHotKey(Handle, id);
            MonitorApi.UnregisterHotKey(Handle, HotkeyRestore);
            MonitorApi.UnregisterHotKey(Handle, HotkeyCycle);
            MonitorApi.UnregisterHotKey(Handle, HotkeyOriginalToggle);
            hotkeyMap.Clear();
            hotkeysRegistered = false;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey)
            {
                int id = message.WParam.ToInt32();
                hotkeyReceivedCount++;
                lastHotkeyReceivedUtc = DateTime.UtcNow;
                StylePresetData preset;
                if (hotkeyMap.TryGetValue(id, out preset))
                {
                    lastHotkeyAction = "应用预设：" + preset.Name;
                    ApplyStylePreset(preset);
                }
                else if (id == HotkeyRestore)
                {
                    lastHotkeyAction = "恢复原始画面";
                    RestoreStyle();
                }
                else if (id == HotkeyCycle)
                {
                    lastHotkeyAction = "循环切换预设";
                    CycleSelectedStyles();
                }
                else if (id == HotkeyOriginalToggle)
                {
                    lastHotkeyAction = "切换原画";
                    ToggleOriginalView();
                }
            }
            base.WndProc(ref message);
            if (message.Msg == WmNcHitTest && WindowState == FormWindowState.Normal &&
                message.Result.ToInt32() == 1)
            {
                int packed = message.LParam.ToInt32();
                var point = PointToClient(new Point((short)(packed & 0xffff),
                    (short)((packed >> 16) & 0xffff)));
                int edge = Math.Max(6, DeviceDpi / 16);
                bool left = point.X <= edge;
                bool right = point.X >= ClientSize.Width - edge;
                bool top = point.Y <= edge;
                bool bottomEdge = point.Y >= ClientSize.Height - edge;
                if (left && top) message.Result = new IntPtr(13);
                else if (right && top) message.Result = new IntPtr(14);
                else if (left && bottomEdge) message.Result = new IntPtr(16);
                else if (right && bottomEdge) message.Result = new IntPtr(17);
                else if (left) message.Result = new IntPtr(10);
                else if (right) message.Result = new IntPtr(11);
                else if (top) message.Result = new IntPtr(12);
                else if (bottomEdge) message.Result = new IntPtr(15);
            }
        }

        private void ApplyStylePreset(StylePresetData preset)
        {
            if (preset == null) return;
            stylePresetBox.SelectedItem = preset;
            SetStyleControls(preset);
            ApplyStylePreview();
            activeStyle = preset;
            trayIcon.Text = "ShadePilot · " + preset.Name;
        }

        private void CycleSelectedStyles()
        {
            var available = new List<StylePresetData>();
            if (styleStore.CyclePresetNames != null)
            {
                foreach (string name in styleStore.CyclePresetNames)
                {
                    var preset = styleStore.Items.Find(item =>
                        string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (preset != null) available.Add(preset);
                }
            }
            if (available.Count == 0)
            {
                SetStatus("循环切换没有可用预设，请在快捷键设置中至少勾选两个模式。", true);
                return;
            }
            int activeIndex = activeStyle == null ? -1 :
                available.FindIndex(item => item == activeStyle ||
                    string.Equals(item.Name, activeStyle.Name, StringComparison.OrdinalIgnoreCase));
            cyclePresetIndex = activeIndex >= 0 ? activeIndex : cyclePresetIndex;
            cyclePresetIndex = (cyclePresetIndex + 1) % available.Count;
            ApplyStylePreset(available[cyclePresetIndex]);
            SetStatus("循环切换：" + available[cyclePresetIndex].Name +
                "（" + (cyclePresetIndex + 1) + "/" + available.Count + "）", false);
        }

        private void ConfigureTrayIcon()
        {
            trayIcon.Icon = Icon ?? SystemIcons.Application;
            trayIcon.Text = "ShadePilot";
            trayIcon.Visible = true;
            var menu = new ContextMenuStrip();
            trayStyleStatus.Enabled = false;
            trayMonitorStatus.Enabled = false;
            trayHotkeyStatus.Enabled = false;
            var show = new ToolStripMenuItem("打开 ShadePilot");
            show.Click += delegate { RestoreWindow(); };
            var runAtStartup = new ToolStripMenuItem("开机启动") { CheckOnClick = true, Checked = appSettings.StartWithWindows };
            runAtStartup.CheckedChanged += delegate {
                appSettings.StartWithWindows = runAtStartup.Checked;
                appSettings.Save();
                AppSettingsStore.SetRunAtStartup(runAtStartup.Checked);
            };
            var startMinimized = new ToolStripMenuItem("启动后最小化到托盘") { CheckOnClick = true, Checked = appSettings.StartMinimized };
            startMinimized.CheckedChanged += delegate {
                appSettings.StartMinimized = startMinimized.Checked;
                appSettings.Save();
            };
            var autoReapply = new ToolStripMenuItem("唤醒或显示器重连后自动恢复") { CheckOnClick = true, Checked = appSettings.AutoReapply };
            autoReapply.CheckedChanged += delegate {
                appSettings.AutoReapply = autoReapply.Checked;
                appSettings.Save();
            };
            var exit = new ToolStripMenuItem("退出并恢复");
            exit.Click += delegate { Close(); };
            menu.Opening += delegate {
                trayStyleStatus.Text = "当前风格：" + (activeStyle == null ? "手动参数/原始画面" : activeStyle.Name);
                trayMonitorStatus.Text = "显示器：" + (Selected == null ? "未检测到" : Selected.Name);
                trayHotkeyStatus.Text = "全局快捷键：" + registeredHotkeyCount + " 个已注册";
            };
            menu.Items.Add(show);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(trayStyleStatus);
            menu.Items.Add(trayMonitorStatus);
            menu.Items.Add(trayHotkeyStatus);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(runAtStartup);
            menu.Items.Add(startMinimized);
            menu.Items.Add(autoReapply);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);
            trayIcon.ContextMenuStrip = menu;
            trayIcon.DoubleClick += delegate { RestoreWindow(); };
        }

        private void OnDisplayEnvironmentChanged(object sender, EventArgs e)
        {
            if (closing || !appSettings.AutoReapply) return;
            if (appSettings.FrameGenerationCompatibility &&
                (performanceProtectionActive ||
                    MonitorApi.IsFullscreenWindow(MonitorApi.GetForegroundWindow())))
            {
                displayRecoveryDeferredForGame = true;
                recoveryTimer.Stop();
                return;
            }
            recoveryTimer.Stop();
            gameRuleTimer.Stop();
            recoveryTimer.Start();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                OnDisplayEnvironmentChanged(sender, EventArgs.Empty);
        }

        private void OnWindowResize(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized) return;
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                trayIcon.BalloonTipTitle = "ShadePilot 已在后台运行";
                trayIcon.BalloonTipText = "可使用你设置的全局快捷键切换画面风格。";
                trayIcon.ShowBalloonTip(2500);
            }
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void DisableControls()
        {
            brightness.Enabled = contrast.Enabled = saturation.Enabled = false;
            saturation.Visible = false;
            saturationValue.Visible = false;
            saturationHint.Visible = true;
        }

        private void SetStatus(string text, bool error)
        {
            status.Text = text;
            status.ForeColor = error ? Color.Firebrick : Color.FromArgb(45, 90, 55);
        }

        private static string CapabilitySummary(PhysicalDisplay d)
        {
            var list = new List<string>();
            if (d.SupportsBrightness) list.Add("亮度" + (d.BrightnessUsesLowLevel ? "（回退）" : ""));
            if (d.SupportsContrast) list.Add("对比度" + (d.ContrastUsesLowLevel ? "（回退）" : ""));
            if (d.SupportsSaturation) list.Add("硬件饱和度（VCP 0x8A）");
            return list.Count == 0
                ? "已连接，但未报告受支持的安全调节项；请检查显示器菜单中的 DDC/CI。"
                : "支持：" + string.Join("、", list) + "。" + d.ProbeNote;
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (closing) return;
            closing = true;
            liveApplyTimer.Stop();
            stylePreviewTimer.Stop();
            recoveryTimer.Stop();
            gameRuleTimer.Stop();
            SystemEvents.DisplaySettingsChanged -= OnDisplayEnvironmentChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            pendingBrightness = pendingContrast = pendingSaturation = false;
            if (hotkeysRegistered) UnregisterGlobalHotkeys();
            try { softwareSaturation.Restore(); } catch { }
            if (restoreOnExit.Checked)
            {
                try { gammaController.Restore(); } catch { }
                try { softwareSaturation.Restore(); } catch { }
                foreach (var d in displays)
                {
                    Snapshot s;
                    if (startup.TryGetValue(d.Id, out s))
                    {
                        try { s.Apply(d); } catch { }
                    }
                }
            }
            foreach (var d in displays) d.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            softwareSaturation.Dispose();
        }

    }

    internal sealed class FullscreenColorController : IDisposable
    {
        private bool initialized;
        private bool initializationFailed;
        public bool IsAvailable { get { return !initializationFailed; } }
        public bool IsActive { get { return initialized; } }
        public int Value { get; private set; }

        public FullscreenColorController()
        {
            Value = 100;
        }

        public void Apply(int value)
        {
            value = Math.Max(0, Math.Min(200, value));
            if (value == 100)
            {
                Deactivate();
                return;
            }
            EnsureInitialized();
            float saturation = value / 100F;
            const float red = 0.2126F;
            const float green = 0.7152F;
            const float blue = 0.0722F;
            float inverse = 1F - saturation;
            var matrix = new float[] {
                inverse * red + saturation, inverse * red, inverse * red, 0, 0,
                inverse * green, inverse * green + saturation, inverse * green, 0, 0,
                inverse * blue, inverse * blue, inverse * blue + saturation, 0, 0,
                0, 0, 0, 1, 0,
                0, 0, 0, 0, 1
            };
            var effect = new MagnificationApi.MagColorEffect { Transform = matrix };
            if (!MagnificationApi.MagSetFullscreenColorEffect(ref effect))
            {
                Deactivate();
                throw new InvalidOperationException(
                    "Windows 拒绝应用全屏颜色矩阵；HDR、放大镜或当前显示模式可能不兼容。");
            }
            Value = value;
        }

        public void Restore()
        {
            Deactivate();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            try { initialized = MagnificationApi.MagInitialize(); }
            catch { initialized = false; }
            if (!initialized)
            {
                initializationFailed = true;
                throw new InvalidOperationException(
                    "Windows 全屏颜色矩阵不可用。请确认系统放大镜未占用该功能。");
            }
        }

        private void Deactivate()
        {
            Value = 100;
            if (!initialized) return;
            try
            {
                var identity = new MagnificationApi.MagColorEffect {
                    Transform = new float[] {
                        1,0,0,0,0, 0,1,0,0,0, 0,0,1,0,0,
                        0,0,0,1,0, 0,0,0,0,1
                    }
                };
                MagnificationApi.MagSetFullscreenColorEffect(ref identity);
            }
            catch { }
            finally
            {
                try { MagnificationApi.MagUninitialize(); } catch { }
                initialized = false;
            }
        }

        public void Dispose() { Deactivate(); }
    }

    internal static class MagnificationApi
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MagColorEffect
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
            public float[] Transform;
        }

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MagInitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MagUninitialize();

        [DllImport("Magnification.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool MagSetFullscreenColorEffect(
            ref MagColorEffect effect);
    }

    internal static class VectorUiPath
    {
        public static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ThemedComboBox : ComboBox
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        public Color BorderColor { get; set; }
        private bool hovered;

        public ThemedComboBox()
        {
            BorderColor = Color.White;
            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true; Invalidate(); base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if ((m.Msg != WmPaint && m.Msg != WmNcPaint) || Width < 2 || Height < 2)
                return;

            using (var graphics = Graphics.FromHwnd(Handle))
            using (var pen = new Pen((Focused || hovered)
                ? ControlPaint.Dark(BorderColor, 0.08F) : BorderColor, 1F))
                graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public Color FillColor { get; set; }
        public int CornerRadius { get; set; }

        public RoundedPanel()
        {
            FillColor = Color.White;
            CornerRadius = 18;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var path = VectorUiPath.Rounded(
                new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), CornerRadius))
            using (var brush = new SolidBrush(FillColor))
                e.Graphics.FillPath(brush, path);
        }
    }

    internal sealed class RoundedFlowLayoutPanel : FlowLayoutPanel
    {
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }

        public RoundedFlowLayoutPanel()
        {
            FillColor = Color.White;
            BorderColor = Color.FromArgb(225, 220, 214);
            CornerRadius = 12;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.Clear(Parent == null ? FillColor : Parent.BackColor);
            using (var path = VectorUiPath.Rounded(
                new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), CornerRadius))
            using (var brush = new SolidBrush(FillColor))
            using (var pen = new Pen(BorderColor, 1F))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class RoundedButton : Button
    {
        private bool hovered;
        public int CornerRadius { get; set; }
        public Color SurfaceColor { get; set; }

        public RoundedButton()
        {
            CornerRadius = 11;
            SurfaceColor = Color.White;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Color fill = hovered ? ControlPaint.Light(BackColor, 0.08f) : BackColor;
            // Clear pixels outside the rounded path on every buffered frame.
            // Without this, ButtonBase can leave fragments from its native
            // focus/default rendering at the bottom and right edges.
            e.Graphics.Clear(SurfaceColor);
            using (var path = VectorUiPath.Rounded(
                new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), CornerRadius))
            using (var brush = new SolidBrush(fill))
                e.Graphics.FillPath(brush, path);
            TextFormatFlags alignment = TextAlign == ContentAlignment.MiddleLeft
                ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter;
            var textBounds = new Rectangle(Padding.Left, 0,
                Math.Max(1, Width - Padding.Horizontal), Height);
            TextRenderer.DrawText(
                e.Graphics, Text, Font, textBounds, ForeColor,
                alignment | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class RoundedPillLabel : Label
    {
        public Color FillColor { get; set; }

        public RoundedPillLabel()
        {
            FillColor = Color.FromArgb(239, 236, 255);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (var path = VectorUiPath.Rounded(
                new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), Height / 2))
            using (var brush = new SolidBrush(FillColor))
                e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(
                e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class NoWheelTrackBar : TrackBar
    {
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            // Do not change the slider accidentally. Forward the wheel to the
            // nearest adaptive scroll container so scrolling remains smooth
            // even while the pointer is above a parameter track.
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            Control current = Parent;
            while (current != null)
            {
                var scroll = current as ScrollableControl;
                if (scroll != null && scroll.AutoScroll && scroll.VerticalScroll.Visible)
                {
                    int step = Math.Max(48, scroll.ClientSize.Height / 7);
                    int maximum = Math.Max(scroll.VerticalScroll.Minimum,
                        scroll.VerticalScroll.Maximum - scroll.VerticalScroll.LargeChange + 1);
                    int target = scroll.VerticalScroll.Value - Math.Sign(e.Delta) * step;
                    scroll.VerticalScroll.Value = Math.Max(scroll.VerticalScroll.Minimum,
                        Math.Min(maximum, target));
                    scroll.PerformLayout();
                    break;
                }
                current = current.Parent;
            }
        }
    }

    internal sealed class ToneCurveGraph : Control
    {
        public double Shadows, Highlights, Midtones, BlackPoint, WhitePoint;
        public double ContrastPivot, Vibrance, Exposure, ShadowRange, HighlightRange, TransitionSoftness;

        public ToneCurveGraph()
        {
            DoubleBuffered = true;
            BackColor = ThemeManager.Current.Surface;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var area = new Rectangle(18, 14, Width - 36, Height - 32);
            AppTheme theme = ThemeManager.Current;
            using (var grid = new Pen(theme.Button, 1F))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int x = area.Left + area.Width * i / 4;
                    int y = area.Top + area.Height * i / 4;
                    e.Graphics.DrawLine(grid, x, area.Top, x, area.Bottom);
                    e.Graphics.DrawLine(grid, area.Left, y, area.Right, y);
                }
            }
            using (var original = new Pen(theme.Muted, 1.5F)) {
                original.DashStyle = DashStyle.Dash;
                e.Graphics.DrawLine(original, area.Left, area.Bottom, area.Right, area.Top);
            }
            var points = new PointF[129];
            for (int i = 0; i < points.Length; i++)
            {
                double x = i / 128.0;
                double y = GammaRampController.TransformValue(
                    x, Shadows, Highlights, Midtones, BlackPoint,
                    WhitePoint, ContrastPivot, Vibrance, Exposure,
                    ShadowRange, HighlightRange, TransitionSoftness);
                points[i] = new PointF(
                    area.Left + (float)(x * area.Width),
                    area.Bottom - (float)(y * area.Height));
            }
            using (var curve = new Pen(Color.FromArgb(88, 91, 225), 3F))
                e.Graphics.DrawLines(curve, points);
            using (var border = new Pen(Color.FromArgb(205, 210, 220), 1F))
                e.Graphics.DrawRectangle(border, area);
        }
    }

    internal sealed class ToneCurveForm : Form
    {
        private readonly GammaRampController controller;
        private readonly TrackBar shadows = new NoWheelTrackBar();
        private readonly TrackBar highlights = new NoWheelTrackBar();
        private readonly TrackBar temperature = new NoWheelTrackBar();
        private readonly TrackBar tint = new NoWheelTrackBar();
        private readonly Label shadowValue = new Label();
        private readonly Label highlightValue = new Label();
        private readonly Label temperatureValue = new Label();
        private readonly Label tintValue = new Label();
        private readonly Label message = new Label();
        private readonly ComboBox stylePresets = new ComboBox();
        private readonly System.Windows.Forms.Timer previewTimer = new System.Windows.Forms.Timer();

        public ToneCurveForm(GammaRampController controller)
        {
            this.controller = controller;
            Text = "画面风格";
            ClientSize = new Size(590, 625);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 251);
            AutoScaleMode = AutoScaleMode.Dpi;

            var title = new Label {
                Text = "画面风格",
                ForeColor = Color.FromArgb(29, 36, 50),
                Font = new Font(Font.FontFamily, 15F, FontStyle.Bold),
                AutoSize = true, Location = new Point(28, 20)
            };
            var note = new Label {
                Text = "选择预设快速开始，再用滑块微调。作用于整个 Windows SDR 桌面。",
                ForeColor = Color.FromArgb(104, 113, 130),
                AutoSize = true, Location = new Point(30, 55)
            };

            ConfigureToneSlider(shadows, 42, 207, 0, 100);
            ConfigureToneSlider(highlights, 42, 282, 0, 100);
            ConfigureToneSlider(temperature, 42, 367, -100, 100);
            ConfigureToneSlider(tint, 42, 442, -100, 100);
            shadows.Scroll += delegate { shadowValue.Text = shadows.Value + "%"; QueuePreview(); };
            highlights.Scroll += delegate { highlightValue.Text = highlights.Value + "%"; QueuePreview(); };
            temperature.Scroll += delegate { temperatureValue.Text = Signed(temperature.Value); QueuePreview(); };
            tint.Scroll += delegate { tintValue.Text = Signed(tint.Value); QueuePreview(); };
            shadows.MouseUp += delegate { FlushPreview(); };
            highlights.MouseUp += delegate { FlushPreview(); };
            temperature.MouseUp += delegate { FlushPreview(); };
            tint.MouseUp += delegate { FlushPreview(); };
            shadowValue.Text = "0%"; shadowValue.AutoSize = true; shadowValue.Location = new Point(524, 211);
            highlightValue.Text = "0%"; highlightValue.AutoSize = true; highlightValue.Location = new Point(524, 286);
            temperatureValue.Text = "0"; temperatureValue.AutoSize = true; temperatureValue.Location = new Point(524, 371);
            tintValue.Text = "0"; tintValue.AutoSize = true; tintValue.Location = new Point(524, 446);
            foreach (var valueLabel in new[] { shadowValue, highlightValue, temperatureValue, tintValue })
                valueLabel.ForeColor = Color.FromArgb(76, 99, 220);

            stylePresets.DropDownStyle = ComboBoxStyle.DropDownList;
            stylePresets.FlatStyle = FlatStyle.Flat;
            stylePresets.BackColor = Color.White;
            stylePresets.Location = new Point(42, 120);
            stylePresets.Size = new Size(330, 28);
            stylePresets.Items.AddRange(new object[] {
                new StylePreset("电影感", 24, 38, -12, 8),
                new StylePreset("蓝调", 24, 28, -58, -4),
                new StylePreset("轻松氛围", 32, 16, 22, 7),
                new StylePreset("清晰夜景", 52, 32, -15, 0),
                new StylePreset("柔和原色", 30, 30, 0, 0)
            });
            stylePresets.SelectedIndex = 0;
            var applyPreset = StyledToneButton("应用预设", 386, 117, 148, true);
            applyPreset.Click += delegate {
                var preset = stylePresets.SelectedItem as StylePreset;
                if (preset == null) return;
                SetControls(preset.Shadows, preset.Highlights, preset.Temperature, preset.Tint);
                ApplyPreview();
            };
            var reset = StyledToneButton("恢复原始", 42, 527, 130, false);
            reset.Click += delegate {
                try {
                    controller.Restore();
                    SetControls(0, 0, 0, 0);
                    SetMessage("已恢复打开程序前的 Gamma。", false);
                } catch (Exception ex) { SetMessage(ex.Message, true); }
            };
            var close = StyledToneButton("完成", 404, 527, 130, true);
            close.DialogResult = DialogResult.OK;

            message.Location = new Point(30, 590);
            message.Size = new Size(530, 24);
            message.Text = "默认不改变画面；拖动后实时预览。";
            message.ForeColor = Color.FromArgb(45, 90, 55);

            previewTimer.Interval = 120;
            previewTimer.Tick += delegate { previewTimer.Stop(); ApplyPreview(); };

            var presetCard = new Panel {
                Location = new Point(24, 89), Size = new Size(542, 76),
                BackColor = Color.White
            };
            var controlsCard = new Panel {
                Location = new Point(24, 180), Size = new Size(542, 325),
                BackColor = Color.White
            };
            var actionsCard = new Panel {
                Location = new Point(24, 517), Size = new Size(542, 52),
                BackColor = Color.White
            };
            Controls.AddRange(new Control[] {
                title, note,
                presetCard, controlsCard, actionsCard,
                NewToneLabel("风格预设", 42, 98), stylePresets, applyPreset,
                NewToneLabel("提亮暗部", 42, 188), shadows, shadowValue,
                NewToneLabel("压低高光", 42, 263), highlights, highlightValue,
                NewToneLabel("色温  ·  冷蓝 ← 0 → 暖黄", 42, 338), temperature, temperatureValue,
                NewToneLabel("色调  ·  偏绿 ← 0 → 洋红", 42, 413), tint, tintValue,
                reset, close, message
            });
            presetCard.SendToBack();
            controlsCard.SendToBack();
            actionsCard.SendToBack();
            AcceptButton = close;
            FormClosing += delegate { previewTimer.Stop(); };
        }

        private static void ConfigureToneSlider(TrackBar slider, int x, int y, int minimum, int maximum)
        {
            slider.Minimum = minimum; slider.Maximum = maximum;
            slider.TickFrequency = 10; slider.LargeChange = 10;
            slider.TickStyle = TickStyle.None;
            slider.Location = new Point(x, y); slider.Size = new Size(470, 34);
        }

        private static Label NewToneLabel(string text, int x, int y)
        {
            return new Label {
                Text = text, AutoSize = true, Location = new Point(x, y),
                ForeColor = Color.FromArgb(29, 36, 50),
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
        }

        private static Button StyledToneButton(string text, int x, int y, int width, bool primary)
        {
            var button = new RoundedButton {
                Text = text, Location = new Point(x, y), Size = new Size(width, 34),
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                BackColor = primary ? Color.FromArgb(76, 99, 220) : Color.FromArgb(238, 241, 247),
                ForeColor = primary ? Color.White : Color.FromArgb(29, 36, 50)
            };
            button.SurfaceColor = Color.FromArgb(245, 247, 251);
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void QueuePreview()
        {
            previewTimer.Stop();
            previewTimer.Start();
            SetMessage("正在预览…", false);
        }

        private void FlushPreview()
        {
            previewTimer.Stop();
            ApplyPreview();
        }

        private void ApplyPreview()
        {
            try
            {
                controller.Apply(
                    shadows.Value, highlights.Value, 0, 0, 0, 0,
                    temperature.Value, tint.Value, 0, 0, 0, 0, 0);
                SetMessage(
                    "已应用：暗部 +" + shadows.Value + "%，高光 -" + highlights.Value +
                    "%，色温 " + Signed(temperature.Value) + "，色调 " + Signed(tint.Value) + "。", false);
            }
            catch (Exception ex) { SetMessage("应用失败：" + ex.Message, true); }
        }

        private void SetControls(int shadow, int highlight, int warmth, int tintValueNumber)
        {
            shadows.Value = shadow;
            highlights.Value = highlight;
            temperature.Value = warmth;
            tint.Value = tintValueNumber;
            shadowValue.Text = shadow + "%";
            highlightValue.Text = highlight + "%";
            temperatureValue.Text = Signed(warmth);
            tintValue.Text = Signed(tintValueNumber);
        }

        private static string Signed(int value)
        {
            return value > 0 ? "+" + value : value.ToString();
        }

        private void SetMessage(string text, bool error)
        {
            message.Text = text;
            message.ForeColor = error ? Color.Firebrick : Color.FromArgb(45, 90, 55);
        }

        private sealed class StylePreset
        {
            public readonly string Name;
            public readonly int Shadows, Highlights, Temperature, Tint;
            public StylePreset(string name, int shadows, int highlights, int temperature, int tint)
            {
                Name = name; Shadows = shadows; Highlights = highlights;
                Temperature = temperature; Tint = tint;
            }
            public override string ToString() { return Name; }
        }
    }

    internal static class CubeLutExporter
    {
        public static void Export(
            string path, int size, int shadows, int highlights, int midtones,
            int blackPoint, int whitePoint, int contrastPivot,
            int temperature, int tint, int vibrance, int saturation,
            int exposure, int shadowRange, int highlightRange,
            int transitionSoftness)
        {
            size = Math.Max(17, Math.Min(65, size));
            double shadow = GammaRampController.ScalePositive(shadows, 200);
            double highlight = GammaRampController.ScalePositive(highlights, 200);
            double midtone = GammaRampController.ScaleSigned(midtones);
            double black = GammaRampController.ScaleSigned(blackPoint);
            double white = GammaRampController.ScaleSigned(whitePoint);
            double pivot = GammaRampController.ScaleSigned(contrastPivot);
            double color = GammaRampController.ScaleSigned(vibrance);
            double exp = GammaRampController.ScaleSigned(exposure);
            double shadowCoverage = GammaRampController.ScaleSigned(shadowRange);
            double highlightCoverage = GammaRampController.ScaleSigned(highlightRange);
            double softness = GammaRampController.ScaleSigned(transitionSoftness);
            double warmth = GammaRampController.ScaleSigned(temperature);
            double tintAmount = GammaRampController.ScaleSigned(tint);

            double redGain = 1.0 + 0.32 * warmth + 0.12 * tintAmount;
            double greenGain = 1.0 - 0.20 * tintAmount;
            double blueGain = 1.0 - 0.32 * warmth + 0.12 * tintAmount;
            double maximumGain = Math.Max(1.0,
                Math.Max(redGain, Math.Max(greenGain, blueGain)));
            redGain /= maximumGain;
            greenGain /= maximumGain;
            blueGain /= maximumGain;
            double sat = Math.Max(0.0, Math.Min(2.0, saturation / 50.0));
            double inverseSat = 1.0 - sat;
            const double lr = 0.2126, lg = 0.7152, lb = 0.0722;

            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                writer.WriteLine("# Generated by ShadePilot");
                writer.WriteLine("# Hardware brightness and contrast are not included");
                writer.WriteLine("TITLE \"ShadePilot current style\"");
                writer.WriteLine("LUT_3D_SIZE " + size);
                writer.WriteLine("DOMAIN_MIN 0.0 0.0 0.0");
                writer.WriteLine("DOMAIN_MAX 1.0 1.0 1.0");
                for (int b = 0; b < size; b++)
                for (int g = 0; g < size; g++)
                for (int r = 0; r < size; r++)
                {
                    double sourceR = r / (double)(size - 1);
                    double sourceG = g / (double)(size - 1);
                    double sourceB = b / (double)(size - 1);
                    double mappedR = Map(sourceR, shadow, highlight, midtone,
                        black, white, pivot, color, exp, shadowCoverage,
                        highlightCoverage, softness) * redGain;
                    double mappedG = Map(sourceG, shadow, highlight, midtone,
                        black, white, pivot, color, exp, shadowCoverage,
                        highlightCoverage, softness) * greenGain;
                    double mappedB = Map(sourceB, shadow, highlight, midtone,
                        black, white, pivot, color, exp, shadowCoverage,
                        highlightCoverage, softness) * blueGain;
                    double outR = (inverseSat * lr + sat) * mappedR +
                        inverseSat * lg * mappedG + inverseSat * lb * mappedB;
                    double outG = inverseSat * lr * mappedR +
                        (inverseSat * lg + sat) * mappedG + inverseSat * lb * mappedB;
                    double outB = inverseSat * lr * mappedR + inverseSat * lg * mappedG +
                        (inverseSat * lb + sat) * mappedB;
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F7} {1:F7} {2:F7}", Clamp01(outR), Clamp01(outG), Clamp01(outB)));
                }
            }
        }

        private static double Map(double value, double shadows, double highlights,
            double midtones, double blackPoint, double whitePoint,
            double contrastPivot, double vibrance, double exposure,
            double shadowRange, double highlightRange, double transitionSoftness)
        {
            return GammaRampController.TransformValue(value, shadows, highlights,
                midtones, blackPoint, whitePoint, contrastPivot, vibrance,
                exposure, shadowRange, highlightRange, transitionSoftness);
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }

    internal sealed class GammaRampController
    {
        private readonly object operationLock = new object();
        private readonly Dictionary<string, GammaRamp> originals =
            new Dictionary<string, GammaRamp>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> compatibilityBaselines =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Once a session baseline has been explicitly installed, it is the
        // only source allowed for a Gamma transform. Re-reading after an
        // effect was applied would turn ShadePilot's own ramp into "original".
        private bool sessionBaselineInstalled;
        private string targetDeviceName;
        private string lastOperation = "尚未执行 Gamma 操作。";
        public string LastOperation { get { lock (operationLock) return lastOperation; } }
        public bool IsCaptured { get { return originals.ContainsKey(TargetKey); } }
        public bool IsUsingCompatibilityBaseline { get {
            return compatibilityBaselines.Contains(TargetKey);
        } }
        private string TargetKey { get { return string.IsNullOrWhiteSpace(targetDeviceName)
            ? "__desktop__" : targetDeviceName; } }

        public void SetTarget(string displayDeviceName)
        {
            lock (operationLock) targetDeviceName = displayDeviceName;
        }

        public void ApplyToTarget(
            string displayDeviceName,
            int shadowPercent, int highlightPercent, int midtoneValue,
            int blackPointValue, int whitePointValue, int contrastPivotValue,
            int temperatureValue, int tintValue, int vibranceValue,
            int exposureValue, int shadowRangeValue,
            int highlightRangeValue, int transitionSoftnessValue)
        {
            lock (operationLock)
            {
                targetDeviceName = displayDeviceName;
                Apply(shadowPercent, highlightPercent, midtoneValue,
                    blackPointValue, whitePointValue, contrastPivotValue,
                    temperatureValue, tintValue, vibranceValue, exposureValue,
                    shadowRangeValue, highlightRangeValue, transitionSoftnessValue);
            }
        }

        private IntPtr CreateTargetDc()
        {
            if (string.IsNullOrWhiteSpace(targetDeviceName))
                return GammaApi.GetDC(IntPtr.Zero);
            // A display DC is documented as driver="DISPLAY" plus the
            // EnumDisplayDevices device name (for example \\.\DISPLAY1).
            IntPtr dc = GammaApi.CreateDC("DISPLAY", targetDeviceName, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
                dc = GammaApi.CreateDC(targetDeviceName, null, null, IntPtr.Zero);
            return dc;
        }

        public void InvalidateCapturedRamps()
        {
            lock (operationLock)
            {
                if (sessionBaselineInstalled)
                {
                    lastOperation = "显示环境已变化；保留本会话 Gamma 基线，未重新采集当前曲线。";
                    return;
                }
                originals.Clear();
                compatibilityBaselines.Clear();
                lastOperation = "显示环境已变化，Gamma 基线将在下次应用时重新读取。";
            }
        }

        public GammaBaseline CaptureSystemBaseline(string displayDeviceName)
        {
            lock (operationLock)
            {
                string previous = targetDeviceName;
                targetDeviceName = displayDeviceName;
                try
                {
                    IntPtr dc = CreateTargetDc();
                    if (dc == IntPtr.Zero)
                        return GammaBaseline.Unavailable(displayDeviceName, "无法取得显示设备 DC。");
                    try
                    {
                        GammaRamp ramp = GammaRamp.Create();
                        if (!GammaApi.GetDeviceGammaRamp(dc, ref ramp))
                            return GammaBaseline.Unavailable(displayDeviceName,
                                "GetDeviceGammaRamp 返回失败，Win32=" + Marshal.GetLastWin32Error());
                        return GammaBaseline.From(displayDeviceName, ramp);
                    }
                    finally { ReleaseTargetDc(dc); }
                }
                finally { targetDeviceName = previous; }
            }
        }

        public void InstallSystemBaselines(IEnumerable<GammaBaseline> baselines)
        {
            lock (operationLock)
            {
                originals.Clear();
                compatibilityBaselines.Clear();
                foreach (var baseline in baselines ?? Enumerable.Empty<GammaBaseline>())
                {
                    if (baseline == null || !baseline.IsReadable ||
                        string.IsNullOrWhiteSpace(baseline.DisplayDeviceName) ||
                        !baseline.IsValid()) continue;
                    originals[baseline.DisplayDeviceName] = baseline.ToRamp();
                }
                sessionBaselineInstalled = true;
                lastOperation = "已安装本会话 Gamma 基线：" + originals.Count + " 个输出。";
            }
        }

        private void ReleaseTargetDc(IntPtr dc)
        {
            if (dc == IntPtr.Zero) return;
            if (string.IsNullOrWhiteSpace(targetDeviceName))
                GammaApi.ReleaseDC(IntPtr.Zero, dc);
            else GammaApi.DeleteDC(dc);
        }

        public void CaptureOriginal()
        {
            lock (operationLock)
            {
            IntPtr dc = CreateTargetDc();
            if (dc == IntPtr.Zero) throw new InvalidOperationException("无法取得桌面显示设备。");
            try
            {
                GammaRamp captured = GammaRamp.Create();
                if (!GammaApi.GetDeviceGammaRamp(dc, ref captured))
                {
                    captured = GammaRamp.CreateIdentity();
                    compatibilityBaselines.Add(TargetKey);
                }
                else compatibilityBaselines.Remove(TargetKey);
                originals[TargetKey] = captured;
            }
            finally { ReleaseTargetDc(dc); }
            }
        }

        public void Apply(
            int shadowPercent, int highlightPercent, int midtoneValue,
            int blackPointValue, int whitePointValue, int contrastPivotValue,
            int temperatureValue, int tintValue, int vibranceValue,
            int exposureValue, int shadowRangeValue,
            int highlightRangeValue, int transitionSoftnessValue)
        {
            lock (operationLock)
            {
            if (!IsCaptured)
            {
                if (sessionBaselineInstalled)
                    throw new InvalidOperationException(
                        "当前输出没有可验证的本会话 Gamma 基线；为避免污染原始状态，已拒绝应用软件曲线。");
                CaptureOriginal();
            }
            double shadow = ScalePositive(shadowPercent, 200);
            double highlight = ScalePositive(highlightPercent, 200);
            double midtone = ScaleSigned(midtoneValue);
            double blackPoint = ScaleSigned(blackPointValue);
            double whitePoint = ScaleSigned(whitePointValue);
            double contrastPivot = ScaleSigned(contrastPivotValue);
            double temperature = ScaleSigned(temperatureValue);
            double tint = ScaleSigned(tintValue);
            double vibrance = ScaleSigned(vibranceValue);
            double exposure = ScaleSigned(exposureValue);
            double shadowRange = ScaleSigned(shadowRangeValue);
            double highlightRange = ScaleSigned(highlightRangeValue);
            double transitionSoftness = ScaleSigned(transitionSoftnessValue);

            double redGain = 1.0 + 0.32 * temperature + 0.12 * tint;
            double greenGain = 1.0 - 0.20 * tint;
            double blueGain = 1.0 - 0.32 * temperature + 0.12 * tint;
            double maximumGain = Math.Max(1.0, Math.Max(redGain, Math.Max(greenGain, blueGain)));
            redGain /= maximumGain;
            greenGain /= maximumGain;
            blueGain /= maximumGain;

            GammaRamp original = originals[TargetKey];
            GammaRamp ramp = GammaRamp.Create();
            BuildChannel(original.Red, ramp.Red, shadow, highlight, midtone,
                blackPoint, whitePoint, contrastPivot, vibrance, exposure,
                shadowRange, highlightRange, transitionSoftness, redGain);
            BuildChannel(original.Green, ramp.Green, shadow, highlight, midtone,
                blackPoint, whitePoint, contrastPivot, vibrance, exposure,
                shadowRange, highlightRange, transitionSoftness, greenGain);
            BuildChannel(original.Blue, ramp.Blue, shadow, highlight, midtone,
                blackPoint, whitePoint, contrastPivot, vibrance, exposure,
                shadowRange, highlightRange, transitionSoftness, blueGain);
            SetAndVerify(ramp);
            }
        }

        // Give the first half of every slider useful resolution. The old linear
        // mapping made small and medium values disappear in the 256-entry LUT.
        internal static double ScalePositive(int value, int maximum)
        {
            double normalized = Math.Max(0, Math.Min(maximum, value)) / (double)maximum;
            return Math.Pow(normalized, 0.78) * 2.0;
        }

        internal static double ScaleSigned(int value)
        {
            double normalized = Math.Max(-100, Math.Min(100, value)) / 100.0;
            if (normalized == 0.0) return 0.0;
            return Math.Sign(normalized) * Math.Pow(Math.Abs(normalized), 0.78);
        }

        public void Restore()
        {
            lock (operationLock)
            {
            if (!IsCaptured) return;
            SetRamp(originals[TargetKey]);
            }
        }

        public void RestoreAll()
        {
            lock (operationLock)
            {
            string previous = targetDeviceName;
            foreach (var pair in new List<KeyValuePair<string, GammaRamp>>(originals))
            {
                targetDeviceName = pair.Key == "__desktop__" ? null : pair.Key;
                try { SetRamp(pair.Value); } catch { }
            }
            targetDeviceName = previous;
            }
        }

        // Recovery must not silently mark a session clean when one of the
        // baseline ramps could not be written. The regular UI restore remains
        // best-effort, while this path reports every failed output.
        public void RestoreSystemBaselines()
        {
            lock (operationLock)
            {
                string previous = targetDeviceName;
                var failures = new List<string>();
                foreach (var pair in new List<KeyValuePair<string, GammaRamp>>(originals))
                {
                    targetDeviceName = pair.Key == "__desktop__" ? null : pair.Key;
                    try { SetRamp(pair.Value.DeepClone()); }
                    catch (Exception ex) { failures.Add(pair.Key + "：" + ex.Message); }
                }
                targetDeviceName = previous;
                if (failures.Count != 0)
                    throw new InvalidOperationException(string.Join("；", failures));
            }
        }

        private static void BuildChannel(
            ushort[] source, ushort[] target, double shadow, double highlight,
            double midtone, double blackPoint, double whitePoint,
            double contrastPivot, double vibrance, double exposure,
            double shadowRange, double highlightRange, double transitionSoftness,
            double channelGain)
        {
            double previous = 0;
            for (int i = 0; i < 256; i++)
            {
                double x = source[i] / 65535.0;
                double y = TransformValue(
                    x, shadow, highlight, midtone, blackPoint,
                    whitePoint, contrastPivot, vibrance, exposure,
                    shadowRange, highlightRange, transitionSoftness);
                y = Math.Max(0.0, Math.Min(1.0, y * channelGain));
                y = Math.Max(previous, y);
                target[i] = (ushort)Math.Round(y * 65535.0);
                previous = y;
            }
        }

        internal static double TransformValue(
            double x, double shadow, double highlight, double midtone,
            double blackPoint, double whitePoint, double contrastPivot, double vibrance,
            double exposure, double shadowRange, double highlightRange,
            double transitionSoftness)
        {
            x = Math.Max(0.0, Math.Min(1.0, x * Math.Pow(2.0, exposure * 2.0)));
            double shadowWarp = Math.Pow(x, Math.Pow(2.0, -shadowRange * 0.85));
            double highlightWarp = Math.Pow(x, Math.Pow(2.0, highlightRange * 0.85));
            double shadowMask = shadowWarp * Math.Pow(1.0 - shadowWarp, 2.0);
            double highlightMask = highlightWarp * highlightWarp * (3.0 - 2.0 * highlightWarp);
            double falloffPower = Math.Pow(2.0, -transitionSoftness * 0.75);
            shadowMask = Math.Pow(Math.Max(0.0, shadowMask), falloffPower);
            highlightMask = Math.Pow(Math.Max(0.0, highlightMask), falloffPower);
            double lift = shadow * 1.35 * shadowMask;
            double compression = highlight * 0.27 * highlightMask;
            double blackOffset = blackPoint * 0.10 * Math.Pow(1.0 - x, 3.0);
            double whiteOffset = whitePoint * 0.10 * Math.Pow(x, 3.0);
            double y = Math.Max(0.0, Math.Min(1.0,
                x + lift - compression + blackOffset + whiteOffset));
            y = Math.Pow(y, Math.Pow(2.0, -midtone));

            // Endpoint-preserving contrast: unlike a final linear stretch this
            // cannot overwrite the black/white endpoints or abruptly clip them.
            double endpointGuard = Math.Max(0.0, 4.0 * y * (1.0 - y));
            double localContrastShape = (y - 0.5) * endpointGuard;
            y += contrastPivot * 0.78 * localContrastShape;

            // Lifting shadows and compressing highlights reduces separation in
            // the middle. Restore a restrained amount automatically so useful
            // shadow detail does not turn into a flat grey veil.
            y = Math.Max(0.0, Math.Min(1.0, y));
            endpointGuard = Math.Max(0.0, 4.0 * y * (1.0 - y));
            double antiHaze = Math.Min(0.34, shadow * 0.13 + highlight * 0.07);
            y += antiHaze * (y - 0.5) * endpointGuard;

            // Gamma ramps cannot inspect pixel saturation. This stronger smooth
            // per-channel expansion is an approximation, but is now visible at
            // medium values and remains anchored at black, middle grey and white.
            y = Math.Max(0.0, Math.Min(1.0, y));
            endpointGuard = Math.Max(0.0, 4.0 * y * (1.0 - y));
            double colorShape = (y - 0.5) * Math.Pow(endpointGuard, 0.72);
            y += vibrance * 0.48 * colorShape;
            return Math.Max(0.0, Math.Min(1.0, y));
        }

        private void SetAndVerify(GammaRamp ramp)
        {
            SetRamp(ramp);
            if (compatibilityBaselines.Contains(TargetKey)) return;
            GammaRamp actual = GammaRamp.Create();
            bool read = false;
            for (int attempt = 1; attempt <= 3 && !read; attempt++)
            {
                if (attempt > 1) Thread.Sleep(attempt == 2 ? 80 : 180);
                IntPtr dc = CreateTargetDc();
                if (dc == IntPtr.Zero) continue;
                try { read = GammaApi.GetDeviceGammaRamp(dc, ref actual); }
                finally { ReleaseTargetDc(dc); }
            }
            if (!read)
                throw new InvalidOperationException("Gamma 已写入，但显示输出路径暂时拒绝回读；请等待全屏或显卡切换完成后重试。");
            int differences = 0;
            for (int i = 0; i < 256; i += 16)
                if (Math.Abs(actual.Red[i] - ramp.Red[i]) > 512) differences++;
            if (differences > 8)
                throw new InvalidOperationException("显卡驱动或色彩管理覆盖了 Gamma 曲线；请确认处于 SDR，并关闭其他调色程序后重试。");
            lastOperation = "Gamma 写入并回读成功：" + TargetKey;
        }

        private void SetRamp(GammaRamp ramp)
        {
            int lastError = 0;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (attempt > 1) Thread.Sleep(attempt == 2 ? 120 : 260);
                IntPtr dc = CreateTargetDc();
                if (dc == IntPtr.Zero)
                {
                    lastError = Marshal.GetLastWin32Error();
                    continue;
                }
                try
                {
                    if (GammaApi.SetDeviceGammaRamp(dc, ref ramp))
                    {
                        lastOperation = "Gamma 写入成功：" + TargetKey + "（第 " + attempt + " 次）";
                        return;
                    }
                    lastError = Marshal.GetLastWin32Error();
                }
                finally { ReleaseTargetDc(dc); }
            }
            lastOperation = "Gamma 写入失败：" + TargetKey + "，Win32=" + lastError;
            throw new InvalidOperationException(
                "显示输出路径连续 3 次拒绝 Gamma Ramp（Win32 " + lastError +
                "）。这通常发生在混合显卡、全屏或 HDR 正在切换时，请稍后重试。");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GammaRamp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Blue;

        public static GammaRamp Create()
        {
            return new GammaRamp {
                Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256]
            };
        }

        public static GammaRamp CreateIdentity()
        {
            GammaRamp ramp = Create();
            for (int i = 0; i < 256; i++)
            {
                ushort value = (ushort)(i * 257);
                ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = value;
            }
            return ramp;
        }

        public GammaRamp DeepClone()
        {
            return new GammaRamp {
                Red = Red == null ? null : (ushort[])Red.Clone(),
                Green = Green == null ? null : (ushort[])Green.Clone(),
                Blue = Blue == null ? null : (ushort[])Blue.Clone()
            };
        }
    }

    [DataContract]
    internal sealed class GammaBaseline
    {
        [DataMember] public string DisplayDeviceName;
        [DataMember] public bool IsReadable;
        [DataMember] public string ReadFailure;
        [DataMember] public ushort[] Red;
        [DataMember] public ushort[] Green;
        [DataMember] public ushort[] Blue;

        public static GammaBaseline From(string deviceName, GammaRamp ramp)
        {
            return new GammaBaseline {
                DisplayDeviceName = deviceName,
                IsReadable = true,
                Red = ramp.Red == null ? null : (ushort[])ramp.Red.Clone(),
                Green = ramp.Green == null ? null : (ushort[])ramp.Green.Clone(),
                Blue = ramp.Blue == null ? null : (ushort[])ramp.Blue.Clone()
            };
        }

        public static GammaBaseline Unavailable(string deviceName, string reason)
        {
            return new GammaBaseline { DisplayDeviceName = deviceName, IsReadable = false, ReadFailure = reason };
        }

        public bool IsValid()
        {
            return Red?.Length == 256 && Green?.Length == 256 && Blue?.Length == 256;
        }

        public GammaRamp ToRamp()
        {
            if (!IsValid()) throw new InvalidOperationException("持久化的 Gamma 基线数据无效。");
            return new GammaRamp {
                Red = (ushort[])Red.Clone(), Green = (ushort[])Green.Clone(), Blue = (ushort[])Blue.Clone()
            };
        }

        public GammaBaseline DeepClone()
        {
            return new GammaBaseline {
                DisplayDeviceName = DisplayDeviceName, IsReadable = IsReadable, ReadFailure = ReadFailure,
                Red = Red == null ? null : (ushort[])Red.Clone(),
                Green = Green == null ? null : (ushort[])Green.Clone(),
                Blue = Blue == null ? null : (ushort[])Blue.Clone()
            };
        }
    }

    internal static class GammaApi
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateDC(
            string driver, string device, string output, IntPtr initData);
        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern bool GetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);
        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern bool SetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);
    }

    internal sealed class PhysicalDisplay : IDisposable
    {
        private static readonly object GlobalDdcLock = new object();
        public IntPtr Handle;
        public string Name;
        public string Id;
        public string DisplayDeviceName;
        public string DisplayAdapterName;
        public uint Capabilities;
        public uint BrightnessMin, BrightnessCurrent, BrightnessMax;
        public uint ContrastMin, ContrastCurrent, ContrastMax;
        public uint SaturationMin, SaturationCurrent, SaturationMax;
        public bool BrightnessUsesLowLevel;
        public bool ContrastUsesLowLevel;
        public bool SaturationUsesLowLevel;
        public bool SaturationRestoreSafe;
        public string ProbeNote;

        public bool SupportsBrightness { get { return (Capabilities & 0x2) != 0; } }
        public bool SupportsContrast { get { return (Capabilities & 0x4) != 0; } }
        public bool SupportsSaturation { get { return SaturationMax > SaturationMin; } }

        public void Refresh()
        {
            lock (GlobalDdcLock)
            {
            uint caps, temps;
            bool capabilityQueryWorked = MonitorApi.GetMonitorCapabilities(Handle, out caps, out temps);
            if (!capabilityQueryWorked)
            {
                caps = 0;
                temps = 0;
            }
            Capabilities = caps;
            BrightnessUsesLowLevel = false;
            ContrastUsesLowLevel = false;
            SaturationUsesLowLevel = false;

            bool brightnessRead = SupportsBrightness &&
                MonitorApi.GetMonitorBrightness(Handle, out BrightnessMin, out BrightnessCurrent, out BrightnessMax);
            if (!brightnessRead)
            {
                uint current, maximum;
                MonitorApi.MC_VCP_CODE_TYPE type;
                if (MonitorApi.GetVCPFeatureAndVCPFeatureReply(Handle, 0x10, out type, out current, out maximum) && maximum > 0)
                {
                    BrightnessMin = 0;
                    BrightnessCurrent = current;
                    BrightnessMax = maximum;
                    Capabilities |= 0x2u;
                    BrightnessUsesLowLevel = true;
                    brightnessRead = true;
                }
                else Capabilities &= ~0x2u;
            }

            bool contrastRead = SupportsContrast &&
                MonitorApi.GetMonitorContrast(Handle, out ContrastMin, out ContrastCurrent, out ContrastMax);
            if (!contrastRead)
            {
                uint current, maximum;
                MonitorApi.MC_VCP_CODE_TYPE type;
                if (MonitorApi.GetVCPFeatureAndVCPFeatureReply(Handle, 0x12, out type, out current, out maximum) && maximum > 0)
                {
                    ContrastMin = 0;
                    ContrastCurrent = current;
                    ContrastMax = maximum;
                    Capabilities |= 0x4u;
                    ContrastUsesLowLevel = true;
                    contrastRead = true;
                }
                else Capabilities &= ~0x4u;
            }

            uint saturationCurrent, saturationMaximum;
            MonitorApi.MC_VCP_CODE_TYPE saturationType;
            bool saturationRead = MonitorApi.GetVCPFeatureAndVCPFeatureReply(
                Handle, 0x8A, out saturationType, out saturationCurrent, out saturationMaximum);
            Thread.Sleep(70);
            uint confirmedCurrent = 0, confirmedMaximum = 0;
            MonitorApi.MC_VCP_CODE_TYPE confirmedType =
                MonitorApi.MC_VCP_CODE_TYPE.MC_MOMENTARY;
            bool saturationConfirmed = saturationRead &&
                MonitorApi.GetVCPFeatureAndVCPFeatureReply(
                    Handle, 0x8A, out confirmedType, out confirmedCurrent, out confirmedMaximum);
            if (saturationConfirmed && saturationType == MonitorApi.MC_VCP_CODE_TYPE.MC_SET_PARAMETER &&
                confirmedType == saturationType && saturationMaximum == confirmedMaximum &&
                saturationCurrent == confirmedCurrent && saturationMaximum >= 10 &&
                saturationMaximum <= 255 && saturationCurrent <= saturationMaximum)
            {
                SaturationMin = 0;
                SaturationCurrent = saturationCurrent;
                SaturationMax = saturationMaximum;
                SaturationUsesLowLevel = true;
                SaturationRestoreSafe = true;
            }
            else
            {
                SaturationMin = SaturationCurrent = SaturationMax = 0;
                SaturationRestoreSafe = false;
            }

            ProbeNote = capabilityQueryWorked
                ? "Windows 高层能力查询成功"
                : (brightnessRead || contrastRead
                    ? "高层查询失败，已使用低层 DDC/CI 白名单回退"
                    : "高层与低层 DDC/CI 均未返回可用参数");
            }
        }

        public void MarkProbeFailed()
        {
            Capabilities = 0;
            SaturationMin = SaturationCurrent = SaturationMax = 0;
            SaturationRestoreSafe = false;
            ProbeNote = "DDC/CI 探测失败；已禁用硬件写入以保护恢复参数";
        }

        public void SetBrightness(uint value)
        {
            lock (GlobalDdcLock)
            {
                BrightnessCurrent = WriteAndVerifyVcp(
                    0x10, value, BrightnessCurrent, BrightnessMax,
                    delegate(uint v) { return MonitorApi.SetMonitorBrightness(Handle, v); },
                    "亮度");
            }
        }
        public void SetContrast(uint value)
        {
            lock (GlobalDdcLock)
            {
                ContrastCurrent = WriteAndVerifyVcp(
                    0x12, value, ContrastCurrent, ContrastMax,
                    delegate(uint v) { return MonitorApi.SetMonitorContrast(Handle, v); },
                    "对比度");
            }
        }
        public void SetSaturation(uint value)
        {
            lock (GlobalDdcLock)
            {
                SaturationCurrent = WriteAndVerifyVcp(
                    0x8A, value, SaturationCurrent, SaturationMax,
                    delegate(uint v) { return false; },
                    "饱和度");
            }
        }
        private uint WriteAndVerifyVcp(
            byte code, uint requested, uint before, uint knownMaximum,
            Func<uint, bool> highLevelFallback, string label)
        {
            // Standard MCCS VCP is preferred even when Windows' high-level query worked:
            // several monitors report high-level support but silently ignore its setter.
            bool writeReturnedSuccess = MonitorApi.SetVCPFeature(Handle, code, requested);
            Thread.Sleep(300);

            uint actual, maximum;
            MonitorApi.MC_VCP_CODE_TYPE type;
            bool readWorked = MonitorApi.GetVCPFeatureAndVCPFeatureReply(
                Handle, code, out type, out actual, out maximum);

            if (!writeReturnedSuccess || (readWorked && requested != before && actual == before))
            {
                bool fallbackReturnedSuccess = highLevelFallback(requested);
                Thread.Sleep(300);
                readWorked = MonitorApi.GetVCPFeatureAndVCPFeatureReply(
                    Handle, code, out type, out actual, out maximum);
                if (!fallbackReturnedSuccess && !writeReturnedSuccess)
                    throw MonitorApi.Error("设置" + label);
            }

            if (!readWorked)
                throw new InvalidOperationException(
                    label + "写入后无法回读确认；显示器或连接链路可能屏蔽了 DDC/CI 写入。");

            uint effectiveMaximum = maximum > 0 ? maximum : knownMaximum;
            uint tolerance = Math.Max(1u, effectiveMaximum / 50u);
            uint difference = actual > requested ? actual - requested : requested - actual;
            if (requested != before && actual == before)
                throw new InvalidOperationException(
                    label + "仍为 " + actual + "；显示器返回成功但没有执行。请关闭 HDR、动态对比度或锁定的游戏图像模式。");
            if (difference > tolerance)
                throw new InvalidOperationException(
                    label + "请求值为 " + requested + "，显示器回读为 " + actual + "；固件可能限制或量化了该参数。");
            return actual;
        }
        public void Dispose()
        {
            lock (GlobalDdcLock)
            {
                if (Handle != IntPtr.Zero)
                {
                    MonitorApi.DestroyPhysicalMonitor(Handle);
                    Handle = IntPtr.Zero;
                }
            }
        }
    }

    [DataContract]
    internal sealed class Snapshot
    {
        [DataMember] public bool HasBrightness, HasContrast, HasSaturation;
        [DataMember] public uint Brightness, Contrast, Saturation;
        public static Snapshot From(PhysicalDisplay d)
        {
            return new Snapshot {
                HasBrightness = d.SupportsBrightness, Brightness = d.BrightnessCurrent,
                HasContrast = d.SupportsContrast, Contrast = d.ContrastCurrent,
                HasSaturation = d.SupportsSaturation && d.SaturationRestoreSafe,
                Saturation = d.SaturationCurrent
            };
        }
        public void Apply(PhysicalDisplay d)
        {
            if (HasBrightness && d.SupportsBrightness && d.BrightnessCurrent != Brightness)
                { d.SetBrightness(Brightness); Thread.Sleep(80); }
            if (HasContrast && d.SupportsContrast && d.ContrastCurrent != Contrast)
                { d.SetContrast(Contrast); Thread.Sleep(80); }
            // VCP 0x8A is restored only when both capture and the current
            // output passed the existing double-read safety probe. This keeps
            // synthetic hybrid-GPU values out of the recovery path while
            // allowing a verified hardware baseline to be restored.
            if (HasSaturation && d.SupportsSaturation && d.SaturationRestoreSafe &&
                d.SaturationCurrent != Saturation)
                { d.SetSaturation(Saturation); Thread.Sleep(80); }
        }
        public Snapshot DeepClone()
        {
            return new Snapshot {
                HasBrightness = HasBrightness, HasContrast = HasContrast, HasSaturation = HasSaturation,
                Brightness = Brightness, Contrast = Contrast, Saturation = Saturation
            };
        }
    }

    [DataContract]
    internal sealed class StylePresetData
    {
        [DataMember] public string Name;
        [DataMember] public int Shadows;
        [DataMember] public int Highlights;
        [DataMember] public int Midtones;
        [DataMember] public int BlackPoint;
        [DataMember] public int WhitePoint;
        [DataMember] public int ContrastPivot;
        [DataMember] public int Temperature;
        [DataMember] public int Tint;
        [DataMember] public int Vibrance;
        // User-facing saturation is normalized to 0..100 with 50 as neutral.
        [DataMember] public int Saturation = 50;
        [DataMember] public int Exposure;
        [DataMember] public int ShadowRange;
        [DataMember] public int HighlightRange;
        [DataMember] public int TransitionSoftness;
        [DataMember] public int HotkeyKey;
        [DataMember] public uint HotkeyModifiers;
        public override string ToString() { return Name; }
        public StylePresetData DeepClone()
        {
            return new StylePresetData {
                Name = Name, Shadows = Shadows, Highlights = Highlights, Midtones = Midtones,
                BlackPoint = BlackPoint, WhitePoint = WhitePoint, ContrastPivot = ContrastPivot,
                Temperature = Temperature, Tint = Tint, Vibrance = Vibrance, Saturation = Saturation,
                Exposure = Exposure, ShadowRange = ShadowRange, HighlightRange = HighlightRange,
                TransitionSoftness = TransitionSoftness, HotkeyKey = HotkeyKey,
                HotkeyModifiers = HotkeyModifiers
            };
        }
    }

    [DataContract]
    internal sealed class StyleSettingsStore
    {
        [DataMember] public List<StylePresetData> Items = new List<StylePresetData>();
        [DataMember] public int RestoreHotkeyKey;
        [DataMember] public uint RestoreHotkeyModifiers;
        [DataMember] public int CycleHotkeyKey;
        [DataMember] public uint CycleHotkeyModifiers;
        [DataMember] public List<string> CyclePresetNames = new List<string>();
        [DataMember] public int SchemaVersion;

        private static string Folder {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayPresetPrototype"); }
        }
        private static string FilePath { get { return Path.Combine(Folder, "styles.json"); } }

        public static StyleSettingsStore Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    StyleSettingsStore loaded;
                    using (var stream = File.OpenRead(FilePath))
                    {
                        loaded = (StyleSettingsStore)new DataContractJsonSerializer(
                            typeof(StyleSettingsStore)).ReadObject(stream);
                    }
                    if (loaded != null && loaded.Items != null && loaded.Items.Count > 0)
                    {
                        if (loaded.CyclePresetNames == null)
                            loaded.CyclePresetNames = new List<string>();
                        if (loaded.SchemaVersion < 2) loaded.UpgradeCuratedPresets();
                            if (loaded.SchemaVersion < 3) loaded.UpgradeAdvancedControls();
                            if (loaded.SchemaVersion < 4) loaded.RemoveLegacyDefaultHotkeys();
                            if (loaded.SchemaVersion < 5) loaded.UpgradeNewToneControls();
                            if (loaded.SchemaVersion < 6) loaded.UpgradeCurveV2Presets();
                            if (loaded.SchemaVersion < 7) loaded.UpgradePresetSaturation();
                            if (loaded.SchemaVersion < 8) loaded.UpgradeSaturationNeutral50();
                        return loaded;
                    }
                }
            }
            catch { }
            return CreateDefaults();
        }

        private static StyleSettingsStore CreateDefaults()
        {
            var store = new StyleSettingsStore {
                RestoreHotkeyKey = 0,
                RestoreHotkeyModifiers = 0,
                SchemaVersion = 8
            };
            AddCurated(store.Items);
            return store;
        }

        private void UpgradeCuratedPresets()
        {
            string[] oldNames = { "电影感", "蓝调", "轻松氛围", "清晰夜景", "柔和原色" };
            Items.RemoveAll(item => Array.IndexOf(oldNames, item.Name) >= 0);
            var curated = new List<StylePresetData>();
            AddCurated(curated);
            Items.InsertRange(0, curated);
            SchemaVersion = 2;
            Save();
        }

        private void UpgradeAdvancedControls()
        {
            foreach (var item in Items)
            {
                if (item.Name == "暗域搜寻") {
                    item.Midtones = 34; item.BlackPoint = 18; item.WhitePoint = -8;
                    item.ContrastPivot = 10; item.Vibrance = 8;
                }
                else if (item.Name == "冷调电影") {
                    item.Midtones = -6; item.BlackPoint = -8; item.WhitePoint = -12;
                    item.ContrastPivot = 20; item.Vibrance = -8;
                }
                else if (item.Name == "胶片柔和") {
                    item.Midtones = 10; item.BlackPoint = 12; item.WhitePoint = -18;
                    item.ContrastPivot = -10; item.Vibrance = -4;
                }
                else if (item.Name == "夜景灰蓝") {
                    item.Midtones = 24; item.BlackPoint = 8; item.WhitePoint = -10;
                    item.ContrastPivot = 14; item.Vibrance = 10;
                }
                else if (item.Name == "暖阳照片") {
                    item.Midtones = 12; item.BlackPoint = 4; item.WhitePoint = 4;
                    item.ContrastPivot = 8; item.Vibrance = 18;
                }
                else if (item.Name == "中性通透") {
                    item.Midtones = 8; item.BlackPoint = 0; item.WhitePoint = 0;
                    item.ContrastPivot = 12; item.Vibrance = 8;
                }
            }
            SchemaVersion = 3;
            Save();
        }

        private void RemoveLegacyDefaultHotkeys()
        {
            string[] curatedNames = {
                "暗域搜寻", "冷调电影", "胶片柔和",
                "夜景灰蓝", "暖阳照片", "中性通透"
            };
            const uint legacyModifiers = 0x0001u | 0x0002u;
            for (int i = 0; i < curatedNames.Length; i++)
            {
                var item = Items.Find(p => p.Name == curatedNames[i]);
                if (item != null &&
                    item.HotkeyModifiers == legacyModifiers &&
                    item.HotkeyKey == (int)Keys.D1 + i)
                {
                    item.HotkeyKey = 0;
                    item.HotkeyModifiers = 0;
                }
            }
            if (RestoreHotkeyModifiers == legacyModifiers &&
                RestoreHotkeyKey == (int)Keys.D0)
            {
                RestoreHotkeyKey = 0;
                RestoreHotkeyModifiers = 0;
            }
            SchemaVersion = 4;
            Save();
        }

        private void UpgradeNewToneControls()
        {
            ApplyNewToneValues("暗域搜寻", 18, 35, 0, 20);
            ApplyNewToneValues("冷调电影", -5, -10, 20, 15);
            ApplyNewToneValues("胶片柔和", 5, 10, 30, 35);
            ApplyNewToneValues("夜景灰蓝", 12, 30, 10, 20);
            ApplyNewToneValues("暖阳照片", 8, 10, -5, 15);
            ApplyNewToneValues("中性通透", 5, 10, 5, 10);
            SchemaVersion = 5;
            Save();
        }

        private void ApplyNewToneValues(
            string name, int exposure, int shadowRange,
            int highlightRange, int transitionSoftness)
        {
            var item = Items.Find(p => p.Name == name);
            if (item == null) return;
            item.Exposure = exposure;
            item.ShadowRange = shadowRange;
            item.HighlightRange = highlightRange;
            item.TransitionSoftness = transitionSoftness;
        }

        private void UpgradeCurveV2Presets()
        {
            var updated = new List<StylePresetData>();
            AddCurated(updated);
            for (int i = 0; i < updated.Count; i++)
            {
                var replacement = updated[i];
                var existing = Items.Find(item => item.Name == replacement.Name);
                if (existing == null)
                {
                    Items.Insert(Math.Min(i, Items.Count), replacement);
                    continue;
                }
                uint hotkeyModifiers = existing.HotkeyModifiers;
                int hotkeyKey = existing.HotkeyKey;
                CopyCurveValues(replacement, existing);
                existing.HotkeyModifiers = hotkeyModifiers;
                existing.HotkeyKey = hotkeyKey;
            }
            SchemaVersion = 6;
            Save();
        }

        private void UpgradePresetSaturation()
        {
            foreach (var item in Items)
                if (item.Saturation <= 0) item.Saturation = 100;
            SchemaVersion = 7;
            Save();
        }

        private void UpgradeSaturationNeutral50()
        {
            // Schema 7 stored Windows-style values where 100 was neutral.
            // Preserve the visual intent while moving the public scale to a
            // consistent 0..100 range where 50 is neutral.
            foreach (var item in Items)
                item.Saturation = Math.Max(0, Math.Min(100,
                    (int)Math.Round((item.Saturation <= 0 ? 100 : item.Saturation) / 2.0)));
            SchemaVersion = 8;
            Save();
        }

        private static void CopyCurveValues(StylePresetData source, StylePresetData target)
        {
            target.Shadows = source.Shadows;
            target.Highlights = source.Highlights;
            target.Midtones = source.Midtones;
            target.BlackPoint = source.BlackPoint;
            target.WhitePoint = source.WhitePoint;
            target.ContrastPivot = source.ContrastPivot;
            target.Temperature = source.Temperature;
            target.Tint = source.Tint;
            target.Vibrance = source.Vibrance;
            target.Saturation = source.Saturation <= 0 ? 50 : source.Saturation;
            target.Exposure = source.Exposure;
            target.ShadowRange = source.ShadowRange;
            target.HighlightRange = source.HighlightRange;
            target.TransitionSoftness = source.TransitionSoftness;
        }

        private static void AddCurated(List<StylePresetData> target)
        {
            target.Add(new StylePresetData { Name = "暗域搜寻", Shadows = 105, Highlights = 35, Midtones = 18, BlackPoint = -8, WhitePoint = -5, ContrastPivot = 8, Temperature = -6, Tint = 0, Vibrance = 16, Exposure = 8, ShadowRange = 24, HighlightRange = 0, TransitionSoftness = 14 });
            target.Add(new StylePresetData { Name = "冷调电影", Shadows = 28, Highlights = 46, Midtones = -4, BlackPoint = -7, WhitePoint = -9, ContrastPivot = 16, Temperature = -18, Tint = 7, Vibrance = -5, Exposure = -3, ShadowRange = -7, HighlightRange = 15, TransitionSoftness = 11 });
            target.Add(new StylePresetData { Name = "胶片柔和", Shadows = 24, Highlights = 54, Midtones = 7, BlackPoint = 7, WhitePoint = -13, ContrastPivot = -7, Temperature = 10, Tint = 5, Vibrance = -3, Exposure = 3, ShadowRange = 7, HighlightRange = 22, TransitionSoftness = 26 });
            target.Add(new StylePresetData { Name = "夜景灰蓝", Shadows = 68, Highlights = 42, Midtones = 14, BlackPoint = -5, WhitePoint = -7, ContrastPivot = 11, Temperature = -28, Tint = -4, Vibrance = 18, Exposure = 6, ShadowRange = 20, HighlightRange = 7, TransitionSoftness = 14 });
            target.Add(new StylePresetData { Name = "暖阳照片", Shadows = 32, Highlights = 26, Midtones = 8, BlackPoint = -3, WhitePoint = 3, ContrastPivot = 7, Temperature = 22, Tint = 6, Vibrance = 24, Exposure = 4, ShadowRange = 7, HighlightRange = -3, TransitionSoftness = 10 });
            target.Add(new StylePresetData { Name = "中性通透", Shadows = 20, Highlights = 28, Midtones = 5, BlackPoint = -4, WhitePoint = 0, ContrastPivot = 10, Temperature = 0, Tint = 0, Vibrance = 16, Exposure = 3, ShadowRange = 7, HighlightRange = 3, TransitionSoftness = 7 });
        }

        public int RestoreBuiltInPresets()
        {
            if (Items == null) Items = new List<StylePresetData>();
            var builtIns = new List<StylePresetData>();
            AddCurated(builtIns);
            int restored = 0;
            foreach (var preset in builtIns)
            {
                bool exists = Items.Exists(item => item != null &&
                    string.Equals(item.Name, preset.Name,
                        StringComparison.OrdinalIgnoreCase));
                if (exists) continue;
                Items.Add(preset);
                restored++;
            }
            if (restored > 0) Save();
            return restored;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            using (var stream = File.Create(FilePath))
                new DataContractJsonSerializer(typeof(StyleSettingsStore)).WriteObject(stream, this);
        }
    }

    [DataContract]
    internal sealed class AppSettingsStore
    {
        [DataMember] public bool StartWithWindows;
        [DataMember] public bool StartMinimized;
        [DataMember] public bool CloseToTray;
        [DataMember] public bool AutoReapply;
        [DataMember] public int StrengthMode;
        [DataMember] public bool GameAutoSwitchEnabled;
        [DataMember] public bool RestoreAfterGame = true;
        [DataMember] public bool FrameGenerationCompatibility = true;
        [DataMember] public List<GamePresetRule> GameRules = new List<GamePresetRule>();
        [DataMember] public int HoldOriginalKey;
        [DataMember] public uint HoldOriginalModifiers;
        // 0 = auto, 1 = monitor DDC/CI, 2 = Windows software color.
        [DataMember] public int SaturationBackend;
        [DataMember] public bool SyncAllDisplays;
        // Name only: startup selects this preset but never auto-applies it.
        // Runtime display state and recovery baseline remain independent.
        [DataMember] public string DefaultPresetName;
        // V2 simple-adjustment UI state. These are UI preferences only; they
        // never replace the recovery baseline or write a preset implicitly.
        [DataMember] public bool SimpleStyleMode = true;
        [DataMember] public int SimpleBrightness;
        [DataMember] public int SimpleDarkArea;
        [DataMember] public int SimpleClarity = 50;
        [DataMember] public int SimpleColor;
        [DataMember] public int SimpleTonalDepth;
        [DataMember] public int SimpleTemperature;
        [DataMember] public int SchemaVersion;

        private static string Folder {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DisplayPresetPrototype"); }
        }
        private static string FilePath { get { return Path.Combine(Folder, "app-settings.json"); } }

        public static AppSettingsStore Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    AppSettingsStore loaded;
                    using (var stream = File.OpenRead(FilePath))
                        loaded = (AppSettingsStore)new DataContractJsonSerializer(
                            typeof(AppSettingsStore)).ReadObject(stream);
                    if (loaded != null)
                    {
                        if (loaded.GameRules == null)
                            loaded.GameRules = new List<GamePresetRule>();
                        if (loaded.SchemaVersion < 1)
                        {
                            loaded.AutoReapply = true;
                            loaded.StrengthMode = 1;
                            loaded.SchemaVersion = 1;
                        }
                        if (loaded.SchemaVersion < 2)
                        {
                            loaded.FrameGenerationCompatibility = true;
                            loaded.SchemaVersion = 2;
                        }
                        if (loaded.SchemaVersion < 3)
                        {
                            loaded.SimpleStyleMode = true;
                            loaded.SimpleClarity = 50;
                            loaded.SchemaVersion = 3;
                        }
                        loaded.Save();
                        return loaded;
                    }
                }
            }
            catch { }
            return new AppSettingsStore {
                AutoReapply = true, StrengthMode = 1,
                FrameGenerationCompatibility = true, SimpleStyleMode = true,
                SimpleClarity = 50, SchemaVersion = 3
            };
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            using (var stream = File.Create(FilePath))
                new DataContractJsonSerializer(typeof(AppSettingsStore)).WriteObject(stream, this);
        }

        public static void SetRunAtStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled)
                    key.SetValue("ShadePilot", "\"" + Application.ExecutablePath + "\"");
                else
                    key.DeleteValue("ShadePilot", false);
            }
        }
    }

    [DataContract]
    internal sealed class GamePresetRule
    {
        [DataMember] public string ProcessName;
        [DataMember] public string PresetName;
        public override string ToString() {
            return ProcessName + "  →  " + PresetName;
        }
    }

    internal sealed class AppTheme
    {
        public readonly string Name;
        public readonly Color Canvas, Surface, TintSurface, SoftSurface;
        public readonly Color Accent, AccentSoft, Button, Ink, Muted, OnAccent;

        public AppTheme(
            string name, Color canvas, Color surface, Color tintSurface, Color softSurface,
            Color accent, Color accentSoft, Color button, Color ink, Color muted, Color onAccent)
        {
            Name = name; Canvas = canvas; Surface = surface; TintSurface = tintSurface;
            SoftSurface = softSurface; Accent = accent; AccentSoft = accentSoft;
            Button = button; Ink = ink; Muted = muted; OnAccent = onAccent;
        }
        public override string ToString() { return Name; }
    }

    internal static class SecondaryWindowTheme
    {
        public static void Apply(Form form)
        {
            AppTheme theme = ThemeManager.Current;
            form.BackColor = theme.Canvas;
            foreach (Control control in form.Controls)
            {
                var panel = control as RoundedPanel;
                if (panel != null)
                {
                    panel.FillColor = Equals(panel.Tag, "TintCard")
                        ? theme.TintSurface : theme.Surface;
                    panel.Invalidate();
                    ApplyCardChildren(panel.Controls, theme, panel.FillColor);
                }
            }
            foreach (Control control in form.Controls)
            {
                if (control is RoundedPanel) continue;
                Color surface = SurfaceFor(form, control, theme);
                var button = control as RoundedButton;
                if (button != null)
                {
                    bool primary = button.ForeColor == Color.White;
                    button.BackColor = primary ? theme.Accent : theme.Button;
                    button.ForeColor = primary ? theme.OnAccent : theme.Ink;
                    button.SurfaceColor = surface;
                    button.Invalidate();
                }
                else if (control is TextBox || control is ComboBox ||
                         control is ListBox || control is CheckedListBox ||
                         control is NumericUpDown)
                {
                    control.BackColor = theme.SoftSurface;
                    control.ForeColor = theme.Ink;
                    var combo = control as ThemedComboBox;
                    if (combo != null) combo.BorderColor = theme.SoftSurface;
                }
                else if (control is TrackBar)
                {
                    control.BackColor = surface;
                    control.ForeColor = theme.Ink;
                }
                else
                {
                    control.BackColor = surface;
                    control.ForeColor = control.ForeColor == Color.Firebrick
                        ? Color.Firebrick : theme.Ink;
                }
                control.Invalidate();
            }
            form.Invalidate(true);
        }

        private static void ApplyCardChildren(Control.ControlCollection controls,
            AppTheme theme, Color surface)
        {
            foreach (Control control in controls)
            {
                var button = control as RoundedButton;
                if (button != null)
                {
                    bool primary = button.ForeColor == Color.White;
                    button.BackColor = primary ? theme.Accent : theme.Button;
                    button.ForeColor = primary ? theme.OnAccent : theme.Ink;
                    button.SurfaceColor = surface;
                }
                else if (control is TextBox || control is ComboBox ||
                         control is ListBox || control is NumericUpDown)
                {
                    control.BackColor = theme.SoftSurface;
                    control.ForeColor = theme.Ink;
                }
                else
                {
                    control.BackColor = Color.Transparent;
                    control.ForeColor = control.ForeColor == Color.Firebrick
                        ? Color.Firebrick : theme.Ink;
                }
                if (control.HasChildren)
                    ApplyCardChildren(control.Controls, theme, surface);
                control.Invalidate();
            }
        }

        private static Color SurfaceFor(Form form, Control control, AppTheme theme)
        {
            Point center = new Point(control.Left + control.Width / 2,
                control.Top + control.Height / 2);
            foreach (Control candidate in form.Controls)
            {
                var panel = candidate as RoundedPanel;
                if (panel != null && panel.Bounds.Contains(center))
                    return panel.FillColor;
            }
            return theme.Canvas;
        }
    }

    internal static class ThemeManager
    {
        public static readonly List<AppTheme> Themes = new List<AppTheme> {
            new AppTheme("雾紫",
                Color.FromArgb(245,247,251), Color.White, Color.FromArgb(249,248,255), Color.FromArgb(249,250,253),
                Color.FromArgb(76,99,220), Color.FromArgb(239,236,255), Color.FromArgb(238,241,247),
                Color.FromArgb(29,36,50), Color.FromArgb(104,113,130), Color.White),
            new AppTheme("极夜蓝",
                Color.FromArgb(20,24,34), Color.FromArgb(29,34,48), Color.FromArgb(32,38,58), Color.FromArgb(27,32,47),
                Color.FromArgb(124,140,255), Color.FromArgb(45,53,88), Color.FromArgb(42,49,68),
                Color.FromArgb(242,245,255), Color.FromArgb(170,178,200), Color.White),
            new AppTheme("薄荷灰",
                Color.FromArgb(242,247,245), Color.White, Color.FromArgb(241,250,246), Color.FromArgb(244,248,246),
                Color.FromArgb(59,170,131), Color.FromArgb(223,245,236), Color.FromArgb(229,240,235),
                Color.FromArgb(31,48,42), Color.FromArgb(107,127,119), Color.White),
            new AppTheme("暖砂",
                Color.FromArgb(248,245,239), Color.White, Color.FromArgb(252,247,238), Color.FromArgb(248,244,236),
                Color.FromArgb(201,130,77), Color.FromArgb(247,231,216), Color.FromArgb(241,232,220),
                Color.FromArgb(57,44,36), Color.FromArgb(139,118,103), Color.White),
            new AppTheme("黑白极简",
                Color.FromArgb(243,243,243), Color.White, Color.FromArgb(248,248,248), Color.FromArgb(246,246,246),
                Color.FromArgb(32,32,32), Color.FromArgb(230,230,230), Color.FromArgb(235,235,235),
                Color.FromArgb(24,24,24), Color.FromArgb(112,112,112), Color.White)
        };

        public static AppTheme Current { get; private set; }
        private static string FilePath {
            get {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DisplayPresetPrototype", "theme.txt");
            }
        }

        public static void Load()
        {
            string name = null;
            try { if (File.Exists(FilePath)) name = File.ReadAllText(FilePath, Encoding.UTF8).Trim(); }
            catch { }
            Current = Themes.Find(theme => theme.Name == name) ?? Themes[0];
        }

        public static void Set(AppTheme theme)
        {
            if (theme == null) return;
            Current = theme;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, theme.Name, Encoding.UTF8);
            }
            catch { }
        }
    }

    internal sealed class StylePresetManagerForm : Form
    {
        private readonly StyleSettingsStore store;
        private readonly ListBox presetList = new ListBox();
        private readonly TextBox nameBox = new TextBox();
        private readonly NumericUpDown shadowsBox = NewNumber(0, 200);
        private readonly NumericUpDown highlightsBox = NewNumber(0, 200);
        private readonly NumericUpDown midtonesBox = NewNumber(-100, 100);
        private readonly NumericUpDown blackPointBox = NewNumber(-100, 100);
        private readonly NumericUpDown whitePointBox = NewNumber(-100, 100);
        private readonly NumericUpDown contrastPivotBox = NewNumber(-100, 100);
        private readonly NumericUpDown temperatureBox = NewNumber(-100, 100);
        private readonly NumericUpDown tintBox = NewNumber(-100, 100);
        private readonly NumericUpDown vibranceBox = NewNumber(-100, 100);
        private readonly NumericUpDown saturationBox = NewNumber(0, 100);
        private readonly NumericUpDown exposureBox = NewNumber(-100, 100);
        private readonly NumericUpDown shadowRangeBox = NewNumber(-100, 100);
        private readonly NumericUpDown highlightRangeBox = NewNumber(-100, 100);
        private readonly NumericUpDown transitionSoftnessBox = NewNumber(-100, 100);
        private readonly Label status = new Label();
        private readonly ToolTip helpTip = new ToolTip();

        public StylePresetManagerForm(StyleSettingsStore store, StylePresetData selected)
        {
            this.store = store;
            Text = "管理画面风格";
            ClientSize = new Size(720, 650);
            MinimumSize = new Size(600, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 251);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            AutoScrollMinSize = new Size(720, 650);

            var listCard = new RoundedPanel { Location = new Point(18, 80), Size = new Size(238, 500), Tag = "TintCard" };
            var editorCard = new RoundedPanel { Location = new Point(260, 80), Size = new Size(442, 500), Tag = "SurfaceCard" };
            Controls.AddRange(new Control[] { listCard, editorCard });

            Controls.Add(new Label {
                Text = "预设管理", AutoSize = true, Location = new Point(24, 18),
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(29, 36, 50)
            });
            Controls.Add(new Label {
                Text = "可重命名、调整参数或删除不需要的预设",
                AutoSize = true, Location = new Point(26, 52),
                ForeColor = Color.FromArgb(104, 113, 130)
            });

            presetList.Location = new Point(26, 86);
            presetList.Size = new Size(220, 480);
            presetList.BorderStyle = BorderStyle.FixedSingle;
            presetList.SelectedIndexChanged += delegate { LoadSelected(); };
            Controls.Add(presetList);

            AddField("名称", nameBox, 270, 88);
            AddField("提亮暗部", shadowsBox, 270, 148);
            AddField("压低高光", highlightsBox, 420, 148);
            AddField("中间调", midtonesBox, 570, 148);
            AddField("黑位", blackPointBox, 270, 218);
            AddField("白位", whitePointBox, 420, 218);
            AddField("中间对比", contrastPivotBox, 570, 218);
            AddField("色温", temperatureBox, 270, 288);
            AddField("色调", tintBox, 420, 288);
            AddField("自然色彩", vibranceBox, 570, 288);
            AddField("整体曝光", exposureBox, 270, 358);
            AddField("暗部范围", shadowRangeBox, 420, 358);
            AddField("高光范围", highlightRangeBox, 570, 358);
            AddField("过渡柔和", transitionSoftnessBox, 270, 428);
            AddField("饱和度", saturationBox, 420, 428);

            var save = NewButton("保存修改", 270, 510, 112, true);
            save.Click += delegate { SaveSelected(); };
            var delete = NewButton("删除预设", 394, 510, 112, false);
            delete.Click += delegate { DeleteSelected(); };
            var restoreBuiltIns = NewButton("恢复内置预设", 518, 510, 164, false);
            restoreBuiltIns.Click += delegate { RestoreBuiltInPresets(); };
            var done = NewButton("完成", 596, 592, 96, true);
            done.DialogResult = DialogResult.OK;
            status.Location = new Point(270, 562);
            status.Size = new Size(420, 24);
            status.ForeColor = Color.FromArgb(45, 90, 55);
            Controls.AddRange(new Control[] {
                save, delete, restoreBuiltIns, done, status
            });
            AcceptButton = done;

            ConfigureToolTips();
            RefreshList(selected);
            editorCard.SendToBack();
            listCard.SendToBack();
            SecondaryWindowTheme.Apply(this);
        }

        private void ExportPresets()
        {
            using (var dialog = new SaveFileDialog {
                Title = "导出 ShadePilot 预设",
                Filter = "ShadePilot 预设 (*.json)|*.json",
                FileName = "ShadePilot-presets.json",
                AddExtension = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                using (var stream = File.Create(dialog.FileName))
                    new DataContractJsonSerializer(typeof(List<StylePresetData>))
                        .WriteObject(stream, store.Items);
                status.ForeColor = Color.FromArgb(45, 90, 55);
                status.Text = "全部画面风格已导出。";
            }
        }

        private void ImportPresets()
        {
            using (var dialog = new OpenFileDialog {
                Title = "导入 ShadePilot 预设",
                Filter = "ShadePilot 预设 (*.json)|*.json",
                CheckFileExists = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    List<StylePresetData> imported;
                    using (var stream = File.OpenRead(dialog.FileName))
                        imported = (List<StylePresetData>)new DataContractJsonSerializer(
                            typeof(List<StylePresetData>)).ReadObject(stream);
                    if (imported == null || imported.Count == 0)
                        throw new InvalidOperationException("文件中没有可用预设。");
                    int count = 0;
                    foreach (var incoming in imported)
                    {
                        if (incoming == null || string.IsNullOrWhiteSpace(incoming.Name)) continue;
                        incoming.Name = incoming.Name.Trim();
                        incoming.HotkeyKey = 0;
                        incoming.HotkeyModifiers = 0;
                        var existing = store.Items.Find(p => string.Equals(
                            p.Name, incoming.Name, StringComparison.OrdinalIgnoreCase));
                        if (existing != null) store.Items.Remove(existing);
                        store.Items.Add(incoming);
                        count++;
                    }
                    store.Save();
                    RefreshList(store.Items.Count > 0 ? store.Items[0] : null);
                    status.ForeColor = Color.FromArgb(45, 90, 55);
                    status.Text = "已导入 " + count + " 个预设；快捷键不会随文件导入。";
                }
                catch (Exception ex)
                {
                    status.ForeColor = Color.FromArgb(180, 55, 55);
                    status.Text = "导入失败：" + ex.Message;
                }
            }
        }

        private void ConfigureToolTips()
        {
            helpTip.IsBalloon = true;
            helpTip.InitialDelay = 500;
            helpTip.AutoPopDelay = 9000;
            helpTip.ShowAlways = true;
            helpTip.SetToolTip(presetList, "选择需要编辑或删除的画面风格预设。");
            helpTip.SetToolTip(nameBox, "修改预设名称。名称不能与其他预设重复。");
            helpTip.SetToolTip(shadowsBox, "提升阴影和较暗的中间区域，范围 0–200。");
            helpTip.SetToolTip(highlightsBox, "压低高亮区域，范围 0–200。");
            helpTip.SetToolTip(midtonesBox, "调整主体中间亮度；正值提亮，负值压暗。");
            helpTip.SetToolTip(blackPointBox, "正值抬起黑位，负值加深黑色。");
            helpTip.SetToolTip(whitePointBox, "正值增强白场，负值收回最亮区域。");
            helpTip.SetToolTip(contrastPivotBox, "围绕中间亮度增强或减弱曲线对比度。");
            helpTip.SetToolTip(temperatureBox, "负值偏冷蓝，正值偏暖黄。");
            helpTip.SetToolTip(tintBox, "负值偏绿，正值偏洋红。");
            helpTip.SetToolTip(vibranceBox, "Gamma 曲线范围内的自然色彩近似调节。");
            helpTip.SetToolTip(exposureBox, "整体增减软件曝光，约对应 -2 到 +2 档。");
            helpTip.SetToolTip(shadowRangeBox, "控制暗部补偿覆盖范围，不改变补偿强度。");
            helpTip.SetToolTip(highlightRangeBox, "控制高光压制覆盖范围，不改变压制强度。");
            helpTip.SetToolTip(transitionSoftnessBox, "控制调整区域边缘的过渡宽缓程度。");
        }

        private void AddField(string label, Control input, int x, int y)
        {
            Controls.Add(new Label {
                Text = label, AutoSize = true, Location = new Point(x, y),
                ForeColor = Color.FromArgb(47, 55, 70)
            });
            input.Location = new Point(x, y + 25);
            input.Size = new Size(input == nameBox ? 425 : 125, 27);
            Controls.Add(input);
        }

        private static NumericUpDown NewNumber(int minimum, int maximum)
        {
            return new NumericUpDown {
                Minimum = minimum, Maximum = maximum,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        private static Button NewButton(string text, int x, int y, int width, bool primary)
        {
            var button = new RoundedButton {
                Text = text, Location = new Point(x, y), Size = new Size(width, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(76, 99, 220) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(29, 36, 50)
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private void RefreshList(StylePresetData selected)
        {
            presetList.BeginUpdate();
            presetList.Items.Clear();
            foreach (var item in store.Items) presetList.Items.Add(item);
            presetList.EndUpdate();
            if (presetList.Items.Count == 0)
            {
                ClearFields();
                return;
            }
            int index = selected == null ? 0 : store.Items.IndexOf(selected);
            presetList.SelectedIndex = index >= 0 ? index : 0;
        }

        private void LoadSelected()
        {
            var item = presetList.SelectedItem as StylePresetData;
            if (item == null) { ClearFields(); return; }
            nameBox.Text = item.Name;
            shadowsBox.Value = Math.Max(shadowsBox.Minimum, Math.Min(shadowsBox.Maximum, item.Shadows));
            highlightsBox.Value = Math.Max(highlightsBox.Minimum, Math.Min(highlightsBox.Maximum, item.Highlights));
            midtonesBox.Value = Math.Max(midtonesBox.Minimum, Math.Min(midtonesBox.Maximum, item.Midtones));
            blackPointBox.Value = Math.Max(blackPointBox.Minimum, Math.Min(blackPointBox.Maximum, item.BlackPoint));
            whitePointBox.Value = Math.Max(whitePointBox.Minimum, Math.Min(whitePointBox.Maximum, item.WhitePoint));
            contrastPivotBox.Value = Math.Max(contrastPivotBox.Minimum, Math.Min(contrastPivotBox.Maximum, item.ContrastPivot));
            temperatureBox.Value = Math.Max(temperatureBox.Minimum, Math.Min(temperatureBox.Maximum, item.Temperature));
            tintBox.Value = Math.Max(tintBox.Minimum, Math.Min(tintBox.Maximum, item.Tint));
            vibranceBox.Value = Math.Max(vibranceBox.Minimum, Math.Min(vibranceBox.Maximum, item.Vibrance));
            saturationBox.Value = Math.Max(saturationBox.Minimum, Math.Min(saturationBox.Maximum, item.Saturation <= 0 ? 50 : item.Saturation));
            exposureBox.Value = Math.Max(exposureBox.Minimum, Math.Min(exposureBox.Maximum, item.Exposure));
            shadowRangeBox.Value = Math.Max(shadowRangeBox.Minimum, Math.Min(shadowRangeBox.Maximum, item.ShadowRange));
            highlightRangeBox.Value = Math.Max(highlightRangeBox.Minimum, Math.Min(highlightRangeBox.Maximum, item.HighlightRange));
            transitionSoftnessBox.Value = Math.Max(transitionSoftnessBox.Minimum, Math.Min(transitionSoftnessBox.Maximum, item.TransitionSoftness));
            status.Text = "";
        }

        private void ClearFields()
        {
            nameBox.Text = "";
            shadowsBox.Value = highlightsBox.Value = midtonesBox.Value = 0;
            blackPointBox.Value = whitePointBox.Value = contrastPivotBox.Value = 0;
            temperatureBox.Value = tintBox.Value = vibranceBox.Value = 0;
            saturationBox.Value = 50;
            exposureBox.Value = shadowRangeBox.Value = highlightRangeBox.Value = transitionSoftnessBox.Value = 0;
            status.Text = "当前没有预设；可点击“恢复内置预设”找回默认方案。";
        }

        private void RestoreBuiltInPresets()
        {
            int restored = store.RestoreBuiltInPresets();
            RefreshList(store.Items.Count > 0 ? store.Items[0] : null);
            status.ForeColor = Color.FromArgb(45, 90, 55);
            status.Text = restored > 0
                ? "已恢复 " + restored + " 个缺失的内置预设。"
                : "内置预设均已存在，没有覆盖当前参数。";
        }

        private void SaveSelected()
        {
            var item = presetList.SelectedItem as StylePresetData;
            if (item == null) return;
            string name = nameBox.Text.Trim();
            if (name.Length == 0)
            {
                status.ForeColor = Color.FromArgb(180, 55, 55);
                status.Text = "名称不能为空。";
                return;
            }
            if (store.Items.Exists(other => other != item &&
                string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                status.ForeColor = Color.FromArgb(180, 55, 55);
                status.Text = "已有同名预设，请换一个名称。";
                return;
            }
            string previousName = item.Name;
            item.Name = name;
            if (store.CyclePresetNames != null)
            {
                for (int cycleIndex = 0;
                    cycleIndex < store.CyclePresetNames.Count; cycleIndex++)
                {
                    if (string.Equals(store.CyclePresetNames[cycleIndex], previousName,
                        StringComparison.OrdinalIgnoreCase))
                        store.CyclePresetNames[cycleIndex] = name;
                }
            }
            item.Shadows = (int)shadowsBox.Value;
            item.Highlights = (int)highlightsBox.Value;
            item.Midtones = (int)midtonesBox.Value;
            item.BlackPoint = (int)blackPointBox.Value;
            item.WhitePoint = (int)whitePointBox.Value;
            item.ContrastPivot = (int)contrastPivotBox.Value;
            item.Temperature = (int)temperatureBox.Value;
            item.Tint = (int)tintBox.Value;
            item.Vibrance = (int)vibranceBox.Value;
            item.Saturation = (int)saturationBox.Value;
            item.Exposure = (int)exposureBox.Value;
            item.ShadowRange = (int)shadowRangeBox.Value;
            item.HighlightRange = (int)highlightRangeBox.Value;
            item.TransitionSoftness = (int)transitionSoftnessBox.Value;
            store.Save();
            int index = presetList.SelectedIndex;
            presetList.Items[index] = item;
            status.ForeColor = Color.FromArgb(45, 90, 55);
            status.Text = "修改已保存。";
        }

        private void DeleteSelected()
        {
            var item = presetList.SelectedItem as StylePresetData;
            if (item == null) return;
            if (MessageBox.Show(this, "删除预设“" + item.Name + "”？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int index = presetList.SelectedIndex;
            store.Items.Remove(item);
            store.Save();
            RefreshList(store.Items.Count == 0 ? null : store.Items[Math.Min(index, store.Items.Count - 1)]);
            status.ForeColor = Color.FromArgb(45, 90, 55);
            status.Text = "预设已删除。";
        }
    }

    internal sealed class HotkeySettingsForm : Form
    {
        public event EventHandler BindingSaved;
        private readonly StyleSettingsStore store;
        private readonly Dictionary<string, string> registrationStatus;
        private readonly ComboBox targetBox = new ThemedComboBox();
        private readonly TextBox hotkeyInput = new TextBox();
        private readonly TextBox cycleHotkeyInput = new TextBox();
        private readonly CheckedListBox cyclePresetList = new CheckedListBox();
        private readonly Label status = new Label();
        private readonly ToolTip helpTip = new ToolTip();
        private bool loading;
        private int capturedKey;
        private uint capturedModifiers;
        private int capturedCycleKey;
        private uint capturedCycleModifiers;

        public HotkeySettingsForm(
            StyleSettingsStore store, Dictionary<string, string> registrationStatus)
        {
            this.store = store;
            this.registrationStatus = registrationStatus;
            if (this.store.CyclePresetNames == null)
                this.store.CyclePresetNames = new List<string>();
            Text = "快捷键设置";
            ClientSize = new Size(560, 650);
            MinimumSize = new Size(500, 480);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 251);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            AutoScrollMinSize = new Size(560, 650);

            var title = new Label {
                Text = "快捷键设置", AutoSize = false, Location = new Point(28, 18),
                Size = new Size(500, 36), TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(29, 36, 50)
            };
            var note = new Label {
                Text = "单独切换与循环切换互不混用；新保存的组合键会自动替换冲突绑定。",
                AutoSize = false, Location = new Point(30, 56), Size = new Size(500, 24),
                ForeColor = Color.FromArgb(104, 113, 130)
            };
            var directCard = NewHotkeyCard(24, 91, 512, 205);
            directCard.Tag = "SurfaceCard";
            var directTitle = NewHotkeyTitle("单独切换", 42, 105);
            var directNote = NewHotkeyNote("为单个滤镜或恢复原画设置一键直达快捷键", 42, 136);
            targetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            targetBox.FlatStyle = FlatStyle.Flat;
            targetBox.Location = new Point(42, 170);
            targetBox.Size = new Size(228, 30);
            foreach (var preset in store.Items) targetBox.Items.Add(new HotkeyTarget(preset));
            targetBox.Items.Add(HotkeyTarget.Restore());
            targetBox.SelectedIndexChanged += delegate { LoadTarget(); };

            ConfigureHotkeyInput(hotkeyInput, 282, 170, 234);
            hotkeyInput.KeyDown += CaptureHotkey;
            hotkeyInput.Enter += delegate {
                status.ForeColor = Color.FromArgb(76, 99, 220);
                status.Text = "现在按下需要的快捷键组合。";
            };

            var saveDirect = NewDialogButton("保存单独快捷键", 366, 226, 150, true);
            saveDirect.Click += delegate { SaveTarget(); };

            var cycleCard = NewHotkeyCard(24, 310, 512, 260);
            cycleCard.Tag = "TintCard";
            var cycleTitle = NewHotkeyTitle("循环切换", 42, 324);
            var cycleNote = NewHotkeyNote("每次按键，按下方勾选顺序切换到下一个模式", 42, 355);
            ConfigureHotkeyInput(cycleHotkeyInput, 42, 389, 474);
            capturedCycleKey = store.CycleHotkeyKey;
            capturedCycleModifiers = store.CycleHotkeyModifiers;
            cycleHotkeyInput.Text = FormatHotkey(capturedCycleModifiers, capturedCycleKey);
            cycleHotkeyInput.KeyDown += CaptureCycleHotkey;
            cycleHotkeyInput.Enter += delegate {
                status.ForeColor = Color.FromArgb(76, 99, 220);
                status.Text = "现在按下循环切换使用的快捷键组合。";
            };
            var cycleLabel = NewHotkeyNote("参与循环的预设（至少选择两个）", 42, 430);
            cyclePresetList.Location = new Point(42, 458);
            cyclePresetList.Size = new Size(316, 92);
            cyclePresetList.CheckOnClick = true;
            cyclePresetList.BorderStyle = BorderStyle.FixedSingle;
            foreach (var preset in store.Items)
            {
                int index = cyclePresetList.Items.Add(preset.Name);
                if (store.CyclePresetNames != null && store.CyclePresetNames.Exists(
                    name => string.Equals(name, preset.Name, StringComparison.OrdinalIgnoreCase)))
                    cyclePresetList.SetItemChecked(index, true);
            }

            var saveCycle = NewDialogButton("保存循环设置", 370, 516, 146, true);
            saveCycle.Click += delegate { SaveCycleTarget(); };
            var done = NewDialogButton("完成", 416, 594, 100, true);
            done.DialogResult = DialogResult.OK;
            status.Location = new Point(30, 585);
            status.Size = new Size(370, 52);
            status.ForeColor = Color.FromArgb(45, 90, 55);

            Controls.AddRange(new Control[] {
                title, note, directCard, cycleCard,
                directTitle, directNote, targetBox, hotkeyInput, saveDirect,
                cycleTitle, cycleNote, cycleHotkeyInput, cycleLabel,
                cyclePresetList, saveCycle, done, status
            });
            cycleCard.SendToBack();
            directCard.SendToBack();
            helpTip.IsBalloon = true;
            helpTip.InitialDelay = 500;
            helpTip.AutoPopDelay = 9000;
            helpTip.ShowAlways = true;
            helpTip.SetToolTip(targetBox, "选择需要绑定快捷键的画面风格或恢复操作。");
            helpTip.SetToolTip(hotkeyInput, "点击后直接按下快捷键组合；按 Backspace 或 Delete 清除。");
            helpTip.SetToolTip(cyclePresetList,
                "只有勾选的画面预设会加入循环；每次按快捷键切换到列表中的下一个。");
            AcceptButton = done;
            if (targetBox.Items.Count > 0) targetBox.SelectedIndex = 0;
            SecondaryWindowTheme.Apply(this);
        }

        private void LoadTarget()
        {
            var target = targetBox.SelectedItem as HotkeyTarget;
            if (target == null) return;
            loading = true;
            uint modifiers = target.Kind == HotkeyTargetKind.Restore
                ? store.RestoreHotkeyModifiers
                : target.Kind == HotkeyTargetKind.Cycle
                    ? store.CycleHotkeyModifiers : target.Preset.HotkeyModifiers;
            int key = target.Kind == HotkeyTargetKind.Restore
                ? store.RestoreHotkeyKey
                : target.Kind == HotkeyTargetKind.Cycle
                    ? store.CycleHotkeyKey : target.Preset.HotkeyKey;
            capturedModifiers = modifiers;
            capturedKey = key;
            hotkeyInput.Text = FormatHotkey(capturedModifiers, capturedKey);
            loading = false;
            RefreshRegistrationStatus();
        }

        public void RefreshRegistrationStatus()
        {
            var target = targetBox.SelectedItem as HotkeyTarget;
            if (target == null) return;
            string name = target.Kind == HotkeyTargetKind.Restore ? "恢复原始画面" :
                target.Kind == HotkeyTargetKind.Cycle ? "循环切换所选预设" : target.Preset.Name;
            string result;
            if (!registrationStatus.TryGetValue(name, out result))
                result = capturedKey == 0 ? "未设置" : "等待注册";
            status.ForeColor = result.StartsWith("注册失败")
                ? Color.FromArgb(180, 55, 55)
                : Color.FromArgb(45, 90, 55);
            status.Text = result;
        }

        private void CaptureHotkey(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                capturedKey = 0;
                capturedModifiers = 0;
                hotkeyInput.Text = "未设置";
                status.Text = "已清除，点击“保存此绑定”后生效。";
                return;
            }

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu ||
                e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                hotkeyInput.Text = FormatPressedModifiers(e.Control, e.Alt, e.Shift);
                return;
            }

            capturedModifiers = (e.Alt ? 0x1u : 0u) |
                                (e.Control ? 0x2u : 0u) |
                                (e.Shift ? 0x4u : 0u);
            capturedKey = (int)e.KeyCode;
            hotkeyInput.Text = FormatHotkey(capturedModifiers, capturedKey);
            status.ForeColor = Color.FromArgb(45, 90, 55);
            status.Text = "已录入，点击“保存此绑定”后生效。";
        }

        private void CaptureCycleHotkey(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete)
            {
                capturedCycleKey = 0;
                capturedCycleModifiers = 0;
                cycleHotkeyInput.Text = "未设置";
                status.Text = "循环快捷键已清除，点击“保存循环设置”后生效。";
                return;
            }
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu ||
                e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                cycleHotkeyInput.Text = FormatPressedModifiers(e.Control, e.Alt, e.Shift);
                return;
            }
            capturedCycleModifiers = (e.Alt ? 0x1u : 0u) |
                                     (e.Control ? 0x2u : 0u) |
                                     (e.Shift ? 0x4u : 0u);
            capturedCycleKey = (int)e.KeyCode;
            cycleHotkeyInput.Text = FormatHotkey(
                capturedCycleModifiers, capturedCycleKey);
            status.ForeColor = Color.FromArgb(45, 90, 55);
            status.Text = "循环快捷键已录入，点击“保存循环设置”后生效。";
        }

        private void SaveCycleTarget()
        {
            if (capturedCycleKey != 0 && cyclePresetList.CheckedItems.Count < 2)
            {
                status.ForeColor = Color.FromArgb(180, 55, 55);
                status.Text = "循环切换至少需要勾选两个预设。";
                return;
            }
            var replaced = new List<string>();
            var cycleTarget = HotkeyTarget.Cycle();
            if (capturedCycleKey != 0)
                ClearConflictingBindings(cycleTarget, capturedCycleModifiers,
                    capturedCycleKey, replaced);
            store.CycleHotkeyModifiers = capturedCycleModifiers;
            store.CycleHotkeyKey = capturedCycleKey;
            store.CyclePresetNames.Clear();
            foreach (object item in cyclePresetList.CheckedItems)
                store.CyclePresetNames.Add(item.ToString());
            store.Save();
            status.ForeColor = Color.FromArgb(45, 90, 55);
            status.Text = capturedCycleKey == 0 ? "已取消循环切换快捷键。" :
                replaced.Count == 0 ? "循环切换设置已保存。" :
                "循环设置已保存，并自动替换：" +
                    string.Join("、", replaced.ToArray());
            if (BindingSaved != null) BindingSaved(this, EventArgs.Empty);
        }

        private void SaveTarget()
        {
            if (loading) return;
            var target = targetBox.SelectedItem as HotkeyTarget;
            if (target == null) return;
            if (target.Kind == HotkeyTargetKind.Cycle && capturedKey != 0 &&
                cyclePresetList.CheckedItems.Count < 2)
            {
                status.ForeColor = Color.FromArgb(180, 55, 55);
                status.Text = "循环切换至少需要勾选两个预设。";
                return;
            }
            var replaced = new List<string>();
            if (capturedKey != 0)
                ClearConflictingBindings(target, capturedModifiers, capturedKey, replaced);
            if (target.Kind == HotkeyTargetKind.Restore)
            {
                store.RestoreHotkeyModifiers = capturedModifiers;
                store.RestoreHotkeyKey = capturedKey;
            }
            else if (target.Kind == HotkeyTargetKind.Cycle)
            {
                store.CycleHotkeyModifiers = capturedModifiers;
                store.CycleHotkeyKey = capturedKey;
                store.CyclePresetNames.Clear();
                foreach (object item in cyclePresetList.CheckedItems)
                    store.CyclePresetNames.Add(item.ToString());
            }
            else
            {
                target.Preset.HotkeyModifiers = capturedModifiers;
                target.Preset.HotkeyKey = capturedKey;
            }
            status.ForeColor = Color.FromArgb(45, 90, 55);
            status.Text = capturedKey == 0 ? "已取消该功能的快捷键。" :
                replaced.Count == 0 ? "绑定已更新。" :
                "绑定已更新，并自动替换：" + string.Join("、", replaced.ToArray());
            store.Save();
            if (BindingSaved != null) BindingSaved(this, EventArgs.Empty);
        }

        private void ClearConflictingBindings(
            HotkeyTarget selected, uint modifiers, int key, List<string> replaced)
        {
            foreach (var preset in store.Items)
            {
                if (selected.Kind == HotkeyTargetKind.Preset && preset == selected.Preset)
                    continue;
                if (preset.HotkeyKey == key && preset.HotkeyModifiers == modifiers)
                {
                    preset.HotkeyKey = 0;
                    preset.HotkeyModifiers = 0;
                    replaced.Add(preset.Name);
                }
            }
            if (selected.Kind != HotkeyTargetKind.Restore &&
                store.RestoreHotkeyKey == key &&
                store.RestoreHotkeyModifiers == modifiers)
            {
                store.RestoreHotkeyKey = 0;
                store.RestoreHotkeyModifiers = 0;
                replaced.Add("恢复原始画面");
            }
            if (selected.Kind != HotkeyTargetKind.Cycle &&
                store.CycleHotkeyKey == key &&
                store.CycleHotkeyModifiers == modifiers)
            {
                store.CycleHotkeyKey = 0;
                store.CycleHotkeyModifiers = 0;
                replaced.Add("循环切换所选预设");
            }
        }

        private static string FormatPressedModifiers(bool control, bool alt, bool shift)
        {
            string text = "";
            if (control) text += "Ctrl + ";
            if (alt) text += "Alt + ";
            if (shift) text += "Shift + ";
            return text.Length == 0 ? "请继续按下主按键" : text + "…";
        }

        private static string FormatHotkey(uint modifiers, int key)
        {
            if (key == 0) return "未设置";
            string text = "";
            if ((modifiers & 0x2u) != 0) text += "Ctrl + ";
            if ((modifiers & 0x1u) != 0) text += "Alt + ";
            if ((modifiers & 0x4u) != 0) text += "Shift + ";
            return text + ((Keys)key).ToString();
        }

        private static Button NewDialogButton(string text, int x, int y, int width, bool primary)
        {
            var button = new RoundedButton {
                Text = text, Location = new Point(x, y), Size = new Size(width, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(76, 99, 220) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(29, 36, 50)
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static RoundedPanel NewHotkeyCard(
            int x, int y, int width, int height)
        {
            return new RoundedPanel {
                Location = new Point(x, y), Size = new Size(width, height),
                FillColor = Color.White, CornerRadius = 16,
                BackColor = Color.Transparent
            };
        }

        private Label NewHotkeyTitle(string text, int x, int y)
        {
            return new Label {
                Text = text, AutoSize = false, Location = new Point(x, y),
                Size = new Size(450, 28), TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.White, ForeColor = Color.FromArgb(29, 36, 50),
                Font = new Font(Font.FontFamily, 11F, FontStyle.Bold)
            };
        }

        private Label NewHotkeyNote(string text, int x, int y)
        {
            return new Label {
                Text = text, AutoSize = false, Location = new Point(x, y),
                Size = new Size(460, 24), TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.White, ForeColor = Color.FromArgb(104, 113, 130),
                Font = new Font(Font.FontFamily, 8.5F)
            };
        }

        private void ConfigureHotkeyInput(
            TextBox input, int x, int y, int width)
        {
            input.Location = new Point(x, y);
            input.Size = new Size(width, 30);
            input.ReadOnly = true;
            input.ShortcutsEnabled = false;
            input.TextAlign = HorizontalAlignment.Center;
            input.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            input.BackColor = Color.FromArgb(249, 250, 253);
            input.ForeColor = Color.FromArgb(29, 36, 50);
            input.BorderStyle = BorderStyle.FixedSingle;
        }

        private enum HotkeyTargetKind { Preset, Restore, Cycle }

        private sealed class HotkeyTarget
        {
            public readonly StylePresetData Preset;
            public readonly HotkeyTargetKind Kind;
            public HotkeyTarget(StylePresetData preset) {
                Preset = preset; Kind = HotkeyTargetKind.Preset;
            }
            private HotkeyTarget(HotkeyTargetKind kind) { Kind = kind; }
            public static HotkeyTarget Restore() { return new HotkeyTarget(HotkeyTargetKind.Restore); }
            public static HotkeyTarget Cycle() { return new HotkeyTarget(HotkeyTargetKind.Cycle); }
            public override string ToString() {
                return Kind == HotkeyTargetKind.Restore ? "恢复原始画面" :
                    Kind == HotkeyTargetKind.Cycle ? "循环切换所选预设" : Preset.Name;
            }
        }

    }

    internal sealed class ToolsCenterForm : Form
    {
        public event EventHandler SettingsChanged;
        private readonly StyleSettingsStore styles;
        private readonly AppSettingsStore settings;
        private readonly Action captureA;
        private readonly Action captureB;
        private readonly Action<bool> applyTemporary;
        private readonly Func<string> diagnose;
        private readonly ComboBox gamePreset = new ThemedComboBox();
        private readonly TextBox processName = new TextBox();
        private readonly ListBox gameRules = new ListBox();
        private readonly TextBox holdInput = new TextBox();
        private readonly TextBox shareCode = new TextBox();
        private readonly TextBox diagnosis = new TextBox();
        private int holdKey;
        private uint holdModifiers;

        public ToolsCenterForm(
            StyleSettingsStore styles, AppSettingsStore settings,
            Action captureA, Action captureB, Action<bool> applyTemporary,
            Func<string> diagnose, CheckBox restoreOnExitOption = null)
        {
            this.styles = styles; this.settings = settings;
            this.captureA = captureA; this.captureB = captureB;
            this.applyTemporary = applyTemporary; this.diagnose = diagnose;
            Text = "ShadePilot 工具中心";
            ClientSize = new Size(760, 680);
            MinimumSize = new Size(620, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true; MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 251);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = true;
            AutoScrollMinSize = new Size(760, 680);

            var compareCard = new RoundedPanel { Location = new Point(20, 14), Size = new Size(720, 150), Tag = "SurfaceCard" };
            var gameCard = new RoundedPanel { Location = new Point(20, 176), Size = new Size(720, 208), Tag = "TintCard" };
            var shareCard = new RoundedPanel { Location = new Point(20, 390), Size = new Size(720, 115), Tag = "SurfaceCard" };
            var diagnosticCard = new RoundedPanel { Location = new Point(20, 511), Size = new Size(720, 151), Tag = "TintCard" };
            Controls.AddRange(new Control[] { compareCard, gameCard, shareCard, diagnosticCard });

            AddTitle("原画 / A-B 画面对比", 34, 22);
            AddNote("按一次快捷键切到原画，再按一次恢复滤镜；A/B 用来保存两组临时效果并来回比较。", 34, 48);
            AddLabel("切换原画快捷键", 34, 82);
            ConfigureInput(holdInput, 130, 79, 220);
            holdKey = settings.HoldOriginalKey;
            holdModifiers = settings.HoldOriginalModifiers;
            holdInput.Text = FormatHotkey(holdModifiers, holdKey);
            holdInput.KeyDown += CaptureHold;
            var saveHold = Button("保存快捷键", 362, 77, 116, true);
            saveHold.Click += delegate {
                settings.HoldOriginalKey = holdKey;
                settings.HoldOriginalModifiers = holdModifiers;
                settings.Save(); Changed();
            };
            var capA = Button("当前效果存为 A", 34, 124, 132, false);
            var capB = Button("当前效果存为 B", 176, 124, 132, false);
            var showA = Button("切换到 A", 318, 124, 100, false);
            var showB = Button("切换到 B", 428, 124, 100, false);
            capA.Click += delegate { captureA(); };
            capB.Click += delegate { captureB(); };
            showA.Click += delegate { applyTemporary(true); };
            showB.Click += delegate { applyTemporary(false); };

            AddTitle("游戏自动切换", 34, 184);
            var enabled = new CheckBox {
                Text = "启用前台游戏自动切换", AutoSize = false,
                Location = new Point(34, 216), Size = new Size(250, 28),
                Checked = settings.GameAutoSwitchEnabled
            };
            enabled.CheckedChanged += delegate {
                settings.GameAutoSwitchEnabled = enabled.Checked;
                settings.Save(); Changed();
            };
            var restoreAfter = new CheckBox {
                Text = "离开游戏后恢复进入前画面", AutoSize = false,
                Location = new Point(300, 216), Size = new Size(250, 28),
                Checked = settings.RestoreAfterGame
            };
            restoreAfter.CheckedChanged += delegate {
                settings.RestoreAfterGame = restoreAfter.Checked;
                settings.Save(); Changed();
            };
            var frameGenCompatibility = new CheckBox {
                Text = "游戏性能保护（全屏时暂停软件色彩与显示器重检）",
                AutoSize = false, Location = new Point(34, 242), Size = new Size(390, 28),
                Checked = settings.FrameGenerationCompatibility
            };
            frameGenCompatibility.CheckedChanged += delegate {
                settings.FrameGenerationCompatibility = frameGenCompatibility.Checked;
                settings.Save(); Changed();
            };
            ConfigureInput(processName, 34, 273, 210);
            processName.ReadOnly = false;
            processName.TextAlign = HorizontalAlignment.Left;
            processName.Text = "EscapeFromTarkov.exe";
            gamePreset.DropDownStyle = ComboBoxStyle.DropDownList;
            gamePreset.FlatStyle = FlatStyle.Flat;
            gamePreset.Location = new Point(254, 273);
            gamePreset.Size = new Size(220, 30);
            foreach (var style in styles.Items) gamePreset.Items.Add(style);
            if (gamePreset.Items.Count > 0) gamePreset.SelectedIndex = 0;
            var addRule = Button("添加规则", 486, 271, 100, true);
            addRule.Click += delegate { AddGameRule(); };
            gameRules.Location = new Point(34, 313);
            gameRules.Size = new Size(552, 61);
            gameRules.BorderStyle = BorderStyle.FixedSingle;
            RefreshRules();
            var deleteRule = Button("删除选中", 598, 313, 112, false);
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
                frameGenCompatibility,
                processName, gamePreset, addRule, gameRules, deleteRule,
                shareCode, exportCode, importCode, diagnosis, refreshDiag,
                copyDiag, done
            });
            if (restoreOnExitOption != null)
            {
                Controls.Add(restoreOnExitOption);
                restoreOnExitOption.AutoSize = false;
                restoreOnExitOption.Location = new Point(430, 242);
                restoreOnExitOption.Size = new Size(180, 28);
                restoreOnExitOption.Text = "退出时自动恢复";
            }
            ReparentRange(compareCard, 14, 164);
            ReparentRange(gameCard, 176, 384);
            ReparentRange(shareCard, 390, 505);
            ReparentRange(diagnosticCard, 511, 662);
            Action fitTools = delegate {
                int cardWidth = Math.Max(580, ClientSize.Width - 40);
                compareCard.Width = cardWidth;
                gameCard.Width = cardWidth;
                shareCard.Width = cardWidth;
                diagnosticCard.Width = cardWidth;
                int actionX = cardWidth - 142;
                deleteRule.Left = actionX;
                exportCode.Left = actionX;
                importCode.Left = actionX;
                refreshDiag.Left = actionX;
                copyDiag.Left = actionX;
                done.Left = cardWidth - 126;
                gameRules.Width = Math.Max(360, cardWidth - 168);
                shareCode.Width = Math.Max(360, cardWidth - 168);
                diagnosis.Width = Math.Max(360, cardWidth - 168);
            };
            SizeChanged += delegate { fitTools(); };
            fitTools();
            diagnosticCard.SendToBack();
            shareCard.SendToBack();
            gameCard.SendToBack();
            compareCard.SendToBack();
            SecondaryWindowTheme.Apply(this);
            AcceptButton = done;
        }

        private void ReparentRange(RoundedPanel card, int top, int bottom)
        {
            var moving = new List<Control>();
            foreach (Control control in Controls)
            {
                if (control == card || control is RoundedPanel) continue;
                int center = control.Top + control.Height / 2;
                if (center >= top && center <= bottom) moving.Add(control);
            }
            foreach (Control control in moving)
            {
                control.Location = new Point(control.Left - card.Left, control.Top - card.Top);
                card.Controls.Add(control);
                if (control is Label || control is CheckBox)
                    control.BackColor = Color.Transparent;
                var button = control as RoundedButton;
                if (button != null) button.SurfaceColor = card.FillColor;
            }
        }

        public void ConfigureEmbedded()
        {
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            Dock = DockStyle.Fill;
            MinimumSize = Size.Empty;
            AutoScroll = false;
            AutoScrollMinSize = Size.Empty;
            AcceptButton = null;
            HideCompletionButton(Controls);
        }

        private static void HideCompletionButton(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                var button = control as Button;
                if (button != null && button.Text == "完成") button.Visible = false;
                if (control.HasChildren) HideCompletionButton(control.Controls);
            }
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
        private const string Prefix = "SP3-";
        public static string Export(StylePresetData p)
        {
            string payload = string.Join("|", new string[] {
                p.Name ?? "预设", p.Shadows.ToString(), p.Highlights.ToString(),
                p.Midtones.ToString(), p.BlackPoint.ToString(), p.WhitePoint.ToString(),
                p.ContrastPivot.ToString(), p.Temperature.ToString(), p.Tint.ToString(),
                p.Vibrance.ToString(), p.Exposure.ToString(), p.ShadowRange.ToString(),
                p.HighlightRange.ToString(), p.TransitionSoftness.ToString(),
                (p.Saturation <= 0 ? 50 : p.Saturation).ToString() });
            byte[] data = Encoding.UTF8.GetBytes(payload);
            string body = Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_');
            return Prefix + body + "-" + Checksum(data).ToString("X8");
        }
        public static StylePresetData Import(string code)
        {
            if (string.IsNullOrWhiteSpace(code) ||
                !(code.StartsWith(Prefix) || code.StartsWith("SP2-") || code.StartsWith("SP1-")))
                throw new InvalidOperationException("不是 ShadePilot 分享代码。");
            string codePrefix = code.StartsWith(Prefix) ? Prefix :
                (code.StartsWith("SP2-") ? "SP2-" : "SP1-");
            int split = code.LastIndexOf('-');
            if (split <= Prefix.Length) throw new InvalidOperationException("代码不完整。");
            string body = code.Substring(codePrefix.Length, split-codePrefix.Length)
                .Replace('-','+').Replace('_','/');
            while (body.Length % 4 != 0) body += "=";
            byte[] data = Convert.FromBase64String(body);
            uint expected;
            if (!uint.TryParse(code.Substring(split+1),
                System.Globalization.NumberStyles.HexNumber, null, out expected) ||
                expected != Checksum(data))
                throw new InvalidOperationException("校验失败，代码可能复制不完整。");
            string[] v = Encoding.UTF8.GetString(data).Split('|');
            if (v.Length != 14 && v.Length != 15) throw new InvalidOperationException("参数数量不正确。");
            var preset = new StylePresetData { Name=v[0], Shadows=I(v[1]),Highlights=I(v[2]),
                Midtones=I(v[3]),BlackPoint=I(v[4]),WhitePoint=I(v[5]),
                ContrastPivot=I(v[6]),Temperature=I(v[7]),Tint=I(v[8]),
                Vibrance=I(v[9]),Exposure=I(v[10]),ShadowRange=I(v[11]),
                HighlightRange=I(v[12]),TransitionSoftness=I(v[13]),
                Saturation=v.Length == 15 ? I(v[14]) : 50 };
            if (codePrefix == "SP2-")
                preset.Saturation = (int)Math.Round(preset.Saturation / 2.0);
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 80 ||
                preset.Shadows < 0 || preset.Shadows > 200 ||
                preset.Highlights < 0 || preset.Highlights > 200 ||
                !Signed(preset.Midtones) || !Signed(preset.BlackPoint) ||
                !Signed(preset.WhitePoint) || !Signed(preset.ContrastPivot) ||
                !Signed(preset.Temperature) || !Signed(preset.Tint) ||
                !Signed(preset.Vibrance) || !Signed(preset.Exposure) ||
                !Signed(preset.ShadowRange) || !Signed(preset.HighlightRange) ||
                !Signed(preset.TransitionSoftness) ||
                preset.Saturation < 0 || preset.Saturation > 100)
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
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool EnumDisplayDevices(
            string device, uint deviceNumber, ref DISPLAY_DEVICE displayDevice, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(
            IntPtr window, out uint processId);

        internal static bool IsFullscreenWindow(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window))
                return false;
            RECT rect;
            if (!GetWindowRect(window, out rect)) return false;
            Rectangle bounds;
            try { bounds = Screen.FromHandle(window).Bounds; }
            catch { return false; }
            const int tolerance = 4;
            return Math.Abs(rect.Left - bounds.Left) <= tolerance &&
                Math.Abs(rect.Top - bounds.Top) <= tolerance &&
                Math.Abs(rect.Right - bounds.Right) <= tolerance &&
                Math.Abs(rect.Bottom - bounds.Bottom) <= tolerance;
        }
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
                var monitorInfo = new MONITORINFOEX();
                monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                string displayDeviceName = GetMonitorInfo(hMonitor, ref monitorInfo)
                    ? monitorInfo.szDevice : null;
                string displayAdapterName = GetDisplayAdapterName(displayDeviceName);
                string stableDisplayKey = GetDisplayStableKey(displayDeviceName);
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
                        DisplayDeviceName = displayDeviceName,
                        DisplayAdapterName = displayAdapterName,
                        Id = stableDisplayKey + "|physical:" + i
                    });
                }
                logicalIndex++;
                return true;
            };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                throw Error("枚举显示器");
            return result;
        }

        private static string GetDisplayAdapterName(string displayDeviceName)
        {
            if (string.IsNullOrWhiteSpace(displayDeviceName)) return null;
            for (uint i = 0; i < 32; i++)
            {
                var adapter = new DISPLAY_DEVICE();
                adapter.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
                if (string.Equals(adapter.DeviceName, displayDeviceName,
                    StringComparison.OrdinalIgnoreCase))
                    return adapter.DeviceString;
            }
            return null;
        }

        private static string GetDisplayStableKey(string displayDeviceName)
        {
            if (!string.IsNullOrWhiteSpace(displayDeviceName))
            {
                var monitor = new DISPLAY_DEVICE();
                monitor.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                const uint EddGetDeviceInterfaceName = 0x00000001;
                if (EnumDisplayDevices(displayDeviceName, 0, ref monitor,
                    EddGetDeviceInterfaceName) && !string.IsNullOrWhiteSpace(monitor.DeviceID))
                    return monitor.DeviceID;
            }
            return string.IsNullOrWhiteSpace(displayDeviceName)
                ? "desktop-monitor" : displayDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        public static Exception Error(string operation)
        {
            int code = Marshal.GetLastWin32Error();
            return new InvalidOperationException(operation + "失败（Windows 错误 " + code + "）");
        }
    }
}
