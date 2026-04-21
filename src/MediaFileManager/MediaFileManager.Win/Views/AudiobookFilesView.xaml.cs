using MediaFileManager.Desktop.Models;
using MediaFileManager.Desktop.Models.AudioBook;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace MediaFileManager.Desktop.Views;

public sealed partial class AudiobookFilesView : UserControl
{
    private readonly Window window;
    private readonly ObservableCollection<OutputMessage> statusMessages = [];
    private readonly ObservableCollection<string> audiobookTitles = [];
    private readonly ObservableCollection<AudiobookFile> audiobookFiles = [];
    private string lastPickedFolder;
    private CancellationTokenSource cts;

    public AudiobookFilesView(Window window)
    {
        InitializeComponent();
        this.window = window;

        StatusListBox.ItemsSource = statusMessages;
        AudiobookTitlesListBox.ItemsSource = audiobookTitles;
        AudiobookFilesGridView.ItemsSource = audiobookFiles;

        lastPickedFolder = App.GetLocalSetting<string>("LastFolder");

        WriteOutput("Ready, open an author folder to begin.", OutputMessageLevel.Success);

        Unloaded += (s, e) => cts?.Cancel();
    }

    private async void SelectAuthorFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WriteOutput("Opening folder picker...", OutputMessageLevel.Normal);
            ShowBusy("opening folder...", true);

            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

            var folder = await picker.PickSingleFolderAsync();

            if (folder == null)
            {
                WriteOutput("Canceled folder selection.", OutputMessageLevel.Normal);
                return;
            }

            lastPickedFolder = folder.Path;
            App.SetLocalSetting("LastFolder", folder.Path);

            Reset();

            UpdateBusyText("searching for albums...");

            var folders = Directory.EnumerateDirectories(folder.Path).ToList();
            audiobookTitles.Clear();

            foreach (var f in folders)
            {
                audiobookTitles.Add(f);
                UpdateBusyText($"added {Path.GetFileName(f)}");
            }

