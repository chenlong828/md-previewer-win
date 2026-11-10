using System.Windows;
using System.Windows.Controls;

namespace MDPreview.Win.Shell;

/// <summary>极简文本输入对话框（链接地址等）；不用系统 InputBox，保持壳内一致的外观与禁用脚本对话框的安全取舍。</summary>
internal static class TextPrompt
{
    public static string? Show(Window owner, string title, string label, string initial)
    {
        var window = new Window
        {
            Title = title,
            Width = 460,
            Height = 170,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow,
        };

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var text = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) };
        Grid.SetRow(text, 0);
        var box = new TextBox { Text = initial, MinWidth = 400 };
        Grid.SetRow(box, 1);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        Grid.SetRow(panel, 2);
        var ok = new Button { Content = "确定", IsDefault = true, MinWidth = 78, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true, MinWidth = 78 };
        ok.Click += (_, _) => window.DialogResult = true;
        cancel.Click += (_, _) => window.DialogResult = false;
        panel.Children.Add(ok);
        panel.Children.Add(cancel);

        grid.Children.Add(text);
        grid.Children.Add(box);
        grid.Children.Add(panel);
        window.Content = grid;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        return window.ShowDialog() == true && !string.IsNullOrWhiteSpace(box.Text)
            ? box.Text.Trim()
            : null;
    }
}
