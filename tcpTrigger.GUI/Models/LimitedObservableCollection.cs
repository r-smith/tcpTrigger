using System;
using System.Collections.ObjectModel;

namespace tcpTrigger.GUI.Models
{
    // An ObservableCollection that holds at most Capacity items.
    // Intended for newest-first lists: insert new items at index 0, and when the
    // collection is full, the last (oldest) item is removed to make room.
    public class LimitedObservableCollection<T> : ObservableCollection<T>
    {
        public int Capacity { get; }

        public LimitedObservableCollection(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
            }

            Capacity = capacity;
        }

        // Called by both Insert and Add, so the limit can't be bypassed.
        protected override void InsertItem(int index, T item)
        {
            if (Count >= Capacity)
            {
                RemoveAt(Count - 1);

                // Removing the last item shortens the list, so an index that pointed
                // past the end (as Add does) must be moved back to the new end.
                if (index > Count)
                {
                    index = Count;
                }
            }

            base.InsertItem(index, item);
        }
    }
}