using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TitanAILivePC.Models;

public sealed class LiveComment : INotifyPropertyChanged
{
    private bool _isLatestInbound;
    private bool _hasAiHandled;

    public string UserName { get; init; } = string.Empty;
    public string CommentText { get; init; } = string.Empty;
    public int ConfidenceScore { get; init; } = 100;
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string TimeDisplay => Timestamp.ToString("HH:mm:ss");

    public bool IsLatestInbound
    {
        get => _isLatestInbound;
        set => SetField(ref _isLatestInbound, value);
    }

    public bool HasAiHandled
    {
        get => _hasAiHandled;
        set => SetField(ref _hasAiHandled, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
