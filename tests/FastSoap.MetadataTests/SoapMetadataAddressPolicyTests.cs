using System;
using FastSoap.Metadata;
using Xunit;

namespace FastSoap.MetadataTests
{

    /// <summary>
    /// Tests for <see cref="SoapMetadataAddressPolicy"/> validating HTTP/HTTPS enforcement
    /// and host-header injection resistance.
    /// </summary>
    public sealed class SoapMetadataAddressPolicyTests
    {

        #region Public Methods

        [Theory]
        [InlineData("http://localhost:5000/PaymentService.svc", "http://localhost:5000/PaymentService.svc")]
        [InlineData("https://api.example.com/soap/PaymentService.svc", "https://api.example.com/soap/PaymentService.svc")]
        [InlineData("https://api.example.com:8443/v1/Service", "https://api.example.com:8443/v1/Service")]
        public void ConfiguredHttpOrHttpsUri_ReturnsCanonicalAddressAndIgnoresUntrustedHeaders(
            string configuredUrl,
            string expectedCanonicalAddress)
        {
            SoapMetadataAddressPolicy policy = SoapMetadataAddressPolicy.Create(configuredUrl);

            Assert.Equal(expectedCanonicalAddress, policy.CanonicalEndpointAddress);
            Assert.Equal(expectedCanonicalAddress, policy.GetEndpointAddress());
            Assert.Equal(
                expectedCanonicalAddress,
                policy.GetEndpointAddress("attacker.invalid:9999", "evil-proxy.invalid"));
            Assert.Equal(
                $"{expectedCanonicalAddress}?xsd=xsd0",
                policy.GetSchemaLocation("xsd0"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("/relative/PaymentService.svc")]
        [InlineData("ftp://files.example.com/PaymentService.svc")]
        [InlineData("file:///C:/PaymentService.svc")]
        [InlineData("javascript:alert(1)")]
        [InlineData("urn:payments:service")]
        [InlineData("https://user:secret@api.example.com/PaymentService.svc")]
        [InlineData("https://api.example.com/PaymentService.svc?wsdl")]
        [InlineData("https://api.example.com/PaymentService.svc#fragment")]
        public void InvalidOrNonHttpEndpointAddress_ThrowsArgumentException(string? invalidAddress)
        {
            Assert.ThrowsAny<ArgumentException>(
                () => new SoapMetadataAddressPolicy(invalidAddress!));
        }

        [Theory]
        [InlineData("../secret.xsd")]
        [InlineData("xsd0?evil=1")]
        [InlineData("https://evil.example/a.xsd")]
        [InlineData("/absolute.xsd")]
        public void GetSchemaLocation_RejectsUnsafeSchemaNames(string unsafeSchemaName)
        {
            SoapMetadataAddressPolicy policy = new SoapMetadataAddressPolicy("https://api.example.com/PaymentService.svc");

            Assert.Throws<ArgumentException>(
                () => policy.GetSchemaLocation(unsafeSchemaName));
        }

        [Fact]
        public void NullUri_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(
                static () => new SoapMetadataAddressPolicy((Uri)null!));
        }

        #endregion

    }

}
