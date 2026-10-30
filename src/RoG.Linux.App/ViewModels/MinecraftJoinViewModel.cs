using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Minecraft;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Core.Navigation;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Server → Minecraft (wie <c>MinecraftJoinViewModel</c> der Windows-App): „So kommst du auf den Server“ für Java,
/// Bedrock und Konsole mit Kopier-Knöpfen, Ressourcenpaket zum Herunterladen – auch im Gastmodus. Bearbeiten der
/// Beitrittsdaten und Hochladen des Pakets (<c>minecraft.manage</c>) kommen mit der Verwaltung (Bauplan Stufe 9).
/// </summary>
public sealed partial class MinecraftJoinViewModel : LoadableViewModel
{
    private readonly MinecraftService _minecraft;
    private readonly FeedbackViewModel _feedback;

    public MinecraftJoinViewModel(AppServices services, FeedbackViewModel feedback)
        : base(services)
    {
        _minecraft = new MinecraftService(services.Api.Http);
        _feedback = feedback;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JavaAddress), nameof(BedrockAddress), nameof(BedrockPort), nameof(ShowConsole), nameof(Gamertag),
        nameof(HasHint), nameof(Hint), nameof(ShowDefaultPortNote))]
    public partial MinecraftJoin? Join { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPack), nameof(PackInfo))]
    public partial MinecraftResourcePack? Pack { get; set; }

    public bool HasPack => Pack is not null;

    public string PackInfo => Pack is { } p
        ? Services.Format(
            p.UpdatedAt is null ? StringKeys.ResourcePackInfoNoDate : StringKeys.ResourcePackInfo,
            MinecraftMapGeometry.FormatBytes(p.Size),
            p.UpdatedAt?.ToLocalTime().ToString("g", Services.Culture) ?? string.Empty)
        : string.Empty;

    public string JavaAddress => Join?.JavaText ?? string.Empty;

    public string BedrockAddress => Join?.Bedrock.Address ?? string.Empty;

    public string BedrockPort => Join?.Bedrock.Port.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public bool ShowConsole => Join?.ConsoleEnabled == true && !string.IsNullOrEmpty(Join.Gamertag);

    public string Gamertag => Join?.Gamertag ?? string.Empty;

    public bool HasHint => ShowConsole && !string.IsNullOrEmpty(Join?.Hint);

    public string Hint => Join?.Hint ?? string.Empty;

    /// <summary>Hinweis „25565 ist Standard“ nur, wenn der Standard-Port gilt (dann steht kein Port in der Adresse).</summary>
    public bool ShowDefaultPortNote => Join is { } j && !MinecraftService.ShowJavaPort(j.Java.Port);

    /// <summary>„Konto verknüpfen“ nur angemeldet (im Gastmodus gibt es kein Konto).</summary>
    public bool IsSignedIn => Services.CurrentUser is not null;

    protected override bool HasData => Join is not null;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var pack = LoadPackAsync(cancellationToken);
        try
        {
            Join = await _minecraft.JoinAsync(cancellationToken) ?? throw new ApiProblemException();
        }
        catch (System.Text.Json.JsonException)
        {
            throw new ApiProblemException();
        }

        await pack;
        OnPropertyChanged(nameof(IsSignedIn));
    }

    private async Task LoadPackAsync(CancellationToken cancellationToken)
    {
        try
        {
            Pack = await _minecraft.ResourcePackAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is ApiProblemException or System.Text.Json.JsonException or HttpRequestException or TaskCanceledException)
        {
            Pack = null; // Paket ist Beiwerk – die Anleitung bleibt nutzbar
        }
    }

    [RelayCommand]
    private void CopyJava() => Copy(JavaAddress);

    [RelayCommand]
    private void CopyBedrockAddress() => Copy(BedrockAddress);

    [RelayCommand]
    private void CopyBedrockPort() => Copy(BedrockPort);

    [RelayCommand]
    private void CopyGamertag() => Copy(Gamertag);

    [RelayCommand]
    private Task DownloadPackAsync() => Pack is { } p ? Services.Browser.OpenAsync(p.Url) : Task.CompletedTask;

    [RelayCommand]
    private void OpenMap() => Services.Navigate(new AppRoute(AppSection.MinecraftMap));

    [RelayCommand]
    private void OpenLeaderboards() => Services.Navigate(new AppRoute(AppSection.Leaderboards));

    /// <summary>Minecraft-Konto verknüpfen – bis die Kontoseite es nativ kann, auf der Website (Konto → Minecraft).</summary>
    [RelayCommand]
    private Task LinkAccountAsync() => Services.Browser.OpenAsync(Services.Environment.WebPage(Services.Language, "account/minecraft"));

    private void Copy(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        var ok = Services.ServerConnector.Copy(text);
        _feedback.Show(Services.Format(ok ? StringKeys.ServersCopied : StringKeys.ServersCopyFailed, text), ok ? MessageTone.Success : MessageTone.Error);
    }
}
