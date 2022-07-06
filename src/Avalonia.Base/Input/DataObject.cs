using System.Collections.Generic;

namespace Avalonia.Input
{
    public class DataObject : IDataObject
    {
        private readonly Dictionary<string, object> _items = new();

        public bool Contains(string dataFormat) => _items.ContainsKey(dataFormat);

        public object? Get(string dataFormat) => _items.TryGetValue(dataFormat, out var value) ? value : null;

        public IEnumerable<string> GetDataFormats() => _items.Keys;

        public IEnumerable<string>? GetFileNames() => Get(DataFormats.FileNames) as IEnumerable<string>;

        public string? GetText() => Get(DataFormats.Text) as string;

        public void Set(string dataFormat, object value) => _items[dataFormat] = value;
    }
}
