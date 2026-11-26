using System;
using System.Net;
using System.Text;

using FastSoap.Abstractions;
using FastSoap.Core.Protocol;

using Xunit;

namespace FastSoap.ProtocolTests
{

    /// <summary>
    /// Verifies strict RFC 9110 media-type, charset, SOAP 1.1 <c>SOAPAction</c>, and SOAP 1.2 <c>action</c> parsing.
    /// </summary>
    public sealed class SoapHttpMetadataParserTests
    {

        #region Public Methods

        [Theory]
        [InlineData("\"urn:payments/IPaymentService/Transfer\"", "urn:payments/IPaymentService/Transfer")]
        [InlineData("  \"http://tempuri.org/IEchoService/EchoString\"  ", "http://tempuri.org/IEchoService/EchoString")]
        public void SoapActionParser_ValidQuotedAction_ReturnsUnquotedAction(string rawHeader, string expectedAction)
        {
            string? parsed = SoapActionParser.ParseSoap11Action(
                rawHeader,
                hasMultipleHeaders: false,
                allowMissingSoap11Action: false);

            Assert.Equal(expectedAction, parsed);

            byte[] utf8 = Encoding.UTF8.GetBytes(rawHeader);
            string? parsedUtf8 = SoapActionParser.ParseSoap11Action(
                utf8,
                isPresent: true,
                hasMultipleHeaders: false,
                allowMissingSoap11Action: false);

            Assert.Equal(expectedAction, parsedUtf8);
        }

        [Fact]
        public void SoapActionParser_MissingAndEmpty_RespectsAllowMissingSoap11Action()
        {
            SoapProtocolException missingStrict = Assert.Throws<SoapProtocolException>(static () =>
                SoapActionParser.ParseSoap11Action(null, hasMultipleHeaders: false, allowMissingSoap11Action: false));
            Assert.Equal(SoapFaultKind.Sender, missingStrict.FaultKind);
            Assert.Equal(HttpStatusCode.InternalServerError, missingStrict.HttpStatusCode);
            Assert.Equal(SoapVersion.Soap11, missingStrict.SoapVersion);

            SoapProtocolException emptyStrict = Assert.Throws<SoapProtocolException>(static () =>
                SoapActionParser.ParseSoap11Action("\"\"", hasMultipleHeaders: false, allowMissingSoap11Action: false));
            Assert.Equal(SoapFaultKind.Sender, emptyStrict.FaultKind);
            Assert.Equal(HttpStatusCode.InternalServerError, emptyStrict.HttpStatusCode);
            Assert.Equal(SoapVersion.Soap11, emptyStrict.SoapVersion);

            Assert.Null(SoapActionParser.ParseSoap11Action(null, hasMultipleHeaders: false, allowMissingSoap11Action: true));
            Assert.Null(SoapActionParser.ParseSoap11Action("\"\"", hasMultipleHeaders: false, allowMissingSoap11Action: true));
            Assert.Null(SoapActionParser.ParseSoap11Action("   ", hasMultipleHeaders: false, allowMissingSoap11Action: true));
        }

        [Theory]
        [InlineData("urn:payments/Transfer")]
        [InlineData("\"urn:payments/Transfer")]
        [InlineData("\"urn:payments/\"Transfer\"")]
        [InlineData("\"   \"")]
        [InlineData("\"urn:payments\r\nEvil\"")]
        public void SoapActionParser_MalformedHeader_ThrowsBadRequestWithoutSoapVersion(string rawHeader)
        {
            SoapProtocolException ex = Assert.Throws<SoapProtocolException>(() =>
                SoapActionParser.ParseSoap11Action(rawHeader, hasMultipleHeaders: false, allowMissingSoap11Action: true));

            Assert.Equal(SoapFaultKind.Sender, ex.FaultKind);
            Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
            Assert.Null(ex.SoapVersion);
        }

        [Fact]
        public void SoapActionParser_MultipleHeadersAndLengthLimit_ThrowBadRequest()
        {
            SoapProtocolException duplicateEx = Assert.Throws<SoapProtocolException>(static () =>
                SoapActionParser.ParseSoap11Action("\"urn:a\"", hasMultipleHeaders: true, allowMissingSoap11Action: false));
            Assert.Equal(HttpStatusCode.BadRequest, duplicateEx.HttpStatusCode);
            Assert.Null(duplicateEx.SoapVersion);

            SoapProtocolException lengthEx = Assert.Throws<SoapProtocolException>(static () =>
                SoapActionParser.ParseSoap11Action("\"urn:abcdef\"", hasMultipleHeaders: false, allowMissingSoap11Action: false, maxActionLength: 5));
            Assert.Equal(HttpStatusCode.BadRequest, lengthEx.HttpStatusCode);
            Assert.Null(lengthEx.SoapVersion);
        }

