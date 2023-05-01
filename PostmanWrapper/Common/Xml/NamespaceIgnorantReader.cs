using System.IO;
using System.Xml;

namespace Postman.Common.Xml
{
    public class NamespaceIgnorantReader<TInnerReader>
        : XmlTextReader
        where TInnerReader : TextReader
    {
        public TInnerReader InnerReader { get; }

        public NamespaceIgnorantReader(TInnerReader reader)
            : base(reader)
        {
            InnerReader = reader;
            Namespaces = false;
        }
    }

    public static class NamespaceIgnorantReaderExtensions
    {
        public static NamespaceIgnorantReader<TInnerReader> AsNamespaceIgnorantReader<TInnerReader>(this TInnerReader reader)
            where TInnerReader : TextReader
            => new NamespaceIgnorantReader<TInnerReader>(reader);
    }
}
