using Microsoft.UI.Xaml.Controls;
using System;
using Windows.ApplicationModel;

namespace MediaFileManager.Desktop.Windows;

public sealed partial class AboutWindow : ContentDialog
{
    public AboutWindow()
    {
        InitializeComponent();
        Loaded += AboutWindow_Loaded;
    }

    private void AboutWindow_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        CopyrightTextBlock.Text = $"Copyright 2017-{DateTime.Now.Year}, Lancelot Software";

        try
        {
            var v = Package.Current.Id.Version;
            VersionTextBlock.Text = $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            VersionTextBlock.Text = "dev";
        }
    }
}
