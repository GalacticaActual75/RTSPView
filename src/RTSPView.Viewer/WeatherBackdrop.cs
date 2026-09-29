using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using RTSPView.Core;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace RTSPView.Viewer;

// Lightweight procedural scenery, with no media downloads or video surfaces.
public sealed class WeatherBackdrop : FrameworkElement
{
    private readonly WeatherCondition _condition;
    private readonly bool _day;
    private readonly double _radius;
    private readonly bool _animate;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double _time;
    public WeatherBackdrop(WeatherCondition condition, bool day, bool animate, double radius)
    {
        _condition=condition;_day=day;_radius=radius;_animate=animate;IsHitTestVisible=false;
        _timer.Tick+=(_,_)=>{_time+=.05;InvalidateVisual();};
        Loaded+=(_,_)=>Sync();Unloaded+=(_,_)=>_timer.Stop();IsVisibleChanged+=(_,_)=>Sync();
    }
    private void Sync() { if(IsLoaded&&IsVisible&&_animate&&SystemParameters.ClientAreaAnimation)_timer.Start();else _timer.Stop(); }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    protected override void OnRender(DrawingContext dc)
    {
        var w=ActualWidth;var h=ActualHeight;if(w<=0||h<=0)return;
        var scene=_condition.Scene;
        dc.PushClip(new RectangleGeometry(new Rect(0,0,w,h),_radius,_radius));
        var baseColor=scene is "clear" or "partly" ? "#667C7A" : scene is "storm" or "hail" ? "#484962" : "#647D91";
        dc.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString(_day?"#21384D":"#101B30"),(Color)ColorConverter.ConvertFromString(_day?baseColor:"#334E78"),45),null,new Rect(0,0,w,h));
        if(scene is "clear" or "partly")
        {
            var r=Math.Min(w,h)*.12;var center=new Point(w*.82,h*.22);
            dc.DrawEllipse(Brush(_day?"#FFF0B8":"#E8EDDA"),null,center,r,r);
            if(!_day)dc.DrawEllipse(Brush("#233B5D"),null,new Point(center.X-r*.45,center.Y-r*.35),r*.9,r*.9);
        }
        if(scene is not ("clear" or "fog"))
        {
            var drift=Math.Sin(_time/9)*w*.04;
            dc.PushOpacity(_day?.36:.18);
            foreach(var (x,y,r) in new[]{(.76,.29,.16),(.9,.33,.12),(.67,.4,.19),(.43,.65,.15)})
                dc.DrawEllipse(Brush("#BACBDC"),null,new Point(w*x+drift,h*y),w*r,h*r*.65);
            dc.Pop();
        }
        var count=8+_condition.Intensity*8;
        if(scene is "rain" or "ice" or "storm" or "snow" or "hail")
        {
            for(var i=0;i<count;i++)
            {
                var speed=scene=="snow"?.10:scene=="hail"?.5:.6+_condition.Intensity*.15;
                var y=((i*.137+_time*speed)%1.2-.1)*h;
                var x=((i*.379+Math.Sin(_time/7+i)*.025)%1)*w;
                if(scene is "snow" or "hail")dc.DrawEllipse(Brush("#BBEEFAFF"),null,new Point(x,y),scene=="hail"?2.5:1.8,scene=="hail"?2.5:1.8);
                else dc.DrawLine(new Pen(Brush(scene=="ice"?"#999EDAFF":"#66D7EAFF"),1.1),new Point(x,y),new Point(x-w*.018,y+h*.07));
            }
        }
        if(scene=="fog")
        {
            for(var i=0;i<6;i++)dc.DrawEllipse(Brush("#20CEDCE2"),null,new Point(w*.5+Math.Sin(_time/9+i)*w*.1,h*i/5),w*.8,h*.12);
        }
        if(scene is "storm" or "hail")
        {
            var glow=Math.Pow(Math.Max(0,Math.Sin(_time*Math.PI*2/9)),16)*.2;
            dc.PushOpacity(glow);dc.DrawRectangle(Brush("#DAD7FF"),null,new Rect(0,0,w,h));dc.Pop();
        }
        dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(210,8,18,29),Color.FromArgb(65,8,18,29),0),null,new Rect(0,0,w,h));
        dc.Pop();
    }
}
