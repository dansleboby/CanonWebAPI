using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReactiveUI;

namespace Canon.Test.Avalonia.ViewModels;

public class ViewModelBase : ReactiveObject
{
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        this.RaisePropertyChanged(propertyName);
        return true;
    }
}