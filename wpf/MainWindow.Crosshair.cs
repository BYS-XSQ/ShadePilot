using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ShadePilotWpf
{
    public partial class MainWindow
    {
        // Crosshair is optional. Do not construct it while MainWindow XAML is loading:
        // a failure in this feature must not prevent display/style/tool pages from starting.
        readonly CrosshairManager crosshair = new();
        CrosshairVisual crosshairAdvancedPreview;
        Canvas crosshairDocumentVisualRoot;
        Line advancedGuideHorizontal, advancedGuideVertical;
        bool crosshairUiLoading = true;
        bool crosshairUiInitialized;
        bool crosshairActivationFailed;
        bool crosshairRefreshPending;
        bool crosshairAdvancedMode;
        bool crosshairBasicColorDirty;
        Thumb crosshairSelectionThumb;
        string crosshairSelectedElementId;
        // IMPORTANT: Structural Element Drag is intentionally isolated from
        // Screen Position Drag. It only edits advanced local element geometry.
        string structuralDraggedElementId;
        bool structuralElementDragging;
        bool structuralElementDragMoved;
        CrosshairDocument structuralDragStartDocument;
        CrosshairDocument advancedStructuralDraft;
        CrosshairDocument advancedEntrySnapshot;
        bool advancedDirty;
        bool crosshairCreateMode;
        bool crosshairCreateNameUserEdited;
        string crosshairCreateTemplateKey;
        double advancedPreviewZoom = 1;
        TextBox crosshairHotkeyRecordingBox;
        string crosshairHotkeyRecordingOriginal;

        static readonly (string Label, string Key, Func<CrosshairDocument> Factory)[] CrosshairTemplates =
        {
            ("小绿点", "Dot", Templates.Dot),
            ("白色十字", "Cross", Templates.Cross),
            ("十字 + 中心点", "CrossDot", Templates.CrossDot),
            ("红色圆环", "Ring", Templates.Ring),
            ("T 形", "T", Templates.T),
            ("Chevron", "Chevron", Templates.Chevron)
        };

        void InitializeCrosshairUi()
        {
            if (crosshairUiInitialized || crosshairActivationFailed) return;
            crosshairUiLoading = true;
            crosshairAdvancedPreview = new CrosshairVisual(id => new CrosshairAssetStore().PathFor(id)) { FixedDocumentOrigin = true };
            crosshairDocumentVisualRoot = new Canvas { Width = CrosshairAdvancedPreviewCanvas.ActualWidth, Height = CrosshairAdvancedPreviewCanvas.ActualHeight };
            CrosshairAdvancedPreviewCanvas.Children.Add(crosshairDocumentVisualRoot);
            crosshairDocumentVisualRoot.Children.Add(crosshairAdvancedPreview);
            AddGuides(CrosshairAdvancedPreviewCanvas, out advancedGuideHorizontal, out advancedGuideVertical);
            crosshairSelectionThumb = new Thumb
            {
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 255, 170, 72)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.SizeAll,
                Visibility = Visibility.Collapsed,
                Focusable = false,
                Template = CreateSelectionThumbTemplate()
            };
            crosshairSelectionThumb.DragStarted += OnStructuralElementDragStarted;
            crosshairSelectionThumb.DragDelta += OnStructuralElementDragDelta;
            crosshairSelectionThumb.DragCompleted += OnStructuralElementDragCompleted;
            Canvas.SetZIndex(crosshairSelectionThumb, 1000);
            crosshairDocumentVisualRoot.Children.Add(crosshairSelectionThumb);
            CrosshairAdvancedPreviewCanvas.SizeChanged += (_,__) => { UpdateAdvancedPreviewViewport(); UpdateGuideLines(); UpdateSelectionThumb(); };
            CrosshairEnabled.Checked += (_,__) => { if (!crosshairUiLoading) crosshair.SetEnabled(true); };
            CrosshairEnabled.Unchecked += (_,__) => { if (!crosshairUiLoading) crosshair.SetEnabled(false); };
            CrosshairPresetBox.SelectionChanged += (_,__) =>
            {
                if (crosshairUiLoading || CrosshairPresetBox.SelectedItem is not ComboBoxItem item || item.Tag is not CrosshairPreset p) return;
                crosshair.SelectPreset(p.Id);
                if (crosshairAdvancedMode && !crosshairCreateMode) BeginCrosshairEditEditor();
            };
            CrosshairMonitorBox.SelectionChanged += (_,__) => { if (!crosshairUiLoading) crosshair.SelectMonitor(CrosshairMonitorBox.SelectedItem as string); };
            CrosshairTemplateBox.SelectionChanged += (_,__) => { if (!crosshairUiLoading) CrosshairTemplate_SelectionChanged(); };
            CrosshairElements.SelectionChanged += (_,__) =>
            {
                if (CrosshairElements.SelectedItem is CrosshairElementItem item)
                    crosshairSelectedElementId = item.Value.Id;
                LoadCrosshairInspector();
                UpdateCrosshairSelection();
            };
            CrosshairPresetList.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(CrosshairPresetCycleToggleChanged));
            CrosshairPresetList.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(CrosshairPresetCycleToggleChanged));
            CrosshairIncludeCycle.Checked += (_,__) => { if (!crosshairUiLoading) crosshair.SetIncludeInCycle(true); };
            CrosshairIncludeCycle.Unchecked += (_,__) => { if (!crosshairUiLoading) crosshair.SetIncludeInCycle(false); };
            CrosshairAdvancedPreviewGuide.Checked += (_,__) => UpdateGuideLines();
            CrosshairAdvancedPreviewGuide.Unchecked += (_,__) => UpdateGuideLines();
            CrosshairAdvancedPreviewBackgroundBox.SelectionChanged += (_,__) => ApplyPreviewPreferences();
            CrosshairAdvancedPreviewBackgroundBox.ItemsSource = new[] { "深色", "浅色", "灰色" };
            CrosshairAdvancedPreviewBackgroundBox.SelectedIndex = 0;
            // PreviewZoom uses one internal unit everywhere: 1.0 = 100%.
            // The initial XAML values are normalized here before any live handler is enabled.
            CrosshairAdvancedPreviewZoomSlider.Minimum = .25;
            CrosshairAdvancedPreviewZoomSlider.Maximum = 4.0;
            CrosshairAdvancedPreviewZoomSlider.SmallChange = .05;
            CrosshairAdvancedPreviewZoomSlider.LargeChange = .1;
            SetAdvancedPreviewZoom(1);
            CrosshairTemplateBox.Items.Clear(); CrosshairCreateTemplateBox.Items.Clear();
            foreach (var t in CrosshairTemplates)
            {
                CrosshairTemplateBox.Items.Add(new ComboBoxItem { Content = t.Label, Tag = t.Key });
                CrosshairCreateTemplateBox.Items.Add(new ComboBoxItem { Content = t.Label, Tag = t.Key });
            }
            InstallColorPicker(BasicColor, false); InstallColorPicker(CrosshairColor, true);
            WireBasicEditor(); WireAdvancedEditor(); ConfigureBasicParameterGrids(); WireCrosshairHotkeyRecorders();
            PreviewKeyDown += CrosshairPositionKeyDown;
            crosshair.Changed += (_,__) => QueueCrosshairRefresh();
            BeginCrosshairEditEditor();
            crosshairUiInitialized = true;
            crosshairUiLoading = false;
            RefreshCrosshairUi();
        }

        // Feature fault boundary: Crosshair failures are logged and rendered inside its
        // navigation slot. They must never turn into an App/MainWindow startup failure.
        void ActivateCrosshairFeature()
        {
            if (crosshairUiInitialized || crosshairActivationFailed) return;
            StartupLog.Write("Crosshair.Activate.Begin");
            try
            {
                InitializeCrosshairUi();
                if (hwnd != IntPtr.Zero) crosshair.Attach(hwnd);
                RegisterHotkeys();
                StartupLog.Write("Crosshair.Activate.End");
            }
            catch (Exception ex)
            {
                StartupLog.Write("Crosshair.Activate.Failed", ex);
                crosshairActivationFailed = true;
                try { crosshair?.Dispose(); } catch { }
                ShowCrosshairLoadError();
            }
        }

        void SetCrosshairPageActive(bool active)
        {
            if (!crosshairUiInitialized || !active) return;
            // The structure editor now lives directly on the main Crosshair page.
            // Its canvas is an additional preview and must not suspend the real overlay.
            crosshair.Reposition();
        }

        void ShowCrosshairLoadError()
        {
            CrosshairPage.Children.Clear();
            var panel = new StackPanel { Margin = new Thickness(24), MaxWidth = 520 };
            panel.Children.Add(new TextBlock { Text = "准星模块加载失败", FontSize = 25, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = "本次功能未能正常初始化。显示控制、画面风格和工具中心仍可正常使用；请重新启动 ShadePilot 或查看日志。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 18) });
            var back = new Button { Content = "返回显示控制", Width = 130, Height = 34, HorizontalAlignment = HorizontalAlignment.Left };
            back.Click += (_, __) => DisplayNav.IsChecked = true;
            panel.Children.Add(back);
            CrosshairPage.Children.Add(panel);
        }

        void InstallColorPicker(TextBox box, bool advanced)
        {
            if (box?.Parent is not Panel panel) return;
            var index = panel.Children.IndexOf(box);
            if (index < 0) return;
            panel.Children.RemoveAt(index);
            var row = new DockPanel { Margin = box.Margin, LastChildFill = true };
            box.Margin = new Thickness(0);
            var swatch = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BorderBrushProperty, "Border"); swatch.Background = new SolidColorBrush(ParseCrosshairColor(box.Text));
            DockPanel.SetDock(swatch, Dock.Left);
            var button = new Button { Content = "选择颜色", Style = (Style)FindResource("LPSecondary"), Width = 112, Height = 34, Margin = new Thickness(8, 0, 0, 0), Tag = advanced, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(button, Dock.Right);
            button.Click += (_,__) => OpenColorPicker(advanced);
            box.TextChanged += (_,__) => swatch.Background = new SolidColorBrush(ParseCrosshairColor(box.Text));
            row.Children.Add(swatch); row.Children.Add(box); row.Children.Add(button); panel.Children.Insert(index, row);
        }

        void ConfigureBasicParameterGrids()
        {
            BasicOpacityValue.Tag = "compact-value"; BasicOpacityValue.Width = 68; BasicOpacityValue.Padding = new System.Windows.Thickness(5, 2, 5, 2);
            BuildParameterGrid(BasicDotFields, new[] { ParameterCell("大小", BasicSize, "0.## px", BasicSizeValue) }, new[] { ToggleParameter("轮廓", BasicOutline, "粗细", BasicOutlineThickness, "0.## px") });
            BuildParameterGrid(BasicCrossFields,
                new[] { ParameterCell("长度", BasicCrossLength, "0.## px"), ParameterCell("粗细", BasicThickness, "0.## px"), ParameterCell("间距", BasicGap, "0.## px") },
                new[] { ToggleParameter("中心点", BasicCenterPoint, "大小", BasicCenterSize, "0.## px"), ToggleParameter("轮廓", BasicCrossOutline, "粗细", BasicCrossOutlineThickness, "0.## px") });
            BuildParameterGrid(BasicRingFields, new[] { ParameterCell("直径", BasicRingDiameter, "0.## px"), ParameterCell("线宽", BasicRingThickness, "0.## px") }, Array.Empty<UIElement>());
            BuildParameterGrid(BasicChevronFields, new[] { ParameterCell("长度", BasicChevronLength, "0.## px"), ParameterCell("夹角", BasicAngle, "0.##°"), ParameterCell("粗细", BasicChevronThickness, "0.## px") }, Array.Empty<UIElement>());
            UpdateToggleParameter(BasicCenterPoint, BasicCenterSize); UpdateToggleParameter(BasicCrossOutline, BasicCrossOutlineThickness); UpdateToggleParameter(BasicOutline, BasicOutlineThickness);
        }

        void BuildParameterGrid(StackPanel host, UIElement[] regular, UIElement[] groups)
        {
            foreach (var child in host.Children.OfType<UIElement>().ToList()) Detach(child);
            host.Children.Clear(); host.Margin = new Thickness(0, 14, 0, 0);
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
            var all = regular.Concat(groups).ToArray();
            var rows = (int)Math.Ceiling(all.Length / 2d); for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var i = 0; i < all.Length; i++) { var col = i % 2 == 0 ? 0 : 2; var row = i / 2; Grid.SetColumn(all[i], col); Grid.SetRow(all[i], row); grid.Children.Add(all[i]); }
            host.Children.Add(grid);
        }

        UIElement ParameterCell(string label, Slider slider, string format, TextBox input = null)
        {
            Detach(slider); if (input != null) Detach(input); slider.Style = (Style)FindResource("LPSlider"); slider.HorizontalAlignment = HorizontalAlignment.Stretch; slider.Margin = new Thickness(0, 8, 0, 0);
            var title = Dynamic(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "Text");
            var value = input ?? new TextBox { IsReadOnly = true, Width = 68, Height = 28, TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Padding = new System.Windows.Thickness(5, 2, 5, 2), Tag = "compact-value" };
            value.Tag = "compact-value"; value.Width = Math.Max(value.Width, 68); value.Padding = new System.Windows.Thickness(5, 2, 5, 2); if (input == null) value.Style = (Style)FindResource("LPTextBox");
            var head = new DockPanel(); DockPanel.SetDock(value, Dock.Right); head.Children.Add(value); head.Children.Add(title);
            var cell = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; cell.Children.Add(head); cell.Children.Add(slider);
            void update() { value.Text = slider.Value.ToString(format, CultureInfo.CurrentCulture); }
            slider.ValueChanged += (_,__) => update(); update(); return cell;
        }

        UIElement ToggleParameter(string title, CheckBox toggle, string label, Slider slider, string format)
        {
            Detach(toggle); Detach(slider); toggle.Style = (Style)FindResource("LPCheck"); toggle.Margin = new Thickness(0, 0, 0, 2); slider.Style = (Style)FindResource("LPSlider"); slider.Margin = new Thickness(0, 2, 0, 0);
            var value = new TextBox { IsReadOnly = true, Width = 68, Height = 28, TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, Padding = new System.Windows.Thickness(5, 2, 5, 2), Tag = "compact-value", Style = (Style)FindResource("LPTextBox") };
            var row = new DockPanel(); DockPanel.SetDock(value, Dock.Right); row.Children.Add(value); row.Children.Add(Dynamic(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "Muted"));
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 12) }; section.Children.Add(toggle); section.Children.Add(row); section.Children.Add(slider);
            slider.ValueChanged += (_,__) => value.Text = slider.Value.ToString(format, CultureInfo.CurrentCulture); value.Text = slider.Value.ToString(format, CultureInfo.CurrentCulture);
            toggle.Checked += (_,__) => UpdateToggleParameter(toggle, slider); toggle.Unchecked += (_,__) => UpdateToggleParameter(toggle, slider); return section;
        }

        void UpdateToggleParameter(CheckBox toggle, Slider slider) { if (toggle == null || slider == null) return; slider.IsEnabled = toggle.IsChecked == true; slider.Opacity = toggle.IsChecked == true ? 1 : .45; }

        void OpenColorPicker(bool advanced)
        {
            var source = advanced ? CrosshairColor.Text : BasicColor.Text;
            var original = ParseCrosshairColor(source);
            var dialog = new CrosshairColorPickerDialog(this, original);
            void ApplyColor(Color color)
            {
                var value = FormatCrosshairColor(color);
                if (advanced) { CrosshairColor.Text = value; ApplyCrosshairInspector(); }
                else { BasicColor.Text = value; ApplyBasicParameters(); }
            }
            dialog.ColorChanged += ApplyColor;
            if (dialog.ShowDialog() != true) ApplyColor(original);
            else ApplyColor(dialog.SelectedColor);
        }

        static Color ParseCrosshairColor(string value)
        {
            try { var color = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value); return Color.FromRgb(color.R, color.G, color.B); }
            catch { return Colors.White; }
        }

        static string FormatCrosshairColor(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        static string NormalizeCrosshairColor(string value) => FormatCrosshairColor(ParseCrosshairColor(value));

        void QueueCrosshairRefresh()
        {
            if (crosshairRefreshPending) return;
            crosshairRefreshPending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                crosshairRefreshPending = false;
                RefreshCrosshairUi();
            }), System.Windows.Threading.DispatcherPriority.DataBind);
        }

        void WireBasicEditor()
        {
            foreach (var slider in new[] { BasicOpacity, BasicSize, BasicThickness, BasicGap, BasicCrossLength, BasicCenterSize, BasicOutlineThickness, BasicCrossOutlineThickness, BasicRingDiameter, BasicRingThickness, BasicChevronLength, BasicAngle, BasicChevronThickness }) slider.ValueChanged += (_,__) => { if (!crosshairUiLoading) ApplyBasicParameters(); };
            CrosshairGlobalScaleSlider.ValueChanged += (_,__) => { if (crosshairUiLoading) return; CrosshairGlobalScaleValue.Text = FormatGlobalScale(CrosshairGlobalScaleSlider.Value); ApplyBasicParameters(); };
            BasicColor.TextChanged += (_,__) => { if (!crosshairUiLoading) crosshairBasicColorDirty = true; };
            BasicColor.LostFocus += (_,__) => { if (!crosshairUiLoading) ApplyBasicParameters(); };
            BasicOpacityValue.LostFocus += (_,__) => ApplyBasicTextValue(BasicOpacityValue, BasicOpacity);
            BasicSizeValue.LostFocus += (_,__) => ApplyBasicTextValue(BasicSizeValue, BasicSize);
            CrosshairGlobalScaleValue.LostFocus += (_,__) => ApplyGlobalScaleTextValue(CrosshairGlobalScaleValue, CrosshairGlobalScaleSlider, ApplyBasicParameters);
            CrosshairGlobalScaleValue.PreviewKeyDown += (_, e) => { if (e.Key != Key.Enter) return; ApplyGlobalScaleTextValue(CrosshairGlobalScaleValue, CrosshairGlobalScaleSlider, ApplyBasicParameters); e.Handled = true; };
            foreach (var box in new[] { CrosshairOffsetX, CrosshairOffsetY }) box.LostFocus += (_,__) => { if (!crosshairUiLoading) ApplyBasicParameters(); };
            foreach (var check in new[] { BasicOutline, BasicCenterPoint, BasicCrossOutline }) { check.Checked += (_,__) => { if (!crosshairUiLoading) ApplyBasicParameters(); }; check.Unchecked += (_,__) => { if (!crosshairUiLoading) ApplyBasicParameters(); }; }
        }

        void ApplyBasicTextValue(TextBox box, Slider slider)
        {
            if (crosshairUiLoading) return;
            if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
            {
                ApplyBasicParameters();
                return;
            }
            crosshairUiLoading = true;
            try { slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum); }
            finally { crosshairUiLoading = false; }
            ApplyBasicParameters();
        }

        static string FormatGlobalScale(double value) => $"{Math.Clamp(value, .25, 4) * 100:0}%";

        void ApplyGlobalScaleTextValue(TextBox box, Slider slider, Action apply)
        {
            if (crosshairUiLoading) return;
            var text = (box.Text ?? "").Trim().TrimEnd('%');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent))
            {
                crosshairUiLoading = true;
                try
                {
                    slider.Value = Math.Clamp(percent / 100, slider.Minimum, slider.Maximum);
                    box.Text = FormatGlobalScale(slider.Value);
                }
                finally { crosshairUiLoading = false; }
            }
            else box.Text = FormatGlobalScale(slider.Value);
            apply();
        }

        void WireAdvancedEditor()
        {
            foreach (var box in new[] { CrosshairElementName, CrosshairX, CrosshairY, CrosshairRotation, CrosshairOpacity, CrosshairColor, CrosshairOffsetX, CrosshairOffsetY }) box.LostFocus += (_,__) => { if (!crosshairUiLoading) ApplyCrosshairInspector(); };
            CrosshairEditorGlobalScaleSlider.ValueChanged += (_,__) => { if (crosshairUiLoading) return; CrosshairEditorGlobalScaleValue.Text = FormatGlobalScale(CrosshairEditorGlobalScaleSlider.Value); ApplyCrosshairInspector(); };
            CrosshairEditorGlobalScaleValue.LostFocus += (_,__) => ApplyGlobalScaleTextValue(CrosshairEditorGlobalScaleValue, CrosshairEditorGlobalScaleSlider, ApplyCrosshairInspector);
            CrosshairEditorGlobalScaleValue.PreviewKeyDown += (_, e) => { if (e.Key != Key.Enter) return; ApplyGlobalScaleTextValue(CrosshairEditorGlobalScaleValue, CrosshairEditorGlobalScaleSlider, ApplyCrosshairInspector); e.Handled = true; };
            CrosshairElementScaleSlider.ValueChanged += (_,__) => { if (crosshairUiLoading) return; CrosshairElementScaleValue.Text = FormatGlobalScale(CrosshairElementScaleSlider.Value); ApplyCrosshairInspector(); };
            CrosshairElementScaleValue.LostFocus += (_,__) => ApplyGlobalScaleTextValue(CrosshairElementScaleValue, CrosshairElementScaleSlider, ApplyCrosshairInspector);
            CrosshairElementScaleValue.PreviewKeyDown += (_, e) => { if (e.Key != Key.Enter) return; ApplyGlobalScaleTextValue(CrosshairElementScaleValue, CrosshairElementScaleSlider, ApplyCrosshairInspector); e.Handled = true; };
            foreach (var check in new[] { CrosshairElementEnabled, CrosshairMirrorX, CrosshairMirrorY }) { check.Checked += (_,__) => { if (!crosshairUiLoading) ApplyCrosshairInspector(); }; check.Unchecked += (_,__) => { if (!crosshairUiLoading) ApplyCrosshairInspector(); }; }
        }

        void SetCrosshairMode(bool advanced)
        {
            if (advanced && !crosshairAdvancedMode)
            {
                if (advancedStructuralDraft == null)
                {
                    advancedEntrySnapshot = Json.Clone(crosshair.Draft);
                    advancedStructuralDraft = Json.Clone(advancedEntrySnapshot);
                }
                advancedDirty = false;
                SetAdvancedPreviewZoom(1);
            }
            else if (!advanced && crosshairAdvancedMode)
            {
                advancedStructuralDraft = null;
                advancedEntrySnapshot = null;
                advancedDirty = false;
                crosshairCreateMode = false;
                crosshairCreateNameUserEdited = false;
                crosshairCreateTemplateKey = null;
            }
            crosshairAdvancedMode = advanced;
            if (CrosshairBasicScroll != null) CrosshairBasicScroll.Visibility = advanced ? Visibility.Collapsed : Visibility.Visible;
            if (CrosshairAdvancedScroll != null) CrosshairAdvancedScroll.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
            CrosshairBasicLayout.Visibility = advanced ? Visibility.Collapsed : Visibility.Visible;
            CrosshairAdvancedLayout.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
            UpdateCrosshairSelection(); UpdateGuideLines(); UpdatePreviewViewportTransform();
        }

        CrosshairDocument EditorDocument => crosshairAdvancedMode && advancedStructuralDraft != null ? advancedStructuralDraft : crosshair.Draft;
        bool AdvancedDirty => crosshairAdvancedMode && advancedDirty;

        string SuggestedCrosshairName(string baseName)
        {
            var name = baseName;
            var suffix = 2;
            var existing = crosshair.Presets.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            while (existing.Contains(name)) name = baseName + " " + suffix++;
            return name;
        }

        void BeginCrosshairCreateEditor()
        {
            var template = CrosshairTemplates[0];
            crosshairCreateMode = true;
            crosshairCreateTemplateKey = template.Key;
            crosshairCreateNameUserEdited = false;
            advancedStructuralDraft = template.Factory();
            advancedStructuralDraft.Name = SuggestedCrosshairName(template.Label);
            advancedEntrySnapshot = Json.Clone(advancedStructuralDraft);
            crosshairSelectedElementId = advancedStructuralDraft.Elements.FirstOrDefault()?.Id;
            SetCrosshairMode(true);
            RefreshAdvancedEditorUi();
        }

        void BeginCrosshairEditEditor()
        {
            var active = crosshair.Presets.FirstOrDefault(p => p.Id == crosshair.ActivePresetId);
            if (active == null) return;

            // Edit the live editor draft, not the saved document. This keeps any
            // Basic-page changes in the same editor session while preserving the
            // active preset's identity and metadata until an explicit save.
            crosshairCreateMode = false;
            crosshairCreateTemplateKey = GetTemplateKey(crosshair.Draft.Name);
            crosshairCreateNameUserEdited = false;
            advancedStructuralDraft = Json.Clone(crosshair.Draft);
            advancedEntrySnapshot = Json.Clone(advancedStructuralDraft);
            advancedDirty = false;
            crosshairSelectedElementId = advancedStructuralDraft.Elements.FirstOrDefault()?.Id;
            SetCrosshairMode(true);
            RefreshAdvancedEditorUi();
        }

        void ResetCreateDraftToTemplate()
        {
            var template = CrosshairTemplates.FirstOrDefault(t => t.Key == crosshairCreateTemplateKey);
            if (template.Factory == null) template = CrosshairTemplates[0];
            var name = string.IsNullOrWhiteSpace(CrosshairCreateName.Text) ? SuggestedCrosshairName(template.Label) : CrosshairCreateName.Text.Trim();
            advancedStructuralDraft = template.Factory();
            advancedStructuralDraft.Name = name;
            advancedEntrySnapshot = Json.Clone(advancedStructuralDraft);
            advancedDirty = false;
            crosshairSelectedElementId = advancedStructuralDraft.Elements.FirstOrDefault()?.Id;
            RefreshAdvancedEditorUi();
        }

        void RefreshAdvancedEditorUi()
        {
            if (!crosshairAdvancedMode || advancedStructuralDraft == null) return;
            var d = advancedStructuralDraft;
            crosshairUiLoading = true;
            try
            {
                CrosshairElements.Items.Clear(); foreach (var e in d.Elements.OrderBy(e => e.ZIndex)) CrosshairElements.Items.Add(new CrosshairElementItem(e));
                var items = CrosshairElements.Items.OfType<CrosshairElementItem>().ToList();
                var index = items.FindIndex(x => x.Value.Id == crosshairSelectedElementId);
                if (index < 0 && items.Count > 0) index = 0;
                CrosshairElements.SelectedIndex = index;
                crosshairSelectedElementId = index >= 0 ? items[index].Value.Id : null;
                CrosshairAdvancedHeaderTitle.Text = crosshairCreateMode ? "新建准星" : "自定义准星";
                CrosshairAdvancedBackButton.Content = "取消新建";
                CrosshairAdvancedBackButton.Visibility = crosshairCreateMode ? Visibility.Visible : Visibility.Collapsed;
                CrosshairAdvancedPresetTitle.Text = (crosshairCreateMode ? d.Name : crosshair.Presets.FirstOrDefault(p => p.Id == crosshair.ActivePresetId)?.Name ?? d.Name) + (advancedDirty ? " *" : "");
                CrosshairCreateSettingsPanel.Visibility = Visibility.Visible;
                CrosshairCreateName.Text = d.Name;
                CrosshairCreateName.IsReadOnly = !crosshairCreateMode;
                CrosshairEditorGlobalScaleSlider.Value = d.GlobalCrosshairScale;
                CrosshairEditorGlobalScaleValue.Text = FormatGlobalScale(d.GlobalCrosshairScale);
                var customTemplateItem = CrosshairCreateTemplateBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => string.Equals(item.Tag as string, "Custom", StringComparison.Ordinal));
                if (!crosshairCreateMode && d.IsCustomStructure)
                {
                    customTemplateItem ??= new ComboBoxItem { Content = "自定义", Tag = "Custom", IsEnabled = false };
                    if (!CrosshairCreateTemplateBox.Items.Contains(customTemplateItem)) CrosshairCreateTemplateBox.Items.Add(customTemplateItem);
                    CrosshairCreateTemplateBox.SelectedItem = customTemplateItem;
                }
                else
                {
                    if (customTemplateItem != null) CrosshairCreateTemplateBox.Items.Remove(customTemplateItem);
                    var templateIndex = Array.FindIndex(CrosshairTemplates, t => t.Key == crosshairCreateTemplateKey);
                    CrosshairCreateTemplateBox.SelectedIndex = Math.Max(0, templateIndex);
                }
                CrosshairCreateTemplateBox.IsEnabled = crosshairCreateMode;
                var canCommit = crosshairCreateMode ? !string.IsNullOrWhiteSpace(d.Name) && d.Elements.Any(e => e.Enabled) : advancedDirty;
                CrosshairAdvancedSaveButton.Content = crosshairCreateMode ? "创建" : "保存修改";
                CrosshairAdvancedSaveButton.IsEnabled = canCommit;
                CrosshairAdvancedSaveButton.Opacity = canCommit ? 1 : .55;
                CrosshairAdvancedResetButton.Content = crosshairCreateMode ? "重置为模板" : "重置修改";
                CrosshairAdvancedResetButton.IsEnabled = crosshairCreateMode || advancedDirty;
                CrosshairAdvancedResetButton.Opacity = crosshairCreateMode || advancedDirty ? 1 : .55;
                var dpi = 96 * (PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1);
                crosshairAdvancedPreview?.SetDocument(d, dpi); UpdateAdvancedPreviewViewport(); ApplyPreviewPreferences(); LoadCrosshairInspector(); UpdateCrosshairSelection();
            }
            finally { crosshairUiLoading = false; }
        }

        void RefreshCrosshairUi()
        {
            if (CrosshairPresetBox == null) return;
            if (crosshairAdvancedMode && crosshair.PositionEditMode && advancedStructuralDraft != null)
            {
                advancedStructuralDraft.OffsetX = crosshair.Draft.OffsetX;
                advancedStructuralDraft.OffsetY = crosshair.Draft.OffsetY;
                advancedDirty = Json.Serialize(advancedStructuralDraft) != Json.Serialize(advancedEntrySnapshot);
            }
            if (crosshairAdvancedMode) RefreshAdvancedEditorUi();
            crosshairUiLoading = true;
            try
            {
                var active = crosshair.Presets.FirstOrDefault(p => p.Id == crosshair.ActivePresetId);
                var ordered = crosshair.Presets.OrderBy(p => p.SortOrder).ToList();
                CrosshairPresetBox.Items.Clear();
                foreach (var p in ordered) CrosshairPresetBox.Items.Add(new ComboBoxItem { Content = p.Name + (p.Id == crosshair.ActivePresetId && crosshair.DraftDirty ? " *" : ""), Tag = p });
                CrosshairPresetBox.SelectedIndex = Math.Max(0, ordered.FindIndex(p => p.Id == crosshair.ActivePresetId));
                CrosshairPresetList.Items.Clear(); foreach (var p in ordered) CrosshairPresetList.Items.Add(new CrosshairPresetListItem(p, p.Id == crosshair.ActivePresetId && crosshair.DraftDirty));
                CrosshairPresetList.SelectedIndex = Math.Max(0, ordered.FindIndex(p => p.Id == crosshair.ActivePresetId));
                var cycleNames = crosshair.CyclePresets.Select(p => p.Name).ToList(); CrosshairCycleCount.Text = $"{cycleNames.Count} 个预设"; CrosshairCycleOrder.Text = cycleNames.Count == 0 ? "暂无参与轮换的预设" : string.Join(" → ", cycleNames) + " ↻";
                CrosshairMonitorBox.ItemsSource = crosshair.MonitorIds().ToList();
                var monitor = string.IsNullOrWhiteSpace(crosshair.SelectedMonitorId) ? System.Windows.Forms.Screen.PrimaryScreen?.DeviceName : crosshair.SelectedMonitorId;
                CrosshairMonitorBox.SelectedItem = CrosshairMonitorBox.Items.Cast<string>().FirstOrDefault(x => string.Equals(x, monitor, StringComparison.OrdinalIgnoreCase)) ?? CrosshairMonitorBox.Items.Cast<string>().FirstOrDefault();
                CrosshairEnabled.IsChecked = crosshair.Enabled;
                CrosshairToggleHotkey.Text = crosshair.HotkeyText("toggle"); CrosshairNextHotkey.Text = crosshair.HotkeyText("next"); CrosshairPreviousHotkey.Text = crosshair.HotkeyText("previous"); CrosshairPresetHotkey.Text = active == null ? "未设置" : crosshair.HotkeyText(active.Id); CrosshairIncludeCycle.IsChecked = active?.IncludeInCycle == true;
                var d = EditorDocument; CrosshairGlobalScaleSlider.Value = d.GlobalCrosshairScale; CrosshairGlobalScaleValue.Text = FormatGlobalScale(d.GlobalCrosshairScale); CrosshairOffsetX.Text = d.OffsetX.ToString("0"); CrosshairOffsetY.Text = d.OffsetY.ToString("0");
                CrosshairElements.Items.Clear(); foreach (var e in d.Elements.OrderBy(e => e.ZIndex)) CrosshairElements.Items.Add(new CrosshairElementItem(e));
                var elementItems = CrosshairElements.Items.OfType<CrosshairElementItem>().ToList();
                var selectedIndex = elementItems.FindIndex(x => string.Equals(x.Value.Id, crosshairSelectedElementId, StringComparison.Ordinal));
                if (selectedIndex < 0 && elementItems.Count > 0) selectedIndex = 0;
                CrosshairElements.SelectedIndex = selectedIndex;
                crosshairSelectedElementId = selectedIndex >= 0 ? elementItems[selectedIndex].Value.Id : null;
                var editingPosition = crosshair.PositionEditMode;
                CrosshairRuntimeStatus.Text = editingPosition ? "正在调整位置 · 拖动准星或使用方向键微调" : (crosshair.Enabled ? (crosshair.DraftDirty ? "已显示 · 未保存修改" : "已显示 · 外部置顶层") : (crosshair.DraftDirty ? "已关闭 · 未保存修改" : "已关闭 · 不占用游戏输入"));
                CrosshairPresetName.Text = (active?.Name ?? d.Name) + (crosshair.DraftDirty ? " *" : "");
                CrosshairAdvancedPresetTitle.Text = (crosshairCreateMode ? d.Name : active?.Name ?? d.Name) + (AdvancedDirty ? " *" : "");
                CrosshairPositionSummary.Text = $"X {FormatOffset(d.OffsetX)} · Y {FormatOffset(d.OffsetY)}";
                CrosshairAdjustPositionButton.Content = editingPosition ? "完成" : "调整位置";
                CrosshairCancelPositionButton.Visibility = editingPosition ? Visibility.Visible : Visibility.Collapsed;
                CrosshairPositionEditHint.Visibility = editingPosition ? Visibility.Visible : Visibility.Collapsed;
                CrosshairPositionEditHint.Text = editingPosition ? "正在调整位置：直接拖动屏幕上的准星，或使用方向键微调。完成不会自动保存预设。" : "";
                CrosshairSaveButton.IsEnabled = crosshair.DraftDirty && !editingPosition; CrosshairSaveButton.Opacity = crosshair.DraftDirty && !editingPosition ? 1 : .55;
                var canSaveAdvanced = crosshairCreateMode ? !string.IsNullOrWhiteSpace(d.Name) && d.Elements.Any(e => e.Enabled) : AdvancedDirty;
                CrosshairAdvancedSaveButton.IsEnabled = canSaveAdvanced; CrosshairAdvancedSaveButton.Opacity = canSaveAdvanced ? 1 : .55;
                CrosshairAdvancedResetButton.IsEnabled = crosshairCreateMode || AdvancedDirty; CrosshairAdvancedResetButton.Opacity = crosshairCreateMode || AdvancedDirty ? 1 : .55;
                CrosshairPresetList.IsEnabled = !editingPosition; CrosshairMonitorBox.IsEnabled = !editingPosition; CrosshairEnabled.IsEnabled = !editingPosition; CrosshairIncludeCycle.IsEnabled = !editingPosition; CrosshairPresetHotkey.IsEnabled = !editingPosition; CrosshairMoreSettings.IsEnabled = !editingPosition; CrosshairNewButton.IsEnabled = !editingPosition; CrosshairEditPresetButton.IsEnabled = !editingPosition; CrosshairDuplicatePresetButton.IsEnabled = !editingPosition; CrosshairRenamePresetButton.IsEnabled = !editingPosition; CrosshairDeletePresetButton.IsEnabled = !editingPosition;
                LoadBasicParameters(d); SelectTemplate(GetTemplateKey(d.Name));
                crosshairAdvancedPreview?.SetDocument(d, 96 * (PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1));
                UpdateAdvancedPreviewViewport();
                ApplyPreviewPreferences();
                LoadCrosshairInspector(); UpdateCrosshairSelection();
            }
            finally { crosshairUiLoading = false; }
        }

        void UpdateAdvancedPreviewViewport()
        {
            if (crosshairAdvancedPreview != null && CrosshairAdvancedPreviewCanvas != null)
            {
                crosshairAdvancedPreview.SetViewportSize(CrosshairAdvancedPreviewCanvas.ActualWidth, CrosshairAdvancedPreviewCanvas.ActualHeight);
                if (crosshairDocumentVisualRoot != null)
                {
                    crosshairDocumentVisualRoot.Width = CrosshairAdvancedPreviewCanvas.ActualWidth;
                    crosshairDocumentVisualRoot.Height = CrosshairAdvancedPreviewCanvas.ActualHeight;
                }
                UpdatePreviewViewportTransform();
            }
        }

        static Matrix BuildPreviewViewportMatrix(double viewportWidth, double viewportHeight, double previewZoom, double panX = 0, double panY = 0)
        {
            previewZoom = Math.Clamp(previewZoom, .25, 4.0);
            return new Matrix(previewZoom, 0, 0, previewZoom, viewportWidth / 2 + panX, viewportHeight / 2 + panY);
        }

        void UpdatePreviewViewportTransform()
        {
            if (crosshairDocumentVisualRoot == null || CrosshairAdvancedPreviewCanvas == null) return;
            crosshairDocumentVisualRoot.RenderTransform = new MatrixTransform(BuildPreviewViewportMatrix(
                CrosshairAdvancedPreviewCanvas.ActualWidth,
                CrosshairAdvancedPreviewCanvas.ActualHeight,
                advancedPreviewZoom));
        }

        void UpdateCrosshairSelection()
        {
            var id = crosshairSelectedElementId ?? "";
            if (crosshairAdvancedPreview != null) crosshairAdvancedPreview.SelectedElementId = crosshairAdvancedMode ? id : "";
            UpdateSelectionThumb();
        }

        void LoadCrosshairInspector()
        {
            crosshairUiLoading = true;
            try
            {
                var selectedId = crosshairSelectedElementId;
                if (string.IsNullOrWhiteSpace(selectedId) && CrosshairElements?.SelectedItem is CrosshairElementItem selectedItem)
                    selectedId = selectedItem.Value.Id;
                var e = EditorDocument.Elements.FirstOrDefault(z => z.Id == selectedId);
                if (e == null) { CrosshairElementName.Text = ""; CrosshairElementKind.Text = ""; CrosshairElementScaleSlider.IsEnabled = false; CrosshairElementScaleValue.IsEnabled = false; return; }
                CrosshairElementScaleSlider.IsEnabled = true; CrosshairElementScaleValue.IsEnabled = true;
                CrosshairElementName.Text = e.Name; CrosshairElementKind.Text = e.Kind.ToString(); CrosshairElementEnabled.IsChecked = e.Enabled; CrosshairX.Text = e.X.ToString("0.##"); CrosshairY.Text = e.Y.ToString("0.##"); CrosshairElementScaleSlider.Value = e.Scale; CrosshairElementScaleValue.Text = FormatGlobalScale(e.Scale); CrosshairRotation.Text = e.Rotation.ToString("0.##"); CrosshairOpacity.Text = (e.Opacity * 100).ToString("0.##"); CrosshairMirrorX.IsChecked = e.MirrorX; CrosshairMirrorY.IsChecked = e.MirrorY;
                CrosshairColor.Text = e switch { CrosshairLine l => FormatCrosshairColor(ParseCrosshairColor(l.ColorHex)), CrosshairDot d => FormatCrosshairColor(ParseCrosshairColor(d.ColorHex)), CrosshairRing r => FormatCrosshairColor(ParseCrosshairColor(r.ColorHex)), CrosshairRectangle r => FormatCrosshairColor(ParseCrosshairColor(r.FillColorHex)), _ => "" };
            }
            finally { crosshairUiLoading = false; }
        }

        void ApplyCrosshairInspector()
        {
            var selectedId = crosshairSelectedElementId;
            var color = NormalizeCrosshairColor(CrosshairColor.Text.Trim());
            var d = crosshairAdvancedMode ? advancedStructuralDraft : null;
            if (d == null) return;
            if (!string.IsNullOrWhiteSpace(selectedId)) { var e = d.Elements.FirstOrDefault(z => z.Id == selectedId); if (e != null) { e.Name = string.IsNullOrWhiteSpace(CrosshairElementName.Text) ? e.Name : CrosshairElementName.Text.Trim(); e.Enabled = CrosshairElementEnabled.IsChecked != false; e.X = ReadText(CrosshairX, e.X); e.Y = ReadText(CrosshairY, e.Y); e.Scale = ReadSlider(CrosshairElementScaleSlider, e.Scale); e.Rotation = ReadText(CrosshairRotation, e.Rotation); var op = ReadText(CrosshairOpacity, e.Opacity * 100); e.Opacity = op > 1 ? op / 100 : op; e.MirrorX = CrosshairMirrorX.IsChecked == true; e.MirrorY = CrosshairMirrorY.IsChecked == true; if (color.Length > 0) { if (e is CrosshairLine l) l.ColorHex = color; else if (e is CrosshairDot dot) dot.ColorHex = color; else if (e is CrosshairRing ring) ring.ColorHex = color; else if (e is CrosshairRectangle rect) rect.FillColorHex = color; } } }
            d.GlobalCrosshairScale = ReadSlider(CrosshairEditorGlobalScaleSlider, d.GlobalCrosshairScale); d.OffsetX = ReadText(CrosshairOffsetX, d.OffsetX); d.OffsetY = ReadText(CrosshairOffsetY, d.OffsetY); d.Normalize(); advancedDirty = Json.Serialize(d) != Json.Serialize(advancedEntrySnapshot); RefreshAdvancedEditorUi();
        }

        void SelectTemplate(string key)
        {
            var index = Array.FindIndex(CrosshairTemplates, t => t.Key == key); if (index >= 0) CrosshairTemplateBox.SelectedIndex = index;
            BasicDotFields.Visibility = key == "Dot" ? Visibility.Visible : Visibility.Collapsed;
            BasicCrossFields.Visibility = key is "Cross" or "CrossDot" or "T" ? Visibility.Visible : Visibility.Collapsed;
            BasicRingFields.Visibility = key == "Ring" ? Visibility.Visible : Visibility.Collapsed;
            BasicChevronFields.Visibility = key == "Chevron" ? Visibility.Visible : Visibility.Collapsed;
            var custom = EditorDocument.IsCustomStructure;
            BasicDotFields.IsEnabled = !custom; BasicCrossFields.IsEnabled = !custom; BasicRingFields.IsEnabled = !custom; BasicChevronFields.IsEnabled = !custom;
            BasicTemplateHint.Text = custom ? "当前准星已进行自定义结构编辑；可调整颜色、透明度和位置，结构参数请进入高级编辑。" : key switch { "Dot" => "小绿点：大小、颜色、透明度与轮廓。", "Ring" => "圆环：直径、线宽、颜色与位置。", "Chevron" => "Chevron：长度、角度、粗细与位置。", _ => "十字准星：长度、粗细、间距、中心点与轮廓。" };
        }

        static string GetTemplateKey(string name)
        {
            if (name?.Contains("Chevron", StringComparison.OrdinalIgnoreCase) == true) return "Chevron";
            if (name?.Contains("Ring", StringComparison.OrdinalIgnoreCase) == true) return "Ring";
            if (name?.Contains("Dot", StringComparison.OrdinalIgnoreCase) == true && name.Contains("Cross", StringComparison.OrdinalIgnoreCase)) return "CrossDot";
            if (name?.Contains("Dot", StringComparison.OrdinalIgnoreCase) == true) return "Dot";
            if (name?.Contains("T Shape", StringComparison.OrdinalIgnoreCase) == true) return "T";
            return "Cross";
        }

        void LoadBasicParameters(CrosshairDocument d)
        {
            var key = GetTemplateKey(d.Name); SelectTemplate(key);
            var color = d.Elements.OfType<CrosshairLine>().Select(x => x.ColorHex).FirstOrDefault() ?? d.Elements.OfType<CrosshairDot>().Select(x => x.ColorHex).FirstOrDefault() ?? d.Elements.OfType<CrosshairRing>().Select(x => x.ColorHex).FirstOrDefault() ?? "#FFFFFFFF";
            BasicColor.Text = FormatCrosshairColor(ParseCrosshairColor(color)); BasicOpacity.Value = d.GlobalOpacity * 100; BasicOpacityValue.Text = (d.GlobalOpacity * 100).ToString("0"); CrosshairGlobalScaleSlider.Value = d.GlobalCrosshairScale; CrosshairGlobalScaleValue.Text = FormatGlobalScale(d.GlobalCrosshairScale); CrosshairOffsetX.Text = d.OffsetX.ToString("0.##"); CrosshairOffsetY.Text = d.OffsetY.ToString("0.##");
            var dot = d.Elements.OfType<CrosshairDot>().FirstOrDefault(); var line = d.Elements.OfType<CrosshairLine>().FirstOrDefault(); var ring = d.Elements.OfType<CrosshairRing>().FirstOrDefault();
            if (dot != null) { BasicSize.Value = dot.Diameter; BasicSizeValue.Text = dot.Diameter.ToString("0.##"); BasicOutline.IsChecked = dot.OutlineEnabled; BasicOutlineThickness.Value = dot.OutlineThickness; BasicCenterSize.Value = dot.Diameter; }
            if (line != null)
            {
                BasicThickness.Value = line.Thickness;
                // A newly loaded template may contain a full line crossing the
                // center. Once edited, lines are stored as one-sided segments.
                // Derive semantic length/gap from the segment itself so gap +
                // length can never push the length slider past its maximum.
                bool crossesCenter = (line.StartX < 0 && line.EndX > 0) || (line.StartY < 0 && line.EndY > 0);
                var length = crossesCenter ? Math.Max(Math.Abs(line.StartX), Math.Abs(line.EndX)) : Math.Abs(line.EndX - line.StartX);
                var gap = crossesCenter ? 0 : Math.Min(Math.Abs(line.StartX), Math.Abs(line.EndX));
                BasicCrossLength.Value = Math.Clamp(Math.Max(1, length), BasicCrossLength.Minimum, BasicCrossLength.Maximum);
                BasicGap.Value = Math.Clamp(gap, BasicGap.Minimum, BasicGap.Maximum);
                BasicCrossOutline.IsChecked = line.OutlineEnabled; BasicCrossOutlineThickness.Value = line.OutlineThickness; BasicChevronThickness.Value = line.Thickness; BasicChevronLength.Value = Math.Max(1, Math.Abs(line.EndX - line.StartX));
            }
            if (ring != null) { BasicRingDiameter.Value = ring.Diameter; BasicRingThickness.Value = ring.Thickness; }
            BasicCenterPoint.IsChecked = dot != null && dot.Enabled;
        }

        void ApplyBasicParameters()
        {
            var current = crosshair.Draft; var color = NormalizeCrosshairColor(string.IsNullOrWhiteSpace(BasicColor.Text) ? "#FFFFFF" : BasicColor.Text.Trim()); var opacity = Math.Clamp(ReadSlider(BasicOpacity, 100) / 100, 0, 1); var globalScale = ReadSlider(CrosshairGlobalScaleSlider, current.GlobalCrosshairScale); var ox = ReadText(CrosshairOffsetX, current.OffsetX); var oy = ReadText(CrosshairOffsetY, current.OffsetY);
            if (current.IsCustomStructure)
            {
                var applyColor = crosshairBasicColorDirty; crosshairBasicColorDirty = false;
                crosshair.UpdateDraft(d => { d.OffsetX = ox; d.OffsetY = oy; d.GlobalCrosshairScale = globalScale; d.GlobalOpacity = opacity; if (applyColor) foreach (var e in d.Elements) SetBasicColor(e, color); });
                return;
            }
            var key = (CrosshairTemplateBox.SelectedItem as ComboBoxItem)?.Tag as string ?? GetTemplateKey(current.Name);
            crosshairBasicColorDirty = false;
            crosshair.UpdateDraft(d =>
            {
                d.OffsetX = ox; d.OffsetY = oy; d.GlobalCrosshairScale = globalScale; d.GlobalOpacity = opacity; foreach (var e in d.Elements) e.Opacity = 1;
                if (key == "Dot") { d.Name = "小绿点"; var dot = d.Elements.OfType<CrosshairDot>().FirstOrDefault() ?? AddDot(d, "中心点"); dot.Diameter = ReadSlider(BasicSize, dot.Diameter); dot.ColorHex = color; dot.OutlineEnabled = BasicOutline.IsChecked == true; dot.OutlineThickness = ReadSlider(BasicOutlineThickness, dot.OutlineThickness); dot.Enabled = true; }
                else if (key is "Cross" or "CrossDot" or "T")
                {
                    d.Name = key == "CrossDot" ? "十字 + 中心点" : key == "T" ? "T 形" : "白色十字"; var lines = EnsureCrossLines(d); var length = ReadSlider(BasicCrossLength, 20); var gap = ReadSlider(BasicGap, 0); var thickness = ReadSlider(BasicThickness, 2); SetLine(lines[0], -gap - length, 0, -gap, 0, color, thickness); SetLine(lines[1], gap, 0, gap + length, 0, color, thickness); SetLine(lines[2], 0, -gap - length, 0, -gap, color, thickness); SetLine(lines[3], 0, gap, 0, gap + length, color, thickness); foreach (var l in lines) { l.OutlineEnabled = BasicCrossOutline.IsChecked == true; l.OutlineThickness = ReadSlider(BasicCrossOutlineThickness, l.OutlineThickness); }
                    if (key == "T") { lines[2].Enabled = false; lines[3].StartX = 0; lines[3].StartY = 0; lines[3].EndX = 0; lines[3].EndY = length; lines[3].Enabled = true; }
                    var center = d.Elements.OfType<CrosshairDot>().FirstOrDefault(); if (key == "CrossDot" || BasicCenterPoint.IsChecked == true) { center ??= AddDot(d, "中心点"); center.Diameter = ReadSlider(BasicCenterSize, 5); center.ColorHex = color; center.Enabled = BasicCenterPoint.IsChecked == true || key == "CrossDot"; } else if (center != null) center.Enabled = false;
                }
                else if (key == "Ring") { d.Name = "红色圆环"; var ring = d.Elements.OfType<CrosshairRing>().FirstOrDefault() ?? AddRing(d, "圆环"); ring.Diameter = ReadSlider(BasicRingDiameter, ring.Diameter); ring.Thickness = ReadSlider(BasicRingThickness, ring.Thickness); ring.ColorHex = color; ring.Enabled = true; }
                else { d.Name = "Chevron"; var lines = EnsureChevronLines(d); var length = ReadSlider(BasicChevronLength, 40); var angle = ReadSlider(BasicAngle, 45) * Math.PI / 180; var half = length / 2; var dx = half * Math.Cos(angle); var dy = half * Math.Sin(angle); SetLine(lines[0], -dx, -dy, 0, 0, color, ReadSlider(BasicChevronThickness, 2)); SetLine(lines[1], 0, 0, dx, -dy, color, ReadSlider(BasicChevronThickness, 2)); }
            });
        }

        static void SetBasicColor(CrosshairElement element, string color)
        {
            switch (element)
            {
                case CrosshairLine line: line.ColorHex = color; break;
                case CrosshairDot dot: dot.ColorHex = color; break;
                case CrosshairRing ring: ring.ColorHex = color; break;
                case CrosshairRectangle rectangle: rectangle.FillColorHex = color; break;
            }
        }

        static CrosshairDot AddDot(CrosshairDocument d, string name) { var dot = new CrosshairDot { Name = name, ZIndex = d.Elements.Count }; d.Elements.Add(dot); return dot; }
        static CrosshairRing AddRing(CrosshairDocument d, string name) { var ring = new CrosshairRing { Name = name, ZIndex = d.Elements.Count }; d.Elements.Add(ring); return ring; }
        static System.Collections.Generic.List<CrosshairLine> EnsureCrossLines(CrosshairDocument d) { var lines = d.Elements.OfType<CrosshairLine>().ToList(); while (lines.Count < 4) { var line = new CrosshairLine { Name = "十字线 " + (lines.Count + 1), ZIndex = d.Elements.Count }; d.Elements.Add(line); lines.Add(line); } return lines; }
        static System.Collections.Generic.List<CrosshairLine> EnsureChevronLines(CrosshairDocument d) { var lines = d.Elements.OfType<CrosshairLine>().ToList(); while (lines.Count < 2) { var line = new CrosshairLine { Name = "Chevron 线", ZIndex = d.Elements.Count }; d.Elements.Add(line); lines.Add(line); } return lines; }
        static void SetLine(CrosshairLine l, double sx, double sy, double ex, double ey, string color, double thickness) { l.StartX = sx; l.StartY = sy; l.EndX = ex; l.EndY = ey; l.ColorHex = color; l.Thickness = thickness; l.Enabled = true; }
        static double ReadSlider(Slider slider, double fallback) => double.IsNaN(slider.Value) ? fallback : slider.Value;
        static double ReadText(TextBox box, double fallback) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ? value : fallback;
        static string FormatOffset(double value) => value > 0 ? $"+{value:0}" : value.ToString("0", CultureInfo.CurrentCulture);

        void CrosshairTemplate_SelectionChanged()
        {
            if (CrosshairTemplateBox.SelectedItem is not ComboBoxItem item || item.Tag is not string key) return; crosshair.ReplaceDraft(CrosshairTemplates.First(t => t.Key == key).Factory()); SelectTemplate(key);
        }

        void CrosshairPresetList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!crosshairUiLoading && !crosshair.PositionEditMode && CrosshairPresetList.SelectedItem is CrosshairPresetListItem item) crosshair.SelectPreset(item.Id); }

        void AddGuides(Canvas canvas, out Line horizontal, out Line vertical)
        {
            horizontal = new Line { Stroke = new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false }; vertical = new Line { Stroke = horizontal.Stroke, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false }; canvas.Children.Add(horizontal); canvas.Children.Add(vertical);
        }
        void UpdateGuideLines() { UpdateGuide(CrosshairAdvancedPreviewCanvas, advancedGuideHorizontal, advancedGuideVertical, CrosshairAdvancedPreviewGuide?.IsChecked == true && crosshairAdvancedMode); }
        static void UpdateGuide(Canvas canvas, Line h, Line v, bool visible) { if (canvas == null || h == null || v == null) return; h.X1 = 0; h.X2 = canvas.ActualWidth; h.Y1 = h.Y2 = canvas.ActualHeight / 2; v.X1 = v.X2 = canvas.ActualWidth / 2; v.Y1 = 0; v.Y2 = canvas.ActualHeight; h.Visibility = v.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; }
        void ApplyPreviewPreferences()
        {
            // XAML may raise ValueChanged/SelectionChanged while names are still
            // being assigned. Treat that construction phase as inert.
            if (!crosshairUiInitialized || CrosshairAdvancedPreviewBackdrop == null || CrosshairAdvancedPreviewBackgroundBox == null) return;
            var background = (CrosshairAdvancedPreviewBackgroundBox.SelectedItem as string) switch { "浅色" => "#F7F7F4", "灰色" => "#68707A", _ => "#252A36" };
            CrosshairAdvancedPreviewBackdrop.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(background));
            UpdatePreviewViewportTransform(); UpdateGuideLines(); UpdateSelectionThumb();
        }

        void SetAdvancedPreviewZoom(double zoom)
        {
            advancedPreviewZoom = Math.Clamp(zoom, .25, 4.0);
            if (CrosshairAdvancedPreviewZoomSlider != null && Math.Abs(CrosshairAdvancedPreviewZoomSlider.Value - advancedPreviewZoom) > .001) CrosshairAdvancedPreviewZoomSlider.Value = advancedPreviewZoom;
            if (CrosshairAdvancedPreviewZoomValue != null) CrosshairAdvancedPreviewZoomValue.Text = (advancedPreviewZoom * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
            ApplyPreviewPreferences();
        }

        void CrosshairAdvancedPreviewZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (crosshairUiLoading || !crosshairUiInitialized) return;
            SetAdvancedPreviewZoom(e.NewValue);
        }

        void CommitAdvancedPreviewZoomText()
        {
            var text = (CrosshairAdvancedPreviewZoomValue.Text ?? "").Trim().TrimEnd('%');
            SetAdvancedPreviewZoom(double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) ? percent / 100 : advancedPreviewZoom);
        }

        void CrosshairAdvancedPreviewZoomValue_LostFocus(object sender, RoutedEventArgs e) => CommitAdvancedPreviewZoomText();
        void CrosshairAdvancedPreviewZoomValue_PreviewKeyDown(object sender, KeyEventArgs e) { if (e.Key != Key.Enter) return; CommitAdvancedPreviewZoomText(); e.Handled = true; }
        void CrosshairAdvancedPreviewZoomReset_Click(object sender, RoutedEventArgs e) => SetAdvancedPreviewZoom(1);
        void CrosshairAdvancedPreviewCanvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
            SetAdvancedPreviewZoom(advancedPreviewZoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
        }

        static ControlTemplate CreateSelectionThumbTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromArgb(220, 255, 170, 72)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            return new ControlTemplate(typeof(Thumb)) { VisualTree = border };
        }

        double GetAdvancedPreviewZoom()
        {
            return advancedPreviewZoom;
        }

        // StructuralElementDragController equivalent: this WPF-only block owns
        // advanced local element coordinates and never changes the overlay HWND.
        void OnStructuralElementDragStarted(object sender, DragStartedEventArgs e)
        {
            if (!crosshairAdvancedMode || string.IsNullOrWhiteSpace(crosshairSelectedElementId)) return;
            var selected = EditorDocument.Elements.FirstOrDefault(x => x.Id == crosshairSelectedElementId);
            if (selected == null) return;
            structuralDraggedElementId = selected.Id;
            structuralDragStartDocument = advancedStructuralDraft;
            structuralElementDragging = true;
            structuralElementDragMoved = false;
        }

        void OnStructuralElementDragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!crosshairAdvancedMode || !structuralElementDragging || string.IsNullOrWhiteSpace(structuralDraggedElementId)) return;
            var zoom = GetAdvancedPreviewZoom();
            var dx = e.HorizontalChange / zoom;
            var dy = e.VerticalChange / zoom;
            var element = advancedStructuralDraft?.Elements.FirstOrDefault(x => x.Id == structuralDraggedElementId);
            if (element == null) return;
            switch (element) { case CrosshairLine line: line.X += dx; line.Y += dy; break; case CrosshairDot dot: dot.X += dx; dot.Y += dy; break; case CrosshairRing ring: ring.X += dx; ring.Y += dy; break; case CrosshairRectangle rectangle: rectangle.X += dx; rectangle.Y += dy; break; case CrosshairImage image: image.X += dx; image.Y += dy; break; default: return; }
            structuralElementDragMoved = true; advancedDirty = true;
            var dpi = 96 * (PresentationSource.FromVisual(this)?.CompositionTarget.TransformToDevice.M11 ?? 1);
            crosshairAdvancedPreview?.SetDocument(advancedStructuralDraft, dpi); LoadCrosshairInspector(); UpdateCrosshairSelection();
        }

        void OnStructuralElementDragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (!structuralElementDragging) return;
            structuralElementDragging = false;
            crosshairSelectionThumb?.ReleaseMouseCapture();
            structuralDraggedElementId = null;
            structuralDragStartDocument = null;
            if (structuralElementDragMoved)
            {
                structuralElementDragMoved = false;
                RefreshCrosshairDirtyIndicators();
            }
        }

        void RefreshCrosshairDirtyIndicators()
        {
            var active = crosshair.Presets.FirstOrDefault(p => p.Id == crosshair.ActivePresetId);
            var dirty = crosshair.DraftDirty;
            var d = EditorDocument;
            CrosshairPresetName.Text = (active?.Name ?? d.Name) + (dirty ? " *" : "");
            CrosshairAdvancedPresetTitle.Text = (active?.Name ?? d.Name) + (AdvancedDirty ? " *" : "");
            CrosshairPositionSummary.Text = $"X {FormatOffset(d.OffsetX)} · Y {FormatOffset(d.OffsetY)}";
            CrosshairRuntimeStatus.Text = crosshair.Enabled ? (dirty ? "已显示 · 未保存修改" : "已显示 · 外部置顶层") : (dirty ? "已关闭 · 未保存修改" : "已关闭 · 不占用游戏输入");
            CrosshairSaveButton.IsEnabled = dirty && !crosshair.PositionEditMode;
            CrosshairSaveButton.Opacity = dirty && !crosshair.PositionEditMode ? 1 : .55;
            CrosshairAdvancedSaveButton.IsEnabled = AdvancedDirty;
            CrosshairAdvancedSaveButton.Opacity = AdvancedDirty ? 1 : .55;
            CrosshairAdvancedResetButton.IsEnabled = AdvancedDirty;
            CrosshairAdvancedResetButton.Opacity = AdvancedDirty ? 1 : .55;
            LoadBasicParameters(d);
            LoadCrosshairInspector();
            UpdateCrosshairSelection();
        }

        void UpdateSelectionThumb()
        {
            if (crosshairSelectionThumb == null || crosshairAdvancedPreview == null || !crosshairAdvancedMode || string.IsNullOrWhiteSpace(crosshairSelectedElementId))
            {
                if (crosshairSelectionThumb != null) crosshairSelectionThumb.Visibility = Visibility.Collapsed;
                return;
            }
            var selected = CrosshairRenderer.GetElementDocumentBounds(EditorDocument, crosshairSelectedElementId);
            if (selected.IsEmpty)
            {
                crosshairSelectionThumb.Visibility = Visibility.Collapsed;
                return;
            }
            try
            {
                // Selection is a document-layer child. Its geometry stays in
                // document units; the parent Viewport Matrix applies zoom once.
                var zoom = Math.Max(.25, advancedPreviewZoom);
                var documentPadding = 4.0 / zoom;
                var minimumHitSize = 32.0 / zoom;
                var width = Math.Max(selected.Width + 2 * documentPadding, minimumHitSize);
                var height = Math.Max(selected.Height + 2 * documentPadding, minimumHitSize);
                var centerX = selected.Left + selected.Width / 2;
                var centerY = selected.Top + selected.Height / 2;
                Canvas.SetLeft(crosshairSelectionThumb, centerX - width / 2);
                Canvas.SetTop(crosshairSelectionThumb, centerY - height / 2);
                crosshairSelectionThumb.Width = width;
                crosshairSelectionThumb.Height = height;
                crosshairSelectionThumb.Visibility = Visibility.Visible;
                crosshairSelectionThumb.IsHitTestVisible = true;
                Canvas.SetZIndex(crosshairSelectionThumb, 1000);
            }
            catch (InvalidOperationException)
            {
                crosshairSelectionThumb.Visibility = Visibility.Collapsed;
                if (IsLoaded) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateSelectionThumb));
            }
        }

        void CrosshairPresetCycleToggleChanged(object sender, RoutedEventArgs e)
        {
            if (crosshairUiLoading || e.OriginalSource is not CheckBox check || check.DataContext is not CrosshairPresetListItem item) return;
            crosshair.SetPresetIncludeInCycle(item.Id, check.IsChecked == true);
            e.Handled = true;
        }

        void CrosshairAddMenu_Click(object s, RoutedEventArgs e)
        {
            if (s is Button button && CrosshairAddPopover != null)
            {
                CrosshairAddPopover.PlacementTarget = button;
                CrosshairAddPopover.IsOpen = true;
            }
        }
        void CloseCrosshairAddPopover() { if (CrosshairAddPopover != null) CrosshairAddPopover.IsOpen = false; }
        void AddAdvancedElement(CrosshairElement element) { if (advancedStructuralDraft == null || element == null) return; element.ZIndex = advancedStructuralDraft.Elements.Count; advancedStructuralDraft.Elements.Add(element); crosshairSelectedElementId = element.Id; advancedDirty = true; RefreshAdvancedEditorUi(); }
        void CrosshairAddLine_Click(object s, RoutedEventArgs e) { CloseCrosshairAddPopover(); AddAdvancedElement(new CrosshairLine { Name = "新线" }); }
        void CrosshairAddDot_Click(object s, RoutedEventArgs e) { CloseCrosshairAddPopover(); AddAdvancedElement(new CrosshairDot { Name = "新点" }); }
        void CrosshairAddRing_Click(object s, RoutedEventArgs e) { CloseCrosshairAddPopover(); AddAdvancedElement(new CrosshairRing { Name = "新圆环" }); }
        void CrosshairAddRect_Click(object s, RoutedEventArgs e) { CloseCrosshairAddPopover(); AddAdvancedElement(new CrosshairRectangle { Name = "新方框" }); }
        void CrosshairImportPng_Click(object s, RoutedEventArgs e)
        {
            CloseCrosshairAddPopover(); var d = new Microsoft.Win32.OpenFileDialog { Filter = "PNG 图片 (*.png)|*.png" }; if (d.ShowDialog() != true || advancedStructuralDraft == null) return; try { var id = new CrosshairAssetStore().ImportPng(d.FileName); AddAdvancedElement(new CrosshairImage { Name = "PNG 图片", AssetId = id, Width = 32, Height = 32 }); } catch (Exception ex) { CrosshairRuntimeStatus.Text = "PNG 导入失败：" + ex.Message; }
        }
        void CrosshairDuplicate_Click(object s, RoutedEventArgs e) { var e0 = advancedStructuralDraft?.Elements.FirstOrDefault(x => x.Id == crosshairSelectedElementId); if (e0 == null) return; var copy = Json.Clone(e0); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " 副本"; AddAdvancedElement(copy); }
        void CrosshairDelete_Click(object s, RoutedEventArgs e)
        {
            var items = CrosshairElements.Items.OfType<CrosshairElementItem>().ToList();
            var index = items.FindIndex(x => x.Value.Id == crosshairSelectedElementId);
            if (index < 0) return;
            if (advancedStructuralDraft == null || !ConfirmDialog.Ask(this, "删除元素", "删除此元素？此操作不可撤销。")) return; advancedStructuralDraft.Elements.RemoveAll(x => x.Id == crosshairSelectedElementId); advancedDirty = true;
            var fallback = items.Skip(index + 1).FirstOrDefault() ?? items.Take(index).LastOrDefault();
            crosshairSelectedElementId = fallback?.Value.Id; RefreshAdvancedEditorUi();
        }
        void CrosshairDeletePreset_Click(object s, RoutedEventArgs e)
        {
            var active = crosshair.Presets.FirstOrDefault(p => p.Id == crosshair.ActivePresetId);
            if (active != null && ConfirmDialog.Ask(this, "删除准星", $"删除“{active.Name}”？此操作不可撤销。")) { crosshair.DeleteActive(); BeginCrosshairEditEditor(); }
        }
        void CrosshairDuplicatePreset_Click(object s, RoutedEventArgs e) { crosshair.DuplicateActive(); BeginCrosshairEditEditor(); }
        void CrosshairRenamePreset_Click(object s, RoutedEventArgs e) { var name = Prompt.Ask(this, "重命名准星", "输入准星名称"); if (!string.IsNullOrWhiteSpace(name)) { crosshair.RenameActive(name); BeginCrosshairEditEditor(); } }
        void CrosshairAdjustPosition_Click(object s, RoutedEventArgs e)
        {
            if (crosshair.PositionEditMode)
            {
                crosshair.EndPositionEdit(true);
                SyncAdvancedPositionFromRuntime();
                CrosshairRuntimeStatus.Text = "位置已完成；点击保存修改可写入预设。";
            }
            else
            {
                if (crosshairAdvancedMode && !crosshairCreateMode && advancedStructuralDraft != null) crosshair.ReplaceDraft(Json.Clone(advancedStructuralDraft));
                crosshair.BeginPositionEdit();
                CrosshairRuntimeStatus.Text = "正在调整位置：拖动屏幕上的准星；方向键微调，Enter 完成，Esc 取消。";
                Focus();
            }
        }
        void CrosshairResetPosition_Click(object s, RoutedEventArgs e)
        {
            if (crosshairAdvancedMode && !crosshair.PositionEditMode && advancedStructuralDraft != null) { advancedStructuralDraft.OffsetX = 0; advancedStructuralDraft.OffsetY = 0; advancedDirty = Json.Serialize(advancedStructuralDraft) != Json.Serialize(advancedEntrySnapshot); RefreshAdvancedEditorUi(); }
            else crosshair.ResetPosition();
            CrosshairRuntimeStatus.Text = crosshair.PositionEditMode ? "已重置居中；点击取消可恢复进入编辑前的位置。" : "已重置居中；点击保存修改可写入预设。";
        }
        void CrosshairCancelPosition_Click(object s, RoutedEventArgs e) { crosshair.EndPositionEdit(false); SyncAdvancedPositionFromRuntime(); CrosshairRuntimeStatus.Text = "已取消位置编辑，恢复进入编辑前的位置。"; }
        void SyncAdvancedPositionFromRuntime()
        {
            if (!crosshairAdvancedMode || advancedStructuralDraft == null) return;
            advancedStructuralDraft.OffsetX = crosshair.Draft.OffsetX;
            advancedStructuralDraft.OffsetY = crosshair.Draft.OffsetY;
            advancedDirty = Json.Serialize(advancedStructuralDraft) != Json.Serialize(advancedEntrySnapshot);
            RefreshAdvancedEditorUi();
        }
        void CrosshairPositionKeyDown(object sender, KeyEventArgs e)
        {
            if (!crosshair.PositionEditMode) return; if (e.Key == Key.Enter) { crosshair.EndPositionEdit(true); SyncAdvancedPositionFromRuntime(); e.Handled = true; return; } if (e.Key == Key.Escape) { crosshair.EndPositionEdit(false); SyncAdvancedPositionFromRuntime(); e.Handled = true; return; }
            var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1; var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0; var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0; if (dx != 0 || dy != 0) { crosshair.NudgePosition(dx, dy); e.Handled = true; }
        }
        void CrosshairApplyProperty_Click(object s, RoutedEventArgs e) => ApplyCrosshairInspector();
        void CommitAdvancedStructuralDraft()
        {
            if (advancedStructuralDraft == null) return;
            var selection = crosshairSelectedElementId;
            var name = string.IsNullOrWhiteSpace(CrosshairCreateName.Text) ? advancedStructuralDraft.Name : CrosshairCreateName.Text.Trim();
            advancedStructuralDraft.Name = name;
            crosshair.ReplaceDraft(Json.Clone(advancedStructuralDraft));
            if (!crosshairCreateMode) crosshair.RenameActive(name);
            crosshair.SaveDraftToActive();
            advancedEntrySnapshot = Json.Clone(advancedStructuralDraft);
            advancedDirty = false;
            crosshairSelectedElementId = selection;
            RefreshAdvancedEditorUi();
            CrosshairRuntimeStatus.Text = "已保存准星修改";
        }
        void CrosshairSave_Click(object s, RoutedEventArgs e)
        {
            if (crosshair.PositionEditMode) return;
            if (crosshairAdvancedMode)
            {
                if (crosshairCreateMode)
                {
                    if (advancedStructuralDraft == null || string.IsNullOrWhiteSpace(CrosshairCreateName.Text) || !advancedStructuralDraft.Elements.Any(x => x.Enabled)) return;
                    var selection = crosshairSelectedElementId;
                    advancedStructuralDraft.Name = CrosshairCreateName.Text.Trim();
                    crosshair.CreatePreset(advancedStructuralDraft.Name, advancedStructuralDraft);
                    crosshairSelectedElementId = selection;
                    SetCrosshairMode(false);
                    BeginCrosshairEditEditor();
                    CrosshairRuntimeStatus.Text = "已创建并保存新准星";
                }
                else if (advancedDirty) CommitAdvancedStructuralDraft();
                return;
            }
            crosshair.SaveDraftToActive(); CrosshairRuntimeStatus.Text = "已保存当前预设";
        }
        void CrosshairSaveAs_Click(object s, RoutedEventArgs e) { var name = Prompt.Ask(this, "另存为准星预设", "输入预设名称"); if (!string.IsNullOrWhiteSpace(name)) crosshair.SaveDraftAs(name); }
        void CrosshairNew_Click(object s, RoutedEventArgs e) => BeginCrosshairCreateEditor();
        void CrosshairTemplate_Click(object s, RoutedEventArgs e)
        {
            var index = Math.Max(0, Array.FindIndex(CrosshairTemplates, t => t.Key == GetTemplateKey(crosshair.Draft.Name))); var nextIndex = (index + 1) % CrosshairTemplates.Length; CrosshairTemplateBox.SelectedIndex = nextIndex; crosshair.ReplaceDraft(CrosshairTemplates[nextIndex].Factory());
        }
        void CrosshairAdvancedReset_Click(object s, RoutedEventArgs e)
        {
            if (crosshairCreateMode) { ResetCreateDraftToTemplate(); return; }
            if (!advancedDirty || advancedEntrySnapshot == null) return;
            var selection = crosshairSelectedElementId;
            advancedStructuralDraft = Json.Clone(advancedEntrySnapshot);
            if (!advancedStructuralDraft.Elements.Any(x => x.Id == selection)) selection = advancedStructuralDraft.Elements.FirstOrDefault()?.Id;
            crosshairSelectedElementId = selection; advancedDirty = false; RefreshAdvancedEditorUi();
        }
        void CrosshairBasicMode_Click(object s, RoutedEventArgs e)
        {
            if (!crosshairCreateMode) return;
            SetCrosshairMode(false);
            BeginCrosshairEditEditor();
        }
        void CrosshairAdvancedMode_Click(object s, RoutedEventArgs e) => BeginCrosshairEditEditor();
        void CrosshairEditPreset_Click(object s, RoutedEventArgs e) => BeginCrosshairEditEditor();
        void CrosshairCreateName_TextChanged(object s, TextChangedEventArgs e)
        {
            if (crosshairUiLoading || !crosshairAdvancedMode || advancedStructuralDraft == null) return;
            crosshairCreateNameUserEdited = true;
            advancedStructuralDraft.Name = CrosshairCreateName.Text.Trim();
            advancedDirty = Json.Serialize(advancedStructuralDraft) != Json.Serialize(advancedEntrySnapshot);
            RefreshAdvancedEditorUi();
        }
        void CrosshairCreateTemplate_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (crosshairUiLoading || !crosshairCreateMode || CrosshairCreateTemplateBox.SelectedItem is not ComboBoxItem item || item.Tag is not string key) return;
            var template = CrosshairTemplates.First(t => t.Key == key);
            crosshairCreateTemplateKey = key;
            var name = crosshairCreateNameUserEdited ? CrosshairCreateName.Text.Trim() : SuggestedCrosshairName(template.Label);
            advancedStructuralDraft = template.Factory();
            advancedStructuralDraft.Name = name;
            advancedEntrySnapshot = Json.Clone(advancedStructuralDraft);
            advancedDirty = false;
            crosshairSelectedElementId = advancedStructuralDraft.Elements.FirstOrDefault()?.Id;
            RefreshAdvancedEditorUi();
        }
        void WireCrosshairHotkeyRecorders()
        {
            foreach (var box in new[] { CrosshairToggleHotkey, CrosshairNextHotkey, CrosshairPreviousHotkey, CrosshairPresetHotkey })
            {
                box.Focusable = true; box.IsReadOnly = true; box.PreviewMouseLeftButtonDown += (_, e) => { BeginCrosshairHotkeyRecording(box); e.Handled = true; }; box.GotKeyboardFocus += (_,__) => { if (crosshairHotkeyRecordingBox == null) BeginCrosshairHotkeyRecording(box); };
            }
        }

        void BeginCrosshairHotkeyRecording(TextBox box)
        {
            if (box == null) return;
            if (ReferenceEquals(crosshairHotkeyRecordingBox, box)) { box.Focus(); return; }
            if (crosshairHotkeyRecordingBox != null) CancelCrosshairHotkeyRecording();
            crosshairHotkeyRecordingBox = box; crosshairHotkeyRecordingOriginal = box.Text; box.Text = "请按快捷键…"; box.Tag = "recording"; box.Focus(); Keyboard.Focus(box); crosshair.UnregisterHotkeys(); CrosshairRuntimeStatus.Text = "正在录制快捷键：先按修饰键，再按主键；Esc 取消，Delete 清除。";
        }

        void CancelCrosshairHotkeyRecording()
        {
            if (crosshairHotkeyRecordingBox == null) return;
            crosshairHotkeyRecordingBox.Text = crosshairHotkeyRecordingOriginal ?? "未设置"; crosshairHotkeyRecordingBox.Tag = null; crosshairHotkeyRecordingBox = null; crosshairHotkeyRecordingOriginal = null; RegisterHotkeys();
        }

        void CrosshairHotkey_PreviewKeyDown(object s, KeyEventArgs e)
        {
            if (s is not TextBox box) return;
            e.Handled = true; if (!ReferenceEquals(crosshairHotkeyRecordingBox, box)) BeginCrosshairHotkeyRecording(box);
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { CancelCrosshairHotkeyRecording(); CrosshairRuntimeStatus.Text = "已取消快捷键录制。"; return; }
            var activePreset = crosshair.Presets.FirstOrDefault(p => p.Id == crosshair.ActivePresetId);
            var action = ReferenceEquals(box, CrosshairToggleHotkey) ? "toggle" : ReferenceEquals(box, CrosshairNextHotkey) ? "next" : ReferenceEquals(box, CrosshairPreviousHotkey) ? "previous" : activePreset?.Id;
            if (key is Key.Back or Key.Delete) { if (!string.IsNullOrWhiteSpace(action)) crosshair.ClearHotkey(action); box.Text = "未设置"; box.Tag = null; crosshairHotkeyRecordingBox = null; crosshairHotkeyRecordingOriginal = null; RegisterHotkeys(); CrosshairRuntimeStatus.Text = "快捷键已清除。"; return; }
            var modifiers = (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) ? 8u : 0);
            if (key is Key.LeftCtrl or Key.RightCtrl) modifiers |= 2u; else if (key is Key.LeftAlt or Key.RightAlt) modifiers |= 1u; else if (key is Key.LeftShift or Key.RightShift) modifiers |= 4u; else if (key is Key.LWin or Key.RWin) modifiers |= 8u;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            { box.Text = ModifierText(modifiers) + "…"; return; }
            var vk = KeyInterop.VirtualKeyFromKey(key);
            if (!string.IsNullOrWhiteSpace(action) && crosshair.SetHotkey(action, modifiers, vk)) { box.Text = crosshair.HotkeyText(action); box.Tag = null; crosshairHotkeyRecordingBox = null; crosshairHotkeyRecordingOriginal = null; RegisterHotkeys(); CrosshairRuntimeStatus.Text = modifiers == 0 ? "单键快捷键已更新；请注意可能与游戏或其他程序冲突。" : "快捷键已更新。"; }
            else { box.Text = "快捷键冲突或已被占用"; box.Tag = "error"; crosshairHotkeyRecordingBox = null; crosshairHotkeyRecordingOriginal = null; RegisterHotkeys(); CrosshairRuntimeStatus.Text = "快捷键冲突或注册失败，已保留原绑定。"; }
        }

        static string ModifierText(uint modifiers) => ((modifiers & 2) != 0 ? "Ctrl + " : "") + ((modifiers & 1) != 0 ? "Alt + " : "") + ((modifiers & 4) != 0 ? "Shift + " : "") + ((modifiers & 8) != 0 ? "Win + " : "");

        sealed class CrosshairElementItem
        {
            public CrosshairElementItem(CrosshairElement value) { Value = value; }
            public CrosshairElement Value { get; }
            public override string ToString() => (Value.Enabled ? "● " : "○ ") + Value.Name + " · " + Value.Kind;
        }
        sealed class CrosshairPresetListItem
        {
            readonly CrosshairPreset value; readonly bool dirty; bool includeInCycle;
            public CrosshairPresetListItem(CrosshairPreset preset, bool isDirty) { value = preset; dirty = isDirty; includeInCycle = preset.IncludeInCycle; }
            public string Id => value.Id;
            public string DisplayName => value.Name + (dirty ? " *" : "");
            public bool IncludeInCycle { get => includeInCycle; set => includeInCycle = value; }
            public override string ToString() => (value.IncludeInCycle ? "● " : "○ ") + value.Name + (dirty ? " *" : "");
        }
    }
}