        [Theory]
        [InlineData("text/xml", SoapVersionFlags.Soap11, SoapVersion.Soap11, null)]
        [InlineData("text/xml; charset=utf-8", SoapVersionFlags.Soap11, SoapVersion.Soap11, null)]
        [InlineData("TEXT/XML; CHARSET=\"UTF-8\"", SoapVersionFlags.Both, SoapVersion.Soap11, null)]
        [InlineData("application/soap+xml", SoapVersionFlags.Soap12, SoapVersion.Soap12, null)]
        [InlineData("application/soap+xml; charset=utf-8; action=\"http://tempuri.org/IEchoService/EchoString\"", SoapVersionFlags.Both, SoapVersion.Soap12, "http://tempuri.org/IEchoService/EchoString")]
        [InlineData("application/soap+xml; action=\"urn:a;b\\\"c\"; charset=utf-8", SoapVersionFlags.Soap12, SoapVersion.Soap12, "urn:a;b\"c")]
        public void SoapContentTypeParser_ValidHeaders_ParseVersionCharsetAndAction(
            string contentType,
            SoapVersionFlags enabledVersions,
            SoapVersion expectedVersion,
            string? expectedAction)
        {
            SoapContentTypeInfo info = SoapContentTypeParser.Parse(contentType, enabledVersions);

            Assert.Equal(expectedVersion, info.Version);
            Assert.Equal(expectedAction, info.Action);

            SoapContentTypeInfo utf8Info = SoapContentTypeParser.Parse(Encoding.UTF8.GetBytes(contentType), enabledVersions);
            Assert.Equal(info, utf8Info);
        }

        [Theory]
        [InlineData(null, SoapVersionFlags.Both)]
        [InlineData("", SoapVersionFlags.Both)]
        [InlineData("application/json; charset=utf-8", SoapVersionFlags.Both)]
        [InlineData("text/plain", SoapVersionFlags.Both)]
        [InlineData("text/xml; charset=utf-16", SoapVersionFlags.Soap11)]
        [InlineData("application/soap+xml; charset=iso-8859-1", SoapVersionFlags.Soap12)]
        [InlineData("application/soap+xml; charset=utf-8", SoapVersionFlags.Soap11)]
        [InlineData("text/xml; charset=utf-8", SoapVersionFlags.Soap12)]
        public void SoapContentTypeParser_UnsupportedMediaTypeOrCharsetOrVersion_Throws415(
            string? contentType,
            SoapVersionFlags enabledVersions)
        {
            SoapProtocolException ex = Assert.Throws<SoapProtocolException>(() =>
                SoapContentTypeParser.Parse(contentType, enabledVersions));

            Assert.Equal(HttpStatusCode.UnsupportedMediaType, ex.HttpStatusCode);
            Assert.Null(ex.SoapVersion);
        }

        [Theory]
        [InlineData("application/soap+xml; charset=utf-8; action=\"urn:a\"; action=\"urn:b\"")]
        [InlineData("application/soap+xml; charset=utf-8; charset=utf-8")]
        [InlineData("application/soap+xml; action=\"\"")]
        [InlineData("application/soap+xml; action=urn:unquoted")]
        [InlineData("application/soap+xml; action=\"unterminated")]
        [InlineData("text/xml;")]
        public void SoapContentTypeParser_MalformedOrDuplicateParameters_Throws400(string contentType)
        {
            SoapProtocolException ex = Assert.Throws<SoapProtocolException>(() =>
                SoapContentTypeParser.Parse(contentType, SoapVersionFlags.Both));

            Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
            Assert.Null(ex.SoapVersion);
        }

        [Fact]
        public void SoapContentTypeParser_OversizedSoap12Action_Throws400()
        {
            SoapProtocolException ex = Assert.Throws<SoapProtocolException>(static () =>
                SoapContentTypeParser.Parse(
                    "application/soap+xml; charset=utf-8; action=\"urn:too-long\"",
                    SoapVersionFlags.Soap12,
                    maxActionLength: 5));

            Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
            Assert.Null(ex.SoapVersion);
        }

        [Fact]
        public void SoapContentTypeParser_QuotedParameterOver512Bytes_ParsesViaPooledBufferWithoutError()
        {
            string longAction = "urn:long/" + new string('a', 700);
            string header = $"application/soap+xml; charset=\"UTF-8\"; action=\"{longAction}\"";

            SoapContentTypeInfo info = SoapContentTypeParser.Parse(header, SoapVersionFlags.Soap12, maxActionLength: 1024);

            Assert.Equal(SoapVersion.Soap12, info.Version);
            Assert.Equal(longAction, info.Action);
        }

        #endregion

    }

}
