using System;
using System.Collections.Generic;
using System.IO;
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
