using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ThbgmPlayer.Core;

/// <summary>最简 MVVM 基类，只提供属性变更通知。不引第三方 MVVM 框架，保持单一依赖。</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>字段赋值并在值变化时发出通知。</summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
