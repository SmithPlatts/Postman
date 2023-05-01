using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Postman.Common.Xml;

namespace Postman.Common
{
    [XmlRoot(ElementName = "Data")]
    public class ProjectConfiguration
    {
        private static readonly Lazy<XmlSerializer> XmlSerialiser = new Lazy<XmlSerializer>(() => new XmlSerializer(typeof(ProjectConfiguration)));
        private static readonly Lazy<JsonSerializer> JsonSerialiser = new Lazy<JsonSerializer>(() => JsonSerializer.CreateDefault());

        public ProjectConnection Connection { get; set; } = new ProjectConnection();
        public ProjectTestCase TestCase { get; set; } = new ProjectTestCase();

        public static ProjectConfiguration DeserializeJson<TReader>(TReader reader)
            where TReader : TextReader
        {
            using (JsonReader jsonReader = new JsonTextReader(reader))
            {
                return JsonSerialiser.Value.Deserialize<ProjectConfiguration>(jsonReader);
            }
        }

        public static ProjectConfiguration DeserializeXml<TReader>(TReader reader)
            where TReader : TextReader
        {
            using (NamespaceIgnorantReader<TReader> xmlReader = reader.AsNamespaceIgnorantReader())
            {
                return XmlSerialiser.Value.Deserialize(xmlReader) as ProjectConfiguration;
            }
        }

        public override string ToString() => ToString(0);

        public string ToString(int indentSize)
        {
            string indent = indentSize > 0 ? "".PadRight(indentSize, ' ') : string.Empty;
            StringBuilder builder = new StringBuilder();
            builder.AppendFormat("{0}- {1}:", indent, nameof(Connection)).AppendLine();
            builder.AppendFormat("{0}| - {1}: {2}", indent, nameof(Connection.Url), Connection.UrlString).AppendLine();
            builder.AppendFormat("{0}| - {1}: {2}", indent, nameof(Connection.Project), Connection.Project).AppendLine();
            builder.AppendFormat("{0}- {1}:", indent, nameof(TestCase)).AppendLine();
            builder.AppendFormat("{0}| - {1}: {2}", indent, nameof(TestCase.AreaPath), TestCase.AreaPath).AppendLine();
            builder.AppendFormat("{0}| - {1}:", indent, nameof(TestCase.CustomFields)).AppendLine();
            foreach (ProjectCustomField field in TestCase?.CustomFields ?? Enumerable.Empty<ProjectCustomField>())
            {
                builder.AppendFormat("{0}| | - {1}: {2}", indent, nameof(field.Id), field.Id).AppendLine();
                builder.AppendFormat("{0}| | - {1}: {2}", indent, nameof(field.DefaultValue), field.DefaultValue).AppendLine();
            }

            return builder.ToString();
        }
    }

    public class ProjectConnection
    {
        private Uri _url = null;

        [JsonIgnore]
        [XmlIgnore]
        public Uri Url
        {
            get
            {
                if (_url is null && !string.IsNullOrWhiteSpace(UrlString))
                {
                    _url = new Uri(UrlString);
                }

                return _url;
            }
            set => _url = value;
        }

        [JsonProperty("Url")]
        [XmlElement("Url")]
        public string UrlString { get; set; } = string.Empty;

        public string Project { get; set; } = string.Empty;
    }

    public class ProjectCustomField
    {
        [XmlAttribute("id")]
        public string Id { get; set; } = string.Empty;

        [XmlAttribute("defaultvalue")]
        public string DefaultValue { get; set; } = string.Empty;
    }

    public class ProjectTestCase
    {
        public string AreaPath { get; set; } = string.Empty;

        [XmlArrayItem("CustomField")]
        public List<ProjectCustomField> CustomFields { get; set; } = new List<ProjectCustomField>();
    }
}
