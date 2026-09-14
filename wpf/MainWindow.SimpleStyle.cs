using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DisplayPresetPrototype;
using Thickness=ShadePilotWpf.UiThickness;

namespace ShadePilotWpf
{
    public partial class MainWindow
    {
        readonly Dictionary<SimpleAdjustmentControl, Slider> simpleSliders = new();
        readonly Dictionary<SimpleAdjustmentControl, TextBlock> simpleValueLabels = new();
        readonly DispatcherTimer simpleApplyTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
        readonly DispatcherTimer simpleSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
        SimpleAdjustmentState simpleState = SimpleAdjustmentState.Neutral;
        StylePresetData simpleBaseState, pendingSimpleState;
        Border simpleStyleCard, advancedStyleCard, curveStyleCard;
        TextBlock simpleStatus;
        Button simpleModeButton, advancedModeButton;
        ComboBox simplePresetBox;
        bool syncingSimplePresetBox;
        bool simpleUiReady, simpleUpdating, simpleCustom, simpleInitialSelectionPending = true;

        void InitializeSimpleStyleUi()
        {
            var root = (StackPanel)StylePage.Content;
            advancedStyleCard = (Border)root.Children[2];
            curveStyleCard = ((curve?.Parent as Canvas)?.Parent as Grid)?.Parent as Border;

            var modeBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };
            simpleModeButton = new Button { Content = "简约调整", MinWidth = 120, Margin = new Thickness(0, 0, 6, 0) };
            advancedModeButton = new Button { Content = "深度调整", MinWidth = 120 };
            simpleModeButton.Click += (_,__) => SetStyleMode(true, true);
            advancedModeButton.Click += (_,__) => SetStyleMode(false, true);
            modeBar.Children.Add(simpleModeButton);
            modeBar.Children.Add(advancedModeButton);
            root.Children.Insert(2, modeBar);

            simpleStyleCard = BuildSimpleStyleCard();
            root.Children.Insert(3, simpleStyleCard);
            simpleUiReady = true;
            InitializeAdvancedRealtimeApply();
            PresetBox.SelectionChanged += (_,__) => SyncSimplePresetBox();

            simpleState = new SimpleAdjustmentState
            {
                Brightness = Math.Clamp(settings.SimpleBrightness, -100, 100),
                DarkArea = Math.Clamp(settings.SimpleDarkArea, -100, 100),
                Clarity = Math.Clamp(settings.SimpleClarity == 0 ? 50 : settings.SimpleClarity, 0, 100),
                Color = Math.Clamp(settings.SimpleColor, -100, 100),
                TonalDepth = Math.Clamp(settings.SimpleTonalDepth, -100, 100),
                Temperature = Math.Clamp(settings.SimpleTemperature, -100, 100)
            };
            SetSimpleStateControls();
            Closing += (_,__) => SaveSimpleSettingsOnExit();
            simpleApplyTimer.Tick += async (_,__) =>
            {
                simpleApplyTimer.Stop();
                var state = pendingSimpleState?.DeepClone();
                pendingSimpleState = null;
                if (state != null && sessionReady)
                    await ApplyPresetAsync(state);
            };
            simpleSaveTimer.Tick += (_,__) =>
            {
                simpleSaveTimer.Stop();
                SaveSimpleSettingsNow();
            };
            SetStyleMode(settings.SimpleStyleMode, false);
        }

        Border BuildSimpleStyleCard()
        {
            var card = new Border { Style = (Style)FindResource("LPCard") };
            card.SetResourceReference(Border.BackgroundProperty, "Tint");
            var panel = new StackPanel();
            var heading = Dynamic(new TextBlock
            {
                Text = "简约调整",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 3)
            }, TextBlock.ForegroundProperty, "Accent");
            panel.Children.Add(heading);
            panel.Children.Add(Dynamic(new TextBlock
            {
                Text = "用容易理解的方式调整画面感受，修改会实时预览。",
                Margin = new Thickness(0, 0, 0, 14)
            }, TextBlock.ForegroundProperty, "Muted"));

