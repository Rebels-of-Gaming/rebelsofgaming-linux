using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Events;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Kalender: Liste der nächsten 60 Tage (nach Tag gruppiert) oder Monatsansicht (6 Wochen ab Montag); Termin daneben
/// bzw. darüber. Zusagen werden nur angezeigt (eigene Antwort, Zahlen, Teilnehmer) – Antworten kommt mit Stufe 4.
/// </summary>
public sealed partial class EventsViewModel : LoadableViewModel, IDetailPage, IRoutablePage
{
    private readonly EventService _events;
    private DateOnly _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string? _pendingId;

    public EventsViewModel(AppServices services)
        : base(services)
    {
        _events = new EventService(services.Api.Client);
        Detail = new EventDetailViewModel(services, _events);
        WeekdayNames = Enumerable.Range(0, 7)
            .Select(i => services.Culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((i + 1) % 7)))
            .ToList();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShowContent))
            {
                OnPropertyChanged(nameof(ShowList));
            }
        };
    }

    public ObservableCollection<EventRowViewModel> Items { get; } = [];

    public EventDetailViewModel Detail { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial EventRowViewModel? Selected { get; set; }

    /// <summary>Termin per Link geöffnet, der nicht in der Liste steht (älter oder weiter in der Zukunft).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetailOpen), nameof(ShowSelectHint))]
    public partial bool DetailForced { get; set; }

    public bool IsDetailOpen => Selected is not null || DetailForced;

    public bool ShowSelectHint => !IsDetailOpen;

    // ---- Monatsansicht ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowList), nameof(ListMode))]
    public partial bool MonthMode { get; set; }

    /// <summary>Gegenstück zu <see cref="MonthMode"/> für den Umschalter „Liste“.</summary>
    public bool ListMode
    {
        get => !MonthMode;
        set => MonthMode = !value;
    }

    public bool ShowList => ShowContent && !MonthMode;

    public ObservableCollection<MonthDayViewModel> MonthDays { get; } = [];

    public ObservableCollection<EventRowViewModel> DayEvents { get; } = [];

    [ObservableProperty]
    public partial string MonthTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DayTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMonthLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMonthError))]
    public partial string? MonthError { get; set; }

    public bool HasMonthError => !string.IsNullOrEmpty(MonthError);

    [ObservableProperty]
    public partial bool HasNoDayEvents { get; set; }

    public IReadOnlyList<string> WeekdayNames { get; }

    protected override bool HasData => Items.Count > 0;

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        var page = await _events.ListAsync(DateTimeOffset.Now.Date, 60, null, cancellationToken);
        var selected = Selected?.Id;
        Items.Clear();
        DateTime? day = null;
        foreach (var e in page.Items)
        {
            var thisDay = e.StartsAt?.ToLocalTime().Date;
            Items.Add(new EventRowViewModel(e, thisDay != day, Services));
            day = thisDay;
        }

        if (selected is not null)
        {
            Selected = Items.FirstOrDefault(i => i.Id == selected);
        }

        if (_pendingId is { } pending)
        {
            _pendingId = null;
            Open(pending);
        }

        if (MonthMode)
        {
            await LoadMonthAsync();
        }
    }

    /// <summary>Termin per ID öffnen (Startseite, Squad-Intern, <c>rog://events/id</c>); vor dem Laden gemerkt.</summary>
    public void Open(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (Items.FirstOrDefault(i => i.Id == key) is { } row)
        {
            DetailForced = false;
            Selected = row;
        }
        else if (Items.Count == 0 && IsLoading)
        {
            _pendingId = key;
        }
        else
        {
            Selected = null;
            DetailForced = true;
            _ = Detail.ShowAsync(key);
        }
    }

    [RelayCommand]
    public void CloseDetail()
    {
        Selected = null;
        DetailForced = false;
    }

    /// <summary>Termin aus der Tagesliste der Monatsansicht öffnen.</summary>
    [RelayCommand]
    private void OpenRow(EventRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        if (Items.FirstOrDefault(i => i.Id == row.Id) is { } inList)
        {
            Selected = inList;
            return;
        }

        Selected = null;
        DetailForced = true;
        Detail.Show(row.Item);
    }

    partial void OnSelectedChanged(EventRowViewModel? value)
    {
        if (value is not null)
        {
            DetailForced = false;
            Detail.Show(value.Item);
        }
    }

    partial void OnMonthModeChanged(bool value)
    {
        if (value)
        {
            _ = LoadMonthAsync();
        }
    }

    [RelayCommand]
    private Task PreviousMonthAsync()
    {
        _month = _month.AddMonths(-1);
        return LoadMonthAsync();
    }

    [RelayCommand]
    private Task NextMonthAsync()
    {
        _month = _month.AddMonths(1);
        return LoadMonthAsync();
    }

    [RelayCommand]
    private Task TodayAsync()
    {
        _month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        return LoadMonthAsync();
    }

    public async Task LoadMonthAsync()
    {
        var month = _month;
        MonthTitle = Services.Culture.TextInfo.ToTitleCase(month.ToDateTime(TimeOnly.MinValue).ToString("MMMM yyyy", Services.Culture));
        var first = MonthGrid.FirstCell(month.Year, month.Month);
        var events = new List<EventItem>();
        IsMonthLoading = true;
        MonthError = null;
        try
        {
            string? cursor = null;
            do
            {
                var page = await _events.ListAsync(new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue)), 42, cursor);
                events.AddRange(page.Items);
                cursor = page.NextCursor;
            }
            while (cursor is not null && events.Count < 500);
        }
        catch (ApiProblemException ex)
        {
            MonthError = Services.Localizer.GetError(ex.Problem.Code);
            return;
        }
        catch (Exception ex) when (ex is RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException or TaskCanceledException)
        {
            MonthError = Services.Localizer.GetError(ApiProblem.Codes.Unknown);
            return;
        }
        finally
        {
            IsMonthLoading = false;
        }

        if (month != _month)
        {
            return; // inzwischen weitergeblättert
        }

        MonthDays.Clear();
        foreach (var day in MonthGrid.Build(month.Year, month.Month, events, DateOnly.FromDateTime(DateTime.Today)))
        {
            MonthDays.Add(new MonthDayViewModel(day, this, Services));
        }

        var start = MonthDays.FirstOrDefault(d => d.Day.IsToday && d.Day.InMonth)
            ?? MonthDays.FirstOrDefault(d => d.Day.InMonth && d.Day.Events.Count > 0)
            ?? MonthDays.FirstOrDefault(d => d.Day.InMonth);
        ShowDay(start);
    }

    internal void ShowDay(MonthDayViewModel? day)
    {
        foreach (var d in MonthDays)
        {
            d.IsSelected = ReferenceEquals(d, day);
        }

        DayEvents.Clear();
        if (day is null)
        {
            DayTitle = string.Empty;
            HasNoDayEvents = false;
            return;
        }

        DayTitle = day.Day.Date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d. MMMM", Services.Culture).ToUpper(Services.Culture);
        foreach (var e in day.Day.Events)
        {
            DayEvents.Add(new EventRowViewModel(e, false, Services));
        }

        HasNoDayEvents = DayEvents.Count == 0;
    }

    internal static string RsvpLabel(Rsvp rsvp, ILocalizer localizer) => rsvp switch
    {
        Rsvp.Yes => localizer.GetString(StringKeys.EventsRsvpYes),
        Rsvp.Maybe => localizer.GetString(StringKeys.EventsRsvpMaybe),
        Rsvp.No => localizer.GetString(StringKeys.EventsRsvpNo),
        _ => string.Empty,
    };

    /// <summary>Typfarbe aus der API (<c>#E0324A</c>); sonst RoG-Rot.</summary>
    internal static IBrush TypeBrush(string? color) => UiResources.FromHex(color, "RoGRedBrush");
}

