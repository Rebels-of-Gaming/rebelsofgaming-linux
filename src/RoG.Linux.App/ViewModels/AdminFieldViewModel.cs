using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RoG.Core.Api;
using RoG.Core.Features.Admin;
using RoG.Core.Localization;

namespace RoG.Linux.App.ViewModels;

/// <summary>
/// Ein Formularfeld des allgemeinen Verwaltungs-Editors (wie <c>AdminFieldViewModel</c> der Windows-App) – genutzt von
/// „Neuer Gameserver“. Die Darstellung wählt <c>Controls/AdminFieldsView</c> über <see cref="IsText"/>,
/// <see cref="IsToggle"/>, <see cref="IsChoice"/> und <see cref="IsTranslated"/> (Avalonia hat keinen Template-Selector).
/// </summary>
public sealed partial class AdminFieldViewModel : ObservableObject
{
    private readonly bool _partial;

    public AdminFieldViewModel(AdminField field, object? value, ILocalizer localizer, bool enabled, string locale)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(localizer);
        Field = field;
        IsEnabled = enabled;
        Label = (field.LabelText ?? localizer.GetString(field.LabelKey)) + (field.Required ? " *" : string.Empty);
        Help = string.IsNullOrEmpty(field.HelpText) ? (field.HelpKey is null ? null : localizer.GetString(field.HelpKey)) : field.HelpText;
        Placeholder = field.Type switch
        {
            AdminFieldType.Color => "#E0324A",
            AdminFieldType.Date => "2026-10-17",
            AdminFieldType.DateTime => "2026-10-17 20:30",
            AdminFieldType.Url => "https://",
            AdminFieldType.Lines => localizer.GetString(StringKeys.AdminFormOnePerLine),
            _ => string.Empty,
        };
        if (field.IsTranslated)
        {
            var map = value as IReadOnlyDictionary<string, string> ?? new Dictionary<string, string>();
            De = map.GetValueOrDefault("de") ?? string.Empty;
            En = map.GetValueOrDefault("en") ?? string.Empty;
            Fr = map.GetValueOrDefault("fr") ?? string.Empty;

            // API lieferte nur eine Sprache: nur diese zeigen, die anderen bleiben auf der Website unverändert
            _partial = value is AdminTranslations { Partial: true };
            ShowDe = !_partial || locale == "de";
            ShowEn = !_partial || locale == "en";
            ShowFr = !_partial || locale == "fr";
            PartialHint = _partial ? localizer.GetString(StringKeys.AdminFormPartialLanguage) : null;
        }
        else if (field.Type == AdminFieldType.Bool)
        {
            IsOn = value is true;
        }
        else
        {
            Text = value as string ?? string.Empty;
        }

        if (field.Choices is { } choices)
        {
            if (!field.Required)
            {
                Choices.Add(new SettingsOption(string.Empty, "–"));
            }

            foreach (var c in choices)
            {
                Choices.Add(new SettingsOption(c.Value, c.LabelText ?? localizer.GetString(c.LabelKey)));
            }

            SelectedChoice = Choices.FirstOrDefault(c => c.Value == Text) ?? (field.Required ? null : Choices[0]);
        }
    }

    public AdminField Field { get; }

    /// <summary>Auswahl (fest oder Verweis) – Mehrfachauswahl bleibt wie in Windows ein Textfeld (IDs, Komma/Zeile).</summary>
    public bool IsChoice => Field.Type is AdminFieldType.Choice or AdminFieldType.Reference;

    public bool IsToggle => Field.Type == AdminFieldType.Bool;

    public bool IsTranslated => Field.IsTranslated;

    public bool IsText => !IsChoice && !IsToggle && !IsTranslated;

    public bool ShowDe { get; } = true;

    public bool ShowEn { get; } = true;

    public bool ShowFr { get; } = true;

    public string? PartialHint { get; }

    public bool HasPartialHint => PartialHint is not null;

    public string Label { get; }

    public string? Help { get; }

    public bool HasHelp => !string.IsNullOrEmpty(Help);

    public string Placeholder { get; }

    public bool IsEnabled { get; }

    public bool AcceptsReturn => Field.Type is AdminFieldType.Multiline or AdminFieldType.TranslatedMultiline or AdminFieldType.Lines or AdminFieldType.MultiChoice;

    public double MinTextHeight => AcceptsReturn ? 96 : 0;

    public int MaxLength => Field.MaxLength ?? 0;

    public ObservableCollection<SettingsOption> Choices { get; } = [];

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    [ObservableProperty]
    public partial string De { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string En { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Fr { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SettingsOption? SelectedChoice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    partial void OnSelectedChoiceChanged(SettingsOption? value) => Text = value?.Value ?? string.Empty;

    /// <summary>Wert für <see cref="AdminForm"/>.</summary>
    public object? Value => Field.IsTranslated ? Translations() : Field.Type == AdminFieldType.Bool ? IsOn : Text;

    private AdminTranslations Translations()
    {
        var map = new AdminTranslations { Partial = _partial };
        if (ShowDe)
        {
            map["de"] = De;
        }

        if (ShowEn)
        {
            map["en"] = En;
        }

        if (ShowFr)
        {
            map["fr"] = Fr;
        }

        return map;
    }

    /// <summary>Auswahl für Verweise aus der Liste des anderen Bereichs (Spiele, Squads).</summary>
    public async Task LoadReferenceAsync(AdminResourceService resources, AdminArea area, string locale)
    {
        ArgumentNullException.ThrowIfNull(resources);
        try
        {
            var current = Text;
            var page = await resources.ListAsync(area, locale);
            if (!Field.Required)
            {
                Choices.Add(new SettingsOption(string.Empty, "–"));
            }

            foreach (var r in page.Items)
            {
                Choices.Add(new SettingsOption(AdminForm.Display(r.Raw, "id", locale) ?? r.Id, r.Title));
            }

            // Wert, den die Liste nicht kennt: als eigene Wahl behalten statt still zu leeren
            if (current.Length > 0 && Choices.All(c => c.Value != current))
            {
                Choices.Add(new SettingsOption(current, current));
            }

            SelectedChoice = Choices.FirstOrDefault(c => c.Value == current);
        }
        catch (Exception ex) when (ex is ApiProblemException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // ohne Liste bleibt das Feld leer – der Server meldet fehlende Pflichtwerte
        }
    }
}
