using System.ComponentModel;

namespace DepartureBoard.Models;

public class PlatformFilter : INotifyPropertyChanged
{
    private bool _isSelected = true;

    public string Name { get; init; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
