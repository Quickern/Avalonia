using System.Collections.Generic;
using Avalonia.Input;

namespace Avalonia.Wayland
{
    internal static class MimeTypes
    {
        internal const string Text = "text/plain";
        internal const string TextUtf8 = "text/plain;charset=utf-8";
        internal const string UriList = "text/uri-list";

        internal static IEnumerable<string> GetMimeTypes(IDataObject dataObject)
        {
            foreach (var dataFormat in dataObject.GetDataFormats())
            {
                switch (dataFormat)
                {
                    case DataFormats.Text:
                        yield return Text;
                        yield return TextUtf8;
                        break;
                    case DataFormats.FileNames:
                        yield return UriList;
                        break;
                }
            }
        }
    }
}
