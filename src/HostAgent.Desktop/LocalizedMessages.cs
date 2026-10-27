using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace HostAgent.Desktop;

/// <summary>Product-owned dialogs use the selected language, including their buttons.</summary>
internal sealed class LocalizedMessages(DesktopLocalization localization)
{
    public MessageBoxResult Show(Window? owner, string text, string title="Host Agent", MessageBoxButton buttons=MessageBoxButton.OK, MessageBoxImage image=MessageBoxImage.None)
    {
        var window=Create(owner,title);
        localization.Apply(window);
        var panel=new DockPanel {Margin=new Thickness(24),LastChildFill=true};
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var result=MessageBoxResult.Cancel;
        var ok=new Button{Content=localization.Text("Confirm"),IsDefault=true,MinWidth=100,Padding=new Thickness(12,8,12,8),Margin=new Thickness(8,0,0,0)};
        ok.Click+=(_,_)=>{result=MessageBoxResult.OK;window.DialogResult=true;};actions.Children.Add(ok);
        if(buttons==MessageBoxButton.OKCancel)
        {
            var cancel=new Button{Content=localization.Text("Cancel"),IsCancel=true,MinWidth=100,Padding=new Thickness(12,8,12,8),Margin=new Thickness(8,0,0,0)};
            actions.Children.Add(cancel);
        }
        DockPanel.SetDock(actions,Dock.Bottom);panel.Children.Add(actions);
        panel.Children.Add(new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MaxHeight=480,Margin=new Thickness(0,0,0,20),
            Content=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap}});
        window.Content=panel;window.ShowDialog();return result;
    }

    private static Window Create(Window? owner,string title) => new()
    {
        Owner=owner,Title=title,Width=620,SizeToContent=SizeToContent.Height,MaxHeight=700,
        WindowStartupLocation=owner is null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner,
        ResizeMode=ResizeMode.NoResize
    };

    public static string? ChooseReportPath(Window owner, DesktopLocalization localization,string fileName)
    {
        var window=Create(owner,localization.Text("ExportIncident"));
        localization.Apply(window);
        var panel=new StackPanel{Margin=new Thickness(24)};
        panel.Children.Add(new TextBlock{Text=localization.Text("FullPathToJSONFile"),TextWrapping=TextWrapping.Wrap});
        var path=new TextBox{Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),fileName),Margin=new Thickness(0,8,0,16)};
        AutomationProperties.SetName(path,localization.Text("FullPathToJSONFile"));
        panel.Children.Add(path);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var save=new Button{Content=localization.Text("Save"),IsDefault=true,MinWidth=100,Padding=new Thickness(12,8,12,8)};
        var cancel=new Button{Content=localization.Text("Cancel"),IsCancel=true,MinWidth=100,Padding=new Thickness(12,8,12,8),Margin=new Thickness(8,0,0,0)};
        string? selected=null;
        save.Click+=(_,_)=>
        {
            if(!Path.IsPathFullyQualified(path.Text)||!string.Equals(Path.GetExtension(path.Text),".json",StringComparison.OrdinalIgnoreCase))
            {new LocalizedMessages(localization).Show(window,localization.Text("EnterTheFullPathToAFileWithTheJsonExtension"));return;}
            if(File.Exists(path.Text)&&new LocalizedMessages(localization).Show(window,localization.Text("TheFileAlreadyExistsDoYouWantToOverwriteIt"),buttons:MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            selected=path.Text;window.DialogResult=true;
        };
        actions.Children.Add(save);actions.Children.Add(cancel);panel.Children.Add(actions);window.Content=panel;
        window.ShowDialog();return selected;
    }
}
