using MarkdownSharp;
using Microsoft.UI.Xaml;
using System;
using System.Net.Http;

namespace MediaFileManager.Desktop.Windows;

public sealed partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        HelpWebView.Loaded += HelpWindow_Loaded;
    }

    private async void HelpWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await HelpWebView.EnsureCoreWebView2Async();

            using var client = new HttpClient();
            var markdownText = await client.GetStringAsync(
                new Uri("https://raw.githubusercontent.com/LanceMcCarthy/MediaFileManager/main/.github/other/help.md"));

            var markdown = new Markdown();
            var html = $"<html><body style='font-family:Segoe UI;font-size:14px;padding:16px'>{markdown.Transform(markdownText)}</body></html>";

            HelpWebView.NavigateToString(html);
        }
        catch
        {
            HelpWebView.NavigateToString("<html><body style='font-family:Segoe UI;font-size:14px;padding:16px'><p>Failed to load help content. Please visit <a href='https://github.com/LanceMcCarthy/MediaFileManager'>the GitHub repository</a>.</p></body></html>");
        }
        finally
        {
            BusyOverlay.Visibility = Visibility.Collapsed;
        }
    }
}
