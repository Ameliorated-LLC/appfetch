using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using AME.FluentUI.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace AME.AppFetch.Windows.Main;

public partial class Window : Avalonia.Controls.Window
{
    private Handler _handler => (DataContext as Handler)!;
    
    public Window() : this(new Handler()) { } 
    public Window(Handler handler)
    {
        DataContext = handler;
        InitializeComponent();

        Loaded += (sender, args) =>
        {
            if (Program._updated)
            {
                _handler.Error = false;
                _handler.Updated = true;
                _handler.UpdatedDescription = $"Successfully updated to App Fetch v{Program.Version}";
            }
            else
            {
                Task.Run(() =>
                {
                    Program._updatesChecked.Wait();
                    if (Program._availableUpdate is not null)
                    {
                        Dispatcher.UIThread.Invoke(() =>
                        {
                            _handler.UpdateDescription = $"App Fetch v{Program._availableUpdate} is available for install";
                            _handler.Update = true;
                            _handler.Error = false;
                        });
                    }
                });
            }

            InputBox.Focus();
        };
    }
    
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);
    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => this.Close();

    private async void InputBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(InputBox.Text))
        {
            UpdateButton.IsEnabled = false;
            InputBox.IsEnabled = false;
            await _handler.Search(InputBox.Text);
            InputBox.IsEnabled = true;
            UpdateButton.IsEnabled = true;
        }
    }

    private void DismissButton_OnClick(object? sender, RoutedEventArgs e) => ErrorContainer.IsEnabled = UpdatedContainer.IsEnabled = false;

    private async void UpdateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        InputBox.IsEnabled = false;
        UpdateButton.IsHitTestVisible = false;
        var progress = new ArcProgress() { ArtificialProgress = true, Foreground = Brushes.White, Background = Brushes.DodgerBlue, ThicknessMultiplier = 0.8, Height = 22, Width = 22 };
        UpdateButton.Content = progress;

        var backgroundWorker = new BackgroundWorker();
        backgroundWorker.ProgressChanged += (sender, args) =>
        {
            Dispatcher.UIThread.Invoke(() => progress.Progress = args.ProgressPercentage);
        };
        try
        {
            
            await Task.Run(async () =>
            {
                await Update.InstallUpdate(backgroundWorker); 
            });
        }
        catch (Exception exception)
        {
            _handler.Update = false;
            await Task.Delay(500);
            _handler.ErrorTitle = "Failed to install update";
            _handler.ErrorDescription = exception.Message;
            _handler.Error = true;
            InputBox.IsEnabled = true;
        }
    }
}