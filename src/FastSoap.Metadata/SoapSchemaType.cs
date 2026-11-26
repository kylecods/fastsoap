using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using FastSoap.Abstractions;

namespace FastSoap.Metadata
{

    /// <summary>
    /// Represents a validated XML Schema type within FastSoap's supported metadata subset
    /// (built-in XSD primitives, ordered <c>xs:sequence</c> complex types, and string enumeration simple types).
    /// </summary>
    /// <example>
    /// <code>
    /// SoapSchemaType transferRequest = SoapSchemaType.Complex(
    ///     new SoapQName("urn:payments", "TransferRequest"),
    ///     [
    ///         SoapSchemaElement.CreateMember("AccountNumber", SoapSchemaType.String, minOccurs: 1, maxOccurs: 1, isNillable: false),
    ///         SoapSchemaElement.CreateMember("Amount", SoapSchemaType.Decimal, minOccurs: 1, maxOccurs: 1, isNillable: false)
    ///     ]);
    /// </code>
    /// </example>
    /// <remarks>
    /// Rejects unqualified type names, reserved protocol namespaces, duplicate sequence member names,
    /// empty or duplicate enumeration values, and unsupported XSD primitive types fail-first.
    /// </remarks>
    public sealed class SoapSchemaType : IEquatable<SoapSchemaType>
    {

        #region Fields

        /// <summary>
        /// The W3C XML Schema namespace URI (<c>http://www.w3.org/2001/XMLSchema</c>).
        /// </summary>
        public const string XmlSchemaNamespace = "http://www.w3.org/2001/XMLSchema";

        private static readonly FrozenSet<string> ReservedUserNamespaces = new[]
        {
            XmlSchemaNamespace,
            "http://www.w3.org/2001/XMLSchema-instance",
            "http://schemas.xmlsoap.org/wsdl/",
            "http://schemas.xmlsoap.org/wsdl/soap/",
            "http://schemas.xmlsoap.org/wsdl/soap12/",
            "http://schemas.xmlsoap.org/soap/encoding/",
            "http://schemas.xmlsoap.org/soap/envelope/",
            "http://www.w3.org/2003/05/soap-envelope"
        }.ToFrozenSet(StringComparer.Ordinal);

        private static readonly FrozenSet<string> SupportedPrimitives = new[]
        {
            "string",
            "boolean",
            "byte",
            "unsignedByte",
            "short",
            "unsignedShort",
            "int",
            "unsignedInt",
            "long",
            "unsignedLong",
            "float",
            "double",
            "decimal",
            "dateTime",
            "date",
            "time",
            "duration",
            "base64Binary",
            "anyURI",
            "QName",
            "integer"
        }.ToFrozenSet(StringComparer.Ordinal);

        #endregion

        #region Properties

        /// <summary>
        /// Gets the built-in <c>xs:anyURI</c> primitive type.
        /// </summary>
        public static SoapSchemaType AnyUri { get; } = CreatePrimitive("anyURI");

        /// <summary>
        /// Gets the built-in <c>xs:base64Binary</c> primitive type.
        /// </summary>
        public static SoapSchemaType Base64Binary { get; } = CreatePrimitive("base64Binary");

        /// <summary>
        /// Gets the built-in <c>xs:boolean</c> primitive type.
        /// </summary>
        public static SoapSchemaType Boolean { get; } = CreatePrimitive("boolean");

        /// <summary>
        /// Gets the built-in <c>xs:byte</c> primitive type.
        /// </summary>
        public static SoapSchemaType Byte { get; } = CreatePrimitive("byte");

        /// <summary>
        /// Gets the built-in <c>xs:date</c> primitive type.
        /// </summary>
        public static SoapSchemaType Date { get; } = CreatePrimitive("date");

        /// <summary>
        /// Gets the built-in <c>xs:dateTime</c> primitive type.
        /// </summary>
        public static SoapSchemaType DateTime { get; } = CreatePrimitive("dateTime");

        /// <summary>
        /// Gets the built-in <c>xs:decimal</c> primitive type.
        /// </summary>
        public static SoapSchemaType Decimal { get; } = CreatePrimitive("decimal");

        /// <summary>
        /// Gets the built-in <c>xs:double</c> primitive type.
        /// </summary>
        public static SoapSchemaType Double { get; } = CreatePrimitive("double");

        /// <summary>
        /// Gets the built-in <c>xs:duration</c> primitive type.
        /// </summary>
        public static SoapSchemaType Duration { get; } = CreatePrimitive("duration");

        /// <summary>
        /// Gets the ordered enumeration string literals when <see cref="Kind"/> is <see cref="SoapSchemaTypeKind.Enum"/>.
        /// </summary>
        public IReadOnlyList<string> EnumValues { get; }

        /// <summary>
        /// Gets the built-in <c>xs:float</c> primitive type.
        /// </summary>
        public static SoapSchemaType Float { get; } = CreatePrimitive("float");

