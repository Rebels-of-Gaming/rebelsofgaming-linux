using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RoG.Linux.App.Localization;

namespace RoG.Linux.App.Controls;

/// <summary>
/// Lade-, Fehler- und Leerzustand einer Liste oder Seite (wie <c>StateView</c> der Windows-App): Fortschrittsbalken,
/// Fehlertext mit „Erneut versuchen“ oder gestalteter Leerzustand. Unsichtbar, wenn Inhalt da ist.
/// </summary>
public sealed class StateView : UserControl
{
    public static readonly StyledProperty<bool> IsLoadingProperty = AvaloniaProperty.Register<StateView, bool>(nameof(IsLoading));

    public static readonly StyledProperty<string?> ErrorMessageProperty = AvaloniaProperty.Register<StateView, string?>(nameof(ErrorMessage));

    public static readonly StyledProperty<bool> IsEmptyProperty = AvaloniaProperty.Register<StateView, bool>(nameof(IsEmpty));

    public static readonly StyledProperty<string?> EmptyTextProperty = AvaloniaProperty.Register<StateView, string?>(nameof(EmptyText));

    public static readonly StyledProperty<ICommand?> RetryCommandProperty = AvaloniaProperty.Register<StateView, ICommand?>(nameof(RetryCommand));

    private readonly StackPanel _loading;
    private readonly StackPanel _error;
    private readonly TextBlock _errorText;
    private readonly Button _retry;
    private readonly StackPanel _empty;
    private readonly TextBlock _emptyText;

    public StateView()
    {
        _loading = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 220 };
        _loading.Children.Add(new ProgressBar { IsIndeterminate = true, Foreground = Brush("RoGRedBrush") });
        _loading.Children.Add(new TextBlock { Text = Loc.Get("Common_Loading"), Classes = { "muted" }, HorizontalAlignment = HorizontalAlignment.Center });
        AutomationProperties.SetName(_loading, Loc.Get("Common_Loading"));

        _errorText = new TextBlock { Classes = { "body" }, TextAlignment = TextAlignment.Center, MaxWidth = 420 };
        _retry = new Button { Content = Loc.Get("Common_Retry").ToUpper(System.Globalization.CultureInfo.CurrentCulture), Classes = { "secondary" }, HorizontalAlignment = HorizontalAlignment.Center };
        _error = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        _error.Children.Add(new Border { Classes = { "error" }, Child = _errorText });
        _error.Children.Add(_retry);

        _emptyText = new TextBlock { Classes = { "muted" }, TextAlignment = TextAlignment.Center, MaxWidth = 420 };
        _empty = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24) };
        _empty.Children.Add(new Image
        {
            Width = 64,
            Height = 64,
            Opacity = 0.3,
            Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://rebelsofgaming/Assets/rog-badge-512.png"))),
        });
        _empty.Children.Add(_emptyText);

        var root = new Panel();
        root.Children.Add(_loading);
        root.Children.Add(_error);
        root.Children.Add(_empty);
        Content = root;
        Update();
    }

    public bool IsLoading
    {
        get => GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public bool IsEmpty
    {
        get => GetValue(IsEmptyProperty);
        set => SetValue(IsEmptyProperty, value);
    }

    public string? EmptyText
    {
        get => GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    public ICommand? RetryCommand
    {
        get => GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsLoadingProperty || change.Property == ErrorMessageProperty || change.Property == IsEmptyProperty
            || change.Property == EmptyTextProperty || change.Property == RetryCommandProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var hasError = !string.IsNullOrEmpty(ErrorMessage);
        _loading.IsVisible = IsLoading && !hasError;
        _error.IsVisible = hasError && !IsLoading;
        _errorText.Text = ErrorMessage;
        _retry.Command = RetryCommand;
        _retry.IsVisible = RetryCommand is not null;
        _empty.IsVisible = IsEmpty && !IsLoading && !hasError;
        _emptyText.Text = string.IsNullOrEmpty(EmptyText) ? Loc.Get("Common_Empty") : EmptyText;
        IsVisible = _loading.IsVisible || _error.IsVisible || _empty.IsVisible;
    }

    private static IBrush? Brush(string key) =>
        Application.Current?.TryGetResource(key, null, out var value) == true ? value as IBrush : null;
}
