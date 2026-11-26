using System;
using FastSoap.Abstractions;
using Xunit;

namespace FastSoap.UnitTests
{

    /// <summary>
    /// Unit tests for <see cref="SoapOperationDescriptor"/> validation, UTF-8 tokens, and ordinal equality.
    /// </summary>
    public sealed class SoapOperationDescriptorTests
    {

        #region Public Methods

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Constructor_BlankNameOrAction_ThrowsArgumentException(string blankValue)
        {
            SoapQName req = new SoapQName("urn:payments", "Transfer");
            SoapQName resp = new SoapQName("urn:payments", "TransferResponse");

            ArgumentException nameEx = Assert.Throws<ArgumentException>(
                () => new SoapOperationDescriptor(blankValue, "urn:payments/Transfer", null, req, resp));
            Assert.Equal("name", nameEx.ParamName);

            ArgumentException actionEx = Assert.Throws<ArgumentException>(
                () => new SoapOperationDescriptor("Transfer", blankValue, null, req, resp));
            Assert.Equal("action", actionEx.ParamName);
        }

        [Theory]
        [InlineData("urn:payments/Transfer\r")]
        [InlineData("urn:payments/Transfer\n")]
        [InlineData("urn:payments/Transfer\0")]
        public void Constructor_ControlCharsInActionOrReplyAction_ThrowsArgumentException(string badAction)
        {
            SoapQName req = new SoapQName("urn:payments", "Transfer");
            SoapQName resp = new SoapQName("urn:payments", "TransferResponse");

            ArgumentException actionEx = Assert.Throws<ArgumentException>(
                () => new SoapOperationDescriptor("Transfer", badAction, null, req, resp));
            Assert.Equal("action", actionEx.ParamName);

            ArgumentException replyEx = Assert.Throws<ArgumentException>(
                () => new SoapOperationDescriptor("Transfer", "urn:payments/Transfer", badAction, req, resp));
            Assert.Equal("replyAction", replyEx.ParamName);
        }

        [Fact]
        public void Constructor_UninitializedQName_ThrowsArgumentException()
        {
            SoapQName valid = new SoapQName("urn:payments", "Transfer");
            SoapQName uninitialized = default;

            ArgumentException reqEx = Assert.Throws<ArgumentException>(
                () => new SoapOperationDescriptor("Transfer", "urn:payments/Transfer", null, uninitialized, valid));
            Assert.Equal("requestElement", reqEx.ParamName);

            ArgumentException respEx = Assert.Throws<ArgumentException>(
                () => new SoapOperationDescriptor("Transfer", "urn:payments/Transfer", null, valid, uninitialized));
            Assert.Equal("responseElement", respEx.ParamName);
        }

        [Fact]
        public void Constructor_ValidInputs_PreEncodesActionTokens()
        {
            SoapQName req = new SoapQName("urn:payments", "Transfer");
            SoapQName resp = new SoapQName("urn:payments", "TransferResponse");

            SoapOperationDescriptor withReply = new SoapOperationDescriptor(
                "Transfer",
                "urn:payments/IPaymentService/Transfer",
                "urn:payments/IPaymentService/TransferResponse",
                req,
                resp);

            Assert.Equal("Transfer", withReply.Name);
            Assert.Equal("urn:payments/IPaymentService/Transfer", withReply.Action);
            Assert.Equal("urn:payments/IPaymentService/TransferResponse", withReply.ReplyAction);
            Assert.True(withReply.ActionUtf8.SequenceEqual("urn:payments/IPaymentService/Transfer"u8));
            Assert.True(withReply.ReplyActionUtf8.SequenceEqual("urn:payments/IPaymentService/TransferResponse"u8));

            SoapOperationDescriptor withoutReply = new SoapOperationDescriptor(
                "Transfer",
                "urn:payments/IPaymentService/Transfer",
                null,
                req,
                resp);

            Assert.Null(withoutReply.ReplyAction);
            Assert.True(withoutReply.ReplyActionUtf8.IsEmpty);
        }

        [Fact]
        public void Constructor_WhitespaceReplyAction_ThrowsArgumentException()
        {
            SoapQName req = new SoapQName("urn:payments", "Transfer");
            SoapQName resp = new SoapQName("urn:payments", "TransferResponse");

            ArgumentException replyEx = Assert.Throws<ArgumentException>(
                () => new SoapOperationDescriptor("Transfer", "urn:payments/Transfer", "   ", req, resp));
            Assert.Equal("replyAction", replyEx.ParamName);
        }

        [Fact]
        public void EqualsAndGetHashCode_UseOrdinalValueEqualityAcrossSeparateInstances()
        {
            SoapOperationDescriptor first = new SoapOperationDescriptor(
                "Transfer",
                "urn:payments/Transfer",
                "urn:payments/TransferResponse",
                new SoapQName("urn:payments", "Transfer"),
                new SoapQName("urn:payments", "TransferResponse"));

            SoapOperationDescriptor second = new SoapOperationDescriptor(
                new string("Transfer".ToCharArray()),
                new string("urn:payments/Transfer".ToCharArray()),
                new string("urn:payments/TransferResponse".ToCharArray()),
                new SoapQName("urn:payments", "Transfer"),
                new SoapQName("urn:payments", "TransferResponse"));

            SoapOperationDescriptor differentCase = new SoapOperationDescriptor(
                "Transfer",
                "URN:PAYMENTS/Transfer",
                "urn:payments/TransferResponse",
                new SoapQName("urn:payments", "Transfer"),
                new SoapQName("urn:payments", "TransferResponse"));

            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.NotEqual(first, differentCase);
        }

        #endregion

    }

}
