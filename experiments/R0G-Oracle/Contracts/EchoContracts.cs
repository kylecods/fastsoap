using System.ServiceModel;
using System.Threading.Tasks;

namespace FastSoap.R0G.Oracle.Contracts
{
    /// <summary>
    /// Shared SOAP constants for the Echo oracle contract, matching CoreWCF <c>IEchoService</c>.
    /// </summary>
    public static class EchoConstants
    {
        #region Fields

        /// <summary>
        /// Service contract name matching CoreWCF <c>IEchoService</c>.
        /// </summary>
        public const string ContractName = "IEchoService";

        /// <summary>
        /// SOAP action for <c>EchoString</c>.
        /// </summary>
        public const string EchoStringAction = OperationBase + "EchoString";

        /// <summary>
        /// SOAP reply action for <c>EchoString</c>.
        /// </summary>
        public const string EchoStringReplyAction = OperationBase + "EchoStringResponse";

        /// <summary>
        /// Service contract XML namespace matching CoreWCF <c>IEchoService</c>.
        /// </summary>
        public const string Namespace = "http://tempuri.org/";

        /// <summary>
        /// Base URI prefix for Echo operation actions.
        /// </summary>
        public const string OperationBase = Namespace + ContractName + "/";

        #endregion
    }

    /// <summary>
    /// Server-side CoreWCF DataContractSerializer Echo service contract.
    /// </summary>
    [CoreWCF.ServiceContract(Namespace = EchoConstants.Namespace, Name = EchoConstants.ContractName)]
    public interface IEchoDcsService
    {
        #region Public Methods

        /// <summary>
        /// Echoes the provided string payload back to the caller.
        /// </summary>
        /// <param name="echo">The string to echo.</param>
        /// <returns>The echoed string.</returns>
        [CoreWCF.OperationContract(
            Name = "EchoString",
            Action = EchoConstants.EchoStringAction,
            ReplyAction = EchoConstants.EchoStringReplyAction)]
        Task<string?> EchoStringAsync(string? echo);

        #endregion
    }

    /// <summary>
    /// Client-side WCF <c>System.ServiceModel</c> DataContractSerializer Echo service contract.
    /// </summary>
    [ServiceContract(Namespace = EchoConstants.Namespace, Name = EchoConstants.ContractName)]
    public interface IEchoDcsClient
    {
        #region Public Methods

        /// <summary>
        /// Echoes the provided string payload back to the caller.
        /// </summary>
        /// <param name="echo">The string to echo.</param>
        /// <returns>The echoed string.</returns>
        [OperationContract(
            Name = "EchoString",
            Action = EchoConstants.EchoStringAction,
            ReplyAction = EchoConstants.EchoStringReplyAction)]
        Task<string?> EchoStringAsync(string? echo);

        #endregion
    }

    /// <summary>
    /// Server-side CoreWCF <c>XmlSerializerFormat</c> Echo service contract.
    /// </summary>
    [CoreWCF.XmlSerializerFormat]
    [CoreWCF.ServiceContract(Namespace = EchoConstants.Namespace, Name = EchoConstants.ContractName)]
    public interface IEchoXmlSerializerService
    {
        #region Public Methods

        /// <summary>
        /// Echoes the provided string payload using <c>XmlSerializer</c>.
        /// </summary>
        /// <param name="echo">The string to echo.</param>
        /// <returns>The echoed string.</returns>
        [CoreWCF.OperationContract(
            Name = "EchoString",
            Action = EchoConstants.EchoStringAction,
            ReplyAction = EchoConstants.EchoStringReplyAction)]
        Task<string?> EchoStringAsync(string? echo);

        #endregion
    }

    /// <summary>
    /// Client-side WCF <c>XmlSerializerFormat</c> Echo service contract.
    /// </summary>
    [XmlSerializerFormat]
    [ServiceContract(Namespace = EchoConstants.Namespace, Name = EchoConstants.ContractName)]
    public interface IEchoXmlSerializerClient
    {
        #region Public Methods

        /// <summary>
        /// Echoes the provided string payload using <c>XmlSerializer</c>.
        /// </summary>
        /// <param name="echo">The string to echo.</param>
        /// <returns>The echoed string.</returns>
        [OperationContract(
            Name = "EchoString",
            Action = EchoConstants.EchoStringAction,
            ReplyAction = EchoConstants.EchoStringReplyAction)]
        Task<string?> EchoStringAsync(string? echo);

        #endregion
    }

    /// <summary>
    /// Server-side CoreWCF contract with SOAP header and body members, matching
    /// CoreWCF <c>ServiceWithMessageBodyAndHeaderTest</c>.
    /// </summary>
    [CoreWCF.XmlSerializerFormat]
    [CoreWCF.ServiceContract(Namespace = EchoConstants.Namespace, Name = "IServiceWithMessageBodyAndHeader")]
    public interface IEchoWithHeadersService
    {
        #region Public Methods

