using System;
using System.Threading.Tasks;

using FastSoap.Abstractions;
using FastSoap.Core.Dispatching;
using FastSoap.Core.Serialization;

using Xunit;

namespace FastSoap.ProtocolTests
{

    /// <summary>
    /// Verifies build-once <see cref="SoapOperationTableBuilder"/> and <see cref="SoapOperationTable"/> invariants.
    /// </summary>
    public sealed class SoapOperationTableTests
    {

        #region Public Methods

        [Fact]
        public void Build_DuplicateAction_ThrowsInvalidOperationException()
        {
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            SoapOperationDescriptor op1 = ProtocolTestFixtures.CreateEchoDescriptor();
            SoapOperationDescriptor op2 = new SoapOperationDescriptor(
                "EchoDuplicate",
                op1.Action,
                null,
                new SoapQName("urn:other", "Other"),
                new SoapQName("urn:other", "OtherResponse"));

            builder.MapOperation(
                op1,
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo }));

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                builder.MapOperation(
                    op2,
                    ProtocolTestFixtures.Context.EchoStringRequestWire,
                    ProtocolTestFixtures.Context.EchoStringResponseWire,
                    static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo })));

            Assert.Equal($"Duplicate SOAP action '{op1.Action}' registered on endpoint.", ex.Message);
        }

        [Fact]
        public void Build_FallbackDisabled_OmitsSecondaryQNameIndexAndAllowsSharedBodyElement()
        {
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            SoapQName sharedElement = new SoapQName("http://tempuri.org/", "EchoString");

            builder.MapOperation(
                new SoapOperationDescriptor("EchoA", "urn:a/Echo", null, sharedElement, new SoapQName("http://tempuri.org/", "EchoStringResponse")),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo }));

            builder.MapOperation(
                new SoapOperationDescriptor("EchoB", "urn:b/Echo", null, sharedElement, new SoapQName("http://tempuri.org/", "EchoStringResponse")),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo }));

            SoapOperationTable table = builder.Build(new SoapEndpointOptions
            {
                AllowMissingSoap11Action = false,
                EnabledVersions = SoapVersionFlags.Soap11
            });

            Assert.False(table.SupportsActionlessRouting);
            Assert.Null(table.OperationsByRequestElement);
            Assert.True(table.TryGetByAction("urn:a/Echo", out ISoapOperation? opA));
            Assert.Equal("EchoA", opA.Descriptor.Name);
            Assert.True(table.TryGetByAction("urn:a/Echo".AsSpan(), out ISoapOperation? opASpan));
            Assert.Same(opA, opASpan);
            Assert.False(table.TryGetByAction("URN:A/ECHO", out _));
            Assert.False(table.TryGetByAction("URN:A/ECHO".AsSpan(), out _));
            Assert.False(table.TryGetByAction("   ".AsSpan(), out _));
            Assert.False(table.TryGetByRequestElement(sharedElement, out _));
        }

        [Fact]
        public void Build_FallbackEnabledWithAmbiguousRequestElement_ThrowsInvalidOperationException()
        {
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            SoapQName sharedElement = new SoapQName("http://tempuri.org/", "EchoString");

            builder.MapOperation(
                new SoapOperationDescriptor("EchoA", "urn:a/Echo", null, sharedElement, new SoapQName("http://tempuri.org/", "EchoStringResponse")),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo }));

            builder.MapOperation(
                new SoapOperationDescriptor("EchoB", "urn:b/Echo", null, sharedElement, new SoapQName("http://tempuri.org/", "EchoStringResponse")),
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo }));

            Assert.Throws<InvalidOperationException>(() => builder.Build(new SoapEndpointOptions
            {
                AllowMissingSoap11Action = true,
                EnabledVersions = SoapVersionFlags.Soap11
            }));

            Assert.Throws<InvalidOperationException>(() => builder.Build(new SoapEndpointOptions
            {
                AllowMissingSoap11Action = false,
                EnabledVersions = SoapVersionFlags.Soap12
            }));
        }

        [Fact]
        public void Build_FallbackEnabledWithUniqueElements_PopulatesSecondaryQNameIndex()
        {
            SoapOperationTableBuilder builder = new SoapOperationTableBuilder();
            SoapOperationDescriptor echo = ProtocolTestFixtures.CreateEchoDescriptor();
            SoapOperationDescriptor transfer = ProtocolTestFixtures.CreateTransferDescriptor();

            builder.MapOperation(
                echo,
                ProtocolTestFixtures.Context.EchoStringRequestWire,
                ProtocolTestFixtures.Context.EchoStringResponseWire,
                static (req, _, _) => ValueTask.FromResult(new EchoStringResponseWire { EchoStringResult = req.Echo }));

            builder.MapOperation(
                transfer,
                ProtocolTestFixtures.Context.TransferRequestWire,
                ProtocolTestFixtures.Context.TransferResponseWire,
                static (_, _, _) => ValueTask.FromResult(new TransferResponseWire()));

            SoapOperationTable table = builder.Build(new SoapEndpointOptions
            {
                EnabledVersions = SoapVersionFlags.Soap12
            });

            Assert.True(table.SupportsActionlessRouting);
            Assert.NotNull(table.OperationsByRequestElement);
            Assert.True(table.TryGetByRequestElement(echo.RequestElement, out ISoapOperation? foundEcho));
            Assert.Equal("EchoString", foundEcho.Descriptor.Name);
            Assert.True(table.TryGetByRequestElement(transfer.RequestElement, out ISoapOperation? foundTransfer));
            Assert.Equal("Transfer", foundTransfer.Descriptor.Name);
        }

        #endregion

    }

}
