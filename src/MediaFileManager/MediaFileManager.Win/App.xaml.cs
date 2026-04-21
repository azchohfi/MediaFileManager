using MediaFileManager.Desktop.Windows;
using Microsoft.UI.Xaml;
using System;
using Windows.Storage;

namespace MediaFileManager.Desktop;

public partial class App : Application
{
    private Window window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new MainWindow();
        window.Activate();
    }

    public static T GetLocalSetting<T>(string key, T defaultValue = default)
    {
        try
        {
            var value = ApplicationData.Current.LocalSettings.Values[key];
            return value is T typedValue ? typedValue : defaultValue;
        }
        catch (InvalidOperationException)
        {
            return defaultValue;
        }
    }

    public static void SetLocalSetting(string key, object value)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
        }
        catch (InvalidOperationException)
        {
        }
    }
}