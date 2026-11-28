using System;
using System.Text;

using FastSoap.Abstractions;

using Xunit;

namespace FastSoap.UnitTests
{

    /// <summary>
    /// Unit tests for <see cref="SoapFaultException"/> and <see cref="SoapExceptionDetail"/>.
    /// </summary>
    public sealed class SoapFaultExceptionTests
    {

        #region Public Methods

        [Fact]
        public void Constructor_DefensivelyCopiesDetailBytesAndConvertsToSoapFault()
        {
            byte[] detail = "<FaultDetail xmlns=\"urn:test\"><Code>ERR1</Code></FaultDetail>"u8.ToArray();
            SoapFaultException exception = new SoapFaultException(
                SoapFaultKind.Sender,
                "Validation failed.",
                detail,
                codeNamespace: "urn:custom",
                codeLocalName: "BadInput",
                actorOrNode: "urn:node:gateway");

            detail[1] = (byte)'X';

            SoapFault soap11Fault = exception.ToSoapFault(SoapVersion.Soap11, correlationId: "CORR-1");
            Assert.Equal(SoapVersion.Soap11, soap11Fault.Version);
            Assert.Equal(SoapFaultKind.Sender, soap11Fault.Kind);
            Assert.Equal("urn:custom", soap11Fault.CodeNamespace);
            Assert.Equal("BadInput", soap11Fault.CodeLocalName);
            Assert.Equal("Validation failed.", soap11Fault.Reason);
            Assert.Equal("urn:node:gateway", soap11Fault.ActorOrNode);
            Assert.Equal("CORR-1", soap11Fault.CorrelationId);
            Assert.Equal("<FaultDetail xmlns=\"urn:test\"><Code>ERR1</Code></FaultDetail>", Encoding.UTF8.GetString(soap11Fault.DetailXmlUtf8.Span));

            SoapFaultException standardException = new SoapFaultException(SoapFaultKind.Receiver, "Upstream failure.");
            SoapFault soap12Standard = standardException.ToSoapFault(SoapVersion.Soap12);
            Assert.Equal("http://www.w3.org/2003/05/soap-envelope", soap12Standard.CodeNamespace);
            Assert.Equal("Receiver", soap12Standard.CodeLocalName);
            Assert.True(soap12Standard.DetailXmlUtf8.IsEmpty);
        }

        [Fact]
        public void Constructor_InvalidInputs_ThrowExpectedExceptions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SoapFaultException((SoapFaultKind)99, "Reason"));

            Assert.Throws<ArgumentException>(() =>
                new SoapFaultException(SoapFaultKind.Sender, "   "));

            Assert.Throws<ArgumentException>(() =>
                new SoapFaultException(SoapFaultKind.Sender, "Reason", codeNamespace: "urn:custom"));

            Assert.Throws<ArgumentException>(() =>
                new SoapFaultException(SoapFaultKind.Sender, "Reason", codeLocalName: "CustomCode"));

            Assert.Throws<ArgumentException>(() =>
                new SoapFaultException(SoapFaultKind.Sender, "Reason", actorOrNode: "  "));

            Assert.Throws<ArgumentException>(() =>
                new SoapFaultException(SoapFaultKind.Sender, "Reason", detailXmlUtf8: new byte[] { 0xFF, 0xFE }));

            Assert.Throws<ArgumentException>(() =>
                new SoapFaultException(SoapFaultKind.Sender, "Reason", detailXmlUtf8: "plain text"u8.ToArray()));

            Assert.Throws<ArgumentException>(() =>
                new SoapFaultException(SoapFaultKind.Sender, "Reason", detailXmlUtf8: "<?xml version=\"1.0\"?><Detail/>"u8.ToArray()));

            Assert.Throws<ArgumentException>(() =>
                new SoapFaultException(SoapFaultKind.Sender, "Reason", detailXmlUtf8: "<!DOCTYPE foo><Detail/>"u8.ToArray()));

            byte[] invalidControlDetail = [.. "<Detail>"u8, 0x07, .. "</Detail>"u8];
            Assert.Throws<ArgumentException>(() =>
                new SoapFaultException(SoapFaultKind.Sender, "Reason", detailXmlUtf8: invalidControlDetail));
        }

        [Fact]
        public void SoapExceptionDetail_FromException_CapturesHierarchyAndRespectsIncludeStackTrace()
        {
            InvalidOperationException inner = new InvalidOperationException("Inner failure");
            ArgumentException outer = new ArgumentException("Outer failure", inner)
            {
                HelpLink = "https://example.test/help"
            };

            SoapExceptionDetail withoutStack = SoapExceptionDetail.FromException(outer, includeStackTrace: false);
            Assert.Equal("System.ArgumentException", withoutStack.Type);
            Assert.Equal("Outer failure", withoutStack.Message);
            Assert.Equal("https://example.test/help", withoutStack.HelpLink);
            Assert.Null(withoutStack.StackTrace);
            Assert.NotNull(withoutStack.InnerException);
            Assert.Equal("System.InvalidOperationException", withoutStack.InnerException.Type);
            Assert.Equal("Inner failure", withoutStack.InnerException.Message);
            Assert.Null(withoutStack.InnerException.StackTrace);

            Exception deep = new Exception("Level 20");
            for (int level = 19; level >= 0; level--)
            {
                deep = new Exception($"Level {level}", deep);
            }

            SoapExceptionDetail bounded = SoapExceptionDetail.FromException(deep, includeStackTrace: false);
            int count = 0;
            SoapExceptionDetail? cursor = bounded;
            while (cursor is not null)
            {
                count++;
                cursor = cursor.InnerException;
            }

            Assert.Equal(16, count);
        }

        #endregion

    }

}
