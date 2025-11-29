using System.ComponentModel;
using System.IO;

namespace TxtConverter.Core.Models;

public class FileTreeNode : System.ComponentModel.INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsFile { get; set; }

    // Родитель для всплывания событий
    public FileTreeNode? Parent { get; set; }

    // Дети для отображения
    public List<FileTreeNode> Children { get; } = new();

    private bool? _isChecked = false;
    private bool _isExpanded = true;

    // IsChecked: true (галочка), false (пусто), null (минус/частично)
    public bool? IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                OnPropertyChanged(nameof(IsChecked));

                // 1. Обновляем детей (вниз)
                // Если мы ставим true/false, дети должны принять то же значение.
                // Если null, то ничего не делаем (пользователь не может кликнуть в null, это делает логика)
                if (_isChecked.HasValue)
                {
                    UpdateChildren(_isChecked.Value);
                }

                // 2. Обновляем родителя (вверх)
                Parent?.RecalculateState();
            }
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); }
    }

    private void UpdateChildren(bool state)
    {
        foreach (var child in Children)
        {
            // Устанавливаем поле напрямую, чтобы не триггерить рекурсию вверх сразу же
            // но нам нужно триггерить обновление детей ребенка
            if (child.IsChecked != state)
            {
                child._isChecked = state;
                child.OnPropertyChanged(nameof(IsChecked));
                child.UpdateChildren(state);
            }
        }
    }

    public void RecalculateState()
    {
        bool allChecked = true;
        bool allUnchecked = true;

        foreach (var child in Children)
        {
            if (child.IsChecked == true) allUnchecked = false;
            else if (child.IsChecked == false) allChecked = false;
            else
            {
                // Если ребенок null, то и мы null
                allChecked = false;
                allUnchecked = false;
                break;
            }
        }

        bool? newState;
        if (allChecked) newState = true;
        else if (allUnchecked) newState = false;
        else newState = null;

        if (_isChecked != newState)
        {
            _isChecked = newState;
            OnPropertyChanged(nameof(IsChecked));
            Parent?.RecalculateState();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}