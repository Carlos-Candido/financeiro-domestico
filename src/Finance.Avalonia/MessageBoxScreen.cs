using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Finance.Avalonia;

/// <summary>
/// Caixas de mensagem modais simples, com textos grandes e foco no botão principal.
/// Avalonia não possui MessageBox embutido, portanto o app mantém o seu próprio.
/// </summary>
internal static class MessageBoxScreen
{
    public static async Task<MessageBoxResult> ShowAsync(Window? owner, string message, string title, MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxType type = MessageBoxType.Information)
    {
        var box = new MessageBoxWindow(message, title, buttons, type);
        if (owner is { IsVisible: true })
        {
            await box.ShowDialog(owner);
            return box.Result;
        }

        // Sem proprietário visível (ex.: erro de inicialização): a caixa fica visível
        // por alguns segundos e então encerra a aplicação com código 1.
        box.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            box.Close();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
                && lifetime.ShutdownMode == ShutdownMode.OnExplicitShutdown)
            {
                lifetime.Shutdown(1);
            }
        };
        timer.Start();
        return box.Result;
    }

    public static void ShowInfo(Window? owner, string message, string title)
        => _ = ShowAsync(owner, message, title, MessageBoxButtons.Ok, MessageBoxType.Information);

    public static void ShowWarning(string message, string title)
        => _ = ShowAsync(null, message, title, MessageBoxButtons.Ok, MessageBoxType.Warning);

    public static void ShowError(Window? owner, string message, string title)
        => _ = ShowAsync(owner, message, title, MessageBoxButtons.Ok, MessageBoxType.Error);

    public static Task<MessageBoxResult> Show(Window? owner, string message, string title, MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxType type = MessageBoxType.Information)
        => ShowAsync(owner, message, title, buttons, type);

    private sealed class MessageBoxWindow : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

        public MessageBoxWindow(string message, string title, MessageBoxButtons buttons, MessageBoxType type)
        {
            Title = title;
            SizeToContent = SizeToContent.WidthAndHeight;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            MinWidth = 380;
            MaxWidth = 560;
            Topmost = true;

            var iconText = type switch
            {
                MessageBoxType.Error => "✖",
                MessageBoxType.Warning => "!",
                _ => "i",
            };
            var iconBrush = type switch
            {
                MessageBoxType.Error => Brushes.IndianRed,
                MessageBoxType.Warning => Brushes.DarkGoldenrod,
                _ => Brushes.SteelBlue,
            };

            var icon = new TextBlock
            {
                Text = iconText,
                FontSize = 30,
                FontWeight = FontWeight.Bold,
                Foreground = iconBrush,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 16, 0),
            };
            var text = new TextBlock
            {
                Text = message,
                FontSize = 17,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 470,
            };

            var messageRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 24, 26, 6) };
            messageRow.Children.Add(icon);
            messageRow.Children.Add(text);

            var buttonBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 10,
                Margin = new Thickness(26, 10, 26, 22),
            };

            void AddButton(string label, MessageBoxResult result, bool isPrimary)
            {
                var button = new Button
                {
                    Content = label,
                    MinWidth = 112,
                    MinHeight = 40,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Padding = new Thickness(14, 6, 14, 6),
                };
                if (isPrimary) button.Classes.Add("primary");
                button.Click += (_, _) =>
                {
                    Result = result;
                    Close(result);
                };
                buttonBar.Children.Add(button);
                if (isPrimary) button.AttachedToVisualTree += (_, _) => button.Focus();
            }

            switch (buttons)
            {
                case MessageBoxButtons.Ok:
                    AddButton("OK", MessageBoxResult.Ok, isPrimary: true);
                    break;
                case MessageBoxButtons.OkCancel:
                    AddButton("OK", MessageBoxResult.Ok, isPrimary: true);
                    AddButton("Cancelar", MessageBoxResult.Cancel, isPrimary: false);
                    break;
                case MessageBoxButtons.YesNo:
                    AddButton("Sim", MessageBoxResult.Yes, isPrimary: true);
                    AddButton("Não", MessageBoxResult.No, isPrimary: false);
                    break;
            }

            var root = new StackPanel();
            root.Children.Add(messageRow);
            root.Children.Add(buttonBar);
            Content = root;
        }
    }
}

internal enum MessageBoxButtons { Ok, OkCancel, YesNo }

internal enum MessageBoxType { Information, Warning, Error }

internal enum MessageBoxResult { Ok, Yes, No, Cancel }
