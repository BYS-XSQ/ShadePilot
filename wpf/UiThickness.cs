namespace ShadePilotWpf
{
 internal readonly struct UiThickness
 {
  readonly System.Windows.Thickness value;
  public UiThickness(double uniform){value=new System.Windows.Thickness(uniform);}
  public UiThickness(double horizontal,double vertical){value=new System.Windows.Thickness(horizontal,vertical,horizontal,vertical);}
  public UiThickness(double left,double top,double right,double bottom){value=new System.Windows.Thickness(left,top,right,bottom);}
  public static implicit operator System.Windows.Thickness(UiThickness value)=>value.value;
 }
}
