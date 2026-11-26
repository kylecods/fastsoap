using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using CoreWCF.Channels;

namespace R0A.CoreWcfSpike
{
    /// <summary>
    /// Captures buffer rentals, byte counts, and encoding seam calls for a <see cref="MessageEncoder" />.
    /// </summary>
    public sealed class EncoderSeamMetrics
    {
        #region Fields

        private int _bufferManagerTakeCount;
        private long _lastBufferedReadBytes;
        private int _lastBufferedRentalCapacity;
        private long _lastBufferedWriteBytes;
        private string? _lastInnerEncoderType;
        private int _readArraySegmentCalls;
        private int _readStreamCalls;
        private int _writeBufferedCalls;
        private int _writeStreamCalls;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the number of buffer rentals observed through the tracking <see cref="BufferManager" />.
        /// </summary>
        public int BufferManagerTakeCount => Volatile.Read(ref _bufferManagerTakeCount);

        /// <summary>
        /// Gets the logical byte length of the last buffered request read.
        /// </summary>
        public long LastBufferedReadBytes => Volatile.Read(ref _lastBufferedReadBytes);

        /// <summary>
        /// Gets the rented buffer capacity of the last buffered write.
        /// </summary>
        public int LastBufferedRentalCapacity => Volatile.Read(ref _lastBufferedRentalCapacity);

        /// <summary>
        /// Gets the logical byte length of the last buffered reply write.
        /// </summary>
        public long LastBufferedWriteBytes => Volatile.Read(ref _lastBufferedWriteBytes);

        /// <summary>
        /// Gets the concrete inner encoder type name.
        /// </summary>
        public string? LastInnerEncoderType => _lastInnerEncoderType;

        /// <summary>
        /// Gets the number of <see cref="MessageEncoder.ReadMessage(ArraySegment{byte}, BufferManager, string)" /> calls.
        /// </summary>
        public int ReadArraySegmentCalls => Volatile.Read(ref _readArraySegmentCalls);

        /// <summary>
        /// Gets the number of <see cref="MessageEncoder.ReadMessageAsync(Stream, int, string)" /> calls.
        /// </summary>
        public int ReadStreamCalls => Volatile.Read(ref _readStreamCalls);

        /// <summary>
        /// Gets the number of <see cref="MessageEncoder.WriteMessage(Message, int, BufferManager, int)" /> calls.
        /// </summary>
        public int WriteBufferedCalls => Volatile.Read(ref _writeBufferedCalls);

        /// <summary>
        /// Gets the number of <see cref="MessageEncoder.WriteMessageAsync(Message, Stream)" /> calls.
        /// </summary>
        public int WriteStreamCalls => Volatile.Read(ref _writeStreamCalls);

        #endregion

        #region Internal Methods

        /// <summary>
        /// Records a buffer rental from the encoder seam.
        /// </summary>
        /// <param name="actualCapacity">The rented array length.</param>
        internal void RecordBufferRental(int actualCapacity)
        {
            Interlocked.Increment(ref _bufferManagerTakeCount);
            Volatile.Write(ref _lastBufferedRentalCapacity, actualCapacity);
        }

        /// <summary>
        /// Records a buffered array-segment read.
        /// </summary>
        /// <param name="bytes">The logical byte count.</param>
        /// <param name="innerType">The inner encoder type name.</param>
        internal void RecordReadArraySegment(int bytes, string innerType)
        {
            Interlocked.Increment(ref _readArraySegmentCalls);
            Volatile.Write(ref _lastBufferedReadBytes, bytes);
            _lastInnerEncoderType = innerType;
        }

        /// <summary>
        /// Records a streamed read.
        /// </summary>
        /// <param name="innerType">The inner encoder type name.</param>
        internal void RecordReadStream(string innerType)
        {
            Interlocked.Increment(ref _readStreamCalls);
            _lastInnerEncoderType = innerType;
        }

        /// <summary>
        /// Records a buffered write.
        /// </summary>
        /// <param name="bytes">The logical byte count.</param>
        /// <param name="innerType">The inner encoder type name.</param>
        internal void RecordWriteBuffered(int bytes, string innerType)
        {
            Interlocked.Increment(ref _writeBufferedCalls);
            Volatile.Write(ref _lastBufferedWriteBytes, bytes);
            _lastInnerEncoderType = innerType;
        }

        /// <summary>
        /// Records a streamed write.
        /// </summary>
        /// <param name="innerType">The inner encoder type name.</param>
        internal void RecordWriteStream(string innerType)
        {
            Interlocked.Increment(ref _writeStreamCalls);
            _lastInnerEncoderType = innerType;
        }

        #endregion
    }

    /// <summary>
    /// Custom <see cref="MessageEncodingBindingElement" /> that wraps <see cref="TextMessageEncodingBindingElement" />
    /// to instrument the CoreWCF encoder seam, buffer rentals, and <see cref="System.Xml" /> coupling.
    /// </summary>
    public sealed class InstrumentedSoap11EncodingBindingElement : MessageEncodingBindingElement
    {
        #region Fields

        private readonly TextMessageEncodingBindingElement _inner;
        private readonly EncoderSeamMetrics _metrics;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="InstrumentedSoap11EncodingBindingElement" /> class.
        /// </summary>
        /// <param name="metrics">The metrics collector.</param>
        public InstrumentedSoap11EncodingBindingElement(EncoderSeamMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(metrics);
            _metrics = metrics;
            _inner = new TextMessageEncodingBindingElement(MessageVersion.Soap11, Encoding.UTF8);
        }