            panel.Children.Add(Dynamic(new TextBlock
            {
                Text = "当前风格预设",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 7)
            }, TextBlock.ForegroundProperty, "Text"));
            var savedPresetRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            savedPresetRow.ColumnDefinitions.Add(new ColumnDefinition());
            savedPresetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            savedPresetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            simplePresetBox = new ComboBox { Style = (Style)FindResource("LPCombo"), MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Stretch };
            simplePresetBox.SelectionChanged += SimplePresetBox_SelectionChanged;
            savedPresetRow.Children.Add(simplePresetBox);
            var applyPreset = new Button { Content = "应用预设", Style = (Style)FindResource("LPPrimary"), MinWidth = 92, Margin = new Thickness(8, 0, 0, 0) };
            applyPreset.Click += ApplyStyle_Click;
            Grid.SetColumn(applyPreset, 1);
            savedPresetRow.Children.Add(applyPreset);
            var savePreset = new Button { Content = "保存当前", Style = (Style)FindResource("LPSecondary"), MinWidth = 92, Margin = new Thickness(8, 0, 0, 0) };
            savePreset.Click += (_,__) => SaveStyle();
            Grid.SetColumn(savePreset, 2);
            savedPresetRow.Children.Add(savePreset);
            panel.Children.Add(savedPresetRow);

            var presetLabel = Dynamic(new TextBlock
            {
                Text = "风格起点",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 7)
            }, TextBlock.ForegroundProperty, "Text");
            panel.Children.Add(presetLabel);
            var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            foreach (var preset in new[] { "自然", "明亮", "暗部增强", "鲜艳", "清晰", "夜间" })
            {
                var button = new Button
                {
                    Content = preset,
                    Style = (Style)FindResource("LPSecondary"),
                    MinWidth = 82,
                    Margin = new Thickness(0, 0, 8, 6)
                };
                button.Click += (_,__) => ApplySimplePreset(preset);
                presets.Children.Add(button);
            }
            panel.Children.Add(presets);

            var grid = new UniformGrid { Columns = 2, HorizontalAlignment = HorizontalAlignment.Stretch };
            AddSimpleSlider(grid, SimpleAdjustmentControl.Brightness, "亮度", "让整体画面更亮或更暗", -100, 100, simpleState.Brightness);
            AddSimpleSlider(grid, SimpleAdjustmentControl.DarkArea, "暗部", "让阴影区域更容易看清", -100, 100, simpleState.DarkArea);
            AddSimpleSlider(grid, SimpleAdjustmentControl.Clarity, "清晰度", "增强画面的边缘层次（不接入锐化）", 0, 100, simpleState.Clarity);
            AddSimpleSlider(grid, SimpleAdjustmentControl.TonalDepth, "画面层次", "调整明暗对比和通透感", -100, 100, simpleState.TonalDepth);
            AddSimpleSlider(grid, SimpleAdjustmentControl.Color, "色彩", "自然或更鲜艳", -100, 100, simpleState.Color);
            AddSimpleSlider(grid, SimpleAdjustmentControl.Temperature, "色温", "冷色或暖色", -100, 100, simpleState.Temperature);
            panel.Children.Add(grid);

            var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var reset = new Button { Content = "恢复默认", Style = (Style)FindResource("LPSecondary"), MinWidth = 96 };
            reset.Click += (_,__) => { ResetStyle(); ResetSimpleState(); };
            DockPanel.SetDock(reset, Dock.Right);
            footer.Children.Add(reset);
            simpleStatus = Dynamic(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "Muted");
            footer.Children.Add(simpleStatus);
            panel.Children.Add(footer);
            card.Child = panel;
            return card;
        }

        void SyncSimplePresetBox()
        {
            if (simplePresetBox == null) return;
            syncingSimplePresetBox = true;
            try
            {
                var selected = SelectedPreset;
                simplePresetBox.Items.Clear();
                foreach (var preset in styles.Items)
                    simplePresetBox.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset });
                if (selected != null)
                    foreach (ComboBoxItem item in simplePresetBox.Items)
                        if (ReferenceEquals(item.Tag, selected)) { simplePresetBox.SelectedItem = item; break; }
                if (simplePresetBox.SelectedIndex < 0 && simplePresetBox.Items.Count > 0)
                    simplePresetBox.SelectedIndex = 0;
            }
            finally { syncingSimplePresetBox = false; }
        }

        void SimplePresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (syncingSimplePresetBox || simplePresetBox?.SelectedItem is not ComboBoxItem selected || selected.Tag is not StylePresetData preset) return;
            syncingSimplePresetBox = true;
            try
            {
                foreach (ComboBoxItem item in PresetBox.Items)
                    if (ReferenceEquals(item.Tag, preset)) { PresetBox.SelectedItem = item; break; }
            }
            finally { syncingSimplePresetBox = false; }
        }

        void AddSimpleSlider(Panel parent, SimpleAdjustmentControl control, string label, string description, double minimum, double maximum, double initial)
        {
            var box = new Border
            {
                Style = (Style)FindResource("LPCard"),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 8, 8)
            };
            var panel = new StackPanel();
            var header = new DockPanel();
            var title = Dynamic(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold }, TextBlock.ForegroundProperty, "Text");
            DockPanel.SetDock(title, Dock.Left);
            header.Children.Add(title);
            var value = Dynamic(new TextBlock { TextAlignment = TextAlignment.Right }, TextBlock.ForegroundProperty, "Muted");
            DockPanel.SetDock(value, Dock.Right);
            header.Children.Add(value);
            panel.Children.Add(header);
            panel.Children.Add(Dynamic(new TextBlock { Text = description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 4) }, TextBlock.ForegroundProperty, "Muted"));
            var slider = new Slider
            {
                Minimum = minimum,
                Maximum = maximum,
                Value = initial,
                SmallChange = 1,
                LargeChange = 10,
                Style = (Style)FindResource("LPSlider")
            };
            slider.ValueChanged += (_,__) => SimpleSliderChanged(control);
            panel.Children.Add(slider);
            box.Child = panel;
            parent.Children.Add(box);
            simpleSliders[control] = slider;
            simpleValueLabels[control] = value;
        }

        void SetStyleMode(bool simple, bool persist)
        {
            if (!simpleUiReady) return;
            settings.SimpleStyleMode = simple;
            if (persist) settings.Save();
            simpleStyleCard.Visibility = simple ? Visibility.Visible : Visibility.Collapsed;
            advancedStyleCard.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
            if (riskStatus != null) riskStatus.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
            if (curveStyleCard != null) curveStyleCard.Visibility = simple ? Visibility.Collapsed : Visibility.Visible;
            UpdateSimpleModeVisual();
        }

        void UpdateSimpleModeVisual()
        {
            if (simpleModeButton == null) return;
            simpleModeButton.Style = (Style)FindResource(settings.SimpleStyleMode ? "LPPrimary" : "LPSecondary");
            advancedModeButton.Style = (Style)FindResource(settings.SimpleStyleMode ? "LPSecondary" : "LPPrimary");
        }

        void SimpleSliderChanged(SimpleAdjustmentControl control)
        {
            if (simpleUpdating || loading || !simpleUiReady) return;
            simpleState = ReadSimpleState();
            simpleCustom = false;
            ApplySimpleDraft();
            ScheduleSimpleSettingsSave();
        }

        SimpleAdjustmentState ReadSimpleState() => new SimpleAdjustmentState
        {
            Brightness = (int)Math.Round(simpleSliders[SimpleAdjustmentControl.Brightness].Value),
            DarkArea = (int)Math.Round(simpleSliders[SimpleAdjustmentControl.DarkArea].Value),
            Clarity = (int)Math.Round(simpleSliders[SimpleAdjustmentControl.Clarity].Value),
            Color = (int)Math.Round(simpleSliders[SimpleAdjustmentControl.Color].Value),
            TonalDepth = (int)Math.Round(simpleSliders[SimpleAdjustmentControl.TonalDepth].Value),
            Temperature = (int)Math.Round(simpleSliders[SimpleAdjustmentControl.Temperature].Value)
        };

        void SetSimpleStateControls()
        {
            if (!simpleUiReady) return;
            simpleUpdating = true;
            simpleSliders[SimpleAdjustmentControl.Brightness].Value = simpleState.Brightness;
            simpleSliders[SimpleAdjustmentControl.DarkArea].Value = simpleState.DarkArea;
            simpleSliders[SimpleAdjustmentControl.Clarity].Value = simpleState.Clarity;
            simpleSliders[SimpleAdjustmentControl.Color].Value = simpleState.Color;
            simpleSliders[SimpleAdjustmentControl.TonalDepth].Value = simpleState.TonalDepth;
            simpleSliders[SimpleAdjustmentControl.Temperature].Value = simpleState.Temperature;
            simpleUpdating = false;
            UpdateSimpleValueLabels();
            UpdateSimpleStatus();
        }

        void UpdateSimpleValueLabels()
        {
            foreach (var pair in simpleValueLabels)
            {
                var value = (int)Math.Round(simpleSliders[pair.Key].Value);
                pair.Value.Text = pair.Key == SimpleAdjustmentControl.Clarity ? value + "/100" : (value > 0 ? "+" : "") + value.ToString();
            }
        }

        void ApplySimpleDraft()
        {
            if (simpleBaseState == null)
                simpleBaseState = (runtimeState ?? ReadStyleControls("简约基准")).DeepClone();
            var mapped = SimpleAdjustmentMapper.Apply(simpleState, simpleBaseState);
            runtimeState = mapped.DeepClone();
            SetControls(mapped);
            pendingSimpleState = mapped.DeepClone();
            simpleApplyTimer.Stop();
            simpleApplyTimer.Start();
            UpdateSimpleValueLabels();
            UpdateSimpleStatus();
        }

        void ApplySimplePreset(string name)
        {
            simpleState = name switch
            {
                "明亮" => new SimpleAdjustmentState { Brightness = 28, Clarity = 54 },
                "暗部增强" => new SimpleAdjustmentState { Brightness = 5, DarkArea = 35, Clarity = 54, TonalDepth = 6 },
                "鲜艳" => new SimpleAdjustmentState { Color = 35, Clarity = 52 },
                "清晰" => new SimpleAdjustmentState { Clarity = 68, TonalDepth = 10 },
                "夜间" => new SimpleAdjustmentState { Brightness = -10, DarkArea = 45, Clarity = 58, Color = -4, TonalDepth = -10, Temperature = -14 },
                _ => SimpleAdjustmentState.Neutral
            };
            simpleCustom = false;
            SetSimpleStateControls();
            ApplySimpleDraft();
            ScheduleSimpleSettingsSave();
        }

        void ResetSimpleAdjustments()
        {
            simpleState = SimpleAdjustmentState.Neutral;
            simpleCustom = false;
            SetSimpleStateControls();
            if (simpleBaseState == null)
                simpleBaseState = (runtimeState ?? ReadStyleControls("简约基准")).DeepClone();
            ApplySimpleDraft();
            ScheduleSimpleSettingsSave();
        }

        void ResetSimpleBaseFromPreset()
        {
            if (!simpleUiReady) return;
            simpleBaseState = (runtimeState ?? SelectedPreset)?.DeepClone();
            if (simpleInitialSelectionPending)
            {
                simpleInitialSelectionPending = false;
                UpdateSimpleStatus();
                return;
            }
            simpleState = SimpleAdjustmentState.Neutral;
            simpleCustom = false;
            SetSimpleStateControls();
            SaveSimpleSettingsNow();
        }

        void OnAdvancedStyleChanged()
        {
            if (!simpleUiReady || simpleUpdating || runtimeState == null) return;
            simpleBaseState = runtimeState.DeepClone();
            simpleState = SimpleAdjustmentState.Neutral;
            simpleCustom = true;
            SetSimpleStateControls();
            UpdateSimpleStatus();
            QueueAdvancedStyleApply();
        }

        void UpdateSimpleStatus()
        {
            if (simpleStatus == null) return;
            simpleStatus.Text = simpleCustom ? "深度调整已改变当前曲线，简约参数显示为自定义。" : "当前修改会实时预览；点击“保存当前”后才写入风格预设。";
        }

        void ScheduleSimpleSettingsSave()
        {
            simpleSaveTimer.Stop();
            simpleSaveTimer.Start();
        }

        void SaveSimpleSettingsNow()
        {
            if (!simpleUiReady) return;
            simpleState = ReadSimpleState();
            settings.SimpleBrightness = simpleState.Brightness;
            settings.SimpleDarkArea = simpleState.DarkArea;
            settings.SimpleClarity = simpleState.Clarity;
            settings.SimpleColor = simpleState.Color;
            settings.SimpleTonalDepth = simpleState.TonalDepth;
            settings.SimpleTemperature = simpleState.Temperature;
            settings.Save();
        }

        void SaveSimpleSettingsOnExit() => SaveSimpleSettingsNow();

        void ResetSimpleState()
        {
            simpleState = SimpleAdjustmentState.Neutral;
            simpleCustom = false;
            simpleBaseState = runtimeState?.DeepClone();
            SetSimpleStateControls();
            SaveSimpleSettingsNow();
        }
    }
}
