using System.Runtime.InteropServices;

namespace FileDial.App.Selection;

/// <summary>Собирает полученные COM-объекты и освобождает их в обратном порядке.</summary>
internal sealed class ComScope : IDisposable
{
    private readonly List<object> _objects = [];

    public T? Track<T>(T? value) where T : class
    {
        if (value is not null && Marshal.IsComObject(value) && !_objects.Any(o => ReferenceEquals(o, value)))
            _objects.Add(value);
        return value;
    }

    public void Dispose()
    {
        for (var i = _objects.Count - 1; i >= 0; i--)
        {
            try
            {
                Marshal.FinalReleaseComObject(_objects[i]);
            }
            catch (Exception)
            {
                // Объект уже отключён — освобождать нечего.
            }
        }
        _objects.Clear();
    }
}
