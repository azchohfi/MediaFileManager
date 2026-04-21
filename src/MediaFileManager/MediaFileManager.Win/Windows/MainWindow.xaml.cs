using MediaFileManager.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace MediaFileManager.Desktop.Windows;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;

        FileTypeComboBox.ItemsSource = new List<string> { "Videos", "Audiobooks", "Music" };

        FileTypeComboBox.SelectedIndex = App.GetLocalSetting("SelectedViewIndex", 0);
    }

    private void FileTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileTypeComboBox.SelectedItem is not string selectedItem)
            return;

        ContentBorder.Child = selectedItem switch
        {
            "Videos" => new VideoFilesView(this),
            "Audiobooks" => new AudiobookFilesView(this),
            "Music" => new MusicFilesView(this),
            _ => null
        };

        Title = $"Media File Manager - {selectedItem}";
        App.SetLocalSetting("SelectedViewIndex", FileTypeComboBox.SelectedIndex);
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var helpWindow = new HelpWindow();
        helpWindow.Activate();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AboutWindow { XamlRoot = Content.XamlRoot };
        _ = dialog.ShowAsync();
    }
}
