using System;
using System.Collections.Generic;
using FastSoap.Abstractions;

namespace FastSoap.Metadata
{

    /// <summary>
    /// Represents either a global WSDL/XSD element declaration or a sequence member element
    /// within a complex type or wrapped operation element.
    /// </summary>
    /// <example>
    /// <code>
    /// SoapSchemaElement requestElement = SoapSchemaElement.CreateWrapped(
    ///     new SoapQName("urn:payments", "Transfer"),
    ///     [SoapSchemaElement.CreateMember("request", transferRequestType, minOccurs: 0, maxOccurs: 1, isNillable: true)]);
    /// </code>
    /// </example>
    public sealed class SoapSchemaElement : IEquatable<SoapSchemaElement>
    {

        #region Properties

        /// <summary>
        /// Gets the ordered inline <c>xs:sequence</c> member elements when this is a document/literal wrapped global element;
        /// empty when <see cref="Type"/> is specified or the wrapped sequence has no parameters.
        /// </summary>
        public IReadOnlyList<SoapSchemaElement> InlineSequence { get; }

        /// <summary>
        /// Gets a value indicating whether <c>nillable="true"</c> is emitted on the element declaration.
        /// </summary>
        public bool IsNillable { get; }

        /// <summary>
        /// Gets the maximum occurrence count, or <see langword="null"/> for <c>unbounded</c>.
        /// </summary>
        public int? MaxOccurs { get; }

        /// <summary>
        /// Gets the minimum occurrence count.
        /// </summary>
        public int MinOccurs { get; }

        /// <summary>
        /// Gets the local XML element name (NCName).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the XML namespace URI for a global element, or <see cref="string.Empty"/> when inherited from an enclosing schema.
        /// </summary>
        public string NamespaceUri { get; }

        /// <summary>
        /// Gets the expanded qualified name of this element.
        /// </summary>
        public SoapQName QName => new SoapQName(NamespaceUri, Name);

        /// <summary>
        /// Gets the named schema type referenced by this element, or <see langword="null"/> for an inline wrapped complex type.
        /// </summary>
        public SoapSchemaType? Type { get; }

        #endregion

        #region Constructors

