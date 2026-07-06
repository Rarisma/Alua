using System.Collections.ObjectModel;

namespace Alua.Services;

/// <summary>
/// Temporary extension method for the test project to provide ToObservableCollection support.
/// The main app gets this from the Uno.Core.Extensions.Collections package, but the test
/// project uses a shared-source pattern and needs its own copy to compile GameGrouping.cs.
/// </summary>
internal static class ObservableCollectionExtensions
{
    public static ObservableCollection<T> ToObservableCollection<T>(this IEnumerable<T> source)
    {
        return new ObservableCollection<T>(source);
    }
}
