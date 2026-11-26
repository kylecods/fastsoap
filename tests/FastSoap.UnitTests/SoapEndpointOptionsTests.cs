using System;
using FastSoap.Abstractions;
using Xunit;

namespace FastSoap.UnitTests
{

    /// <summary>
    /// Unit tests for <see cref="SoapEndpointOptions"/> defaults and fail-first validation.
    /// </summary>
    public sealed class SoapEndpointOptionsTests
    {

        #region Public Methods

        [Fact]
        public void Defaults_MatchSpecificationLimits()
        {
            SoapEndpointOptions options = new SoapEndpointOptions();

            Assert.Equal(1024 * 1024, options.MaxRequestBytes);
            Assert.Equal(2 * 1024 * 1024, options.MaxResponseBytes);
            Assert.Equal(64, options.MaxXmlDepth);
            Assert.Equal(64 * 1024, options.MaxHeaderBytes);
            Assert.Equal(32, options.MaxHeaderCount);
            Assert.Equal(64, options.MaxAttributesPerElement);
            Assert.Equal(1024, options.MaxActionLength);
            Assert.Equal(SoapVersionFlags.Soap11, options.EnabledVersions);
            Assert.False(options.AllowMissingSoap11Action);
            Assert.False(options.EnableMetadata);
            Assert.True(options.ClearPooledBuffers);

            options.Validate();
        }

        [Fact]
        public void Validate_InvalidEnabledVersions_ThrowsArgumentOutOfRangeException()
        {
            SoapEndpointOptions noneOptions = new SoapEndpointOptions
            {
                EnabledVersions = SoapVersionFlags.None
            };

            ArgumentOutOfRangeException noneEx = Assert.Throws<ArgumentOutOfRangeException>(noneOptions.Validate);
            Assert.Equal(nameof(SoapEndpointOptions.EnabledVersions), noneEx.ParamName);

            SoapEndpointOptions unknownFlagOptions = new SoapEndpointOptions
            {
                EnabledVersions = (SoapVersionFlags)8
            };

            ArgumentOutOfRangeException unknownEx = Assert.Throws<ArgumentOutOfRangeException>(unknownFlagOptions.Validate);
            Assert.Equal(nameof(SoapEndpointOptions.EnabledVersions), unknownEx.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Validate_NonPositiveQuotas_ThrowsArgumentOutOfRangeException(int badLimit)
        {
            SoapEndpointOptions badRequest = new SoapEndpointOptions { MaxRequestBytes = badLimit };
            Assert.Equal(nameof(SoapEndpointOptions.MaxRequestBytes), Assert.Throws<ArgumentOutOfRangeException>(badRequest.Validate).ParamName);

            SoapEndpointOptions badResponse = new SoapEndpointOptions { MaxResponseBytes = badLimit };
            Assert.Equal(nameof(SoapEndpointOptions.MaxResponseBytes), Assert.Throws<ArgumentOutOfRangeException>(badResponse.Validate).ParamName);

            SoapEndpointOptions badDepth = new SoapEndpointOptions { MaxXmlDepth = badLimit };
            Assert.Equal(nameof(SoapEndpointOptions.MaxXmlDepth), Assert.Throws<ArgumentOutOfRangeException>(badDepth.Validate).ParamName);

            SoapEndpointOptions badAttributes = new SoapEndpointOptions { MaxAttributesPerElement = badLimit };
            Assert.Equal(nameof(SoapEndpointOptions.MaxAttributesPerElement), Assert.Throws<ArgumentOutOfRangeException>(badAttributes.Validate).ParamName);

            SoapEndpointOptions badAction = new SoapEndpointOptions { MaxActionLength = badLimit };
            Assert.Equal(nameof(SoapEndpointOptions.MaxActionLength), Assert.Throws<ArgumentOutOfRangeException>(badAction.Validate).ParamName);
        }

        [Fact]
        public void Validate_ValidCustomFlagsAndZeroHeaderBudgets_Succeeds()
        {
            SoapEndpointOptions options = new SoapEndpointOptions
            {
                EnabledVersions = SoapVersionFlags.Both,
                MaxHeaderBytes = 0,
                MaxHeaderCount = 0,
                AllowMissingSoap11Action = true,
                EnableMetadata = true,
                ClearPooledBuffers = false
            };

            options.Validate();

            SoapEndpointOptions negativeHeaderBytes = new SoapEndpointOptions { MaxHeaderBytes = -1 };
            Assert.Equal(nameof(SoapEndpointOptions.MaxHeaderBytes), Assert.Throws<ArgumentOutOfRangeException>(negativeHeaderBytes.Validate).ParamName);

            SoapEndpointOptions negativeHeaderCount = new SoapEndpointOptions { MaxHeaderCount = -1 };
            Assert.Equal(nameof(SoapEndpointOptions.MaxHeaderCount), Assert.Throws<ArgumentOutOfRangeException>(negativeHeaderCount.Validate).ParamName);
        }

        #endregion

    }

}