        private SoapSchemaElement(
            string namespaceUri,
            string name,
            SoapSchemaType? type,
            IReadOnlyList<SoapSchemaElement> inlineSequence,
            int minOccurs,
            int? maxOccurs,
            bool isNillable)
        {
            NamespaceUri = namespaceUri;
            Name = name;
            Type = type;
            InlineSequence = inlineSequence;
            MinOccurs = minOccurs;
            MaxOccurs = maxOccurs;
            IsNillable = isNillable;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a global XSD element declaration referencing a named <see cref="SoapSchemaType"/>.
        /// </summary>
        /// <param name="elementQName">The qualified global element name (must specify a non-empty namespace URI).</param>
        /// <param name="type">The named schema type referenced by this global element.</param>
        /// <param name="isNillable">Whether the global element permits <c>xsi:nil="true"</c>.</param>
        /// <returns>An immutable global <see cref="SoapSchemaElement"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="elementQName"/> is uninitialized or unqualified.</exception>
        public static SoapSchemaElement CreateGlobal(SoapQName elementQName, SoapSchemaType type, bool isNillable = false)
        {
            ValidateGlobalQName(elementQName, nameof(elementQName));
            ArgumentNullException.ThrowIfNull(type);

            return new SoapSchemaElement(
                elementQName.NamespaceUri,
                elementQName.LocalName,
                type,
                [],
                minOccurs: 1,
                maxOccurs: 1,
                isNillable);
        }

        /// <summary>
        /// Creates a sequence member element declaration for use inside a complex type or wrapped global element.
        /// </summary>
        /// <param name="name">The local XML element name (valid NCName).</param>
        /// <param name="type">The member's schema type.</param>
        /// <param name="minOccurs">Minimum occurrences (must be non-negative).</param>
        /// <param name="maxOccurs">Maximum occurrences (<see langword="null"/> for <c>unbounded</c>, otherwise <c>&gt;= 1</c> and <c>&gt;= minOccurs</c>).</param>
        /// <param name="isNillable">Whether <c>nillable="true"</c> is emitted.</param>
        /// <param name="namespaceUri">Optional explicit namespace URI (must match the enclosing schema's target namespace when provided).</param>
        /// <returns>An immutable sequence member <see cref="SoapSchemaElement"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is not a valid XML NCName.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minOccurs"/> or <paramref name="maxOccurs"/> bounds are invalid.</exception>
        public static SoapSchemaElement CreateMember(
            string name,
            SoapSchemaType type,
            int minOccurs = 1,
            int? maxOccurs = 1,
            bool isNillable = false,
            string? namespaceUri = null)
        {
            SoapQName validatedName = new SoapQName(namespaceUri ?? string.Empty, name);
            ArgumentNullException.ThrowIfNull(type);
            ValidateOccurrenceBounds(minOccurs, maxOccurs);

            return new SoapSchemaElement(
                validatedName.NamespaceUri,
                validatedName.LocalName,
                type,
                [],
                minOccurs,
                maxOccurs,
                isNillable);
        }

        /// <summary>
        /// Creates a global document/literal wrapped element declaration containing an inline anonymous <c>xs:complexType</c> and <c>xs:sequence</c>.
        /// </summary>
        /// <param name="elementQName">The qualified global wrapper element name (must specify a non-empty namespace URI).</param>
        /// <param name="sequenceMembers">Optional ordered sequence members inside the wrapper element.</param>
        /// <returns>An immutable wrapped global <see cref="SoapSchemaElement"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="elementQName"/> is unqualified or <paramref name="sequenceMembers"/> contains invalid, duplicate, or mismatched-namespace members.</exception>
        public static SoapSchemaElement CreateWrapped(
            SoapQName elementQName,
            IReadOnlyList<SoapSchemaElement>? sequenceMembers = null)
        {
            ValidateGlobalQName(elementQName, nameof(elementQName));

            if (sequenceMembers is null || sequenceMembers.Count == 0)
            {
                return new SoapSchemaElement(
                    elementQName.NamespaceUri,
                    elementQName.LocalName,
                    type: null,
                    [],
                    minOccurs: 1,
                    maxOccurs: 1,
                    isNillable: false);
            }

            SoapSchemaElement[] membersCopy = new SoapSchemaElement[sequenceMembers.Count];
            HashSet<string> seenMemberNames = new HashSet<string>(sequenceMembers.Count, StringComparer.Ordinal);

            for (int index = 0; index < sequenceMembers.Count; index++)
            {
                SoapSchemaElement member = sequenceMembers[index];
                ArgumentNullException.ThrowIfNull(member);

                if (member.Type is null || member.InlineSequence.Count > 0)
                {
                    throw new ArgumentException(
                        $"Wrapped element '{elementQName}' member '{member.Name}' must reference a concrete SoapSchemaType.",
                        nameof(sequenceMembers));
                }

                if (!string.IsNullOrEmpty(member.NamespaceUri)
                    && !string.Equals(member.NamespaceUri, elementQName.NamespaceUri, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Wrapped element '{elementQName}' member '{member.Name}' specifies namespace '{member.NamespaceUri}', which differs from element namespace '{elementQName.NamespaceUri}'.",
                        nameof(sequenceMembers));
                }

                if (!seenMemberNames.Add(member.Name))
                {
                    throw new ArgumentException(
                        $"Wrapped element '{elementQName}' contains duplicate sequence member '{member.Name}'.",
                        nameof(sequenceMembers));
                }

                membersCopy[index] = member;
            }

            return new SoapSchemaElement(
                elementQName.NamespaceUri,
                elementQName.LocalName,
                type: null,
                membersCopy,
                minOccurs: 1,
                maxOccurs: 1,
                isNillable: false);
        }

        /// <summary>
        /// Determines whether this element declaration equals another structurally using ordinal comparison.
        /// </summary>
        /// <param name="other">The other element to compare.</param>
        /// <returns><see langword="true"/> when all element metadata matches; otherwise <see langword="false"/>.</returns>
        public bool Equals(SoapSchemaElement? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (!string.Equals(NamespaceUri, other.NamespaceUri, StringComparison.Ordinal)
                || !string.Equals(Name, other.Name, StringComparison.Ordinal)
                || MinOccurs != other.MinOccurs
                || MaxOccurs != other.MaxOccurs
                || IsNillable != other.IsNillable
                || !Equals(Type, other.Type)
                || InlineSequence.Count != other.InlineSequence.Count)
            {
                return false;
            }

            for (int index = 0; index < InlineSequence.Count; index++)
            {
                if (!InlineSequence[index].Equals(other.InlineSequence[index]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether this element equals another object.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="SoapSchemaElement"/>; otherwise <see langword="false"/>.</returns>
        public override bool Equals(object? obj)
        {
            return obj is SoapSchemaElement other && Equals(other);
        }

        /// <summary>
        /// Computes a hash code consistent with structural equality.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(NamespaceUri),
                StringComparer.Ordinal.GetHashCode(Name),
                MinOccurs,
                MaxOccurs,
                IsNillable,
                Type,
                InlineSequence.Count);
        }

        #endregion

        #region Private Methods

        private static void ValidateGlobalQName(SoapQName elementQName, string paramName)
        {
            if (string.IsNullOrWhiteSpace(elementQName.LocalName))
            {
                throw new ArgumentException("Global schema element qualified name must be initialized.", paramName);
            }

            if (string.IsNullOrWhiteSpace(elementQName.NamespaceUri))
            {
                throw new ArgumentException(
                    $"Global schema element '{elementQName.LocalName}' must specify a qualified namespace URI.",
                    paramName);
            }
        }

        private static void ValidateOccurrenceBounds(int minOccurs, int? maxOccurs)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(minOccurs);

            if (maxOccurs is not null)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxOccurs.Value, 0);
                if (maxOccurs.Value < minOccurs)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(maxOccurs),
                        maxOccurs,
                        "Maximum occurrences cannot be less than minimum occurrences.");
                }
            }
        }

        #endregion

    }

}
