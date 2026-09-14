using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using DisplayPresetPrototype;

namespace ShadePilotWpf
{
 public partial class MainWindow : Window
 {
  private readonly List<PhysicalDisplay> displays=new();
  private readonly Dictionary<string,Snapshot> startup=new(StringComparer.OrdinalIgnoreCase);
  private readonly GammaRampController gamma=new();
  private readonly StyleSettingsStore styles=StyleSettingsStore.Load();
  private readonly AppSettingsStore settings=AppSettingsStore.Load();
  private readonly DispatcherTimer hardwareTimer=new(){Interval=TimeSpan.FromMilliseconds(180)};
  private bool loading;

  public MainWindow()
  {
   InitializeComponent();
   WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),CornerRadius=new CornerRadius(10),GlassFrameThickness=new Thickness(0),UseAeroCaptionButtons=false});
   foreach(var p in styles.Items)PresetBox.Items.Add(new ComboBoxItem{Content=p.Name,Tag=p});
   if(PresetBox.Items.Count>0)PresetBox.SelectedIndex=0;
   AutoSwitch.IsChecked=settings.GameAutoSwitchEnabled; RestoreAfterGame.IsChecked=settings.RestoreAfterGame; FrameProtection.IsChecked=settings.FrameGenerationCompatibility;
   hardwareTimer.Tick+=async (_,__)=>{hardwareTimer.Stop();if(LiveApply.IsChecked==true)await ApplyHardwareAsync();};
   Loaded+=async (_,__)=>await RefreshDisplaysAsync();
   Closing+=OnClosing;
  }

  private void Header_MouseLeftButtonDown(object sender,MouseButtonEventArgs e){if(e.ClickCount==2)ToggleMaximize();else if(e.LeftButton==MouseButtonState.Pressed)DragMove();}
  private void Minimize_Click(object s,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
  private void Maximize_Click(object s,RoutedEventArgs e)=>ToggleMaximize();
  private void Close_Click(object s,RoutedEventArgs e)=>Close();
  private void ToggleMaximize(){WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;MaxButton.Content=WindowState==WindowState.Maximized?"❐":"□";}

  private void Nav_Checked(object sender,RoutedEventArgs e)
  {
   if(DisplayPage==null)return; string tag=(sender as FrameworkElement)?.Tag?.ToString()??"Display";
   DisplayPage.Visibility=tag=="Display"?Visibility.Visible:Visibility.Collapsed;
   StylePage.Visibility=tag=="Style"?Visibility.Visible:Visibility.Collapsed;
   ToolsPage.Visibility=tag=="Tools"?Visibility.Visible:Visibility.Collapsed;
  }

  private async void Refresh_Click(object s,RoutedEventArgs e)=>await RefreshDisplaysAsync();
  private async Task RefreshDisplaysAsync()
  {
   DisplayStatus.Text="正在检测显示器…";
   var found=await Task.Run(()=>{var list=MonitorApi.Enumerate();foreach(var d in list){try{d.Refresh();}catch{d.MarkProbeFailed();}}return list;});
   foreach(var d in displays)d.Dispose(); displays.Clear(); displays.AddRange(found);
   MonitorBox.ItemsSource=null;MonitorBox.ItemsSource=displays.ConvertAll(d=>new DisplayChoice(d));
   if(displays.Count>0)MonitorBox.SelectedIndex=0;
   DisplayStatus.Text=$"已检测到 {displays.Count} 个物理显示器。";
  }

  private void MonitorBox_SelectionChanged(object sender,SelectionChangedEventArgs e)
  {
   var d=(MonitorBox.SelectedItem as DisplayChoice)?.Value;if(d==null)return;loading=true;
   BrightnessSlider.IsEnabled=d.SupportsBrightness;BrightnessSlider.Minimum=d.BrightnessMin;BrightnessSlider.Maximum=Math.Max(d.BrightnessMin+1,d.BrightnessMax);BrightnessSlider.Value=d.BrightnessCurrent;
   ContrastSlider.IsEnabled=d.SupportsContrast;ContrastSlider.Minimum=d.ContrastMin;ContrastSlider.Maximum=Math.Max(d.ContrastMin+1,d.ContrastMax);ContrastSlider.Value=d.ContrastCurrent;
   SaturationSlider.IsEnabled=d.SupportsSaturation;SaturationSlider.Minimum=d.SaturationMin;SaturationSlider.Maximum=Math.Max(d.SaturationMin+1,d.SaturationMax);SaturationSlider.Value=d.SaturationCurrent;
   gamma.SetTarget(d.DisplayDeviceName);if(!startup.ContainsKey(d.Id))startup[d.Id]=Snapshot.From(d);loading=false;
   DisplayStatus.Text=d.ProbeNote??"显示器参数已载入。";
  }

  private void HardwareSlider_ValueChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(loading)return;hardwareTimer.Stop();hardwareTimer.Start();}
  private async void ApplyHardware_Click(object s,RoutedEventArgs e)=>await ApplyHardwareAsync();
  private async Task ApplyHardwareAsync()
  {
   var d=(MonitorBox.SelectedItem as DisplayChoice)?.Value;if(d==null)return;
   uint b=(uint)Math.Round(BrightnessSlider.Value),c=(uint)Math.Round(ContrastSlider.Value),sat=(uint)Math.Round(SaturationSlider.Value);
   DisplayStatus.Text="正在写入并回读确认…";
   try{await Task.Run(()=>{if(d.SupportsBrightness)d.SetBrightness(b);if(d.SupportsContrast)d.SetContrast(c);if(d.SupportsSaturation)d.SetSaturation(sat);});DisplayStatus.Text="硬件参数已应用并确认。";}
   catch(Exception ex){DisplayStatus.Text="应用失败："+ex.Message;}
  }

  private void PresetBox_SelectionChanged(object sender,SelectionChangedEventArgs e)
  {
   var p=(PresetBox.SelectedItem as ComboBoxItem)?.Tag as StylePresetData;if(p==null)return;
   Shadows.Value=p.Shadows;Highlights.Value=p.Highlights;Midtones.Value=p.Midtones;BlackPoint.Value=p.BlackPoint;WhitePoint.Value=p.WhitePoint;ContrastPivot.Value=p.ContrastPivot;
   Temperature.Value=p.Temperature;TintSlider.Value=p.Tint;Vibrance.Value=p.Vibrance;Exposure.Value=p.Exposure;ShadowRange.Value=p.ShadowRange;HighlightRange.Value=p.HighlightRange;Softness.Value=p.TransitionSoftness;
  }
  private void ApplyStyle_Click(object s,RoutedEventArgs e)
  {
   var d=(MonitorBox.SelectedItem as DisplayChoice)?.Value;if(d!=null)gamma.SetTarget(d.DisplayDeviceName);
   try{gamma.Apply(I(Shadows),I(Highlights),I(Midtones),I(BlackPoint),I(WhitePoint),I(ContrastPivot),I(Temperature),I(TintSlider),I(Vibrance),I(Exposure),I(ShadowRange),I(HighlightRange),I(Softness));StyleStatus.Text="画面风格已应用。";}
   catch(Exception ex){StyleStatus.Text="应用失败："+ex.Message;}
  }
  private static int I(Slider slider)=>(int)Math.Round(slider.Value);

  private sealed class DisplayChoice
  {
   public DisplayChoice(PhysicalDisplay value){Value=value;Name=value.Name;}
   public string Name{get;}
   public PhysicalDisplay Value{get;}
  }

  private void OnClosing(object sender,System.ComponentModel.CancelEventArgs e)
  {
   settings.GameAutoSwitchEnabled=AutoSwitch.IsChecked==true;settings.RestoreAfterGame=RestoreAfterGame.IsChecked==true;settings.FrameGenerationCompatibility=FrameProtection.IsChecked==true;settings.Save();
   if(RestoreOnExit.IsChecked==true){try{gamma.RestoreAll();}catch{}foreach(var d in displays){if(startup.TryGetValue(d.Id,out var snap)){try{snap.Apply(d);}catch{}}}}
   foreach(var d in displays)d.Dispose();
  }
 }
}
