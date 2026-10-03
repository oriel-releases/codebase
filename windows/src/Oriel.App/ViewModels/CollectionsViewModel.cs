using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Oriel.Core.Settings;

namespace Oriel.UI.ViewModels;

public sealed partial class TopicViewModel(CollectionsViewModel owner, string query, bool enabled, bool isCustom) : ObservableObject
{
    public string Query { get; } = query;
    public bool IsCustom { get; } = isCustom;
    public string Label => char.ToUpper(Query[0]) + Query[1..];

    [ObservableProperty] public partial bool IsEnabled { get; set; } = enabled;

    partial void OnIsEnabledChanged(bool value) => owner.Save();

    [RelayCommand]
    void Remove() => owner.RemoveTopic(this);
}

public sealed partial class KeywordViewModel(CollectionsViewModel owner, string keyword)
{
    public string Keyword { get; } = keyword;

    [RelayCommand]
    void Remove() => owner.RemoveKeyword(this);
}

public sealed partial class CollectionsViewModel : ObservableObject
{
    readonly ISettingsStore _settings;
    bool _loading;

    public CollectionsViewModel(ISettingsStore settings)
    {
        _settings = settings;
        Load();
    }

    public ObservableCollection<TopicViewModel> BuiltInTopics { get; } = [];
    public ObservableCollection<TopicViewModel> CustomTopics { get; } = [];
    public ObservableCollection<KeywordViewModel> ExcludedKeywords { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddTopicCommand))]
    public partial string NewTopic { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddKeywordCommand))]
    public partial string NewKeyword { get; set; } = "";

    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial bool NothingEnabled { get; set; }
    [ObservableProperty] public partial bool HasCustomTopics { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoKeywords))]
    public partial bool HasKeywords { get; set; }
    public bool HasNoKeywords => !HasKeywords;

    void Load()
    {
        _loading = true;
        var s = _settings.Load();
        BuiltInTopics.Clear();
        CustomTopics.Clear();
        ExcludedKeywords.Clear();
        foreach (var t in s.Topics.Where(t => !string.IsNullOrWhiteSpace(t.Query)))
            (t.IsCustom ? CustomTopics : BuiltInTopics).Add(new TopicViewModel(this, t.Query, t.Enabled, t.IsCustom));
        foreach (var k in s.ExcludedKeywords) ExcludedKeywords.Add(new KeywordViewModel(this, k));
        _loading = false;
        UpdateSummary();
    }

    internal void Save()
    {
        if (_loading) return;
        _settings.Update(s =>
        {
            s.Topics = BuiltInTopics.Concat(CustomTopics)
                .Select(t => new TopicSetting { Query = t.Query, Enabled = t.IsEnabled, IsCustom = t.IsCustom })
                .ToList();
            s.ExcludedKeywords = ExcludedKeywords.Select(k => k.Keyword).ToList();
        });
        UpdateSummary();
    }

    void UpdateSummary()
    {
        var on = BuiltInTopics.Concat(CustomTopics).Count(t => t.IsEnabled);
        NothingEnabled = on == 0;
        Summary = on switch
        {
            0 => "No topics on",
            1 => "1 topic on. Every day uses it.",
            _ => $"{on} topics on. Each day picks one at random.",
        };
        HasCustomTopics = CustomTopics.Count > 0;
        HasKeywords = ExcludedKeywords.Count > 0;
    }

    bool CanAddTopic() => !string.IsNullOrWhiteSpace(NewTopic);

    [RelayCommand(CanExecute = nameof(CanAddTopic))]
    void AddTopic()
    {
        var q = NewTopic.Trim().ToLowerInvariant();
        NewTopic = "";
        var existing = BuiltInTopics.Concat(CustomTopics).FirstOrDefault(t => t.Query.Equals(q, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsEnabled = true;
            return;
        }
        CustomTopics.Add(new TopicViewModel(this, q, true, isCustom: true));
        Save();
    }

    internal void RemoveTopic(TopicViewModel topic)
    {
        CustomTopics.Remove(topic);
        Save();
    }

    bool CanAddKeyword() => !string.IsNullOrWhiteSpace(NewKeyword);

    [RelayCommand(CanExecute = nameof(CanAddKeyword))]
    void AddKeyword()
    {
        var k = NewKeyword.Trim().ToLowerInvariant();
        NewKeyword = "";
        if (ExcludedKeywords.Any(x => x.Keyword == k)) return;
        ExcludedKeywords.Add(new KeywordViewModel(this, k));
        Save();
    }

    internal void RemoveKeyword(KeywordViewModel keyword)
    {
        ExcludedKeywords.Remove(keyword);
        Save();
    }

    [RelayCommand]
    void SelectAll(bool enable)
    {
        _loading = true;
        foreach (var t in BuiltInTopics) t.IsEnabled = enable;
        _loading = false;
        Save();
    }
}
