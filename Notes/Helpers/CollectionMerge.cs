using System.Collections.ObjectModel;

namespace Notes.Helpers;

public static class CollectionMerge
{
  // Brings target to match desired (order included) with minimal changes:
  // removes missing items, inserts new ones, moves the rest. Items already in
  // place produce no collection events at all, so the UI doesn't rebuild rows
  // that didn't change — no flicker, selection and scroll position survive.
  public static void MergeInto<T>(ObservableCollection<T> target, IReadOnlyList<T> desired) where T : class
  {
    var desiredSet = new HashSet<T>(desired);
    for (int i = target.Count - 1; i >= 0; i--)
      if (!desiredSet.Contains(target[i]))
        target.RemoveAt(i);

    for (int i = 0; i < desired.Count; i++)
    {
      var item = desired[i];
      int current = target.IndexOf(item);
      if (current == -1)
        target.Insert(i, item);
      else if (current != i)
        target.Move(current, i);
    }
  }
}