        /// <summary>
        /// Echoes a message contract carrying both SOAP headers and body content.
        /// </summary>
        /// <param name="request">The message contract request.</param>
        /// <returns>The message contract response.</returns>
        [CoreWCF.OperationContract(
            Name = "EchoWithMessageContract",
            Action = "http://tempuri.org/IServiceWithMessageBodyAndHeader/EchoWithMessageContract",
            ReplyAction = "http://tempuri.org/IServiceWithMessageBodyAndHeader/EchoWithMessageContractResponse")]
        Task<EchoHeaderResponseMessage> EchoWithMessageContractAsync(EchoHeaderRequestMessage request);

        #endregion
    }

    /// <summary>
    /// Client-side WCF contract with SOAP header and body members.
    /// </summary>
    [XmlSerializerFormat]
    [ServiceContract(Namespace = EchoConstants.Namespace, Name = "IServiceWithMessageBodyAndHeader")]
    public interface IEchoWithHeadersClient
    {
        #region Public Methods

        /// <summary>
        /// Echoes a message contract carrying both SOAP headers and body content.
        /// </summary>
        /// <param name="request">The message contract request.</param>
        /// <returns>The message contract response.</returns>
        [OperationContract(
            Name = "EchoWithMessageContract",
            Action = "http://tempuri.org/IServiceWithMessageBodyAndHeader/EchoWithMessageContract",
            ReplyAction = "http://tempuri.org/IServiceWithMessageBodyAndHeader/EchoWithMessageContractResponse")]
        Task<EchoHeaderClientResponseMessage> EchoWithMessageContractAsync(EchoHeaderClientRequestMessage request);

        #endregion
    }

    /// <summary>
    /// CoreWCF server message contract carrying optional SOAP headers and body text.
    /// </summary>
    [CoreWCF.MessageContract(WrapperName = "EchoHeaderRequest", WrapperNamespace = EchoConstants.Namespace)]
    public sealed class EchoHeaderRequestMessage
    {
        #region Properties

        /// <summary>
        /// Gets or sets the API key SOAP header value.
        /// </summary>
        [CoreWCF.MessageHeader(Namespace = EchoConstants.Namespace)]
        public string? ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the body text payload.
        /// </summary>
        [CoreWCF.MessageBodyMember(Namespace = EchoConstants.Namespace)]
        public string? Text { get; set; }

        #endregion
    }

    /// <summary>
    /// CoreWCF server message contract response carrying a SOAP header and body text.
    /// </summary>
    [CoreWCF.MessageContract(WrapperName = "EchoHeaderResponse", WrapperNamespace = EchoConstants.Namespace)]
    public sealed class EchoHeaderResponseMessage
    {
        #region Properties

        /// <summary>
        /// Gets or sets the echoed greeting body member.
        /// </summary>
        [CoreWCF.MessageBodyMember(Namespace = EchoConstants.Namespace)]
        public string? SayHello { get; set; }

        /// <summary>
        /// Gets or sets the echoed SOAP header value.
        /// </summary>
        [CoreWCF.MessageHeader(Namespace = EchoConstants.Namespace)]
        public string? SayHi { get; set; }

        #endregion
    }

    /// <summary>
    /// WCF client message contract carrying optional SOAP headers and body text.
    /// </summary>
    [MessageContract(WrapperName = "EchoHeaderRequest", WrapperNamespace = EchoConstants.Namespace)]
    public sealed class EchoHeaderClientRequestMessage
    {
        #region Properties

        /// <summary>
        /// Gets or sets the API key SOAP header value.
        /// </summary>
        [MessageHeader(Namespace = EchoConstants.Namespace)]
        public string? ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the body text payload.
        /// </summary>
        [MessageBodyMember(Namespace = EchoConstants.Namespace)]
        public string? Text { get; set; }

        #endregion
    }

    /// <summary>
    /// WCF client message contract response carrying a SOAP header and body text.
    /// </summary>
    [MessageContract(WrapperName = "EchoHeaderResponse", WrapperNamespace = EchoConstants.Namespace)]
    public sealed class EchoHeaderClientResponseMessage
    {
        #region Properties

        /// <summary>
        /// Gets or sets the echoed greeting body member.
        /// </summary>
        [MessageBodyMember(Namespace = EchoConstants.Namespace)]
        public string? SayHello { get; set; }

        /// <summary>
        /// Gets or sets the echoed SOAP header value.
        /// </summary>
        [MessageHeader(Namespace = EchoConstants.Namespace)]
        public string? SayHi { get; set; }

        #endregion
    }
}
