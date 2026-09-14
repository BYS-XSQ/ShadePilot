using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DisplayPresetPrototype;
using Thickness=ShadePilotWpf.UiThickness;

namespace ShadePilotWpf
{
 internal abstract class LpDialog:Window
 {
  protected readonly Grid Body=new();
  protected LpDialog(Window owner,string title,double width,double height)
  {
   Owner=owner;Title=title;Width=width;Height=height;MinWidth=720;MinHeight=520;WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.CanResize;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;
   Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("LpStyles.xaml",UriKind.Relative)});foreach(string key in new[]{"Canvas","Surface","Tint","Soft","Accent","AccentSoft","Text","Muted","Border"})Resources[key]=owner.FindResource(key);Background=(Brush)Resources["Canvas"];
   var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(72)});root.RowDefinitions.Add(new RowDefinition());Content=root;
   var header=new Border{Background=(Brush)Resources["Tint"],BorderBrush=(Brush)Resources["Border"],BorderThickness=new Thickness(0,0,0,1)};header.MouseLeftButtonDown+=(_,e)=>{if(e.LeftButton==MouseButtonState.Pressed)DragMove();};var hg=new Grid{Margin=new Thickness(24,0)};hg.ColumnDefinitions.Add(new ColumnDefinition());hg.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});header.Child=hg;hg.Children.Add(new TextBlock{Text=title,FontSize=20,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});var close=new Button{Content="×",Style=S("LPWindowControl"),VerticalAlignment=VerticalAlignment.Center};close.Click+=(_,__)=>Close();Grid.SetColumn(close,1);hg.Children.Add(close);root.Children.Add(header);Body.Margin=new Thickness(24,20,24,22);Grid.SetRow(Body,1);root.Children.Add(Body);
  }
  protected Style S(string key)=>(Style)FindResource(key);
  protected Button Button(string text,bool primary=false){return new Button{Content=text,Style=S(primary?"LPPrimary":"LPSecondary"),MinWidth=88,Margin=new Thickness(8,0,0,0)};}
  protected Border Card()=>new(){Style=S("LPCard")};
  protected TextBox Input(string text=""){return new TextBox{Text=text,Style=S("LPTextBox"),Padding=new Thickness(10,4)};}
 }

 internal sealed class PresetManagerWindow:LpDialog
 {
  readonly StyleSettingsStore store;readonly AppSettingsStore settings;readonly ListBox list=new();readonly TextBox name;readonly Dictionary<string,TextBox> fields=new();StylePresetData current;
  static readonly (string key,string label,int min,int max)[] Specs={
   ("Shadows","提亮暗部",0,200),("Highlights","压低高光",0,200),("Midtones","中间调",-100,100),("BlackPoint","黑位",-100,100),("WhitePoint","白位",-100,100),("ContrastPivot","中间对比",-100,100),("Temperature","色温",-100,100),("Tint","色调",-100,100),("Vibrance","自然色彩",-100,100),("Saturation","饱和度（50 中性）",0,100),("Exposure","整体曝光",-100,100),("ShadowRange","暗部范围",-100,100),("HighlightRange","高光范围",-100,100),("TransitionSoftness","过渡柔和",-100,100)};
  public PresetManagerWindow(Window owner,StyleSettingsStore store,AppSettingsStore settings,StylePresetData selected):base(owner,"预设管理",940,680)
  {
   this.store=store;this.settings=settings;Body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)});Body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(16)});Body.ColumnDefinitions.Add(new ColumnDefinition());
   var left=Card();var lp=new DockPanel();left.Child=lp;var leftTitle=new TextBlock{Text="画面风格",FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=(Brush)Resources["Accent"],Margin=new Thickness(0,0,0,12)};DockPanel.SetDock(leftTitle,Dock.Top);lp.Children.Add(leftTitle);list.Style=S("LPListBox");list.BorderThickness=new Thickness(0);list.SelectionChanged+=(_,__)=>Load(list.SelectedItem as StylePresetData);lp.Children.Add(list);Body.Children.Add(left);
   var right=Card();Grid.SetColumn(right,2);Body.Children.Add(right);var rp=new Grid();rp.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});rp.RowDefinitions.Add(new RowDefinition());rp.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});right.Child=rp;
   var head=new Grid();head.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(70)});head.ColumnDefinitions.Add(new ColumnDefinition());head.Children.Add(new TextBlock{Text="名称",VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold});name=Input();Grid.SetColumn(name,1);head.Children.Add(name);rp.Children.Add(head);
   var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,16,0,14)};Grid.SetRow(scroll,1);rp.Children.Add(scroll);var grid=new UniformGrid{Columns=3};scroll.Content=grid;foreach(var s in Specs){var box=new StackPanel{Margin=new Thickness(0,0,12,12)};box.Children.Add(new TextBlock{Text=s.label,Foreground=(Brush)Resources["Muted"],Margin=new Thickness(0,0,0,5)});var input=Input("0");box.Children.Add(input);fields[s.key]=input;grid.Children.Add(box);}
   var actions=new DockPanel();Grid.SetRow(actions,2);rp.Children.Add(actions);var restore=Button("恢复缺失预设");restore.Click+=(_,__)=>{store.RestoreBuiltInPresets();Refresh(null);};DockPanel.SetDock(restore,Dock.Left);actions.Children.Add(restore);var setDefault=Button("设为启动默认");setDefault.Click+=(_,__)=>SetDefault();DockPanel.SetDock(setDefault,Dock.Right);actions.Children.Add(setDefault);var delete=Button("删除预设");delete.Click+=(_,__)=>Delete();DockPanel.SetDock(delete,Dock.Right);actions.Children.Add(delete);var save=Button("保存预设修改",true);save.Click+=(_,__)=>Save();DockPanel.SetDock(save,Dock.Right);actions.Children.Add(save);
   Refresh(selected);
  }
  void Refresh(StylePresetData select){list.ItemsSource=null;list.ItemsSource=store.Items;if(select!=null)list.SelectedItem=select;if(list.SelectedIndex<0&&store.Items.Count>0)list.SelectedIndex=0;}
  void Load(StylePresetData p){if(p==null)return;current=p;name.Text=p.Name;foreach(var s in Specs)fields[s.key].Text=Get(p,s.key).ToString();}
  void Save(){if(current==null||string.IsNullOrWhiteSpace(name.Text))return;string oldName=current.Name;current.Name=name.Text.Trim();foreach(var s in Specs){if(!int.TryParse(fields[s.key].Text,out int v))v=0;Set(current,s.key,Math.Clamp(v,s.min,s.max));}if(string.Equals(settings.DefaultPresetName,oldName,StringComparison.OrdinalIgnoreCase))settings.DefaultPresetName=current.Name;store.Save();settings.Save();Refresh(current);}
  void SetDefault(){if(current==null)return;settings.DefaultPresetName=current.Name;settings.Save();MessageBox.Show(this,"“"+current.Name+"”已设为启动默认预设。启动时只会自动选中，不会自动改动显示参数。","预设管理",MessageBoxButton.OK,MessageBoxImage.Information);}
  void Delete(){if(current==null)return;if(MessageBox.Show(this,"删除预设“"+current.Name+"”？","预设管理",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;if(string.Equals(settings.DefaultPresetName,current.Name,StringComparison.OrdinalIgnoreCase)){settings.DefaultPresetName=null;settings.Save();}store.Items.Remove(current);store.Save();current=null;Refresh(null);}
  static int Get(StylePresetData p,string k)=>k switch{"Shadows"=>p.Shadows,"Highlights"=>p.Highlights,"Midtones"=>p.Midtones,"BlackPoint"=>p.BlackPoint,"WhitePoint"=>p.WhitePoint,"ContrastPivot"=>p.ContrastPivot,"Temperature"=>p.Temperature,"Tint"=>p.Tint,"Vibrance"=>p.Vibrance,"Saturation"=>p.Saturation,"Exposure"=>p.Exposure,"ShadowRange"=>p.ShadowRange,"HighlightRange"=>p.HighlightRange,_=>p.TransitionSoftness};
  static void Set(StylePresetData p,string k,int v){switch(k){case"Shadows":p.Shadows=v;break;case"Highlights":p.Highlights=v;break;case"Midtones":p.Midtones=v;break;case"BlackPoint":p.BlackPoint=v;break;case"WhitePoint":p.WhitePoint=v;break;case"ContrastPivot":p.ContrastPivot=v;break;case"Temperature":p.Temperature=v;break;case"Tint":p.Tint=v;break;case"Vibrance":p.Vibrance=v;break;case"Saturation":p.Saturation=v;break;case"Exposure":p.Exposure=v;break;case"ShadowRange":p.ShadowRange=v;break;case"HighlightRange":p.HighlightRange=v;break;default:p.TransitionSoftness=v;break;}}
 }

 internal sealed class HotkeyWindow:LpDialog
 {
  readonly StyleSettingsStore store;readonly ListBox targets=new();readonly TextBox input;readonly TextBlock status;readonly StackPanel cycleChoices=new();readonly Dictionary<string,CheckBox> cycleChecks=new(StringComparer.OrdinalIgnoreCase);Target current;int key;uint modifiers;
  public HotkeyWindow(Window owner,StyleSettingsStore store):base(owner,"后台快捷键",760,560)
  {
   this.store=store;Body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(280)});Body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(16)});Body.ColumnDefinitions.Add(new ColumnDefinition());var left=Card();left.Child=targets;targets.Style=S("LPListBox");targets.BorderThickness=new Thickness(0);targets.SelectionChanged+=(_,__)=>Load(targets.SelectedItem as Target);Body.Children.Add(left);var right=Card();Grid.SetColumn(right,2);Body.Children.Add(right);var p=new StackPanel();right.Child=p;p.Children.Add(new TextBlock{Text="设置快捷键",FontSize=16,FontWeight=FontWeights.SemiBold,Foreground=(Brush)Resources["Accent"]});p.Children.Add(new TextBlock{Text="点击输入框后直接按组合键；Backspace 清除。",Foreground=(Brush)Resources["Muted"],Margin=new Thickness(0,6,0,14),TextWrapping=TextWrapping.Wrap});input=Input("未设置");input.IsReadOnly=true;input.TextAlignment=TextAlignment.Center;input.PreviewKeyDown+=Capture;p.Children.Add(input);var row=new WrapPanel{Margin=new Thickness(0,12,0,0)};var save=Button("保存快捷键",true);save.Click+=(_,__)=>Save();var clear=Button("清除");clear.Click+=(_,__)=>{key=0;modifiers=0;input.Text="未设置";};row.Children.Add(save);row.Children.Add(clear);p.Children.Add(row);p.Children.Add(new TextBlock{Text="参与循环的预设",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,18,0,6)});p.Children.Add(new TextBlock{Text="循环快捷键至少选择两个模式；其他快捷键不受影响。",Foreground=(Brush)Resources["Muted"],TextWrapping=TextWrapping.Wrap});var cycleScroll=new ScrollViewer{Height=145,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,8,0,0),Content=cycleChoices};p.Children.Add(cycleScroll);foreach(var preset in store.Items){var cb=new CheckBox{Content=preset.Name,Style=S("LPCheck"),IsChecked=store.CyclePresetNames?.Exists(n=>string.Equals(n,preset.Name,StringComparison.OrdinalIgnoreCase))==true};cycleChoices.Children.Add(cb);cycleChecks[preset.Name]=cb;}status=new TextBlock{Foreground=(Brush)Resources["Muted"],Margin=new Thickness(0,12,0,0),TextWrapping=TextWrapping.Wrap};p.Children.Add(status);Refresh();
  }
  void Refresh(){var all=store.Items.Select(x=>new Target{x=x,name=x.Name}).ToList();all.Add(new Target{name="恢复原始画面",kind=1});all.Add(new Target{name="循环切换预设",kind=2});targets.ItemsSource=all;if(all.Count>0)targets.SelectedIndex=0;}
  void Load(Target t){if(t==null)return;current=t;if(t.kind==1){key=store.RestoreHotkeyKey;modifiers=store.RestoreHotkeyModifiers;}else if(t.kind==2){key=store.CycleHotkeyKey;modifiers=store.CycleHotkeyModifiers;}else{key=t.x.HotkeyKey;modifiers=t.x.HotkeyModifiers;}input.Text=Format(modifiers,key);status.Text="保存后主窗口会立即重新注册；若被占用，工具中心会显示具体 Win32 错误。";}
  void Capture(object s,KeyEventArgs e){e.Handled=true;if(e.Key is Key.Back or Key.Delete){key=0;modifiers=0;input.Text="未设置";return;}if(e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)return;modifiers=(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)?1u:0)|(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)?2u:0)|(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?4u:0);key=KeyInterop.VirtualKeyFromKey(e.Key);input.Text=Format(modifiers,key);}
  void Save(){if(current==null)return;if(current.kind==1){store.RestoreHotkeyKey=key;store.RestoreHotkeyModifiers=modifiers;}else if(current.kind==2){var selected=cycleChecks.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToList();if(key!=0&&selected.Count<2){status.Text="循环快捷键至少需要选择两个预设。";return;}store.CycleHotkeyKey=key;store.CycleHotkeyModifiers=modifiers;store.CyclePresetNames.Clear();store.CyclePresetNames.AddRange(selected);}else{current.x.HotkeyKey=key;current.x.HotkeyModifiers=modifiers;}RemoveDuplicates(current);store.Save();status.Text="已保存。关闭窗口后立即生效。";}
  void RemoveDuplicates(Target keep){foreach(var p in store.Items)if(!ReferenceEquals(p,keep.x)&&p.HotkeyKey==key&&p.HotkeyModifiers==modifiers){p.HotkeyKey=0;p.HotkeyModifiers=0;}if(keep.kind!=1&&store.RestoreHotkeyKey==key&&store.RestoreHotkeyModifiers==modifiers){store.RestoreHotkeyKey=0;store.RestoreHotkeyModifiers=0;}if(keep.kind!=2&&store.CycleHotkeyKey==key&&store.CycleHotkeyModifiers==modifiers){store.CycleHotkeyKey=0;store.CycleHotkeyModifiers=0;}}
  static string Format(uint m,int k){if(k==0)return"未设置";string s="";if((m&2)!=0)s+="Ctrl + ";if((m&1)!=0)s+="Alt + ";if((m&4)!=0)s+="Shift + ";return s+((System.Windows.Forms.Keys)k);}
  sealed class Target{public StylePresetData x;public int kind;public string name;public override string ToString()=>name;}
 }
}
