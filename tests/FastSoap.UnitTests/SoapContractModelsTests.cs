using System;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using FastSoap.Abstractions;
using Xunit;

namespace FastSoap.UnitTests
{

    /// <summary>
    /// Unit tests for <see cref="SoapHeaderValue"/>, <see cref="SoapFault"/>,
    /// <see cref="SoapProtocolException"/>, <see cref="SoapCallContext"/>, and contract attributes.
    /// </summary>
    public sealed class SoapContractModelsTests
    {

        #region Public Methods

        [Fact]
        public void ContractAttributes_PreserveExplicitWireMetadata()
        {
            SoapServiceAttribute? serviceAttr = typeof(ISampleContract).GetCustomAttribute<SoapServiceAttribute>();
            SoapWireContractAttribute? serviceWireAttr = typeof(ISampleContract).GetCustomAttribute<SoapWireContractAttribute>();
            MethodInfo? method = typeof(ISampleContract).GetMethod(nameof(ISampleContract.InvokeAsync));
            SoapOperationAttribute? opAttr = method?.GetCustomAttribute<SoapOperationAttribute>();
            SoapWireContractAttribute? opWireAttr = method?.GetCustomAttribute<SoapWireContractAttribute>();

            Assert.NotNull(serviceAttr);
            Assert.Equal("SampleService", serviceAttr.Name);
            Assert.Equal("urn:sample", serviceAttr.Namespace);

            Assert.NotNull(serviceWireAttr);
            Assert.Equal(typeof(FakeXmlContext), serviceWireAttr.ContextType);

            Assert.NotNull(opAttr);
            Assert.Equal("Invoke", opAttr.Name);
            Assert.Equal("urn:sample/ISampleContract/Invoke", opAttr.Action);
            Assert.Equal("urn:sample/ISampleContract/InvokeResponse", opAttr.ReplyAction);
            Assert.Equal("Invoke", opAttr.RequestName);
            Assert.Equal("InvokeResponse", opAttr.ResponseName);

            Assert.NotNull(opWireAttr);
            Assert.Equal(typeof(string), opWireAttr.RequestType);
            Assert.Equal("Value", opWireAttr.RequestMemberName);
            Assert.Equal("CustomReqInfo", opWireAttr.RequestTypeInfoPropertyName);
            Assert.Equal(typeof(int), opWireAttr.ResponseType);
            Assert.Equal("Result", opWireAttr.ResponseMemberName);
            Assert.Equal("CustomRespInfo", opWireAttr.ResponseTypeInfoPropertyName);
        }

        [Fact]
        public async Task SoapCallContext_AndHeaderHandler_InvokeWithoutMocking()
        {
            FakeServiceProvider services = new FakeServiceProvider();
            ClaimsPrincipal user = new ClaimsPrincipal(new ClaimsIdentity("TestAuth"));
            SoapOperationDescriptor descriptor = new SoapOperationDescriptor(
                "Transfer",
                "urn:payments/Transfer",
                null,
                new SoapQName("urn:payments", "Transfer"),
                new SoapQName("urn:payments", "TransferResponse"));

            SoapHeaderValue header = SoapHeaderValue.CreateCopy(
                new SoapQName("urn:auth", "ApiKey"),
                "http://schemas.xmlsoap.org/soap/actor/next",
                mustUnderstand: true,
                "<h:ApiKey xmlns:h=\"urn:auth\">secret</h:ApiKey>"u8);

            SoapCallContext context = new SoapCallContext
            {
                Action = descriptor.Action,
                Headers = [header],
                OperationDescriptor = descriptor,
                Services = services,
                User = user,
                Version = SoapVersion.Soap11
            };

            FakeHeaderHandler handler = new FakeHeaderHandler(new SoapQName("urn:auth", "ApiKey"));
            await handler.HandleAsync(header, context, TestContext.Current.CancellationToken);

            Assert.Equal(1, handler.InvocationCount);
            Assert.Same(header, handler.LastHeader);
            Assert.Same(context, handler.LastContext);
            Assert.Same(services, context.Services);
            Assert.Same(user, context.User);
            Assert.Equal(SoapVersion.Soap11, context.Version);

            SoapOperationHandler<string, string> opHandler = static (request, ctx, _) =>
                ValueTask.FromResult($"{ctx.OperationDescriptor.Name}:{request}");

            string response = await opHandler("ok", context, TestContext.Current.CancellationToken);
            Assert.Equal("Transfer:ok", response);
        }

