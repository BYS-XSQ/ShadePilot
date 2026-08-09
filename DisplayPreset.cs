using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

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
        private readonly Label brightnessValue = new Label();
        private readonly Label contrastValue = new Label();
        private readonly Label saturationValue = new Label();
        private readonly Label saturationHint = new Label();
        private readonly Label status = new Label();
        private readonly CheckBox restoreOnExit = new CheckBox();
        private readonly CheckBox liveApply = new CheckBox();
        private readonly System.Windows.Forms.Timer liveApplyTimer = new System.Windows.Forms.Timer();
        private readonly GammaRampController gammaController = new GammaRampController();
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
        private StylePresetData activeStyle;
        private int registeredHotkeyCount;

        private const int WmHotkey = 0x0312;
        private const int HotkeyStyleBase = 2100;
        private const int HotkeyRestore = 2199;

        public MainForm()
        {
            Text = "ShadePilot";
            ClientSize = new Size(700, 760);
            MinimumSize = new Size(700, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Canvas;
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
                AutoSize = true, Location = new Point(80, 18)
            };
            var safety = new Label {
                Text = "明暗与色彩控制台",
                ForeColor = Muted,
                AutoSize = true, Location = new Point(83, 57)
            };
            var themeLabel = new Label {
                Text = "界面主题", AutoSize = true, Location = new Point(540, 17),
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
            saturationHint.AutoSize = true;
            saturationHint.Location = new Point(255, 337);
            saturationHint.ForeColor = Muted;
            saturationHint.BackColor = Color.White;
            saturationHint.Visible = false;

            liveApply.Text = "拖动时实时应用";
            liveApply.Checked = true;
            liveApply.AutoSize = true;
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
            restoreOnExit.AutoSize = true;
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

            Controls.AddRange(new Control[] {
                title, safety,
                logo,
                themeLabel, themeBox,
                mainCard, styleCard, actionCard,
                NewLabel("显示器", 42, 98), monitorBox, refresh,
                NewLabel("亮度", 42, 164), brightness, brightnessValue,
                NewLabel("对比度", 42, 236), contrast, contrastValue,
                NewLabel("硬件饱和度", 42, 308), saturation, saturationValue, saturationHint,
                liveApply, apply,
                NewTintedSectionTitle("画面风格", 42, 463),
                NewTintedMutedLabel("选择预设，或直接拖动参数微调", 42, 490),
                NewTintedLabel("强度档位", 230, 465), strengthBox,
                stylePresetBox, applyStyle, saveStyle, manageStyles, configureHotkeys,
                NewTintedLabel("提亮暗部", 42, 561), styleShadows, styleShadowValue,
                NewTintedLabel("压低高光", 250, 561), styleHighlights, styleHighlightValue,
                NewTintedLabel("中间调", 458, 561), styleMidtones, styleMidtoneValue,
                NewTintedLabel("黑位", 42, 627), styleBlackPoint, styleBlackPointValue,
                NewTintedLabel("白位", 250, 627), styleWhitePoint, styleWhitePointValue,
                NewTintedLabel("中间对比", 458, 627), styleContrastPivot, styleContrastPivotValue,
                NewTintedLabel("色温", 42, 693), styleTemperature, styleTemperatureValue,
                NewTintedLabel("色调", 250, 693), styleTint, styleTintValue,
                NewTintedLabel("自然色彩", 458, 693), styleVibrance, styleVibranceValue,
                NewTintedLabel("整体曝光", 42, 759), styleExposure, styleExposureValue,
                NewTintedLabel("暗部范围", 356, 759), styleShadowRange, styleShadowRangeValue,
                NewTintedLabel("高光范围", 42, 825), styleHighlightRange, styleHighlightRangeValue,
                NewTintedLabel("过渡柔和", 356, 825), styleTransitionSoftness, styleTransitionSoftnessValue,
                styleRiskStatus, optimizeStyle,
                restore, restoreOnExit, curvePreview, restoreStyle,
                NewSoftMutedLabel("后台快捷键可自由分配；最小化后仍可切换画面风格", 42, 1064),
                status, authorFooter, sharingFooter
            });
            // Put each decorative panel behind its controls, then place the
            // unified white surface behind all nested panels.
            styleCard.SendToBack();
            actionCard.SendToBack();
            mainCard.SendToBack();
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
                manageStyles, configureHotkeys, curvePreview, restoreStyle, optimizeStyle);
            ConfigureToolTips(
                refresh, apply, restore, restoreStyle,
                applyStyle, saveStyle, manageStyles, configureHotkeys, curvePreview, optimizeStyle);
            ApplyTheme(ThemeManager.Current);
            UpdateStyleRiskStatus();
            themeBox.SelectedItem = ThemeManager.Current;
            themeBox.SelectedIndexChanged += delegate {
                var selectedTheme = themeBox.SelectedItem as AppTheme;
                if (selectedTheme == null) return;
                ThemeManager.Set(selectedTheme);
                ApplyTheme(selectedTheme);
                UpdateStyleRiskStatus();
            };

            FormClosing += OnFormClosing;
            Resize += OnWindowResize;
            Shown += delegate {
                RegisterGlobalHotkeys();
                if (appSettings.StartMinimized)
                    BeginInvoke((MethodInvoker)delegate { WindowState = FormWindowState.Minimized; });
            };
            HandleCreated += delegate {
                if (hotkeysRegistered)
                    BeginInvoke((MethodInvoker)delegate { RegisterGlobalHotkeys(); });
            };
            ConfigureTrayIcon();
            recoveryTimer.Interval = 1400;
            recoveryTimer.Tick += delegate {
                recoveryTimer.Stop();
                if (closing || !appSettings.AutoReapply) return;
                RefreshDisplays();
                if (gammaController.IsCaptured) ApplyStylePreview();
            };
            SystemEvents.DisplaySettingsChanged += OnDisplayEnvironmentChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            RefreshDisplays();
        }

        private void ConfigureToolTips(
            Button refresh, Button apply, Button restore, Button restoreStyle,
            Button applyStyle, Button saveStyle, Button manageStyles,
            Button configureHotkeys, Button curvePreview, Button optimizeStyle)
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
            SetHelp(saturation, "调节显示器提供的硬件饱和度。仅在显示器支持 VCP 0x8A 时可用。");
            SetHelp(liveApply, "开启后，拖动硬件滑块会在短暂防抖后自动写入显示器。");
            SetHelp(apply, "立即写入当前亮度、对比度、硬件饱和度和硬件色温。");
            SetHelp(themeBox, "只改变 ShadePilot 自身的界面配色，不影响游戏画面。");

            SetHelp(stylePresetBox, "选择一套画面风格。点击“应用”或使用分配的全局快捷键切换。");
            SetHelp(strengthBox, "限制画面参数的可调范围：柔和适合日常，标准兼顾效果，极限开放完整范围。");
            SetHelp(applyStyle, "应用当前选中的画面风格预设。");
            SetHelp(saveStyle, "将当前九项画面参数保存为新预设，或覆盖同名预设。");
            SetHelp(manageStyles, "重命名、修改或删除画面风格预设。");
            SetHelp(configureHotkeys, "为每个画面风格和恢复原始画面录入全局快捷键。");
            SetHelp(curvePreview, "查看当前明暗参数合成后的输入/输出曲线，辅助判断压黑或高光压缩程度。");
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
                Text = text, AutoSize = true, Location = new Point(x, y),
                ForeColor = Ink, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                BackColor = Color.White, Padding = new Padding(0, 2, 0, 1)
            };
        }

        private static Label NewSectionTitle(string text, int x, int y)
        {
            return new Label {
                Text = text, AutoSize = true, Location = new Point(x, y),
                ForeColor = Ink, BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                Padding = new Padding(0, 2, 0, 1)
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
                Text = text, AutoSize = true, Location = new Point(x, y),
                ForeColor = Muted, BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 8F),
                Padding = new Padding(0, 2, 0, 1)
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
                button.Tag = button.ForeColor == Color.White ? "PrimaryButton" : "SecondaryButton";
            themeBox.Tag = "CanvasCombo";
            stylePresetBox.Tag = "TintCombo";
            strengthBox.Tag = "TintCombo";
            monitorBox.Tag = "SurfaceCombo";
            liveApply.Tag = "SurfaceMuted";
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
            foreach (Control control in Controls)
            {
                string role = control.Tag as string;
                if (role == null) continue;
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
                    bool primary = role == "PrimaryButton";
                    button.BackColor = primary ? theme.Accent : theme.Button;
                    button.ForeColor = primary ? theme.OnAccent : theme.Ink;
                    button.SurfaceColor =
                        button.Location.Y >= 447 && button.Location.Y < 1001 ? theme.TintSurface :
                        button.Location.Y >= 1013 ? theme.SoftSurface : theme.Surface;
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
                bool muted = role.EndsWith("Muted", StringComparison.Ordinal);
                control.ForeColor = muted ? theme.Muted : theme.Ink;
                if (role.StartsWith("Canvas", StringComparison.Ordinal)) control.BackColor = theme.Canvas;
                else if (role.StartsWith("Tint", StringComparison.Ordinal)) control.BackColor = theme.TintSurface;
                else if (role.StartsWith("Soft", StringComparison.Ordinal)) control.BackColor = theme.SoftSurface;
                else control.BackColor = theme.Surface;
                var themedCombo = control as ThemedComboBox;
                if (themedCombo != null)
                    themedCombo.BorderColor = control.BackColor;
                control.Invalidate();
            }
            status.ForeColor = theme.Muted;
            Invalidate(true);
        }

        private PhysicalDisplay Selected
        {
            get {
                int index = monitorBox.SelectedIndex;
                return index >= 0 && index < displays.Count ? displays[index] : null;
            }
        }

        private void RefreshDisplays()
        {
            foreach (var d in displays) d.Dispose();
            displays.Clear();
            monitorBox.Items.Clear();
            try
            {
                displays.AddRange(MonitorApi.Enumerate());
                foreach (var d in displays) monitorBox.Items.Add(d.Name);
                if (displays.Count > 0)
                {
                    monitorBox.SelectedIndex = 0;
                    SetStatus("已检测到 " + displays.Count + " 个物理显示器。", false);
                }
                else
                {
                    DisableControls();
                    SetStatus("没有发现可通过 Windows DDC/CI 访问的物理显示器。", true);
                }
            }
            catch (Exception ex)
            {
                DisableControls();
                SetStatus("检测失败：" + ex.Message, true);
            }
        }

        private void LoadSelectedMonitor(bool refreshHardware = true)
        {
            var d = Selected;
            if (d == null) return;
            try
            {
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
                saturation.Enabled = d.SupportsSaturation;
                saturation.Visible = d.SupportsSaturation;
                saturationHint.Visible = !d.SupportsSaturation;
                saturation.Minimum = (int)Math.Min(d.SaturationMin, 100);
                saturation.Maximum = (int)Math.Max(d.SaturationMax, d.SaturationMin + 1);
                saturation.Value = Clamp((int)d.SaturationCurrent, saturation.Minimum, saturation.Maximum);
                saturationValue.Text = d.SupportsSaturation ? saturation.Value.ToString() : "";
                saturationValue.Visible = d.SupportsSaturation;

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
                if (d.SupportsSaturation)
                {
                    d.SetSaturation((uint)saturation.Value);
                    results.Add("饱和度");
                    Thread.Sleep(80);
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
                            if (doSaturation && d.SupportsSaturation)
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
                if (!gammaController.IsCaptured) gammaController.CaptureOriginal();
                gammaController.Apply(
                    styleShadows.Value, styleHighlights.Value,
                    styleMidtones.Value, styleBlackPoint.Value, styleWhitePoint.Value,
                    styleContrastPivot.Value, styleTemperature.Value,
                    styleTint.Value, styleVibrance.Value, styleExposure.Value,
                    styleShadowRange.Value, styleHighlightRange.Value,
                    styleTransitionSoftness.Value);
                SetStatus(
                    "画面风格已应用 · 暗部 " + FormatStyleValue(styleShadows.Value, true) +
                    " · 高光 " + FormatStyleValue(styleHighlights.Value, true) +
                    " · 色温 " + FormatStyleValue(styleTemperature.Value, false) +
                    " · 色调 " + FormatStyleValue(styleTint.Value, false), false);
            }
            catch (Exception ex)
            {
                SetStatus("画面风格应用失败：" + ex.Message, true);
            }
        }

        private void RestoreStyle()
        {
            try
            {
                stylePreviewTimer.Stop();
                gammaController.Restore();
                activeStyle = null;
                SetStyleControls(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                SetStatus("已恢复程序启动前的原始画面。", false);
                trayIcon.Text = "ShadePilot · 原始画面";
            }
            catch (Exception ex)
            {
                SetStatus("恢复原始画面失败：" + ex.Message, true);
            }
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
                    hotkeyRegistrationStatus[preset.Name] = "注册失败：可能被其他程序占用";
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
                    hotkeyRegistrationStatus["恢复原始画面"] = "注册失败：可能被其他程序占用";
                }
                else {
                    hotkeysRegistered = true;
                    registeredHotkeyCount++;
                    hotkeyRegistrationStatus["恢复原始画面"] = "已注册，可在后台使用";
                }
            }
            else hotkeyRegistrationStatus["恢复原始画面"] = "未设置";

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
            hotkeyMap.Clear();
            hotkeysRegistered = false;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey)
            {
                int id = message.WParam.ToInt32();
                StylePresetData preset;
                if (hotkeyMap.TryGetValue(id, out preset))
                    ApplyStylePreset(preset);
                else if (id == HotkeyRestore)
                    RestoreStyle();
            }
            base.WndProc(ref message);
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
            recoveryTimer.Stop();
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
            SystemEvents.DisplaySettingsChanged -= OnDisplayEnvironmentChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            pendingBrightness = pendingContrast = pendingSaturation = false;
            if (hotkeysRegistered) UnregisterGlobalHotkeys();
            if (restoreOnExit.Checked)
            {
                try { gammaController.Restore(); } catch { }
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
        }

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

        public ThemedComboBox()
        {
            BorderColor = Color.White;
            FlatStyle = FlatStyle.Flat;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if ((m.Msg != WmPaint && m.Msg != WmNcPaint) || Width < 2 || Height < 2)
                return;

            using (var graphics = Graphics.FromHwnd(Handle))
            using (var pen = new Pen(BorderColor, 2F))
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
            TextRenderer.DrawText(
                e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
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
            // Intentionally consume the wheel input. Accidental scrolling over a
            // focused slider must scroll the page, not change display settings.
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
        }
    }

    internal sealed class CurvePreviewForm : Form
    {
        public CurvePreviewForm(
            int shadows, int highlights, int midtones,
            int blackPoint, int whitePoint, int contrastPivot, int vibrance,
            int exposure, int shadowRange, int highlightRange, int transitionSoftness)
        {
            Text = "当前明暗曲线";
            ClientSize = new Size(520, 430);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(245, 247, 251);
            Font = new Font("Microsoft YaHei UI", 9F);
            Controls.Add(new Label {
                Text = "输入亮度 → 输出亮度", AutoSize = true,
                Location = new Point(28, 22),
                Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
                ForeColor = Color.FromArgb(29, 36, 50)
            });
            Controls.Add(new Label {
                Text = "灰色虚线为原始线；紫色曲线为当前参数结果",
                AutoSize = true, Location = new Point(30, 54),
                ForeColor = Color.FromArgb(104, 113, 130)
            });
            Controls.Add(new ToneCurveGraph {
                Location = new Point(30, 84), Size = new Size(460, 300),
                Shadows = GammaRampController.ScalePositive(shadows, 200),
                Highlights = GammaRampController.ScalePositive(highlights, 200),
                Midtones = GammaRampController.ScaleSigned(midtones),
                BlackPoint = GammaRampController.ScaleSigned(blackPoint),
                WhitePoint = GammaRampController.ScaleSigned(whitePoint),
                ContrastPivot = GammaRampController.ScaleSigned(contrastPivot),
                Vibrance = GammaRampController.ScaleSigned(vibrance),
                Exposure = GammaRampController.ScaleSigned(exposure),
                ShadowRange = GammaRampController.ScaleSigned(shadowRange),
                HighlightRange = GammaRampController.ScaleSigned(highlightRange),
                TransitionSoftness = GammaRampController.ScaleSigned(transitionSoftness)
            });
        }
    }

    internal sealed class ToneCurveGraph : Control
    {
        public double Shadows, Highlights, Midtones, BlackPoint, WhitePoint;
        public double ContrastPivot, Vibrance, Exposure, ShadowRange, HighlightRange, TransitionSoftness;

        public ToneCurveGraph()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var area = new Rectangle(18, 14, Width - 36, Height - 32);
            using (var grid = new Pen(Color.FromArgb(228, 231, 238), 1F))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int x = area.Left + area.Width * i / 4;
                    int y = area.Top + area.Height * i / 4;
                    e.Graphics.DrawLine(grid, x, area.Top, x, area.Bottom);
                    e.Graphics.DrawLine(grid, area.Left, y, area.Right, y);
                }
            }
            using (var original = new Pen(Color.FromArgb(165, 170, 180), 1.5F)) {
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

    internal sealed class GammaRampController
    {
        private GammaRamp original;
        public bool IsCaptured { get; private set; }

        public void CaptureOriginal()
        {
            IntPtr dc = GammaApi.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new InvalidOperationException("无法取得桌面显示设备。");
            try
            {
                original = GammaRamp.Create();
                if (!GammaApi.GetDeviceGammaRamp(dc, ref original))
                    throw new InvalidOperationException("显卡驱动不支持读取 Gamma Ramp。");
                IsCaptured = true;
            }
            finally { GammaApi.ReleaseDC(IntPtr.Zero, dc); }
        }

        public void Apply(
            int shadowPercent, int highlightPercent, int midtoneValue,
            int blackPointValue, int whitePointValue, int contrastPivotValue,
            int temperatureValue, int tintValue, int vibranceValue,
            int exposureValue, int shadowRangeValue,
            int highlightRangeValue, int transitionSoftnessValue)
        {
            if (!IsCaptured) CaptureOriginal();
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
            if (!IsCaptured) return;
            SetRamp(original);
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

        private static void SetAndVerify(GammaRamp ramp)
        {
            SetRamp(ramp);
            IntPtr dc = GammaApi.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new InvalidOperationException("无法回读桌面 Gamma。");
            try
            {
                GammaRamp actual = GammaRamp.Create();
                if (!GammaApi.GetDeviceGammaRamp(dc, ref actual))
                    throw new InvalidOperationException("Gamma 已写入，但显卡驱动拒绝回读确认。");
                int differences = 0;
                for (int i = 0; i < 256; i += 16)
                    if (Math.Abs(actual.Red[i] - ramp.Red[i]) > 512) differences++;
                if (differences > 8)
                    throw new InvalidOperationException("显卡驱动覆盖或忽略了 Gamma 曲线；请确认处于 SDR 模式。");
            }
            finally { GammaApi.ReleaseDC(IntPtr.Zero, dc); }
        }

        private static void SetRamp(GammaRamp ramp)
        {
            IntPtr dc = GammaApi.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) throw new InvalidOperationException("无法取得桌面显示设备。");
            try
            {
                if (!GammaApi.SetDeviceGammaRamp(dc, ref ramp))
                    throw new InvalidOperationException("显卡驱动拒绝设置 Gamma Ramp。");
            }
            finally { GammaApi.ReleaseDC(IntPtr.Zero, dc); }
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
    }

    internal static class GammaApi
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern bool GetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);
        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern bool SetDeviceGammaRamp(IntPtr dc, ref GammaRamp ramp);
    }

    internal sealed class PhysicalDisplay : IDisposable
    {
        private readonly object ioLock = new object();
        public IntPtr Handle;
        public string Name;
        public string Id;
        public uint Capabilities;
        public uint BrightnessMin, BrightnessCurrent, BrightnessMax;
        public uint ContrastMin, ContrastCurrent, ContrastMax;
        public uint SaturationMin, SaturationCurrent, SaturationMax;
        public bool BrightnessUsesLowLevel;
        public bool ContrastUsesLowLevel;
        public bool SaturationUsesLowLevel;
        public string ProbeNote;

        public bool SupportsBrightness { get { return (Capabilities & 0x2) != 0; } }
        public bool SupportsContrast { get { return (Capabilities & 0x4) != 0; } }
        public bool SupportsSaturation { get { return SaturationMax > SaturationMin; } }

        public void Refresh()
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
            if (MonitorApi.GetVCPFeatureAndVCPFeatureReply(
                Handle, 0x8A, out saturationType, out saturationCurrent, out saturationMaximum) &&
                saturationMaximum > 0)
            {
                SaturationMin = 0;
                SaturationCurrent = saturationCurrent;
                SaturationMax = saturationMaximum;
                SaturationUsesLowLevel = true;
            }
            else
            {
                SaturationMin = SaturationCurrent = SaturationMax = 0;
            }

            ProbeNote = capabilityQueryWorked
                ? "Windows 高层能力查询成功"
                : (brightnessRead || contrastRead
                    ? "高层查询失败，已使用低层 DDC/CI 白名单回退"
                    : "高层与低层 DDC/CI 均未返回可用参数");

        }

        public void SetBrightness(uint value)
        {
            lock (ioLock)
            {
                BrightnessCurrent = WriteAndVerifyVcp(
                    0x10, value, BrightnessCurrent, BrightnessMax,
                    delegate(uint v) { return MonitorApi.SetMonitorBrightness(Handle, v); },
                    "亮度");
            }
        }
        public void SetContrast(uint value)
        {
            lock (ioLock)
            {
                ContrastCurrent = WriteAndVerifyVcp(
                    0x12, value, ContrastCurrent, ContrastMax,
                    delegate(uint v) { return MonitorApi.SetMonitorContrast(Handle, v); },
                    "对比度");
            }
        }
        public void SetSaturation(uint value)
        {
            lock (ioLock)
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
            lock (ioLock)
            {
                if (Handle != IntPtr.Zero)
                {
                    MonitorApi.DestroyPhysicalMonitor(Handle);
                    Handle = IntPtr.Zero;
                }
            }
        }
    }

    internal sealed class Snapshot
    {
        public bool HasBrightness, HasContrast, HasSaturation;
        public uint Brightness, Contrast, Saturation;
        public static Snapshot From(PhysicalDisplay d)
        {
            return new Snapshot {
                HasBrightness = d.SupportsBrightness, Brightness = d.BrightnessCurrent,
                HasContrast = d.SupportsContrast, Contrast = d.ContrastCurrent,
                HasSaturation = d.SupportsSaturation, Saturation = d.SaturationCurrent
            };
        }
        public void Apply(PhysicalDisplay d)
        {
            if (HasBrightness && d.SupportsBrightness) { d.SetBrightness(Brightness); Thread.Sleep(80); }
            if (HasContrast && d.SupportsContrast) { d.SetContrast(Contrast); Thread.Sleep(80); }
            if (HasSaturation && d.SupportsSaturation) { d.SetSaturation(Saturation); Thread.Sleep(80); }
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
        [DataMember] public int Exposure;
        [DataMember] public int ShadowRange;
        [DataMember] public int HighlightRange;
        [DataMember] public int TransitionSoftness;
        [DataMember] public int HotkeyKey;
        [DataMember] public uint HotkeyModifiers;
        public override string ToString() { return Name; }
    }

    [DataContract]
    internal sealed class StyleSettingsStore
    {
        [DataMember] public List<StylePresetData> Items = new List<StylePresetData>();
        [DataMember] public int RestoreHotkeyKey;
        [DataMember] public uint RestoreHotkeyModifiers;
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
                        if (loaded.SchemaVersion < 2) loaded.UpgradeCuratedPresets();
                            if (loaded.SchemaVersion < 3) loaded.UpgradeAdvancedControls();
                            if (loaded.SchemaVersion < 4) loaded.RemoveLegacyDefaultHotkeys();
                            if (loaded.SchemaVersion < 5) loaded.UpgradeNewToneControls();
                            if (loaded.SchemaVersion < 6) loaded.UpgradeCurveV2Presets();
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
                SchemaVersion = 6
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
        [DataMember] public bool AutoReapply;
        [DataMember] public int StrengthMode;
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
                        if (loaded.SchemaVersion < 1)
                        {
                            loaded.AutoReapply = true;
                            loaded.StrengthMode = 1;
                            loaded.SchemaVersion = 1;
                            loaded.Save();
                        }
                        return loaded;
                    }
                }
            }
            catch { }
            return new AppSettingsStore { AutoReapply = true, StrengthMode = 1, SchemaVersion = 1 };
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
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 251);
            AutoScaleMode = AutoScaleMode.Dpi;

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

            var save = NewButton("保存修改", 270, 510, 112, true);
            save.Click += delegate { SaveSelected(); };
            var delete = NewButton("删除预设", 394, 510, 112, false);
            delete.Click += delegate { DeleteSelected(); };
            var import = NewButton("导入", 518, 510, 82, false);
            import.Click += delegate { ImportPresets(); };
            var export = NewButton("导出", 610, 510, 82, false);
            export.Click += delegate { ExportPresets(); };
            var done = NewButton("完成", 596, 592, 96, true);
            done.DialogResult = DialogResult.OK;
            status.Location = new Point(270, 562);
            status.Size = new Size(420, 24);
            status.ForeColor = Color.FromArgb(45, 90, 55);
            Controls.AddRange(new Control[] { save, delete, import, export, done, status });
            AcceptButton = done;

            ConfigureToolTips();
            RefreshList(selected);
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
            exposureBox.Value = shadowRangeBox.Value = highlightRangeBox.Value = transitionSoftnessBox.Value = 0;
            status.Text = "当前没有预设，可回到主界面保存一个新的预设。";
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
            item.Name = name;
            item.Shadows = (int)shadowsBox.Value;
            item.Highlights = (int)highlightsBox.Value;
            item.Midtones = (int)midtonesBox.Value;
            item.BlackPoint = (int)blackPointBox.Value;
            item.WhitePoint = (int)whitePointBox.Value;
            item.ContrastPivot = (int)contrastPivotBox.Value;
            item.Temperature = (int)temperatureBox.Value;
            item.Tint = (int)tintBox.Value;
            item.Vibrance = (int)vibranceBox.Value;
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
        private readonly ComboBox targetBox = new ComboBox();
        private readonly TextBox hotkeyInput = new TextBox();
        private readonly Label status = new Label();
        private readonly ToolTip helpTip = new ToolTip();
        private bool loading;
        private int capturedKey;
        private uint capturedModifiers;

        public HotkeySettingsForm(
            StyleSettingsStore store, Dictionary<string, string> registrationStatus)
        {
            this.store = store;
            this.registrationStatus = registrationStatus;
            Text = "快捷键设置";
            ClientSize = new Size(480, 280);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(245, 247, 251);
            AutoScaleMode = AutoScaleMode.Dpi;

            var title = new Label {
                Text = "自定义后台快捷键", AutoSize = true, Location = new Point(24, 20),
                Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(29, 36, 50)
            };
            var note = new Label {
                Text = "先选择功能，再点击录入框并直接按下快捷键；Backspace 或 Delete 可清除。",
                AutoSize = true, Location = new Point(26, 54),
                ForeColor = Color.FromArgb(104, 113, 130)
            };
            targetBox.DropDownStyle = ComboBoxStyle.DropDownList;
            targetBox.FlatStyle = FlatStyle.Flat;
            targetBox.Location = new Point(28, 94);
            targetBox.Size = new Size(420, 28);
            foreach (var preset in store.Items) targetBox.Items.Add(new HotkeyTarget(preset));
            targetBox.Items.Add(new HotkeyTarget(null));
            targetBox.SelectedIndexChanged += delegate { LoadTarget(); };

            hotkeyInput.Location = new Point(28, 141);
            hotkeyInput.Size = new Size(420, 30);
            hotkeyInput.ReadOnly = true;
            hotkeyInput.ShortcutsEnabled = false;
            hotkeyInput.TextAlign = HorizontalAlignment.Center;
            hotkeyInput.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            hotkeyInput.BackColor = Color.White;
            hotkeyInput.ForeColor = Color.FromArgb(29, 36, 50);
            hotkeyInput.KeyDown += CaptureHotkey;
            hotkeyInput.Enter += delegate {
                status.ForeColor = Color.FromArgb(76, 99, 220);
                status.Text = "现在按下需要的快捷键组合。";
            };

            var save = NewDialogButton("保存此绑定", 28, 192, 130, true);
            save.Click += delegate { SaveTarget(); };
            var done = NewDialogButton("完成", 348, 192, 100, true);
            done.DialogResult = DialogResult.OK;
            status.Location = new Point(28, 238);
            status.Size = new Size(420, 24);
            status.ForeColor = Color.FromArgb(45, 90, 55);

            Controls.AddRange(new Control[] { title, note, targetBox, hotkeyInput, save, done, status });
            helpTip.IsBalloon = true;
            helpTip.InitialDelay = 500;
            helpTip.AutoPopDelay = 9000;
            helpTip.ShowAlways = true;
            helpTip.SetToolTip(targetBox, "选择需要绑定快捷键的画面风格或恢复操作。");
            helpTip.SetToolTip(hotkeyInput, "点击后直接按下快捷键组合；按 Backspace 或 Delete 清除。");
            AcceptButton = done;
            if (targetBox.Items.Count > 0) targetBox.SelectedIndex = 0;
        }

        private void LoadTarget()
        {
            var target = targetBox.SelectedItem as HotkeyTarget;
            if (target == null) return;
            loading = true;
            uint modifiers = target.Preset == null ? store.RestoreHotkeyModifiers : target.Preset.HotkeyModifiers;
            int key = target.Preset == null ? store.RestoreHotkeyKey : target.Preset.HotkeyKey;
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
            string name = target.Preset == null ? "恢复原始画面" : target.Preset.Name;
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

        private void SaveTarget()
        {
            if (loading) return;
            var target = targetBox.SelectedItem as HotkeyTarget;
            if (target == null) return;
            if (target.Preset == null)
            {
                store.RestoreHotkeyModifiers = capturedModifiers;
                store.RestoreHotkeyKey = capturedKey;
            }
            else
            {
                target.Preset.HotkeyModifiers = capturedModifiers;
                target.Preset.HotkeyKey = capturedKey;
            }
            status.Text = capturedKey == 0 ? "已取消该功能的快捷键。" : "绑定已更新。";
            store.Save();
            if (BindingSaved != null) BindingSaved(this, EventArgs.Empty);
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

        private sealed class HotkeyTarget
        {
            public readonly StylePresetData Preset;
            public HotkeyTarget(StylePresetData preset) { Preset = preset; }
            public override string ToString() { return Preset == null ? "恢复原始画面" : Preset.Name; }
        }

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
