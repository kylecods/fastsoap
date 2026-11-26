using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Xml;
using CloudNimble.Text.Xml;
using R0B.SoapCoreSpike.Contracts;
using R0B.SoapCoreSpike.Serialization;
using SoapCore.Serializer;

namespace R0B.SoapCoreSpike.SoapCoreAdaptation
{
    /// <summary>
    /// Attempts to bridge SoapCore's <see cref="IXmlSerializationHandler"/> ingress seam to XmlEssentials'
    /// source-generated <see cref="PaymentXmlContext"/>.
    /// </summary>
    /// <remarks>
    /// This class demonstrates the exact architectural friction of adapting SoapCore to XmlEssentials:
    /// SoapCore has already parsed the request into an <see cref="System.Xml.Linq.XDocument"/> via
    /// <c>ParsedMessage</c>, wrapped it in <c>XDocumentXmlReader</c> / <see cref="XmlDictionaryReader"/>,
    /// and passes a runtime <see cref="Type"/> rather than a typed context token. Bridging requires
    /// materializing <c>xmlReader.ReadOuterXml()</c> to a <see cref="string"/> and re-encoding to UTF-8
    /// before <see cref="Utf8XmlReader"/> can deserialize it. Additionally, SoapCore provides no
    /// corresponding response serialization hook on <see cref="IXmlSerializationHandler"/>.
    /// </remarks>
    public sealed class XmlEssentialsSerializationHandler : IXmlSerializationHandler
    {
        #region Public Methods

        /// <inheritdoc />
        public object? DeserializeInputParameter(
            XmlDictionaryReader xmlReader,
            Type parameterType,
            string parameterName,
            string parameterNs,
            ICustomAttributeProvider customAttributeProvider,
            IEnumerable<Type>? knownTypes = null)
        {
            ArgumentNullException.ThrowIfNull(xmlReader);
            ArgumentNullException.ThrowIfNull(parameterType);
            ArgumentException.ThrowIfNullOrWhiteSpace(parameterName);

            while (xmlReader.NodeType == XmlNodeType.Whitespace && xmlReader.Read())
            {
            }

            if (!xmlReader.IsStartElement(parameterName, parameterNs))
            {
                return null;
            }

            string outerXml = xmlReader.ReadOuterXml();
            byte[] utf8Bytes = Encoding.UTF8.GetBytes(outerXml);
            Utf8XmlReader utf8Reader = new Utf8XmlReader(utf8Bytes);

            if (!utf8Reader.Read())
            {
                return null;
            }

            if (parameterType == typeof(string) && string.Equals(parameterName, "message", StringComparison.Ordinal))
            {
                EchoMessageWire? echoWire = CloudNimble.Text.Xml.XmlSerializer.Deserialize(
                    ref utf8Reader,
                    PaymentXmlContext.Default.EchoMessageWire);

                return echoWire?.Value;
            }

            if (parameterType == typeof(TransferRequest))
            {
                TransferRequestWire? wire = CloudNimble.Text.Xml.XmlSerializer.Deserialize(
                    ref utf8Reader,
                    PaymentXmlContext.Default.TransferRequestWire);

                return wire?.ToContract();
            }

            throw new NotSupportedException(
                $"No static XmlEssentials XmlTypeInfo<T> mapping is registered for parameter '{parameterName}' of type '{parameterType.FullName}'.");
        }

        #endregion
    }
}
