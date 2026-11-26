using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;

using Xunit;

namespace FastSoap.CompatibilityTests
{

    /// <summary>
    /// End-to-end interoperability tests using a real <c>System.ServiceModel.Http</c> WCF
    /// <see cref="ChannelFactory{TChannel}"/> client calling FastSoap over loopback Kestrel HTTP.
    /// </summary>
    public sealed class WcfClientCompatibilityTests
    {

        #region Public Methods

        [Fact]
        public async Task BasicHttpBinding_WcfClient_EchoNonAsciiNullNestedTransferCollectionsAndFaults_Succeeds()
        {
            await using CompatibilityServerHost host = await CompatibilityServerHost.StartAsync();

            BasicHttpBinding binding = new BasicHttpBinding(BasicHttpSecurityMode.None);

            using (ChannelFactory<IWcfEchoClient> echoFactory = new ChannelFactory<IWcfEchoClient>(
                binding,
                new EndpointAddress(new Uri(host.BaseUri, "/soap11/echo-dcs.svc"))))
            {
                IWcfEchoClient echoClient = echoFactory.CreateChannel();
                try
                {
                    Assert.Equal("hello-wcf", await echoClient.EchoStringAsync("hello-wcf"));
                    Assert.Equal("Hello, 世界! € — café 🚀", await echoClient.EchoStringAsync("Hello, 世界! € — café 🚀"));
                    Assert.Null(await echoClient.EchoStringAsync(null));

                    FaultException serverFault = await Assert.ThrowsAsync<FaultException>(() => echoClient.EchoStringAsync("__THROW_INTERNAL__"));
                    Assert.Equal("Server", serverFault.Code.Name);
                    Assert.Equal("An internal server error occurred while processing the SOAP request.", serverFault.Message);
                    Assert.DoesNotContain("SECRET_DB_CONNECTION_STRING", serverFault.Message, StringComparison.Ordinal);
                }
                finally
                {
                    CloseChannel(echoClient);
                }
            }

            using (ChannelFactory<IWcfPaymentClient> paymentFactory = new ChannelFactory<IWcfPaymentClient>(
                binding,
                new EndpointAddress(new Uri(host.BaseUri, "/soap11/payment-dcs-urn.svc"))))
            {
                IWcfPaymentClient paymentClient = paymentFactory.CreateChannel();
                try
                {
                    TransferResult transferResult = await paymentClient.TransferAsync(new TransferRequest
                    {
                        AccountNumber = "123456",
                        Amount = 100.50m,
                        Currency = "USD",
                        Metadata = new TransferMetadata
                        {
                            CorrelationId = "CORR-WCF-1",
                            Reference = "REF-WCF-1"
                        },
                        Note = "WCF BasicHttpBinding"
                    });

                    Assert.Equal("TX-001", transferResult.TransactionId);
                    Assert.True(transferResult.Successful);
                    Assert.Equal(new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc), transferResult.ProcessedAtUtc);
                    Assert.Equal(PaymentStatus.Completed, transferResult.Status);

                    TransferResult nilTransferResult = await paymentClient.TransferAsync(new TransferRequest
                    {
                        AccountNumber = "123456",
                        Amount = 25.00m,
                        Currency = "EUR",
                        Metadata = null,
                        Note = null
                    });

                    Assert.Equal("TX-001", nilTransferResult.TransactionId);
                    Assert.True(nilTransferResult.Successful);

                    BatchTransferResult batchResult = await paymentClient.BatchTransferAsync(new BatchTransferRequest
                    {
                        BatchId = "BATCH-WCF",
                        Items =
                        [
                            new TransferRequest
                            {
                                AccountNumber = "111",
                                Amount = 10.00m,
                                Currency = "USD",
                                Metadata = new TransferMetadata { CorrelationId = "C1" },
                                Note = "item1"
                            },
                            new TransferRequest
                            {
                                AccountNumber = "222",
                                Amount = 20.00m,
                                Currency = "EUR",
                                Metadata = null,
                                Note = null
                            }
                        ]
                    });

                    Assert.Equal("BATCH-WCF", batchResult.BatchId);
                    Assert.Equal(2, batchResult.Results.Count);
                    Assert.Equal("TX-BATCH-WCF-001", batchResult.Results[0].TransactionId);
                    Assert.Equal("TX-BATCH-WCF-002", batchResult.Results[1].TransactionId);

                    FaultException clientFault = await Assert.ThrowsAsync<FaultException>(() => paymentClient.TransferAsync(new TransferRequest
                    {
                        AccountNumber = "123456",
                        Amount = -5.00m,
                        Currency = "USD"
                    }));

                    Assert.Equal("Client", clientFault.Code.Name);
                    Assert.Equal("Payment validation failed.", clientFault.Message);

                    FaultException receiverFault = await Assert.ThrowsAsync<FaultException>(() => paymentClient.TransferAsync(new TransferRequest
                    {
                        AccountNumber = "FAULT-RECEIVER",
                        Amount = 10.00m,
                        Currency = "USD"
                    }));

                    Assert.Equal("Server", receiverFault.Code.Name);
                    Assert.DoesNotContain("SECRET_PAYMENT_SWITCH_PASSWORD", receiverFault.Message, StringComparison.Ordinal);
                }
                finally
                {
                    CloseChannel(paymentClient);
                }
            }
        }

