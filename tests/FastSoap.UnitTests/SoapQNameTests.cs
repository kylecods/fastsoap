using System;
using System.Text;
using FastSoap.Abstractions;
using Xunit;

namespace FastSoap.UnitTests
{

    /// <summary>
    /// Unit tests for <see cref="SoapQName"/> validation, UTF-8 token matching, and ordinal equality.
    /// </summary>
    public sealed class SoapQNameTests
    {

        #region Public Methods

        [Fact]
        public void Constructor_EmptyNamespaceUri_AllowsUnqualifiedName()
        {
            SoapQName qname = new SoapQName(string.Empty, "Transfer");

            Assert.Equal(string.Empty, qname.NamespaceUri);
            Assert.Equal("Transfer", qname.LocalName);
            Assert.True(qname.NamespaceUriUtf8.IsEmpty);
            Assert.True(qname.LocalNameUtf8.SequenceEqual("Transfer"u8));
            Assert.Equal("Transfer", qname.ToString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("p:Transfer")]
        [InlineData("1Transfer")]
        [InlineData("-Transfer")]
        [InlineData(".Transfer")]
        [InlineData("Trans fer")]
        [InlineData("Trans<fer")]
        [InlineData("Trans\nfer")]
        public void Constructor_InvalidLocalName_ThrowsArgumentException(string invalidLocalName)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new SoapQName("urn:payments", invalidLocalName));

            Assert.Equal("localName", exception.ParamName);
        }

        [Theory]
        [InlineData("urn:pay\0ments")]
        [InlineData("urn:pay\rments")]
        [InlineData("urn:pay\nments")]
        public void Constructor_InvalidNamespaceControlChars_ThrowsArgumentException(string invalidNamespace)
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => new SoapQName(invalidNamespace, "Transfer"));

            Assert.Equal("namespaceUri", exception.ParamName);
        }

        [Fact]
        public void Constructor_NullArguments_ThrowsArgumentNullException()
        {
            ArgumentNullException nullNamespace = Assert.Throws<ArgumentNullException>(
                static () => new SoapQName(null!, "Transfer"));
            Assert.Equal("namespaceUri", nullNamespace.ParamName);

            ArgumentNullException nullLocalName = Assert.Throws<ArgumentNullException>(
                static () => new SoapQName("urn:payments", null!));
            Assert.Equal("localName", nullLocalName.ParamName);
        }

        [Fact]
        public void Constructor_ValidNamespaceAndLocalName_PreEncodesUtf8AndFormatsClarkNotation()
        {
            SoapQName qname = new SoapQName("urn:payments", "Transfer");
            (string ns, string local) = qname;

            Assert.Equal("urn:payments", ns);
            Assert.Equal("Transfer", local);
            Assert.True(qname.NamespaceUriUtf8.SequenceEqual("urn:payments"u8));
            Assert.True(qname.LocalNameUtf8.SequenceEqual("Transfer"u8));
            Assert.Equal("{urn:payments}Transfer", qname.ToString());
        }

        [Fact]
        public void DefaultValue_IsSafeForEqualityAndSpans()
        {
            SoapQName defaultQName = default;
            SoapQName otherDefault = default;
            SoapQName valid = new SoapQName("urn:payments", "Transfer");

            Assert.True(defaultQName.Equals(otherDefault));
            Assert.Equal(defaultQName.GetHashCode(), otherDefault.GetHashCode());
            Assert.False(defaultQName.Equals(valid));
            Assert.True(defaultQName.NamespaceUriUtf8.IsEmpty);
            Assert.True(defaultQName.LocalNameUtf8.IsEmpty);
            Assert.Equal(string.Empty, defaultQName.ToString());
        }

        [Fact]
        public void EqualsAndGetHashCode_UseOrdinalValueEqualityAcrossSeparateInstances()
        {
            SoapQName first = new SoapQName("urn:payments", "Transfer");
            SoapQName second = new SoapQName(new string("urn:payments".ToCharArray()), new string("Transfer".ToCharArray()));
            SoapQName differentCase = new SoapQName("URN:PAYMENTS", "Transfer");

            Assert.True(first == second);
            Assert.True(first.Equals(second));
            Assert.Equal(first.GetHashCode(), second.GetHashCode());

            Assert.False(first == differentCase);
            Assert.False(first.Equals(differentCase));
        }

        [Fact]
        public void Matches_ComparesUtf8SpansExactly()
        {
            SoapQName qname = new SoapQName("urn:payments", "Transfer");

            Assert.True(qname.Matches("urn:payments"u8, "Transfer"u8));
            Assert.False(qname.Matches("urn:PAYMENTS"u8, "Transfer"u8));
            Assert.False(qname.Matches("urn:payments"u8, "transfer"u8));
            Assert.False(qname.Matches(""u8, "Transfer"u8));
        }

        [Fact]
        public void Matches_SupportsNonAsciiUtf8()
        {
            SoapQName qname = new SoapQName("urn:café", "Opération");
            byte[] expectedNs = Encoding.UTF8.GetBytes("urn:café");
            byte[] expectedLocal = Encoding.UTF8.GetBytes("Opération");

            Assert.True(qname.Matches(expectedNs, expectedLocal));
        }

        #endregion

    }

}
