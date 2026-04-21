using MediaFileManager.Desktop.Models;
using MediaFileManager.Desktop.Models.Audio;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace MediaFileManager.Desktop.Views;

public sealed partial class MusicFilesView : UserControl
{
    private readonly Window _window;
    private readonly ObservableCollection<Artist> sourceList = new();
    private readonly ObservableCollection<OutputMessage> statusMessages = new();
    private int totalSongs;
    private CancellationTokenSource _cts;

    public MusicFilesView(Window window)
    {
        InitializeComponent();
        _window = window;

        StatusListBox.ItemsSource = statusMessages;
        ArtistListBox.ItemsSource = sourceList;
    }

    private async void BrowseSourceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path != null)
            SourceFolderTextBox.Text = path;
    }

    private async void BrowseDestinationFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFolderAsync();
        if (path != null)
            DestinationFolderTextBox.Text = path;
    }

    private async void ScanSourceFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var sourceDirectory = SourceFolderTextBox.Text;

        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            WriteOutput("Select a valid source folder first.", OutputMessageLevel.Error);
            return;
        }

        ShowBusy("scanning for mp3 files...", true);
        sourceList.Clear();
        totalSongs = 0;

        _cts = new CancellationTokenSource();

        try
        {
            var artists = await Task.Run(() => GenerateSourceList(sourceDirectory, _cts.Token), _cts.Token);

            foreach (var artist in artists)
                sourceList.Add(artist);

            ArtistListBox.ItemsSource = sourceList.Select(a => $"{a.Name} ({a.Albums.Count} albums)").ToList();
            StartProcessingButton.IsEnabled = sourceList.Count > 0 && !string.IsNullOrWhiteSpace(DestinationFolderTextBox.Text);
        }
        catch (OperationCanceledException)
        {
            WriteOutput("Scan cancelled.", OutputMessageLevel.Warning);
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

    private async void StartProcessingButton_Click(object sender, RoutedEventArgs e)
    {
        var targetDirectory = DestinationFolderTextBox.Text;

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            WriteOutput("Select a destination folder first.", OutputMessageLevel.Error);
            return;
        }

        ShowBusy("copying and renaming files...", false);
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<(int percent, string msg)>(p =>
            {
                BusyProgressBar.Value = p.percent;
                BusyTextBlock.Text = p.msg;
            });

            await Task.Run(() => RenameAndCopyFiles(targetDirectory, sourceList.ToList(), progress, _cts.Token), _cts.Token);
            WriteOutput("Success, file copy complete.", OutputMessageLevel.Success);
        }
        catch (OperationCanceledException)
        {
            WriteOutput("Processing cancelled.", OutputMessageLevel.Warning);
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

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        Reset();
    }

    private List<Artist> GenerateSourceList(string directory, CancellationToken ct)
    {
        WriteOutput("Checking for mp3 files in directory...", OutputMessageLevel.Normal);

        var mp3Files = Directory.EnumerateFiles(directory, "*.mp3").ToList();
        var total = mp3Files.Count;
        var artists = new List<Artist>();

        if (total < 1)
        {
            WriteOutput("There are no mp3 files present.", OutputMessageLevel.Error);
            return artists;
        }

        WriteOutput($"Found {total} mp3 files, continuing processing...", OutputMessageLevel.Normal);

        var totalArtists = 0;
        var totalAlbums = 0;

        foreach (var mp3File in mp3Files)
        {
            ct.ThrowIfCancellationRequested();

            var tfile = TagLib.File.Create(mp3File);
            var song = tfile.Tag.Title;
            var album = tfile.Tag.Album;
            var artist = tfile.Tag.FirstPerformer;

            var matchingArtist = artists.FirstOrDefault(a => a.Name == artist);

            if (matchingArtist == null)
            {
                artists.Add(new Artist
                {
                    Name = artist,
                    Albums = new List<Album>
                    {
                        new Album
                        {
                            Name = album,
                            Songs = new List<Song> { new Song { Name = song, FilePath = mp3File } }
                        }
                    }
                });

                WriteOutput($"ARTIST Discovered: Added {artist}, {album}, {song} to list.", OutputMessageLevel.Normal);
                totalArtists++;
                totalAlbums++;
                totalSongs++;
            }
            else
            {
                WriteOutput($"{artist} exists, checking for Albums...", OutputMessageLevel.Informational);

                var matchingAlbum = matchingArtist.Albums.FirstOrDefault(a => a.Name == album);

                if (matchingAlbum == null)
                {
                    WriteOutput($"ALBUM Discovered: {album}, adding.", OutputMessageLevel.Normal);
                    matchingArtist.Albums.Add(new Album
                    {
                        Name = album,
                        Songs = new List<Song> { new Song { Name = song, FilePath = mp3File } }
                    });
                    totalAlbums++;
                    totalSongs++;
                }
                else
                {
                    var matchingSong = matchingAlbum.Songs.FirstOrDefault(a => a.Name == song);
                    if (matchingSong == null)
                    {
                        WriteOutput($"SONG Discovered: {song}", OutputMessageLevel.Normal);
                        matchingAlbum.Songs.Add(new Song { Name = song, FilePath = mp3File });
                        totalSongs++;
                    }
                    else
                    {
                        WriteOutput($"{song} was already present in {album} for {artist}. Moving to next file.", OutputMessageLevel.Warning);
                    }
                }
            }
        }

        WriteOutput($"Discovered: {totalArtists} artists, {totalAlbums} albums and {totalSongs} songs.", OutputMessageLevel.Success);
        return artists;
    }

    private void RenameAndCopyFiles(string baseDirectory, List<Artist> artists, IProgress<(int, string)> progress, CancellationToken ct)
    {
        int currentPass = 0;

        foreach (var artist in artists)
        {
            ct.ThrowIfCancellationRequested();

            var validArtistName = ReplaceInvalidChars(artist.Name);
            var artistDirectory = Path.Join(baseDirectory, validArtistName);

            if (!Directory.Exists(artistDirectory))
                Directory.CreateDirectory(artistDirectory);

            WriteOutput($"{artist.Name} - Created directory.", OutputMessageLevel.Normal);

            foreach (var album in artist.Albums)
            {
                ct.ThrowIfCancellationRequested();

                var validAlbumName = ReplaceInvalidChars(album.Name);
                var albumDirectory = Path.Join(baseDirectory, validArtistName, validAlbumName);

                if (!Directory.Exists(albumDirectory))
                {
                    Directory.CreateDirectory(albumDirectory);
                    WriteOutput($"- Created {albumDirectory} subdirectory.", OutputMessageLevel.Informational);
                }

                foreach (var song in album.Songs)
                {
                    ct.ThrowIfCancellationRequested();

                    currentPass++;
                    var percentComplete = totalSongs > 0 ? (int)((double)currentPass / totalSongs * 100) : 0;
                    var validSongName = ReplaceInvalidChars(song.Name);
                    var destPath = Path.Join(baseDirectory, validArtistName, validAlbumName, $"{validSongName}.mp3");

                    if (!File.Exists(destPath))
                    {
                        WriteOutput($" | {percentComplete}% | Saving {validSongName} to {destPath}", OutputMessageLevel.Informational);
                        File.Copy(song.FilePath, destPath);
                    }
                    else
                    {
                        WriteOutput($"{validSongName} already existed, skipping...", OutputMessageLevel.Warning);
                    }

                    progress.Report((percentComplete, $"Copying {currentPass}/{totalSongs}..."));
                }
            }
        }
    }

    private async Task<string> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private string ReplaceInvalidChars(string filename)
    {
        if (string.IsNullOrEmpty(filename))
        {
            filename = "MISSING";
            Console.WriteLine("*** ALERT - input string was null, check artist/album/song for MISSING name ***");
        }
        return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
    }

    private void Reset()
    {
        SourceFolderTextBox.Text = string.Empty;
        DestinationFolderTextBox.Text = string.Empty;
        sourceList.Clear();
        statusMessages.Clear();
        totalSongs = 0;
        StartProcessingButton.IsEnabled = false;
        ArtistListBox.ItemsSource = null;
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

    private void HideBusy()
    {
        BusyOverlay.Visibility = Visibility.Collapsed;
        BusyRing.IsActive = false;
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
