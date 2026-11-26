using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Xml;

using CoreWCF;
using CoreWCF.Channels;
using CoreWCF.Description;
using CoreWCF.Dispatcher;

namespace R0A.CoreWcfSpike
{
    /// <summary>
    /// Records observations from the custom <see cref="IDispatchMessageFormatter" /> seam.
    /// </summary>
    public sealed class FormatterSeamMetrics
    {
        #region Fields

        private int _deserializeCalls;
        private string? _lastReaderType;
        private string? _lastWriterType;
        private int _serializeCalls;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the total number of <see cref="IDispatchMessageFormatter.DeserializeRequest" /> calls observed.
        /// </summary>
        public int DeserializeCalls => Volatile.Read(ref _deserializeCalls);

        /// <summary>
        /// Gets the runtime type name of the last <see cref="XmlDictionaryReader" /> exposed by CoreWCF.
        /// </summary>
        public string? LastReaderType => _lastReaderType;

        /// <summary>
        /// Gets the runtime type name of the last <see cref="XmlDictionaryWriter" /> exposed by CoreWCF.
        /// </summary>
        public string? LastWriterType => _lastWriterType;

        /// <summary>
        /// Gets the total number of <see cref="IDispatchMessageFormatter.SerializeReply" /> calls observed.
        /// </summary>
        public int SerializeCalls => Volatile.Read(ref _serializeCalls);

        #endregion

        #region Internal Methods

        /// <summary>
        /// Records a deserialization observation.
        /// </summary>
        /// <param name="readerType">The full type name of the reader.</param>
        internal void RecordDeserialize(string readerType)
        {
            Interlocked.Increment(ref _deserializeCalls);
            _lastReaderType = readerType;
        }

        /// <summary>
        /// Records a serialization observation.
        /// </summary>
        /// <param name="writerType">The full type name of the writer.</param>
        internal void RecordSerialize(string writerType)
        {
            Interlocked.Increment(ref _serializeCalls);
            _lastWriterType = writerType;
        }

        #endregion
    }

    /// <summary>
    /// Attaches a reflection-free <see cref="IDispatchMessageFormatter" /> to Echo and Transfer operations
    /// to evaluate CoreWCF's formatter extension seam.
    /// </summary>
    public sealed class CustomFormatterEndpointBehavior : IEndpointBehavior
    {
        #region Fields

        private readonly FormatterSeamMetrics _metrics;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="CustomFormatterEndpointBehavior" /> class.
        /// </summary>
        /// <param name="metrics">Metrics collector for formatter observations.</param>
        public CustomFormatterEndpointBehavior(FormatterSeamMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(metrics);
            _metrics = metrics;
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
        {
        }

        /// <inheritdoc />
        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
        }

        /// <inheritdoc />
        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(endpointDispatcher);

            foreach (DispatchOperation dispatchOperation in endpointDispatcher.DispatchRuntime.Operations)
            {
                if (string.Equals(dispatchOperation.Name, "Echo", StringComparison.Ordinal))
                {
                    dispatchOperation.Formatter = new EchoManualDispatchFormatter(_metrics);
                }
                else if (string.Equals(dispatchOperation.Name, "Transfer", StringComparison.Ordinal))
                {
                    dispatchOperation.Formatter = new TransferManualDispatchFormatter(_metrics);
                }
            }
        }

        /// <inheritdoc />
        public void Validate(ServiceEndpoint endpoint)
        {
        }