/// <summary>Zelle der Monatsansicht: Tageszahl, bis zu drei Punkte, heute rot umrandet; Klick zeigt die Termine des Tages.</summary>
public sealed partial class MonthDayViewModel : ObservableObject
{
    private readonly EventsViewModel _owner;

    public MonthDayViewModel(MonthDay day, EventsViewModel owner, AppServices services)
    {
        Day = day;
        _owner = owner;
        Number = day.Date.Day.ToString(services.Culture);
        DotCount = Math.Min(3, day.Events.Count);
        Dots = day.Events.Take(3).Select(e => EventsViewModel.TypeBrush(e.Color)).ToList();
        AutomationName = $"{day.Date.ToDateTime(TimeOnly.MinValue).ToString("D", services.Culture)}: {day.Events.Count}";
    }

    public MonthDay Day { get; }

    public string Number { get; }

    public int DotCount { get; }

    public IReadOnlyList<IBrush> Dots { get; }

    public bool IsOutside => !Day.InMonth;

    public bool IsToday => Day.IsToday;

    public string AutomationName { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => _owner.ShowDay(this);
}

/// <summary>Zeile: Tagesüberschrift (beim ersten Termin des Tages), Uhrzeit, Titel, Typfarbe, eigene Antwort.</summary>
public sealed class EventRowViewModel(EventItem item, bool showDayHeader, AppServices services)
{
    public EventItem Item => item;

