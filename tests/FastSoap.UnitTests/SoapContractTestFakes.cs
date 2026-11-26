using System;
using System.Threading;
using System.Threading.Tasks;
using FastSoap.Abstractions;

namespace FastSoap.UnitTests
{

    [SoapService(Name = "SampleService", Namespace = "urn:sample")]
    [SoapWireContract(typeof(FakeXmlContext))]
    internal interface ISampleContract
    {

        #region Public Methods

        [SoapOperation(
            Name = "Invoke",
            Action = "urn:sample/ISampleContract/Invoke",
            ReplyAction = "urn:sample/ISampleContract/InvokeResponse",
            RequestName = "Invoke",
            ResponseName = "InvokeResponse")]
        [SoapWireContract(
            RequestType = typeof(string),
            RequestMemberName = "Value",
            RequestTypeInfoPropertyName = "CustomReqInfo",
            ResponseType = typeof(int),
            ResponseMemberName = "Result",
            ResponseTypeInfoPropertyName = "CustomRespInfo")]
        Task<int> InvokeAsync(string input, CancellationToken cancellationToken);

        #endregion

    }

    internal sealed class FakeHeaderHandler : ISoapHeaderHandler
    {

        #region Properties

        public SoapQName HeaderName { get; }

        public int InvocationCount { get; private set; }

        public SoapCallContext? LastContext { get; private set; }

        public SoapHeaderValue? LastHeader { get; private set; }

        #endregion

        #region Constructors

        public FakeHeaderHandler(SoapQName headerName)
        {
            HeaderName = headerName;
        }

        #endregion

        #region Public Methods

        public ValueTask HandleAsync(
            SoapHeaderValue header,
            SoapCallContext context,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            LastHeader = header;
            LastContext = context;

            return ValueTask.CompletedTask;
        }

        #endregion

    }

    internal sealed class FakeServiceProvider : IServiceProvider
    {

        #region Public Methods

        public object? GetService(Type serviceType)
        {
            return null;
        }

        #endregion

    }

    internal sealed class FakeXmlContext
    {
    }

}
