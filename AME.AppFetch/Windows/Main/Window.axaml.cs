using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AME.AppFetch.Windows.Main;

public partial class Window : Avalonia.Controls.Window
{
    private Handler _handler => (DataContext as Handler)!;
    
    public Window() : this(new Handler()) { } 
    public Window(Handler handler)
    {
        DataContext = handler;
        InitializeComponent();

        Loaded += (sender, args) => InputBox.Focus();
    }
    
    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);
    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => this.Close();

    private async void InputBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(InputBox.Text))
        {
            InputBox.IsEnabled = false;
            await _handler.Search(InputBox.Text);
            InputBox.IsEnabled = true;
        }
    }

    private void DismissButton_OnClick(object? sender, RoutedEventArgs e) => ErrorContainer.IsEnabled = false;
}