    public string Id => item.Id;

    public bool ShowDayHeader => showDayHeader;

    public string DayHeader { get; } = item.StartsAt?.ToLocalTime().ToString("dddd, d. MMMM", services.Culture).ToUpper(services.Culture) ?? string.Empty;

    public string Time { get; } = item.StartsAt?.ToLocalTime().ToString("t", services.Culture) ?? string.Empty;

    public string Title => item.Title;

    public string Subtitle { get; } = string.Join(" · ", new[] { item.TypeLabel, item.SquadName, item.LocationLabel }.Where(s => !string.IsNullOrEmpty(s)));

    public IBrush TypeBrush { get; } = EventsViewModel.TypeBrush(item.Color);

    public string RsvpText { get; } = EventsViewModel.RsvpLabel(item.MyRsvp, services.Localizer);

    public bool HasRsvp => item.MyRsvp != Rsvp.None;

    public bool Cancelled => item.Cancelled;

    public bool Mandatory => item.Mandatory;

    public double Opacity => item.Cancelled ? 0.55 : 1;

    public string AutomationName { get; } = string.Join(", ", new[]
    {
        item.StartsAt?.ToLocalTime().ToString("g", services.Culture),
        item.Title,
        item.Cancelled ? services.T(StringKeys.EventsCancelled) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>Termin: Kopf, Zeit, Ort, Zahlen, eigene Antwort (nur lesen), Teilnehmer, Server mit „Verbinden“, Beschreibung.</summary>
public sealed partial class EventDetailViewModel(AppServices services, EventService events) : LoadableViewModel(services)
{
    private EventItem? _item;
    private string? _id;

    public string? Id => _id;

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? TypeLabel { get; set; }

    [ObservableProperty]
    public partial IBrush? TypeBrush { get; set; }

    [ObservableProperty]
    public partial string? When { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWhere))]
    public partial string? Where { get; set; }

    public bool HasWhere => !string.IsNullOrEmpty(Where);

    [ObservableProperty]
    public partial string? Counts { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFlags))]
    public partial string? Flags { get; set; }

    public bool HasFlags => !string.IsNullOrEmpty(Flags);

    [ObservableProperty]
    public partial bool IsCancelled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    public partial string? Description { get; set; }

    public bool HasDescription => !string.IsNullOrEmpty(Description);

    /// <summary>Eigene Antwort („Du bist dabei“) oder Hinweis, dass Antworten in der App noch nicht geht.</summary>
    [ObservableProperty]
    public partial string? RsvpText { get; set; }

    [ObservableProperty]
    public partial bool HasMyRsvp { get; set; }

