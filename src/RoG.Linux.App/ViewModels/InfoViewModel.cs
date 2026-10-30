using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Content;
using RoG.Core.Features.Info;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>Info: Regeln, Seiten (Über uns, Partner …) und Rechtstexte – Liste nach Gruppen, Dokument nativ gerendert.</summary>
public sealed partial class InfoViewModel : LoadableViewModel, IDetailPage, IRoutablePage
{
    private readonly InfoService _info;
    private string? _pendingKey;

    public InfoViewModel(AppServices services)
        : base(services)
    {
        _info = new InfoService(services.Api.Client, services.Environment.WebBaseUrl);
        Detail = new InfoDetailViewModel(services, _info);
    }

    /// <summary>Gruppenüberschriften und Einträge in einer Liste (wie die Windows-App).</summary>
    public ObservableCollection<InfoRowViewModel> Rows { get; } = [];

    public InfoDetailViewModel Detail { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial InfoRowViewModel? Selected { get; set; }

    public bool IsDetailOpen => Selected is { IsGroup: false };

    public bool ShowSelectHint => !IsDetailOpen;

    protected override bool HasData => Rows.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var entries = await _info.ListAsync(cancellationToken);
        var selected = Selected?.Entry;
        Rows.Clear();
        foreach (var group in entries.GroupBy(e => e.Kind))
        {
            Rows.Add(InfoRowViewModel.Group(Services.T(group.Key switch
            {
                InfoKind.Rules => StringKeys.InfoGroupRules,
                InfoKind.Page => StringKeys.InfoGroupPages,
                _ => StringKeys.InfoGroupLegal,
            }).ToUpper(Services.Culture)));
            foreach (var entry in group)
            {
                Rows.Add(InfoRowViewModel.Item(entry, Services, row => Selected = row));
            }
        }

        if (selected is not null)
        {
            Selected = Rows.FirstOrDefault(r => r.Entry is { } e && e.Kind == selected.Kind && e.Key == selected.Key);
        }

        if (_pendingKey is { } pending)
        {
            _pendingKey = null;
            Open(pending);
        }
    }

    /// <summary>
    /// Eintrag per Schlüssel öffnen (<c>rog://info/rules</c>, Website-Pfade wie <c>/rules</c>, <c>/privacy</c>, <c>/about</c>):
    /// passender Slug bzw. Typ, „rules“ = erstes Regelwerk.
    /// </summary>
    public void Open(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (Rows.Count == 0)
        {
            _pendingKey = key;
            return;
        }

        var items = Rows.Where(r => !r.IsGroup).ToList();
        Selected = items.FirstOrDefault(r => string.Equals(r.Entry!.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? (key is "rules" ? items.FirstOrDefault(r => r.Entry!.Kind == InfoKind.Rules) : null)
            ?? Selected;
    }

    [RelayCommand]
    public void CloseDetail() => Selected = null;

    partial void OnSelectedChanged(InfoRowViewModel? value)
    {
        if (value is { IsGroup: true })
        {
            // Überschriften sind nicht wählbar
            Selected = null;
            return;
        }

        foreach (var row in Rows)
        {
            row.IsSelected = ReferenceEquals(row, value);
        }

        if (value?.Entry is { } entry)
        {
            _ = Detail.ShowAsync(entry);
        }
    }
}

/// <summary>Zeile: Gruppenüberschrift oder Eintrag (Titel, Untertitel, Hinweis „bitte bestätigen“).</summary>
public sealed partial class InfoRowViewModel : ObservableObject
{
    private Action<InfoRowViewModel>? _select;

    private InfoRowViewModel(string title)
    {
        Title = title;
    }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => _select?.Invoke(this);

    public InfoEntry? Entry { get; private init; }

    public string Title { get; }

    public string Subtitle { get; private init; } = string.Empty;

    public bool HasSubtitle => Subtitle.Length > 0;

    public bool IsGroup => Entry is null;

    public bool IsItem => Entry is not null;

    public bool NeedsAcceptance { get; private init; }

    public string AcceptanceText { get; private init; } = string.Empty;

    public static InfoRowViewModel Group(string title) => new(title);

    public static InfoRowViewModel Item(InfoEntry entry, AppServices services, Action<InfoRowViewModel> select) => new(entry.Title)
    {
        _select = select,
        Entry = entry,
        Subtitle = entry.Subtitle ?? string.Empty,
        NeedsAcceptance = entry.NeedsAcceptance,
        AcceptanceText = services.T(StringKeys.InfoNeedsAcceptance),
    };
}

/// <summary>Dokument: Titel, Einleitung, Version, Abschnitte (Regeln mit Nummer, wichtige markiert), „Im Browser öffnen“.</summary>
public sealed partial class InfoDetailViewModel(AppServices services, InfoService info) : LoadableViewModel(services)
{
    private InfoEntry? _entry;
    private InfoDocument? _document;

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIntro))]
    public partial string? Intro { get; set; }

    public bool HasIntro => !string.IsNullOrEmpty(Intro);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMeta))]
    public partial string? Meta { get; set; }

    public bool HasMeta => !string.IsNullOrEmpty(Meta);

    public ObservableCollection<InfoSectionViewModel> Sections { get; } = [];

    /// <summary>Regelwerk noch nicht bestätigt – Bestätigen kommt mit Stufe 6, bis dahin über die Website.</summary>
    [ObservableProperty]
    public partial bool NeedsAcceptance { get; set; }

    [ObservableProperty]
    public partial bool HasWebUrl { get; set; }

    protected override bool HasData => _document is not null;

    public Task ShowAsync(InfoEntry entry)
    {
        _entry = entry;
        _document = null;
        return LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_entry is null)
        {
            return;
        }

        var entry = _entry;
        var document = await info.GetAsync(entry, cancellationToken);
        if (!ReferenceEquals(entry, _entry))
        {
            return;
        }

        Title = document.Title;
        Intro = document.Intro;
        Meta = document.Meta;
        NeedsAcceptance = document.CanAccept;
        HasWebUrl = WebUrl(document) is not null;
        Sections.Clear();
        foreach (var section in document.Sections)
        {
            Sections.Add(new InfoSectionViewModel(section));
        }

        _document = document;
    }

    private Uri? WebUrl(InfoDocument document) => document.WebUrl
        ?? (document.Entry.Kind == InfoKind.Rules ? Services.Environment.WebPage(Services.Language, "rules") : null);

    [RelayCommand]
    private async Task OpenInBrowserAsync()
    {
        if (_document is not null && WebUrl(_document) is { } url)
        {
            await Services.Browser.OpenAsync(url);
        }
    }
}

public sealed class InfoSectionViewModel(InfoSection section)
{
    public string? Title => section.Title;

    public bool HasTitle => !string.IsNullOrEmpty(section.Title);

    public bool Important => section.Important;

    public RichDocument Content => section.Content;
}
