using WhisperMd.App.Infrastructure;

namespace WhisperMd.App.Models;

public sealed class RecordingGroup : ViewModelBase
{
    private string _name;

    public RecordingGroup(string name)
    {
        _name = name;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string Name
    {
        get => _name;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Без названия" : value.Trim();
            SetProperty(ref _name, normalized);
        }
    }
}
