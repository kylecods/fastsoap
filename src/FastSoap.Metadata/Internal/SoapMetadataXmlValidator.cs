using System;
using CloudNimble.Text.Xml;

namespace FastSoap.Metadata.Internal
{

    /// <summary>
    /// Validates UTF-8 WSDL 1.1 and XML Schema documents using <see cref="Utf8XmlReader"/> without BCL <c>System.Xml</c>.
    /// </summary>
    internal static class SoapMetadataXmlValidator
    {

        #region Fields

        private const int MaxAttributesPerElement = 64;
        private const int MaxDepth = 64;
        private const int MaxNameByteCount = 1024;
        private const int MaxTextByteCount = 16 * 1024 * 1024;

        private static ReadOnlySpan<byte> WsdlDefinitionsLocalNameUtf8 => "definitions"u8;
        private static ReadOnlySpan<byte> WsdlNamespaceUtf8 => "http://schemas.xmlsoap.org/wsdl/"u8;
        private static ReadOnlySpan<byte> XsdNamespaceUtf8 => "http://www.w3.org/2001/XMLSchema"u8;
        private static ReadOnlySpan<byte> XsdSchemaLocalNameUtf8 => "schema"u8;

        #endregion

        #region Internal Methods

        internal static void ValidateDocument(ReadOnlySpan<byte> utf8Bytes, SoapMetadataDocumentKind expectedKind, string paramName)
        {
            if (utf8Bytes.IsEmpty)
            {
                throw new ArgumentException("Metadata document content cannot be empty.", paramName);
            }

            XmlReaderOptions readerOptions = new XmlReaderOptions
            {
                AllowMultipleRoots = false,
                MaxAttributeCount = MaxAttributesPerElement,
                MaxDepth = MaxDepth,
                MaxDocumentByteCount = utf8Bytes.Length,
                MaxNameByteCount = MaxNameByteCount,
                MaxTextByteCount = MaxTextByteCount
            };

            bool sawRoot = false;
            Utf8XmlReader reader = new Utf8XmlReader(utf8Bytes, readerOptions);

            try
            {
                while (reader.Read())
                {
                    if (reader.Version != XmlVersion.Version10)
                    {
                        throw new ArgumentException("Metadata document must use XML 1.0.", paramName);
                    }

                    if (reader.TokenType == XmlTokenType.ProcessingInstruction)
                    {
                        throw new ArgumentException("Metadata document cannot contain processing instructions.", paramName);
                    }

                    if (reader.TokenType != XmlTokenType.StartElement || sawRoot)
                    {
                        continue;
                    }

                    sawRoot = true;
                    ValidateRootElement(ref reader, expectedKind, paramName);
                }
            }
            catch (XmlException ex)
            {
                throw new ArgumentException($"Metadata document is not valid UTF-8 XML 1.0: {ex.Message}", paramName, ex);
            }

            if (!sawRoot)
            {
                throw new ArgumentException("Metadata document does not contain a root XML element.", paramName);
            }
        }

        #endregion

        #region Private Methods

        private static void ValidateRootElement(ref Utf8XmlReader reader, SoapMetadataDocumentKind expectedKind, string paramName)
        {
            if (expectedKind == SoapMetadataDocumentKind.Wsdl)
            {
                if (!reader.LocalNameEquals(WsdlDefinitionsLocalNameUtf8) || !reader.NamespaceUriEquals(WsdlNamespaceUtf8))
                {
                    throw new ArgumentException(
                        "WSDL document root element must be <definitions> in namespace 'http://schemas.xmlsoap.org/wsdl/'.",
                        paramName);
                }

                return;
            }

            if (expectedKind == SoapMetadataDocumentKind.XmlSchema)
            {
                if (!reader.LocalNameEquals(XsdSchemaLocalNameUtf8) || !reader.NamespaceUriEquals(XsdNamespaceUtf8))
                {
                    throw new ArgumentException(
                        "XML Schema document root element must be <schema> in namespace 'http://www.w3.org/2001/XMLSchema'.",
                        paramName);
                }

                return;
            }

            throw new ArgumentOutOfRangeException(nameof(expectedKind), expectedKind, "Unsupported metadata document kind.");
        }

        #endregion

    }

}