        /// <summary>
        /// Gets the built-in <c>xs:int</c> primitive type.
        /// </summary>
        public static SoapSchemaType Int { get; } = CreatePrimitive("int");

        /// <summary>
        /// Gets the built-in <c>xs:integer</c> primitive type.
        /// </summary>
        public static SoapSchemaType Integer { get; } = CreatePrimitive("integer");

        /// <summary>
        /// Gets the schema type kind.
        /// </summary>
        public SoapSchemaTypeKind Kind { get; }

        /// <summary>
        /// Gets the built-in <c>xs:long</c> primitive type.
        /// </summary>
        public static SoapSchemaType Long { get; } = CreatePrimitive("long");

        /// <summary>
        /// Gets the qualified name of this schema type.
        /// </summary>
        public SoapQName QName { get; }

        /// <summary>
        /// Gets the ordered <c>xs:sequence</c> members when <see cref="Kind"/> is <see cref="SoapSchemaTypeKind.Complex"/>.
        /// </summary>
        public IReadOnlyList<SoapSchemaElement> SequenceMembers { get; }

        /// <summary>
        /// Gets the built-in <c>xs:short</c> primitive type.
        /// </summary>
        public static SoapSchemaType Short { get; } = CreatePrimitive("short");

        /// <summary>
        /// Gets the built-in <c>xs:string</c> primitive type.
        /// </summary>
        public static SoapSchemaType String { get; } = CreatePrimitive("string");

        /// <summary>
        /// Gets the built-in <c>xs:time</c> primitive type.
        /// </summary>
        public static SoapSchemaType Time { get; } = CreatePrimitive("time");

        /// <summary>
        /// Gets the built-in <c>xs:unsignedByte</c> primitive type.
        /// </summary>
        public static SoapSchemaType UnsignedByte { get; } = CreatePrimitive("unsignedByte");

        /// <summary>
        /// Gets the built-in <c>xs:unsignedInt</c> primitive type.
        /// </summary>
        public static SoapSchemaType UnsignedInt { get; } = CreatePrimitive("unsignedInt");

        /// <summary>
        /// Gets the built-in <c>xs:unsignedLong</c> primitive type.
        /// </summary>
        public static SoapSchemaType UnsignedLong { get; } = CreatePrimitive("unsignedLong");

        /// <summary>
        /// Gets the built-in <c>xs:unsignedShort</c> primitive type.
        /// </summary>
        public static SoapSchemaType UnsignedShort { get; } = CreatePrimitive("unsignedShort");

        #endregion

        #region Constructors

