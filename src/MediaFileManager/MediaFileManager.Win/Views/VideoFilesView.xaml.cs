using MediaFileManager.Desktop.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace MediaFileManager.Desktop.Views;

public sealed partial class VideoFilesView : UserControl
{
    private readonly Window window;
    private readonly ObservableCollection<OutputMessage> statusMessages = [];
    private readonly ObservableCollection<string> seasons = [];
    private readonly ObservableCollection<string> episodes = [];
    private readonly ObservableCollection<string> renamedEpisodesPreviewList = [];
    private string lastPickedFolder;
    private CancellationTokenSource cts;

    public VideoFilesView(Window window)
    {
        InitializeComponent();
        this.window = window;

        SeasonsListBox.ItemsSource = seasons;
        EpisodesListBox.ItemsSource = episodes;
        EpisodeRenamedPreviewListBox.ItemsSource = renamedEpisodesPreviewList;
        StatusListBox.ItemsSource = statusMessages;

        lastPickedFolder = App.GetLocalSetting<string>("LastFolder");

        WriteOutput("Ready, open a folder to begin.", OutputMessageLevel.Success);

        Unloaded += (s, e) => cts?.Cancel();
    }

    private async void SelectFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WriteOutput("Opening folder picker...", OutputMessageLevel.Normal);
            ShowBusy("opening folder...", true);

            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

            if (!string.IsNullOrEmpty(lastPickedFolder))
            {
                var parentDir = Directory.GetParent(lastPickedFolder)?.FullName;
                if (!string.IsNullOrEmpty(parentDir))
                    picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
                WriteOutput("Starting at saved folder.", OutputMessageLevel.Normal);
            }
            else
            {
                WriteOutput("No saved folder, starting at root.", OutputMessageLevel.Warning);
            }

            var folder = await picker.PickSingleFolderAsync();

            if (folder == null)
            {
                WriteOutput("Canceled folder selection.", OutputMessageLevel.Normal);
                return;
            }

            lastPickedFolder = folder.Path;
            App.SetLocalSetting("LastFolder", folder.Path);

            Reset();

            UpdateBusyText("searching for seasons...");

            var seasonsResult = Directory.EnumerateDirectories(folder.Path).ToList();
            seasons.Clear();

            foreach (var season in seasonsResult)
            {
                seasons.Add(season);
                UpdateBusyText($"added {Path.GetFileName(season)}");
            }

            switch (seasons.Count)
            {
                case 0:
                    WriteOutput("No seasons detected, make sure there are subfolders.", OutputMessageLevel.Warning);
                    break;
                case 1:
                    WriteOutput($"Opened '{folder.Name}' ({seasons.Count} season).", OutputMessageLevel.Success);
                    break;
                default:
                    WriteOutput($"Opened '{folder.Name}' ({seasons.Count} seasons).", OutputMessageLevel.Success);
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

    private void SeasonsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        episodes.Clear();

        foreach (string season in SeasonsListBox.SelectedItems.Cast<string>())
        {
            foreach (var filePath in Directory.EnumerateFiles(season))
                episodes.Add(filePath);
        }

        if (SeasonsListBox.SelectedItems.Count == 0)
            WriteOutput("Selections cleared.", OutputMessageLevel.Warning);
        else if (SeasonsListBox.SelectedItems.Count == 1)
            WriteOutput($"Selected 1 season ({episodes.Count} episodes).", OutputMessageLevel.Informational);
        else
            WriteOutput($"{SeasonsListBox.SelectedItems.Count} seasons selected ({episodes.Count} total episodes).", OutputMessageLevel.Informational);
    }

