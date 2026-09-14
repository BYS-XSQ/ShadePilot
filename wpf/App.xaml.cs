using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using DisplayPresetPrototype;
namespace ShadePilotWpf {
 public partial class App : Application {
  private Mutex instance;
  public App(){DispatcherUnhandledException+=OnDispatcherUnhandledException;AppDomain.CurrentDomain.UnhandledException+=(_,e)=>StartupLog.Write("AppDomain.UnhandledException",e.ExceptionObject as System.Exception);TaskScheduler.UnobservedTaskException+=(_,e)=>StartupLog.Write("TaskScheduler.UnobservedTaskException",e.Exception);}
  void OnDispatcherUnhandledException(object sender,DispatcherUnhandledExceptionEventArgs e)=>StartupLog.Write("DispatcherUnhandledException",e.Exception);
  protected override void OnStartup(StartupEventArgs e) {
   StartupLog.Write("App.Start");
   bool created; instance=new Mutex(true,@"Local\ShadePilot.Wpf.SingleInstance",out created);
   if(!created){MessageBox.Show("ShadePilot WPF 已经在运行。","ShadePilot");Shutdown();return;}
   ThemeManager.Load(); StartupLog.Write("ThemeLoaded"); base.OnStartup(e); var window=new MainWindow(); StartupLog.Write("MainWindowCreated"); window.Show();
  }
  protected override void OnExit(ExitEventArgs e){instance?.Dispose();base.OnExit(e);}
 }
}