            switch (audiobookTitles.Count)
            {
                case 0:
                    WriteOutput("No titles detected.", OutputMessageLevel.Error);
                    await ShowErrorAsync("The selected Author's folder should have subfolders named with each audiobook's title.");
                    break;
                case 1:
                    WriteOutput($"Opened '{folder.Name}' ({audiobookTitles.Count} title).", OutputMessageLevel.Success);
                    break;
                default:
                    WriteOutput($"Opened '{folder.Name}' ({audiobookTitles.Count} titles).", OutputMessageLevel.Success);
                    break;
            }
        }
        catch (Exception ex)
        {
            WriteOutput(ex.Message, OutputMessageLevel.Error);
            await ShowErrorAsync(ex.Message);
        }
        finally
        {
            HideBusy();
        }
    }

    private void AudiobookTitlesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshFileList();
    }

    private void AudiobookFilesGridView_SelectionChanged(object sender, Telerik.UI.Xaml.Controls.Grid.DataGridSelectionChangedEventArgs e)
    {
        var firstItem = AudiobookFilesGridView.SelectedItem as AudiobookFile;

        if (firstItem == null)
            return;

        if (!string.IsNullOrEmpty(firstItem.Artist))
            ArtistTextBox.Text = firstItem.Artist;

        if (!string.IsNullOrEmpty(firstItem.Album))
            AlbumNameTextBox.Text = firstItem.Album;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        Reset();
        WriteOutput("Reset complete! Open a folder to continue.", OutputMessageLevel.Success);
    }

    private void AlbumNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTagsButton.IsEnabled = !string.IsNullOrEmpty(AlbumNameTextBox.Text)
                                  || !string.IsNullOrEmpty(ArtistTextBox.Text);
    }

    private void ArtistTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTagsButton.IsEnabled = !string.IsNullOrEmpty(AlbumNameTextBox.Text)
                                  || !string.IsNullOrEmpty(ArtistTextBox.Text);
    }

    private async void SetTagsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SetAlbumNameCheckBox.IsChecked == true && string.IsNullOrEmpty(AlbumNameTextBox.Text))
        {
            WriteOutput("Album (book title) is empty.", OutputMessageLevel.Error);
            return;
        }

        if (SetArtistNameCheckBox.IsChecked == true && string.IsNullOrEmpty(ArtistTextBox.Text))
        {
            WriteOutput("Artist (author name) is empty.", OutputMessageLevel.Error);
            return;
        }

        if (AudiobookFilesGridView.SelectedItems == null || !AudiobookFilesGridView.SelectedItems.Any())
        {
            WriteOutput("No files have been selected.", OutputMessageLevel.Error);
            return;
        }

        var selectedFiles = AudiobookFilesGridView.SelectedItems.OfType<AudiobookFile>().ToList();

        var tagParams = new TagWorkerParameters(selectedFiles)
        {
            UpdateAlbumName = SetAlbumNameCheckBox.IsChecked,
            UpdateTitle = SetTitleCheckBox.IsChecked,
            UpdateArtistName = SetArtistNameCheckBox.IsChecked
        };

        ShowBusy("updating tags...", false);

        var progress = new Progress<(int percent, string msg)>(p =>
        {
            BusyProgressBar.Value = p.percent;
            BusyTextBlock.Text = p.msg;
        });

        cts = new CancellationTokenSource();

        try
        {
            var finalMessage = await Task.Run(() => DoTagWork(tagParams, progress, cts.Token), cts.Token);
            WriteOutput(finalMessage, OutputMessageLevel.Success);
            RefreshFileList();
        }
        catch (OperationCanceledException)
        {
            WriteOutput("Tag update cancelled.", OutputMessageLevel.Warning);
        }
        catch (Exception ex)
        {
            WriteOutput(ex.Message, OutputMessageLevel.Error);
        }
        finally
        {
            HideBusy();
        }
    }

    private string DoTagWork(TagWorkerParameters tagParams, IProgress<(int, string)> progress, CancellationToken ct)
    {
        for (int i = 0; i < tagParams.AudiobookFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var audiobookFile = tagParams.AudiobookFiles[i];

            using var tagLibFile = TagLib.File.Create(audiobookFile.FilePath);
            if (tagLibFile != null)
            {
                if (tagParams.UpdateAlbumName == true)
                    tagLibFile.Tag.Album = audiobookFile.Album;

                if (tagParams.UpdateTitle == true)
                    tagLibFile.Tag.Title = Path.GetFileNameWithoutExtension(audiobookFile.FilePath);

                if (tagParams.UpdateArtistName == true)
                {
                    var author = new[] { audiobookFile.Artist };
#pragma warning disable CS0618
                    tagLibFile.Tag.Artists = author;
#pragma warning restore CS0618
                    tagLibFile.Tag.Performers = author;
                    tagLibFile.Tag.AlbumArtists = author;
                    tagLibFile.Tag.Composers = author;
                }

                tagLibFile.Save();
            }

            var pct = (int)((double)(i + 1) / tagParams.AudiobookFiles.Count * 100);
            progress.Report((pct, $"Updating tags, {pct}%..."));
        }

        return $"Complete, updated {tagParams.AudiobookFiles.Count} files.";
    }

    private void RefreshFileList()
    {
        audiobookFiles.Clear();

        foreach (string album in AudiobookTitlesListBox.SelectedItems.Cast<string>())
        {
            foreach (var filePath in Directory.EnumerateFiles(album))
            {
                if (!Path.GetExtension(filePath).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    WriteOutput($"Skipping {Path.GetFileNameWithoutExtension(filePath)} (only MP3s allowed)...", OutputMessageLevel.Warning);
                    continue;
                }

                var audiobookFile = new AudiobookFile { FilePath = filePath };
                var fileName = Path.GetFileName(filePath);

                if (!string.IsNullOrEmpty(fileName))
                    audiobookFile.FileName = fileName;

                using var tagLibFile = TagLib.File.Create(filePath);
                if (tagLibFile?.Tag != null)
                {
                    audiobookFile.Title = tagLibFile.Tag.Title;
                    audiobookFile.Album = tagLibFile.Tag.Album;

                    try
                    {
#pragma warning disable CS0618
                        audiobookFile.Artist = tagLibFile.Tag.Artists?.FirstOrDefault();
#pragma warning restore CS0618
                    }
                    catch (NullReferenceException ex)
                    {
                        Trace.TraceError("Tag.Artists was null. {0}", ex);
                    }

                    audiobookFile.Performer = tagLibFile.Tag.Performers?.FirstOrDefault();
                }

                audiobookFiles.Add(audiobookFile);
            }
        }

        if (AudiobookTitlesListBox.SelectedItems.Count == 0)
            WriteOutput("Selections cleared.", OutputMessageLevel.Warning);
        else if (AudiobookTitlesListBox.SelectedItems.Count == 1)
            WriteOutput($"1 folder selected ({audiobookFiles.Count} files).", OutputMessageLevel.Informational);
        else
            WriteOutput($"{AudiobookTitlesListBox.SelectedItems.Count} selected ({audiobookFiles.Count} total files).", OutputMessageLevel.Informational);
    }

    private void Reset()
    {
        AlbumNameTextBox.Text = string.Empty;
        ArtistTextBox.Text = string.Empty;
        SetAlbumNameCheckBox.IsChecked = true;
        SetTitleCheckBox.IsChecked = true;
        SetArtistNameCheckBox.IsChecked = true;
        audiobookTitles.Clear();
        audiobookFiles.Clear();
        statusMessages.Clear();
    }

    private void ShowBusy(string message, bool indeterminate)
    {
        BusyOverlay.Visibility = Visibility.Visible;
        BusyRing.IsActive = indeterminate;
        BusyRing.Visibility = indeterminate ? Visibility.Visible : Visibility.Collapsed;
        BusyProgressBar.Visibility = indeterminate ? Visibility.Collapsed : Visibility.Visible;
        BusyProgressBar.Value = 0;
        BusyTextBlock.Text = message;
    }

    private void UpdateBusyText(string message) => BusyTextBlock.Text = message;

    private void HideBusy()
    {
        BusyOverlay.Visibility = Visibility.Collapsed;
        BusyRing.IsActive = false;
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void WriteOutput(string text, OutputMessageLevel level, bool removeLastItem = false)
    {
        var messageColor = level switch
        {
            OutputMessageLevel.Normal => Microsoft.UI.Colors.LightGray,
            OutputMessageLevel.Informational => Microsoft.UI.Colors.Gray,
            OutputMessageLevel.Success => Microsoft.UI.Colors.LightGreen,
            OutputMessageLevel.Warning => Microsoft.UI.Colors.Goldenrod,
            OutputMessageLevel.Error => Microsoft.UI.Colors.OrangeRed,
            _ => Microsoft.UI.Colors.Gray
        };

        void Update()
        {
            if (removeLastItem && statusMessages.Count > 0)
                statusMessages.Remove(statusMessages.LastOrDefault());

            var message = new OutputMessage { Message = text, MessageColor = messageColor };
            statusMessages.Add(message);
            StatusListBox.ScrollIntoView(message);
        }

        if (DispatcherQueue.HasThreadAccess)
            Update();
        else
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, Update);
    }
}
