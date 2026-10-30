using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Features.Servers;
using RoG.Core.Localization;
using RoG.Linux.App.Services;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// „Neuer Gameserver“ (Pterodactyl über die Website, wie <c>ServerProvisionViewModel</c> der Windows-App): Node und Egg
/// wählen, Formular nach Egg (Variablen mit Regeln), Limits, Website-Felder, Ablauf. Nach dem Anlegen bzw. für einen
/// bestehenden, über die Website angelegten Server: Installationsstand, Ablauf ändern/verlängern, Löschen in der
/// Gefahrenzone (Bestätigung mit dem Servernamen). Rückfragen über <see cref="Confirmation"/> im Inhalt.
/// </summary>
public sealed partial class ServerProvisionViewModel : ObservableObject
{
    private static readonly int[] s_defaultPresets = [1, 3, 7, 14, 30];

    private readonly AppServices _services;
    private readonly ServerProvisioningService _provisioning;
    private readonly AdminResourceService _resources;
    private ProvisionCatalog? _catalog;
    private AdminArea? _area;
    private string? _slug;

    public ServerProvisionViewModel(AppServices services, GameServer? existing)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _provisioning = new ServerProvisioningService(services.Api.Http);
        _resources = new AdminResourceService(services.Api.Http);
        Confirm = (title, text) => Confirmation.AskAsync(title, text, services.T(StringKeys.ProvisionConfirmYes), services.T(StringKeys.CommonCancel));
        if (existing is not null)
        {
            ShowManage(existing.Slug, existing.Name, existing.Provisioning);
        }
    }

    /// <summary>Server angelegt oder gelöscht – Serverliste neu laden.</summary>
    public event EventHandler? Changed;

    /// <summary>Rückfrage (Titel, Text) → bestätigt; Standard: Leiste im Inhalt.</summary>
    public Func<string, string, Task<bool>> Confirm { get; set; }

    public InlineConfirmViewModel Confirmation { get; } = new();

    public FeedbackViewModel Feedback { get; } = new();

    public ObservableCollection<SettingsOption> Nodes { get; } = [];

    public ObservableCollection<SettingsOption> Eggs { get; } = [];

    public ObservableCollection<AdminFieldViewModel> Fields { get; } = [];

    public ObservableCollection<SettingsOption> ExpiryOptions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCreate), nameof(ShowManagePanel), nameof(Title))]
    public partial bool IsManaging { get; set; }

    public bool ShowCreate => !IsManaging && IsCatalogReady;

    public bool ShowManagePanel => IsManaging;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCreate))]
    public partial bool IsCatalogReady { get; set; }

    /// <summary>Katalog konnte nicht geladen werden – Fehlerzustand mit „Erneut versuchen“.</summary>
    [ObservableProperty]
    public partial string? LoadError { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Katalog wird geladen (Ladezustand statt leerem Formular).</summary>
    [ObservableProperty]
    public partial bool IsLoadingCatalog { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedNode { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedEgg { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNodeInfo))]
    public partial string? NodeInfo { get; set; }

    public bool HasNodeInfo => !string.IsNullOrEmpty(NodeInfo);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEggInfo))]
    public partial string? EggInfo { get; set; }

    public bool HasEggInfo => !string.IsNullOrEmpty(EggInfo);

    // ---- Verwaltung eines angelegten Servers ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    public partial string? ServerName { get; set; }

    [ObservableProperty]
    public partial string? StatusText { get; set; }

    [ObservableProperty]
    public partial string? ExpiryText { get; set; }

    [ObservableProperty]
    public partial bool HasExpiry { get; set; }

    [ObservableProperty]
    public partial SettingsOption? SelectedExpiry { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    public partial string ConfirmName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    public partial bool Deletable { get; set; }

    public bool CanDelete => Deletable && ServerName is { } name && ServerProvisioningService.ConfirmMatches(name, ConfirmName.Trim());

    public string Title => _services.T(IsManaging ? StringKeys.ProvisionManageTitle : StringKeys.ProvisionTitle);

    /// <summary>Katalog laden (nur beim Anlegen) und Ablauf-Vorgaben füllen.</summary>
    public async Task LoadAsync()
    {
        if (IsManaging)
        {
            if (ExpiryOptions.Count == 0)
            {
                FillExpiryOptions(s_defaultPresets);
            }

            return;
        }

        LoadError = null;
        IsLoadingCatalog = true;
        var ok = await RunAsync(async () =>
        {
            _catalog = await _provisioning.CatalogAsync();
            Nodes.Clear();
            foreach (var n in _catalog.Nodes)
            {
                Nodes.Add(new SettingsOption(n.Id.ToString(CultureInfo.InvariantCulture), $"{n.Name} · {_catalog.LocationOf(n)}"));
            }

            Eggs.Clear();
            foreach (var e in _catalog.Eggs.OrderBy(e => e.NestName, StringComparer.CurrentCulture).ThenBy(e => e.Name, StringComparer.CurrentCulture))
            {
                Eggs.Add(new SettingsOption(e.Id.ToString(CultureInfo.InvariantCulture), e.Display));
            }

            FillExpiryOptions(_catalog.ExpiryPresets);
            SelectedNode = Nodes.FirstOrDefault();
            IsCatalogReady = true;
            await RebuildAsync();
        }, success: null, report: false);
        IsLoadingCatalog = false;
        if (!ok)
        {
            LoadError = _lastError;
        }
    }

    [RelayCommand]
    private Task RetryAsync() => LoadAsync();

    private void FillExpiryOptions(IEnumerable<int> presets)
    {
        ExpiryOptions.Clear();
        foreach (var d in presets)
        {
            ExpiryOptions.Add(new SettingsOption(d.ToString(CultureInfo.InvariantCulture), _services.T("Provision_Days_" + d.ToString(CultureInfo.InvariantCulture))));
        }

        SelectedExpiry = ExpiryOptions.FirstOrDefault(o => o.Value == "7") ?? ExpiryOptions.FirstOrDefault();
    }

    private ProvisionNode? Node => _catalog?.Nodes.FirstOrDefault(n => n.Id.ToString(CultureInfo.InvariantCulture) == SelectedNode?.Value);

    private ProvisionEgg? Egg => _catalog?.Eggs.FirstOrDefault(e => e.Id.ToString(CultureInfo.InvariantCulture) == SelectedEgg?.Value);

    partial void OnSelectedNodeChanged(SettingsOption? value) => _ = RebuildAsync();

    partial void OnSelectedEggChanged(SettingsOption? value) => _ = RebuildAsync();

    /// <summary>Formular neu aufbauen (Node/Egg gewechselt) – allgemeine Eingaben bleiben erhalten.</summary>
    private async Task RebuildAsync()
    {
        if (_catalog is null)
        {
            return;
        }

        var node = Node;
        var egg = Egg;
        NodeInfo = node is null ? null : _services.Format(StringKeys.ProvisionNodeInfo, node.Fqdn, node.MemoryMb / 1024, node.DiskMb / 1024, node.Allocations.Count);
        EggInfo = egg?.Description;
        var keep = Fields.Count > 0 ? Current() : null;
        var values = ProvisionForm.Defaults(egg, keep);
        _area = ProvisionForm.Area(_catalog, node, egg);
        Fields.Clear();
        foreach (var f in _area.Fields)
        {
            Fields.Add(new AdminFieldViewModel(f, values.GetValueOrDefault(f.Key), _services.Localizer, enabled: true, _services.Language));
        }

        foreach (var field in Fields.Where(f => f.Field.Type == AdminFieldType.Reference && f.Field.Reference is not null).ToList())
        {
            if (AdminAreas.Find(field.Field.Reference!) is { } refArea)
            {
                await field.LoadReferenceAsync(_resources, refArea, _services.Language);
            }
        }
    }

    private AdminFormValues Current()
    {
        var values = new AdminFormValues();
        foreach (var f in Fields)
        {
            values[f.Field.Key] = f.Value;
        }

        return values;
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (_area is null || Node is not { } node || Egg is not { } egg)
        {
            Show(_services.T(StringKeys.ProvisionChooseNodeEgg), MessageTone.Info);
            return;
        }

        var values = Current();
        var errors = ProvisionForm.Validate(_area, egg, values);
        foreach (var f in Fields)
        {
            f.Error = errors.TryGetValue(f.Field.Key, out var e) ? ErrorText(e) : null;
        }

        if (errors.Count > 0)
        {
            Show(_services.T(StringKeys.AdminFormCheckFields), MessageTone.Error);
            return;
        }

        var name = values.Text("name").Trim();
        if (!await Confirm(_services.T(StringKeys.ProvisionConfirmTitle), _services.Format(StringKeys.ProvisionConfirmText, name, node.Name, egg.Display)))
        {
            return;
        }

        await RunAsync(async () =>
        {
            var (_, slug, provisioning) = await _provisioning.ProvisionAsync(ProvisionForm.Body(_area, node, egg, values));
            Changed?.Invoke(this, EventArgs.Empty);
            ShowManage(slug, name, provisioning);
        }, success: StringKeys.ProvisionCreated);
    }

    private string ErrorText(string error)
    {
        // „Schlüssel|Wert“ aus den Egg-Regeln, sonst Fehler des allgemeinen Editors
        var parts = error.Split('|', 2);
        var text = _services.T(parts[0]);
        return parts.Length == 2 ? string.Format(_services.Culture, text, parts[1]) : text;
    }

    private void ShowManage(string slug, string name, ServerProvisioning? provisioning)
    {
        _slug = slug;
        ServerName = name;
        IsManaging = true;
        Apply(provisioning);
        if (ExpiryOptions.Count == 0)
        {
            FillExpiryOptions(_catalog?.ExpiryPresets ?? s_defaultPresets);
        }
    }

    private void Apply(ServerProvisioning? p)
    {
        StatusText = p is null
            ? _services.T(StringKeys.ProvisionStatusUnknown)
            : _services.T("Provision_Status_" + p.Status) is var t && !t.StartsWith("Provision_Status_", StringComparison.Ordinal) ? t : p.Status;
        HasExpiry = p?.ExpiresAt is not null;
        ExpiryText = p?.ExpiresAt is { } at
            ? _services.Format(StringKeys.ProvisionExpiresAt, at.ToLocalTime().ToString("g", _services.Culture))
            : _services.T(StringKeys.ProvisionNoExpiry);
        Deletable = p?.Deletable == true;
    }

    [RelayCommand]
    private Task RefreshStatusAsync() => _slug is not { } slug ? Task.CompletedTask : RunAsync(async () => Apply(await _provisioning.RefreshAsync(slug)), success: null);

    [RelayCommand]
    private Task SetExpiryAsync() => ExpiryDays() is not { } days || _slug is not { } slug ? Task.CompletedTask
        : RunAsync(async () => Apply(await _provisioning.SetExpiryAsync(slug, inDays: days)), success: StringKeys.ProvisionExpirySaved);

    [RelayCommand]
    private Task ExtendExpiryAsync() => ExpiryDays() is not { } days || _slug is not { } slug ? Task.CompletedTask
        : RunAsync(async () => Apply(await _provisioning.SetExpiryAsync(slug, extendDays: days)), success: StringKeys.ProvisionExpirySaved);

    [RelayCommand]
    private Task RemoveExpiryAsync() => _slug is not { } slug ? Task.CompletedTask
        : RunAsync(async () => Apply(await _provisioning.SetExpiryAsync(slug)), success: StringKeys.ProvisionExpirySaved);

    private int? ExpiryDays() => int.TryParse(SelectedExpiry?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : null;

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!CanDelete || _slug is not { } slug || ServerName is not { } name)
        {
            return;
        }

        if (!await Confirm(_services.T(StringKeys.ProvisionDeleteTitle), _services.Format(StringKeys.ProvisionDeleteText, name)))
        {
            return;
        }

        await RunAsync(async () =>
        {
            await _provisioning.DeleteAsync(slug, ConfirmName.Trim());
            Deletable = false;
            ConfirmName = string.Empty;
            StatusText = _services.T(StringKeys.ProvisionDeleted);
            HasExpiry = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }, success: StringKeys.ProvisionDeleted);
    }

    private string? _lastError;

    private async Task<bool> RunAsync(Func<Task> action, string? success, bool report = true)
    {
        IsBusy = true;
        string message;
        try
        {
            await action();
            if (success is not null)
            {
                Show(_services.T(success), MessageTone.Success);
            }

            return true;
        }
        catch (ApiProblemException ex)
        {
            // Feldfehler der API (auch environment.<VARIABLE>) direkt am Feld zeigen
            foreach (var f in Fields)
            {
                if (ex.Problem.Errors.TryGetValue(f.Field.Key, out var messages) && messages.Count > 0)
                {
                    f.Error = string.Join(" ", messages);
                }
            }

            // Prüfung bzw. Ablehnung: Text der API ist aussagekräftiger als der allgemeine
            message = ex.Problem.Code is "validation_failed" or "provisioning_rejected" && ex.Problem.Detail is { } d ? d : _services.Localizer.GetError(ex.Problem.Code);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException)
        {
            message = _services.Localizer.GetError(ex is HttpRequestException ? "network_error" : ApiProblem.Codes.Unknown);
        }
        finally
        {
            IsBusy = false;
        }

        _lastError = message;
        if (report)
        {
            Show(message, MessageTone.Error);
        }

        return false;
    }

    private void Show(string text, MessageTone tone) => Feedback.Show(text, tone);
}