        [Theory]
        [InlineData(SoapVersion.Soap11, SoapFaultKind.Sender, "http://schemas.xmlsoap.org/soap/envelope/", "Client")]
        [InlineData(SoapVersion.Soap11, SoapFaultKind.Receiver, "http://schemas.xmlsoap.org/soap/envelope/", "Server")]
        [InlineData(SoapVersion.Soap11, SoapFaultKind.VersionMismatch, "http://schemas.xmlsoap.org/soap/envelope/", "VersionMismatch")]
        [InlineData(SoapVersion.Soap11, SoapFaultKind.MustUnderstand, "http://schemas.xmlsoap.org/soap/envelope/", "MustUnderstand")]
        [InlineData(SoapVersion.Soap12, SoapFaultKind.Sender, "http://www.w3.org/2003/05/soap-envelope", "Sender")]
        [InlineData(SoapVersion.Soap12, SoapFaultKind.Receiver, "http://www.w3.org/2003/05/soap-envelope", "Receiver")]
        [InlineData(SoapVersion.Soap12, SoapFaultKind.VersionMismatch, "http://www.w3.org/2003/05/soap-envelope", "VersionMismatch")]
        [InlineData(SoapVersion.Soap12, SoapFaultKind.MustUnderstand, "http://www.w3.org/2003/05/soap-envelope", "MustUnderstand")]
        public void SoapFault_CreateStandard_MapsCanonicalNamespacesAndCodes(
            SoapVersion version,
            SoapFaultKind kind,
            string expectedNamespace,
            string expectedLocalName)
        {
            byte[] detailBytes = "<PaymentFaultDetail xmlns=\"urn:payments\" />"u8.ToArray();

            SoapFault fault = SoapFault.CreateStandard(
                version,
                kind,
                "Public fault reason.",
                actorOrNode: "urn:node",
                detailXmlUtf8: detailBytes,
                correlationId: "corr-123");

            Assert.Equal(version, fault.Version);
            Assert.Equal(kind, fault.Kind);
            Assert.Equal(expectedNamespace, fault.CodeNamespace);
            Assert.Equal(expectedLocalName, fault.CodeLocalName);
            Assert.Equal("Public fault reason.", fault.Reason);
            Assert.Equal("urn:node", fault.ActorOrNode);
            Assert.True(fault.DetailXmlUtf8.Span.SequenceEqual(detailBytes));
            Assert.Equal("corr-123", fault.CorrelationId);
        }

        [Fact]
        public void SoapFault_InvalidArguments_ThrowExpectedExceptions()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                static () => SoapFault.CreateStandard((SoapVersion)99, SoapFaultKind.Sender, "Reason"));

            Assert.Throws<ArgumentOutOfRangeException>(
                static () => SoapFault.CreateStandard(SoapVersion.Soap11, (SoapFaultKind)99, "Reason"));

            Assert.Throws<ArgumentException>(
                static () => new SoapFault(SoapVersion.Soap11, SoapFaultKind.Sender, "urn:test", "Client", "   "));

            Assert.Throws<ArgumentException>(
                static () => SoapFault.CreateStandard(SoapVersion.Soap11, SoapFaultKind.Sender, "Reason", actorOrNode: "  "));

            Assert.Throws<ArgumentException>(
                static () => SoapFault.CreateStandard(SoapVersion.Soap11, SoapFaultKind.Sender, "Reason", correlationId: "  "));
        }

        [Fact]
        public void SoapHeaderValue_CreateCopy_OwnsIndependentBytesAndValidatesInputs()
        {
            byte[] mutableSource = "<h:Test xmlns:h=\"urn:test\">1</h:Test>"u8.ToArray();
            SoapQName headerName = new SoapQName("urn:test", "Test");

            SoapHeaderValue header = SoapHeaderValue.CreateCopy(headerName, null, false, mutableSource);
            mutableSource[0] = (byte)'X';

            Assert.Equal(headerName, header.QName);
            Assert.Null(header.ActorOrRole);
            Assert.False(header.MustUnderstand);
            Assert.True(header.RawUtf8Payload.Span.SequenceEqual("<h:Test xmlns:h=\"urn:test\">1</h:Test>"u8));

            Assert.Throws<ArgumentException>(
                static () => new SoapHeaderValue(default, null, false, ReadOnlyMemory<byte>.Empty));

            Assert.Throws<ArgumentException>(
                () => new SoapHeaderValue(headerName, "   ", true, ReadOnlyMemory<byte>.Empty));
        }

        [Fact]
        public void SoapProtocolException_PreservesPropertiesAndValidatesBounds()
        {
            InvalidOperationException inner = new InvalidOperationException("sensitive internal detail");
            SoapProtocolException ex = new SoapProtocolException(
                SoapFaultKind.Sender,
                HttpStatusCode.BadRequest,
                "Malformed SOAP envelope.",
                SoapVersion.Soap12,
                inner);

            Assert.Equal(SoapFaultKind.Sender, ex.FaultKind);
            Assert.Equal(HttpStatusCode.BadRequest, ex.HttpStatusCode);
            Assert.Equal("Malformed SOAP envelope.", ex.PublicReason);
            Assert.Equal("Malformed SOAP envelope.", ex.Message);
            Assert.Equal(SoapVersion.Soap12, ex.SoapVersion);
            Assert.Same(inner, ex.InnerException);

            Assert.Throws<ArgumentOutOfRangeException>(
                static () => new SoapProtocolException(SoapFaultKind.Sender, HttpStatusCode.OK, "Not an error"));

            Assert.Throws<ArgumentOutOfRangeException>(
                static () => new SoapProtocolException((SoapFaultKind)99, HttpStatusCode.BadRequest, "Bad kind"));

            Assert.Throws<ArgumentOutOfRangeException>(
                static () => new SoapProtocolException(SoapFaultKind.Sender, HttpStatusCode.BadRequest, "Bad version", (SoapVersion)99));

            Assert.Throws<ArgumentException>(
                static () => new SoapProtocolException(SoapFaultKind.Sender, HttpStatusCode.BadRequest, "   "));
        }

        #endregion

    }

}
