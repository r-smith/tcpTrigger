using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using tcpTrigger.GUI.Models;

namespace tcpTrigger.GUI.Configuration
{
    // Exports ignore rules as text, and imports them back. The text is XML, with the same <rule> elements as the
    // configuration file inside an <ignoreRules> element.
    internal static class IgnoreRuleTransfer
    {
        // Returns the rules as text, for a file or the clipboard.
        public static string Export(IEnumerable<IgnoreRule> rules)
        {
            StringBuilder text = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true };
            using (XmlWriter writer = XmlWriter.Create(text, settings))
            {
                writer.WriteStartElement("ignoreRules");
                foreach (IgnoreRule rule in rules)
                {
                    ConfigWriter.WriteIgnoreRule(writer, rule);
                }
                writer.WriteEndElement(); // End: ignoreRules
            }
            return text.ToString();
        }

        // Returns the rules in the text: an export, a whole configuration file, or <rule> elements copied from one.
        // Returns an empty list if the text has no rules or isn't XML. Throws FormatException if a rule can't be read.
        public static List<IgnoreRule> Import(string text)
        {
            List<IgnoreRule> rules = new List<IgnoreRule>();
            XmlDocument xd = LoadXml(text);
            if (xd == null)
            {
                return rules;
            }

            foreach (XmlNode node in xd.SelectNodes("//rule"))
            {
                IgnoreRule rule;
                try
                {
                    rule = ConfigReader.ReadIgnoreRule(node);
                }
                catch (Exception ex)
                {
                    throw new FormatException($"An ignore rule couldn't be read.{Environment.NewLine}{Environment.NewLine}{ex.Message}", ex);
                }

                if (string.IsNullOrWhiteSpace(rule.Name))
                {
                    rule.Name = "Imported rule";
                }
                if (rule.Created == default(DateTime))
                {
                    rule.Created = DateTime.Now;
                }
                if (rule.Modified == default(DateTime))
                {
                    rule.Modified = rule.Created;
                }
                rules.Add(rule);
            }
            return rules;
        }

        // Returns true if both rules match the same sources, destinations, and ports, whatever their names.
        public static bool HasSameMatch(IgnoreRule a, IgnoreRule b)
        {
            return HasSameEntries(a.Sources, b.Sources)
                && HasSameEntries(a.Destinations, b.Destinations)
                && HasSameEntries(a.Services, b.Services);
        }

        private static bool HasSameEntries(List<string> a, List<string> b)
        {
            HashSet<string> entries = new HashSet<string>(a.Select(entry => entry.Trim()), StringComparer.OrdinalIgnoreCase);
            return entries.SetEquals(b.Select(entry => entry.Trim()));
        }

        // Rules copied out of a configuration file have no single root element, so if the text doesn't load as it is,
        // it's tried again inside one. Returns null if it isn't XML either way. DTDs aren't allowed, so the text can't
        // pull in outside files.
        private static XmlDocument LoadXml(string text)
        {
            foreach (string xml in new[] { text, "<root>" + text + "</root>" })
            {
                try
                {
                    XmlReaderSettings settings = new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null
                    };
                    using (XmlReader reader = XmlReader.Create(new StringReader(xml), settings))
                    {
                        XmlDocument xd = new XmlDocument { XmlResolver = null };
                        xd.Load(reader);
                        return xd;
                    }
                }
                catch (XmlException)
                {
                    // Not XML as it is. Try the next way.
                }
            }
            return null;
        }
    }
}