        private InstrumentedSoap11EncodingBindingElement(InstrumentedSoap11EncodingBindingElement other)
            : base(other)
        {
            _metrics = other._metrics;
            _inner = (TextMessageEncodingBindingElement)other._inner.Clone();
        }

        #endregion

        #region Properties

        /// <inheritdoc />
        public override MessageVersion MessageVersion
        {
            get => _inner.MessageVersion;
            set => _inner.MessageVersion = value;
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public override BindingElement Clone()
        {
            return new InstrumentedSoap11EncodingBindingElement(this);
        }

        /// <inheritdoc />
        public override MessageEncoderFactory CreateMessageEncoderFactory()
        {
            return new InstrumentedMessageEncoderFactory(_inner.CreateMessageEncoderFactory(), _metrics);
        }

        /// <inheritdoc />
        public override T GetProperty<T>(BindingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (typeof(T) == typeof(XmlDictionaryReaderQuotas))
            {
                return _inner.GetProperty<T>(context);
            }

            return base.GetProperty<T>(context);
        }

        #endregion
    }

    internal sealed class InstrumentedMessageEncoderFactory : MessageEncoderFactory
    {
        #region Fields

        private readonly InstrumentedSoap11MessageEncoder _encoder;
        private readonly MessageEncoderFactory _innerFactory;

        #endregion

        #region Constructors

        public InstrumentedMessageEncoderFactory(MessageEncoderFactory innerFactory, EncoderSeamMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(innerFactory);
            ArgumentNullException.ThrowIfNull(metrics);
            _innerFactory = innerFactory;
            _encoder = new InstrumentedSoap11MessageEncoder(innerFactory.Encoder, metrics);
        }

        #endregion

        #region Properties

        public override MessageEncoder Encoder => _encoder;

        public override MessageVersion MessageVersion => _innerFactory.MessageVersion;

        #endregion
    }

    internal sealed class InstrumentedSoap11MessageEncoder : MessageEncoder
    {
        #region Fields

        private readonly MessageEncoder _inner;
        private readonly EncoderSeamMetrics _metrics;

        #endregion

        #region Constructors

        public InstrumentedSoap11MessageEncoder(MessageEncoder inner, EncoderSeamMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(metrics);
            _inner = inner;
            _metrics = metrics;
        }

        #endregion

        #region Properties

        public override string ContentType => _inner.ContentType;

        public override string MediaType => _inner.MediaType;

        public override MessageVersion MessageVersion => _inner.MessageVersion;

        #endregion

        #region Public Methods

        public override bool IsContentTypeSupported(string contentType)
        {
            return _inner.IsContentTypeSupported(contentType);
        }

        public override Message ReadMessage(ArraySegment<byte> buffer, BufferManager bufferManager, string contentType)
        {
            string innerType = _inner.GetType().FullName ?? _inner.GetType().Name;
            _metrics.RecordReadArraySegment(buffer.Count, innerType);
            TrackingBufferManager trackingManager = new TrackingBufferManager(bufferManager, _metrics);

            return _inner.ReadMessage(buffer, trackingManager, contentType);
        }

        public override Task<Message> ReadMessageAsync(Stream stream, int maxSizeOfHeaders, string contentType)
        {
            string innerType = _inner.GetType().FullName ?? _inner.GetType().Name;
            _metrics.RecordReadStream(innerType);

            return _inner.ReadMessageAsync(stream, maxSizeOfHeaders, contentType);
        }

        public override ArraySegment<byte> WriteMessage(Message message, int maxMessageSize, BufferManager bufferManager, int messageOffset)
        {
            string innerType = _inner.GetType().FullName ?? _inner.GetType().Name;
            TrackingBufferManager trackingManager = new TrackingBufferManager(bufferManager, _metrics);
            ArraySegment<byte> segment = _inner.WriteMessage(message, maxMessageSize, trackingManager, messageOffset);
            _metrics.RecordWriteBuffered(segment.Count, innerType);

            return segment;
        }

        public override Task WriteMessageAsync(Message message, Stream stream)
        {
            string innerType = _inner.GetType().FullName ?? _inner.GetType().Name;
            _metrics.RecordWriteStream(innerType);

            return _inner.WriteMessageAsync(message, stream);
        }

        #endregion

        private sealed class TrackingBufferManager : BufferManager
        {
            #region Fields

            private readonly BufferManager _inner;
            private readonly EncoderSeamMetrics _metrics;

            #endregion

            #region Constructors

            public TrackingBufferManager(BufferManager inner, EncoderSeamMetrics metrics)
            {
                ArgumentNullException.ThrowIfNull(inner);
                ArgumentNullException.ThrowIfNull(metrics);
                _inner = inner;
                _metrics = metrics;
            }

            #endregion

            #region Public Methods

            public override void Clear()
            {
                _inner.Clear();
            }

            public override void ReturnBuffer(byte[] buffer)
            {
                _inner.ReturnBuffer(buffer);
            }

            public override byte[] TakeBuffer(int bufferSize)
            {
                byte[] rented = _inner.TakeBuffer(bufferSize);
                _metrics.RecordBufferRental(rented.Length);

                return rented;
            }

            #endregion
        }
    }
}
