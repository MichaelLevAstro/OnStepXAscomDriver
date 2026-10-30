using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ASCOM.OnStepX.ViewModels
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // Returns true when the value actually changed; lets callers conditionally
        // chain side effects without re-checking equality.
        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
