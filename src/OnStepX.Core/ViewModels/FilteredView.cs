using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace ASCOM.OnStepX.ViewModels
{
    // Stands in for WPF's CollectionViewSource.GetDefaultView(...) + Filter,
    // which has no Avalonia equivalent.
    //
    // Only what the console actually used is here: a predicate and a Refresh.
    // No grouping, no sorting, no currency, no deferred refresh. Bind a view
    // to Items.
    //
    // Adds are handled incrementally rather than by rebuilding, because the
    // console appends a row per mount round-trip — at a 750 ms poll with
    // several commands a pass, a full rebuild per line would reset the list
    // control thousands of times a session.
    public sealed class FilteredView<T>
    {
        private readonly ObservableCollection<T> _visible = new ObservableCollection<T>();
        private Predicate<T> _filter;

        public FilteredView(ObservableCollection<T> source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Items = new ReadOnlyObservableCollection<T>(_visible);
            Source.CollectionChanged += OnSourceChanged;
            Rebuild();
        }

        public ObservableCollection<T> Source { get; }

        // What a view binds to.
        public ReadOnlyObservableCollection<T> Items { get; }

        // Null means everything passes.
        public Predicate<T> Filter
        {
            get => _filter;
            set { _filter = value; Rebuild(); }
        }

        // Call after something the predicate closes over has changed — the
        // predicate itself is usually stable and the filter text is not.
        public void Refresh() => Rebuild();

        private bool Passes(T item) => _filter == null || _filter(item);

        private void OnSourceChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    // Appends keep their order; an insert in the middle would
                    // need the source index mapped into the visible list, and
                    // nothing here inserts.
                    if (e.NewItems != null && e.NewStartingIndex == Source.Count - e.NewItems.Count)
                    {
                        foreach (T item in e.NewItems)
                            if (Passes(item)) _visible.Add(item);
                        return;
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    if (e.OldItems != null)
                    {
                        foreach (T item in e.OldItems) _visible.Remove(item);
                        return;
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                    _visible.Clear();
                    return;
            }

            Rebuild();
        }

        private void Rebuild()
        {
            _visible.Clear();
            foreach (var item in Source)
                if (Passes(item)) _visible.Add(item);
        }
    }
}
