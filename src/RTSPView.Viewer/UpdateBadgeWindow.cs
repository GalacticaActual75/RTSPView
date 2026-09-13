using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RTSPView.Core;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace RTSPView.Viewer;

public sealed class UpdateBadgeWindow : Window
{
    private readonly Button _button;
    private WallUpdateNotice? _notice;
    private readonly Func<WallUpdateRequest, Task<WallUpdateResponse>> _install;
    private readonly Func<Func<bool>, bool> _showDialog;
    public bool Busy { get; private set; }
    public event EventHandler? PointerActivity;

    public UpdateBadgeWindow(Window owner, Func<WallUpdateRequest, Task<WallUpdateResponse>>? install = null, Func<Func<bool>, bool>? showDialog = null)
    {
        _install=install??SendAsync;
        _showDialog=showDialog??(show=>show());
        Owner=owner; Title="RTSPView update available"; WindowStyle=WindowStyle.None;
        AllowsTransparency=true; Background=Brushes.Transparent; ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false; ShowActivated=false; Width=178; Height=38;
        _button = new Button {Content="↑ Update available",Foreground=new SolidColorBrush(Color.FromRgb(152,198,241)),
            Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(12,6,12,6),FontSize=13,
            ToolTip="Install the available RTSPView update",Cursor=Cursors.Hand};
        System.Windows.Automation.AutomationProperties.SetName(_button,"Install available RTSPView update");
        Content=new Border {Background=new SolidColorBrush(Color.FromArgb(225,20,25,32)),BorderBrush=new SolidColorBrush(Color.FromArgb(180,70,85,103)),
            BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Child=_button};
        MouseMove+=(_,_)=>PointerActivity?.Invoke(this,EventArgs.Empty);
        _button.Click+=async (_,_)=>await InstallAsync();
    }

    public void SetNotice(WallUpdateNotice? notice) => _notice=notice;

    private async Task InstallAsync()
    {
        if(Busy || _notice is not {Visible:true,Version:not null} notice)return;
        Busy=true; PointerActivity?.Invoke(this,EventArgs.Empty);
        try
        {
            if(!Confirm(notice.Version,notice.Channel))return;
            _button.IsEnabled=false; _button.Content="Preparing update…";
            var result=await _install(new WallUpdateRequest(notice.Version,notice.Channel,true));
            if(!result.Started)_showDialog(()=>{MessageBox.Show(Owner,result.Message,"RTSPView update",MessageBoxButton.OK,MessageBoxImage.Information);return false;});
            else Hide();
        }
        catch(Exception error) when(error is IOException or OperationCanceledException or JsonException)
        { _showDialog(()=>{MessageBox.Show(Owner,"The Controller could not confirm the update request. Check System → Updates before trying again.","RTSPView update",MessageBoxButton.OK,MessageBoxImage.Information);return false;}); }
        finally {Busy=false;_button.IsEnabled=true;_button.Content="↑ Update available";PointerActivity?.Invoke(this,EventArgs.Empty);}
    }

    private static async Task<WallUpdateResponse> SendAsync(WallUpdateRequest request)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var pipe=new NamedPipeClientStream(".","RTSPView.WallUpdates.v1",PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        using var connect=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);connect.CancelAfter(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(connect.Token);
        await using var writer=new StreamWriter(pipe,leaveOpen:true){AutoFlush=true};
        using var reader=new StreamReader(pipe,leaveOpen:true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        var line=await reader.ReadLineAsync(timeout.Token)??throw new IOException();
        return JsonSerializer.Deserialize<WallUpdateResponse>(line)??throw new InvalidDataException();
    }

    private bool Confirm(string version,string channel)
    {
        var dialog=new Window {Owner=this,Title="Install RTSPView update",Width=430,SizeToContent=SizeToContent.Height,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,
            Background=new SolidColorBrush(Color.FromRgb(22,28,36)),Foreground=Brushes.White};
        // Center on the viewer, rather than the small corner badge.
        dialog.Owner=Owner; dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel {Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock {Text=$"Install {channel} {version} now?",FontSize=18,Margin=new Thickness(0,0,0,12),TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(new TextBlock {Text="The stream wall will briefly restart. Export your configuration from System first if needed. Windows may ask for administrator approval.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,20)});
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button {Content="_Cancel",IsCancel=true,MinWidth=85,Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,10,0)};
        var install=new Button {Content="_Install",MinWidth=85,Padding=new Thickness(12,7,12,7)};
        install.Click+=(_,_)=>dialog.DialogResult=true;
        buttons.Children.Add(cancel);buttons.Children.Add(install);panel.Children.Add(buttons);dialog.Content=panel;
        dialog.Loaded+=(_,_)=>cancel.Focus();
        return _showDialog(()=>dialog.ShowDialog()==true);
    }
}