    private void EpisodesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EpisodesListBox.SelectedItems.Count <= 0)
            return;

        var firstEpisodeFilePath = EpisodesListBox.SelectedItems.OfType<string>().FirstOrDefault();
        var curName = Path.GetFileName(firstEpisodeFilePath) ?? string.Empty;

        EpisodeName_Renaming_TextBox.Text = curName;
        EpisodeName_Renumbering_TextBox.Text = curName;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        Reset();
        WriteOutput("Reset complete! Open a folder to continue.", OutputMessageLevel.Success);
    }

    private void EpisodeNameTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        OriginalTextBox_Renaming.Text = EpisodeName_Renaming_TextBox.SelectedText;
    }

    private void OriginalTextBox_Renaming_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateFileNameButton.IsEnabled = !string.IsNullOrEmpty(OriginalTextBox_Renaming?.Text);
    }

    private async void UpdateFileNameButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(OriginalTextBox_Renaming.Text))
        {
            WriteOutput("No text selected, aborting file name update.", OutputMessageLevel.Error);
            return;
        }

        WriteOutput("Renaming operation starting...", OutputMessageLevel.Warning);

        var selectedItems = EpisodesListBox.SelectedItems.Cast<string>().ToList();
        var selectedText = OriginalTextBox_Renaming.Text;
        var replacementText = ReplacementTextBox.Text;

        ShowBusy("updating file names...", false);

        var progress = new Progress<(int percent, string msg)>(p =>
        {
            BusyProgressBar.Value = p.percent;
            BusyTextBlock.Text = p.msg;
        });

        cts = new CancellationTokenSource();

        try
        {
            await Task.Run(() =>
            {
                for (var i = 0; i < selectedItems.Count; i++)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    var episodeFilePath = selectedItems[i];
                    var curDir = Path.GetDirectoryName(episodeFilePath);
                    var curName = Path.GetFileName(episodeFilePath);

                    if (string.IsNullOrEmpty(curDir) || string.IsNullOrEmpty(curName))
                    {
                        WriteOutput("Could not find file, skipping.", OutputMessageLevel.Error);
                        continue;
                    }

                    var newName = curName.Replace(selectedText, replacementText, StringComparison.InvariantCulture);
                    File.Move(episodeFilePath, Path.Combine(curDir, newName));

                    var pct = (int)((double)(i + 1) / selectedItems.Count * 100);
                    ((IProgress<(int, string)>)progress).Report((pct, $"Renaming {i + 1}/{selectedItems.Count}..."));
                }
            }, cts.Token);

            WriteOutput("Renaming operation complete!", OutputMessageLevel.Success);
        }
        catch (OperationCanceledException)
        {
            WriteOutput("Renaming cancelled.", OutputMessageLevel.Warning);
        }
        catch (Exception ex)
        {
            WriteOutput(ex.Message, OutputMessageLevel.Error);
        }
        finally
        {
            HideBusy();
        }

        RefreshEpisodesList();
    }

    private void EpisodeNameTextBox_Renumbering_SelectionChanged(object sender, RoutedEventArgs e)
    {
        OriginalTextBox_Renumbering.Text = EpisodeName_Renumbering_TextBox.SelectedText;
    }

    private void OriginalTextBox_Renumbering_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenumberingButton.IsEnabled = !string.IsNullOrEmpty(OriginalTextBox_Renumbering?.Text)
                                   && !string.IsNullOrEmpty(SeasonNumberTextBox?.Text);
    }

    private void SeasonNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenumberingButton.IsEnabled = !string.IsNullOrEmpty(OriginalTextBox_Renumbering?.Text)
                                   && !string.IsNullOrEmpty(SeasonNumberTextBox?.Text);
    }

    private async void RenumberingButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ValidateRenumberingInputs(out var seasonNumber, out var startingEpisodeNumber, out var lastEpisodeNumber))
                return;

            ShowBusy("re-numbering and renaming files...", false);
            renamedEpisodesPreviewList.Clear();

            var selectedEpisodes = EpisodesListBox.SelectedItems.Cast<string>().ToList();
            var parameters = new WorkerParameters(selectedEpisodes)
            {
                IsPreview = true,
                SeasonNumber = seasonNumber,
                EpisodeNumberStart = startingEpisodeNumber,
                EpisodeNumberEnd = lastEpisodeNumber,
                SelectedTextLength = OriginalTextBox_Renumbering.Text.Length
            };

            await RunRenumberWorkerAsync(parameters);
        }
        catch (Exception ex)
        {
            WriteOutput(ex.Message, OutputMessageLevel.Error);
        }
    }

    private async void ApproveResultButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ValidateRenumberingInputs(out var seasonNumber, out var startingEpisodeNumber, out var lastEpisodeNumber))
                return;

            ShowBusy("re-numbering and renaming files...", false);

            var selectedEpisodes = EpisodesListBox.SelectedItems.Cast<string>().ToList();
            var parameters = new WorkerParameters(selectedEpisodes)
            {
                IsPreview = false,
                SeasonNumber = seasonNumber,
                EpisodeNumberStart = startingEpisodeNumber,
                EpisodeNumberEnd = lastEpisodeNumber,
                SelectedTextLength = OriginalTextBox_Renumbering.Text.Length
            };

            await RunRenumberWorkerAsync(parameters);
        }
        catch (Exception ex)
        {
            WriteOutput(ex.Message, OutputMessageLevel.Error);
        }
    }

    private void CancelResultButton_Click(object sender, RoutedEventArgs e)
    {
        renamedEpisodesPreviewList.Clear();
        EpisodesTabView.SelectedIndex = 0;
        ApproveResultButton.IsEnabled = false;
        CancelResultButton.IsEnabled = false;
    }

    private bool ValidateRenumberingInputs(out int seasonNumber, out int startingEpisodeNumber, out int lastEpisodeNumber)
    {
        seasonNumber = 0;
        startingEpisodeNumber = 0;
        lastEpisodeNumber = 0;

        if (string.IsNullOrEmpty(OriginalTextBox_Renumbering?.Text))
        {
            WriteOutput("You must select text that will be replaced by the season and episode number.", OutputMessageLevel.Error);
            return false;
        }

        if (string.IsNullOrEmpty(SeasonNumberTextBox?.Text) || !int.TryParse(SeasonNumberTextBox.Text, out seasonNumber))
        {
            WriteOutput("You must enter a valid two-digit number for the season.", OutputMessageLevel.Error);
            return false;
        }

        if (string.IsNullOrEmpty(EpisodeStartTextBox?.Text) || string.IsNullOrEmpty(EpisodeEndTextBox?.Text))
        {
            WriteOutput("You must enter a first and last episode number.", OutputMessageLevel.Error);
            return false;
        }

        if (!int.TryParse(EpisodeStartTextBox.Text, out startingEpisodeNumber) || !int.TryParse(EpisodeEndTextBox.Text, out lastEpisodeNumber))
        {
            WriteOutput("You must use a valid two-digit value for the start and end episode number.", OutputMessageLevel.Error);
            return false;
        }

        if (lastEpisodeNumber - startingEpisodeNumber + 1 != EpisodesListBox.SelectedItems.Count)
        {
            WriteOutput("The episode numbers do not match the total selected episodes.", OutputMessageLevel.Error);
            return false;
        }

        return true;
    }

    private async Task RunRenumberWorkerAsync(WorkerParameters parameters)
    {
        var progress = new Progress<WorkerProgress>(p =>
        {
            BusyProgressBar.Value = p.PercentComplete;
            BusyTextBlock.Text = p.BusyMessage;
            if (p.IsPreview)
                renamedEpisodesPreviewList.Add(p.FileName);
        });

        cts = new CancellationTokenSource();
        WorkerResult result = null;

        try
        {
            result = await Task.Run(() => DoRenumberWork(parameters, progress, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            WriteOutput("Operation cancelled.", OutputMessageLevel.Warning);
        }
        catch (Exception ex)
        {
            WriteOutput(ex.Message, OutputMessageLevel.Error);
        }
        finally
        {
            HideBusy();
        }

        if (result != null)
        {
            WriteOutput(result.FinalMessage, OutputMessageLevel.Success);

            if (!result.IsPreview)
            {
                renamedEpisodesPreviewList.Clear();
                RefreshEpisodesList();
                EpisodesTabView.SelectedIndex = 0;
                ApproveResultButton.IsEnabled = false;
                CancelResultButton.IsEnabled = false;
            }
            else
            {
                EpisodesTabView.SelectedIndex = 1;
                ApproveResultButton.IsEnabled = true;
                CancelResultButton.IsEnabled = true;
            }
        }
    }

    private WorkerResult DoRenumberWork(WorkerParameters workerParameter, IProgress<WorkerProgress> progress, CancellationToken ct)
    {
        int currentEpisodeNumber = workerParameter.EpisodeNumberStart;

        for (int i = 0; i < workerParameter.SelectedEpisodes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var episodeFilePath = workerParameter.SelectedEpisodes[i];
            var curDir = Path.GetDirectoryName(episodeFilePath);
            var curName = Path.GetFileName(episodeFilePath);

            if (string.IsNullOrEmpty(curDir) || string.IsNullOrEmpty(curName))
            {
                WriteOutput("Could not find file, skipping.", OutputMessageLevel.Error);
                continue;
            }

            var selectedText = curName.Substring(0, Math.Min(workerParameter.SelectedTextLength, curName.Length));
            var showName = Directory.GetParent(episodeFilePath)?.Parent?.Name;
            var newName = curName.Replace(selectedText, $"{showName} - S{workerParameter.SeasonNumber}E{currentEpisodeNumber:00} -", StringComparison.InvariantCulture);

            if (!workerParameter.IsPreview)
                File.Move(episodeFilePath, Path.Combine(curDir, newName));

            currentEpisodeNumber++;

            progress.Report(new WorkerProgress
            {
                IsPreview = workerParameter.IsPreview,
                PercentComplete = (int)((double)(i + 1) / workerParameter.SelectedEpisodes.Count * 100),
                BusyMessage = $"Completed: S{workerParameter.SeasonNumber}E{currentEpisodeNumber:00}...",
                FileName = newName
            });
        }

        return new WorkerResult
        {
            FinalMessage = $"Complete, renumbered {workerParameter.SelectedEpisodes.Count} episodes.",
            IsPreview = workerParameter.IsPreview
        };
    }

    private void RefreshEpisodesList()
    {
        episodes.Clear();

        foreach (string season in SeasonsListBox.SelectedItems.Cast<string>())
        {
            var folderName = Path.GetFileName(season);
            if (string.IsNullOrEmpty(folderName))
            {
                WriteOutput("Could not identify directory.", OutputMessageLevel.Error);
                return;
            }

            WriteOutput($"Searching {folderName} episodes...", OutputMessageLevel.Normal);

            foreach (var filePath in Directory.EnumerateFiles(season))
            {
                if (Path.HasExtension(filePath))
                {
                    episodes.Add(filePath);
                    WriteOutput($"Adding {Path.GetFileName(filePath)}...", OutputMessageLevel.Normal, true);
                }
            }

            WriteOutput($"Refreshed {folderName}: {episodes.Count} episodes.", OutputMessageLevel.Normal, true);
        }
    }

    private void Reset()
    {
        EpisodeName_Renaming_TextBox.Text = string.Empty;
        EpisodeName_Renumbering_TextBox.Text = string.Empty;
        OriginalTextBox_Renaming.Text = string.Empty;
        OriginalTextBox_Renumbering.Text = string.Empty;
        ReplacementTextBox.Text = string.Empty;
        SeasonNumberTextBox.Text = string.Empty;

        seasons.Clear();
        episodes.Clear();
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