    [ObservableProperty]
    public partial bool IsSignedIn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParticipants))]
    public partial string? ParticipantsText { get; set; }

    public bool HasParticipants => !string.IsNullOrEmpty(ParticipantsText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasServer))]
    public partial ServerItemViewModel? Server { get; set; }

    public bool HasServer => Server is not null;

    public FeedbackViewModel Feedback { get; } = new();

    protected override bool HasData => _item is not null;

    /// <summary>Aus der Liste: sofort zeigen, dann still den neuesten Stand und die Teilnehmer holen.</summary>
    public void Show(EventItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _id = item.Id;
        Feedback.Close();
        ErrorMessage = null;
        _ = ApplyAsync(item);
        _ = RefreshAsync(item.Id);
    }

    /// <summary>Per ID (Link): laden mit Ladezustand.</summary>
    public Task ShowAsync(string id)
    {
        _id = id;
        _item = null;
        Feedback.Close();
        return LoadAsync();
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (_id is null)
        {
            return;
        }

        var id = _id;
        var item = await events.GetAsync(id, cancellationToken);
        if (id == _id)
        {
            await ApplyAsync(item);
        }
    }

    private async Task RefreshAsync(string id)
    {
        try
        {
            var fresh = await events.GetAsync(id);
            if (id == _id)
            {
                await ApplyAsync(fresh);
            }
        }
        catch (Exception ex) when (ex is ApiProblemException or RoG.Core.Api.Generated.GeneratedApiException or HttpRequestException)
        {
            // Stand aus der Liste bleibt
        }
    }

    private async Task ApplyAsync(EventItem item)
    {
        _item = item;
        Title = item.Title;
        TypeLabel = item.TypeLabel.ToUpper(Services.Culture);
        TypeBrush = EventsViewModel.TypeBrush(item.Color);
        When = item.StartsAt is { } start ? EventTimes.Range(start, item.EndsAt, Services.Culture) : null;
        Where = item.LocationLabel;
        Counts = item.MaxParticipants is { } max
            ? Services.Format(StringKeys.EventsCountsMax, item.Yes, item.Maybe, max)
            : Services.Format(StringKeys.EventsCounts, item.Yes, item.Maybe);
        IsCancelled = item.Cancelled;
        Flags = string.Join(" · ", new[]
        {
            item.Cancelled ? Services.T(StringKeys.EventsCancelled) : null,
            item.Mandatory ? Services.T(StringKeys.EventsMandatory) : null,
            item.SquadName,
        }.Where(s => !string.IsNullOrEmpty(s)));
        Description = item.Description;
        Server = item.Server is null ? null : new ServerItemViewModel(item.Server, Services, Feedback);
        IsSignedIn = await Services.Tokens.GetAsync() is not null;
        HasMyRsvp = item.MyRsvp != Rsvp.None;
        RsvpText = HasMyRsvp
            ? EventsViewModel.RsvpLabel(item.MyRsvp, Services.Localizer)
            : Services.T(IsSignedIn ? "Linux_EventsNoRsvp" : "Linux_EventsRsvpGuest");
        DataChanged();
        await LoadParticipantsAsync(item.Id);
    }

    /// <summary>Teilnehmerliste (nur angemeldet, nur Mitglieder/Verwalter) – Fehler blenden sie nur aus.</summary>
    private async Task LoadParticipantsAsync(string id)
    {
        if (Services.CurrentUser is null)
        {
            ParticipantsText = null;
            return;
        }

        try
        {
            var participants = await new EventLineupService(Services.Api.Http).ParticipantsAsync(id);
            if (id != _id)
            {
                return;
            }

            string Names(IEnumerable<EventParticipant> list) =>
                string.Join(", ", list.Select(p => p.Absent ? p.Name + " (" + Services.T(StringKeys.EventsAbsent) + ")" : p.Name));
            ParticipantsText = string.Join("\n", new[]
            {
                participants.Yes.Count > 0 ? Services.Format(StringKeys.EventsParticipantsYes, participants.Yes.Count, Names(participants.Yes)) : null,
                participants.Maybe.Count > 0 ? Services.Format(StringKeys.EventsParticipantsMaybe, participants.Maybe.Count, Names(participants.Maybe)) : null,
                participants.No.Count > 0 ? Services.Format(StringKeys.EventsParticipantsNo, participants.No.Count, Names(participants.No)) : null,
                participants.Open.Count > 0 ? Services.Format(StringKeys.EventsParticipantsOpen, participants.Open.Count, Names(participants.Open)) : null,
            }.Where(x => x is not null));
        }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            ParticipantsText = null;
        }
    }

    [RelayCommand]
    private async Task OpenInBrowserAsync()
    {
        if (_item?.WebUrl is { } url)
        {
            await Services.Browser.OpenAsync(url);
        }
    }

    /// <summary>„In Kalender übernehmen“: die .ics-Datei öffnet der Browser bzw. die Kalender-App des Systems.</summary>
    [RelayCommand]
    private async Task AddToCalendarAsync()
    {
        if (_item?.IcsUrl is { } ics && !await Services.Browser.OpenAsync(ics))
        {
            Feedback.Show(Services.T(StringKeys.EventsNoCalendarApp), MessageTone.Info);
        }
    }
}