        [Fact]
        public async Task Soap12HttpBinding_WcfClient_EchoTransferAndFaults_Succeeds()
        {
            await using CompatibilityServerHost host = await CompatibilityServerHost.StartAsync();

            CustomBinding soap12Binding = new CustomBinding(
                new TextMessageEncodingBindingElement(MessageVersion.Soap12, Encoding.UTF8),
                new HttpTransportBindingElement());

            using (ChannelFactory<IWcfEchoClient> echo12Factory = new ChannelFactory<IWcfEchoClient>(
                soap12Binding,
                new EndpointAddress(new Uri(host.BaseUri, "/soap12/echo-dcs.svc"))))
            {
                IWcfEchoClient echo12Client = echo12Factory.CreateChannel();
                try
                {
                    Assert.Equal("soap12-wcf-🚀", await echo12Client.EchoStringAsync("soap12-wcf-🚀"));
                }
                finally
                {
                    CloseChannel(echo12Client);
                }
            }

            using (ChannelFactory<IWcfPaymentClient> payment12Factory = new ChannelFactory<IWcfPaymentClient>(
                soap12Binding,
                new EndpointAddress(new Uri(host.BaseUri, "/soap12/payment-dcs-urn.svc"))))
            {
                IWcfPaymentClient payment12Client = payment12Factory.CreateChannel();
                try
                {
                    TransferResult result = await payment12Client.TransferAsync(new TransferRequest
                    {
                        AccountNumber = "999888",
                        Amount = 75.25m,
                        Currency = "GBP",
                        Metadata = new TransferMetadata { CorrelationId = "SOAP12-CORR" },
                        Note = "WCF SOAP 1.2"
                    });

                    Assert.Equal("TX-001", result.TransactionId);
                    Assert.Equal(PaymentStatus.Completed, result.Status);

                    FaultException serverFault = await Assert.ThrowsAsync<FaultException>(() => payment12Client.TransferAsync(new TransferRequest
                    {
                        AccountNumber = "FAULT-RECEIVER",
                        Amount = 10.00m,
                        Currency = "GBP"
                    }));

                    Assert.Equal("Receiver", serverFault.Code.Name);
                    Assert.DoesNotContain("SECRET_PAYMENT_SWITCH_PASSWORD", serverFault.Message, StringComparison.Ordinal);
                }
                finally
                {
                    CloseChannel(payment12Client);
                }
            }
        }

        #endregion

        #region Private Methods

        private static void CloseChannel(object channel)
        {
            if (channel is ICommunicationObject communicationObject)
            {
                if (communicationObject.State == CommunicationState.Faulted)
                {
                    communicationObject.Abort();
                }
                else
                {
                    communicationObject.Close();
                }
            }
        }

        #endregion

    }

}