        private SoapSchemaType(
            SoapQName qName,
            SoapSchemaTypeKind kind,
            IReadOnlyList<SoapSchemaElement> sequenceMembers,
            IReadOnlyList<string> enumValues)
        {
            QName = qName;
            Kind = kind;
            SequenceMembers = sequenceMembers;
            EnumValues = enumValues;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates a validated named complex schema type with an ordered <c>xs:sequence</c> of members.
        /// </summary>
        /// <param name="qName">The qualified type name (must specify a non-empty non-reserved namespace URI).</param>
        /// <param name="sequenceMembers">The ordered sequence member elements.</param>
        /// <returns>An immutable <see cref="SoapSchemaType"/> of kind <see cref="SoapSchemaTypeKind.Complex"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sequenceMembers"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="qName"/> is unqualified or reserved, or when <paramref name="sequenceMembers"/> contains invalid, duplicate, or mismatched-namespace members.</exception>
        public static SoapSchemaType Complex(SoapQName qName, IReadOnlyList<SoapSchemaElement> sequenceMembers)
        {
            ValidateUserTypeQName(qName, nameof(qName));
            ArgumentNullException.ThrowIfNull(sequenceMembers);

            SoapSchemaElement[] membersCopy = new SoapSchemaElement[sequenceMembers.Count];
            HashSet<string> seenMemberNames = new HashSet<string>(sequenceMembers.Count, StringComparer.Ordinal);

            for (int index = 0; index < sequenceMembers.Count; index++)
            {
                SoapSchemaElement member = sequenceMembers[index];
                ArgumentNullException.ThrowIfNull(member);

                if (member.Type is null || member.InlineSequence.Count > 0)
                {
                    throw new ArgumentException(
                        $"Complex type '{qName}' member '{member.Name}' must reference a concrete SoapSchemaType rather than an anonymous inline sequence.",
                        nameof(sequenceMembers));
                }

                if (!string.IsNullOrEmpty(member.NamespaceUri)
                    && !string.Equals(member.NamespaceUri, qName.NamespaceUri, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Complex type '{qName}' member '{member.Name}' specifies namespace '{member.NamespaceUri}', which differs from declaring type namespace '{qName.NamespaceUri}'.",
                        nameof(sequenceMembers));
                }

                if (!seenMemberNames.Add(member.Name))
                {
                    throw new ArgumentException(
                        $"Complex type '{qName}' contains duplicate sequence member '{member.Name}'.",
                        nameof(sequenceMembers));
                }

                membersCopy[index] = member;
            }

            return new SoapSchemaType(qName, SoapSchemaTypeKind.Complex, membersCopy, []);
        }

        /// <summary>
        /// Creates a validated named string enumeration schema type.
        /// </summary>
        /// <param name="qName">The qualified simple type name (must specify a non-empty non-reserved namespace URI).</param>
        /// <param name="enumValues">The non-empty ordered list of unique enumeration values.</param>
        /// <returns>An immutable <see cref="SoapSchemaType"/> of kind <see cref="SoapSchemaTypeKind.Enum"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="enumValues"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="qName"/> is invalid or <paramref name="enumValues"/> is empty, blank, or contains duplicates.</exception>
        public static SoapSchemaType Enum(SoapQName qName, IReadOnlyList<string> enumValues)
        {
            ValidateUserTypeQName(qName, nameof(qName));
            ArgumentNullException.ThrowIfNull(enumValues);

            if (enumValues.Count == 0)
            {
                throw new ArgumentException($"Enum type '{qName}' must define at least one enumeration value.", nameof(enumValues));
            }

            string[] valuesCopy = new string[enumValues.Count];
            HashSet<string> seenValues = new HashSet<string>(enumValues.Count, StringComparer.Ordinal);

            for (int index = 0; index < enumValues.Count; index++)
            {
                string value = enumValues[index];
                ArgumentException.ThrowIfNullOrWhiteSpace(value);

                if (!seenValues.Add(value))
                {
                    throw new ArgumentException(
                        $"Enum type '{qName}' contains duplicate enumeration value '{value}'.",
                        nameof(enumValues));
                }

                valuesCopy[index] = value;
            }

            return new SoapSchemaType(qName, SoapSchemaTypeKind.Enum, [], valuesCopy);
        }

        /// <summary>
        /// Determines whether this schema type equals another structurally using ordinal comparison.
        /// </summary>
        /// <param name="other">The other schema type to compare.</param>
        /// <returns><see langword="true"/> when kind, qualified name, sequence members, and enum values match; otherwise <see langword="false"/>.</returns>
        public bool Equals(SoapSchemaType? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (Kind != other.Kind || !QName.Equals(other.QName))
            {
                return false;
            }

            if (SequenceMembers.Count != other.SequenceMembers.Count || EnumValues.Count != other.EnumValues.Count)
            {
                return false;
            }

            for (int index = 0; index < SequenceMembers.Count; index++)
            {
                if (!SequenceMembers[index].Equals(other.SequenceMembers[index]))
                {
                    return false;
                }
            }

            for (int index = 0; index < EnumValues.Count; index++)
            {
                if (!string.Equals(EnumValues[index], other.EnumValues[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines whether this schema type equals another object.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is an equal <see cref="SoapSchemaType"/>; otherwise <see langword="false"/>.</returns>
        public override bool Equals(object? obj)
        {
            return obj is SoapSchemaType other && Equals(other);
        }

        /// <summary>
        /// Computes a hash code consistent with structural equality.
        /// </summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Kind, QName, SequenceMembers.Count, EnumValues.Count);
        }

        /// <summary>
        /// Returns a supported built-in W3C XML Schema primitive <see cref="SoapSchemaType"/>.
        /// </summary>
        /// <param name="xsdLocalName">The W3C XSD built-in primitive local name (for example, <c>string</c>, <c>decimal</c>, <c>dateTime</c>).</param>
        /// <returns>The built-in <see cref="SoapSchemaType"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="xsdLocalName"/> is blank or not in the supported W3C XSD primitive subset.</exception>
        public static SoapSchemaType Primitive(string xsdLocalName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(xsdLocalName);

            if (!SupportedPrimitives.Contains(xsdLocalName))
            {
                throw new ArgumentException(
                    $"XSD type '{xsdLocalName}' is not in the supported W3C XML Schema primitive subset.",
                    nameof(xsdLocalName));
            }

            return CreatePrimitive(xsdLocalName);
        }

        #endregion

        #region Private Methods

        private static SoapSchemaType CreatePrimitive(string localName)
        {
            return new SoapSchemaType(
                new SoapQName(XmlSchemaNamespace, localName),
                SoapSchemaTypeKind.Primitive,
                [],
                []);
        }

        private static void ValidateUserTypeQName(SoapQName qName, string paramName)
        {
            if (string.IsNullOrWhiteSpace(qName.LocalName))
            {
                throw new ArgumentException("Schema type qualified name must be initialized.", paramName);
            }

            if (string.IsNullOrWhiteSpace(qName.NamespaceUri))
            {
                throw new ArgumentException($"Schema type '{qName.LocalName}' must specify a qualified namespace URI.", paramName);
            }

            if (ReservedUserNamespaces.Contains(qName.NamespaceUri))
            {
                throw new ArgumentException(
                    $"Schema type '{qName}' cannot use reserved protocol namespace '{qName.NamespaceUri}'.",
                    paramName);
            }
        }

        #endregion

    }

}
