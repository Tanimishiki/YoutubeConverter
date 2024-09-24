using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Dialogs;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using YoutubeExplode;
using YoutubeExplode.Converter;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;


namespace App.Views;

public partial class MainWindow : Window
{
    private readonly YoutubeClient _youtubeClient = new();
    private string _convertOption = "mp4";
    private CancellationTokenSource? _cancellationTokenSource;
    private string _videoTitleCache = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        _videoTitleCache = videoTitleTextBlock.Text ?? string.Empty;
    }

    private async Task MessageBoxAsync(string title, string content)
    {
        await MessageBoxManager
            .GetMessageBoxStandard(title, content)
            .ShowWindowDialogAsync(this);
    }

    public async void ConvertButtonClicked(object source, RoutedEventArgs args)
    {
        if (string.IsNullOrEmpty(youtubeUrlTextBox.Text) || string.IsNullOrWhiteSpace(youtubeUrlTextBox.Text))
        {
            await MessageBoxAsync("Warning", content: "The URL cannot be empty.");
            return;
        }

        string selectedOption = convertOptions
            .GetLogicalChildren()
            .OfType<RadioButton>()
            .Where(rb => (rb.GroupName == "ConvertOption") && rb.IsChecked is bool isChecked && isChecked)
            .Select(rb => rb.Content)
            .OfType<string>()
            .First();

        _convertOption = selectedOption switch
        {
            "MP3" => "mp3",
            "MP4" => "mp4",
            _ => throw new Exception("Unknown convert option.")
        };
        
        try
        {
            Video videoInfo = await _youtubeClient.Videos.GetAsync(youtubeUrlTextBox.Text);

            videoTitleTextBlock.Text = videoInfo.Title;
            downloadButton.IsEnabled = true;
        }
        catch (ArgumentException ex) when (ex.Message.StartsWith("Invalid YouTube video ID or URL"))
        {
            await MessageBoxAsync("Invalid", content: "Invalid URL.");
        }
        catch (Exception ex)
        {
            await MessageBoxAsync("Exception", content: ex.ToString());
        }
    }

    public async void DownloadButtonClicked(object source, RoutedEventArgs args)
    {
        if (videoTitleTextBlock.Text == null)
        {
            return;
        }

        string moveFileTo = string.Empty;
        
        if (GetTopLevel(this) is TopLevel topLevel)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = "Choose folder",
                AllowMultiple = false
            });

            if (!folders.Any())
            {
                return;
            }

            moveFileTo = folders[0].Path.AbsolutePath;
        }

        convertButton.IsEnabled = false;
        downloadButton.IsEnabled = false;
        cancelDownloadButton.IsEnabled = true;

        _cancellationTokenSource ??= new CancellationTokenSource();

        StreamManifest streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(youtubeUrlTextBox.Text!);
        var streams = new List<IStreamInfo>();

        if (_convertOption == "mp4")
        {
            streams.Add(streamManifest
                .GetVideoOnlyStreams()
                .Where(s => s.Container == Container.Mp4)
                .GetWithHighestVideoQuality());
        }

        streams.Add(streamManifest
            .GetAudioOnlyStreams()
            .Where(s => s.Container == Container.Mp4)
            .GetWithHighestBitrate());

        var progress = new Progress<double>(p =>
        {
            downloadProgressBar.Value = Math.Floor(Math.Round(p * 100));
        });

        try
        {
            string tempOutputPath = $"./Output/youtube.{_convertOption}";
            string fileName = IllegalCharactersRegex().Replace(videoTitleTextBlock.Text, string.Empty);

            await _youtubeClient.Videos.DownloadAsync(
                streams, new ConversionRequestBuilder(tempOutputPath).Build(),
                progress, _cancellationTokenSource.Token);

            File.Move(tempOutputPath, GetFileName($"{moveFileTo}/{fileName}.{_convertOption}"));
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            await MessageBoxAsync("Exception", ex.ToString());
        }

        youtubeUrlTextBox.Text = null;
        videoTitleTextBlock.Text = _videoTitleCache;
        convertButton.IsEnabled = true;
        cancelDownloadButton.IsEnabled = false;
        downloadProgressBar.Value = 0;
        
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = null;
    }

    private static string GetFileName(string filePath)
    {
        int count = 0;

        string directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        string extension = Path.GetExtension(filePath);
        string newFilePath = filePath;

        while (File.Exists(newFilePath))
        {
            newFilePath = $"{directoryPath}/{fileName} ({++count}).{extension}";
        }

        return newFilePath;
    }

    public async void CancelDownloadButtonClicked(object source, RoutedEventArgs args)
    {
        cancelDownloadButton.IsEnabled = false;

        if (_cancellationTokenSource is CancellationTokenSource token)
        {
            await token.CancelAsync();
        }
    }

    [GeneratedRegex(@"[\\\/:*?""<>|]")]
    private partial Regex IllegalCharactersRegex();

    public void GitHubButtonClicked(object source, RoutedEventArgs args)
    {
        var info = new ProcessStartInfo()
        {
            FileName = "https://github.com/Tanimishiki",
            UseShellExecute = true
        };

        Process.Start(info);
    }
}