        #endregion
    }

    /// <summary>
    /// Reflection-free <see cref="IDispatchMessageFormatter" /> for the Echo contract using
    /// <see cref="XmlDictionaryReader" /> and <see cref="XmlDictionaryWriter" /> directly.
    /// </summary>
    internal sealed class EchoManualDispatchFormatter : IDispatchMessageFormatter
    {
        #region Fields

        private const string EchoNamespace = "urn:echo";
        private const string ReplyAction = "urn:echo/IEchoService/EchoResponse";
        private readonly FormatterSeamMetrics _metrics;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="EchoManualDispatchFormatter" /> class.
        /// </summary>
        /// <param name="metrics">Metrics collector.</param>
        public EchoManualDispatchFormatter(FormatterSeamMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(metrics);
            _metrics = metrics;
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public void DeserializeRequest(Message message, object[] parameters)
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(parameters);

            using XmlDictionaryReader reader = message.GetReaderAtBodyContents();
            _metrics.RecordDeserialize(reader.GetType().FullName ?? reader.GetType().Name);

            reader.MoveToContent();
            reader.ReadStartElement("Echo", EchoNamespace);
            reader.MoveToContent();
            string value = reader.ReadElementContentAsString("message", EchoNamespace);
            reader.MoveToContent();
            reader.ReadEndElement();

            parameters[0] = value;
        }

        /// <inheritdoc />
        public Message SerializeReply(MessageVersion messageVersion, object[] parameters, object result)
        {
            ArgumentNullException.ThrowIfNull(messageVersion);

            string responseText = result as string ?? string.Empty;

            return Message.CreateMessage(
                messageVersion,
                ReplyAction,
                new EchoBodyWriter(responseText, _metrics));
        }

        #endregion

        private sealed class EchoBodyWriter : BodyWriter
        {
            #region Fields

            private readonly FormatterSeamMetrics _metrics;
            private readonly string _responseText;

            #endregion

            #region Constructors

            public EchoBodyWriter(string responseText, FormatterSeamMetrics metrics)
                : base(isBuffered: true)
            {
                _responseText = responseText;
                _metrics = metrics;
            }

            #endregion

            #region Protected Methods

            protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
            {
                ArgumentNullException.ThrowIfNull(writer);
                _metrics.RecordSerialize(writer.GetType().FullName ?? writer.GetType().Name);

                writer.WriteStartElement("EchoResponse", EchoNamespace);
                writer.WriteElementString("EchoResult", EchoNamespace, _responseText);
                writer.WriteEndElement();
            }

            #endregion
        }
    }

    /// <summary>
    /// Reflection-free <see cref="IDispatchMessageFormatter" /> for the nested Transfer contract under urn:payments.
    /// </summary>
    internal sealed class TransferManualDispatchFormatter : IDispatchMessageFormatter
    {
        #region Fields

        private const string PaymentsNamespace = "urn:payments";
        private const string ReplyAction = "urn:payments/IPaymentService/TransferResponse";
        private readonly FormatterSeamMetrics _metrics;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="TransferManualDispatchFormatter" /> class.
        /// </summary>
        /// <param name="metrics">Metrics collector.</param>
        public TransferManualDispatchFormatter(FormatterSeamMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(metrics);
            _metrics = metrics;
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public void DeserializeRequest(Message message, object[] parameters)
        {
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(parameters);

            using XmlDictionaryReader reader = message.GetReaderAtBodyContents();
            _metrics.RecordDeserialize(reader.GetType().FullName ?? reader.GetType().Name);

            reader.MoveToContent();
            reader.ReadStartElement("Transfer", PaymentsNamespace);
            reader.MoveToContent();
            reader.ReadStartElement("request", PaymentsNamespace);

            string accountNumber = string.Empty;
            decimal amount = 0m;

            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (reader.IsStartElement("AccountNumber", PaymentsNamespace))
                {
                    accountNumber = reader.ReadElementContentAsString();
                }
                else if (reader.IsStartElement("Amount", PaymentsNamespace))
                {
                    string rawAmount = reader.ReadElementContentAsString();
                    amount = decimal.Parse(rawAmount, CultureInfo.InvariantCulture);
                }
                else
                {
                    reader.Skip();
                }
            }

            reader.MoveToContent();
            reader.ReadEndElement();
            reader.MoveToContent();
            reader.ReadEndElement();

            parameters[0] = new TransferRequest
            {
                AccountNumber = accountNumber,
                Amount = amount,
            };
        }

        /// <inheritdoc />
        public Message SerializeReply(MessageVersion messageVersion, object[] parameters, object result)
        {
            ArgumentNullException.ThrowIfNull(messageVersion);

            TransferResult transferResult = result as TransferResult
                ?? throw new InvalidOperationException("Expected a TransferResult return value.");

            return Message.CreateMessage(
                messageVersion,
                ReplyAction,
                new TransferBodyWriter(transferResult, _metrics));
        }

        #endregion

        private sealed class TransferBodyWriter : BodyWriter
        {
            #region Fields

            private readonly FormatterSeamMetrics _metrics;
            private readonly TransferResult _result;

            #endregion

            #region Constructors

            public TransferBodyWriter(TransferResult result, FormatterSeamMetrics metrics)
                : base(isBuffered: true)
            {
                _result = result;
                _metrics = metrics;
            }

            #endregion

            #region Protected Methods

            protected override void OnWriteBodyContents(XmlDictionaryWriter writer)
            {
                ArgumentNullException.ThrowIfNull(writer);
                _metrics.RecordSerialize(writer.GetType().FullName ?? writer.GetType().Name);

                writer.WriteStartElement("TransferResponse", PaymentsNamespace);
                writer.WriteStartElement("TransferResult", PaymentsNamespace);
                writer.WriteElementString("TransactionId", PaymentsNamespace, _result.TransactionId);
                writer.WriteElementString("Successful", PaymentsNamespace, _result.Successful ? "true" : "false");
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            #endregion
        }
    }
}
