using Azure.Messaging.ServiceBus;
using Dapper;
using MetaboyApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Data.SqlClient;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetaboyApi.Controllers
{
    [ApiController]
    [Route("api/nft")]
    [ApiVersion("2.0")]
    public class NftV2Controller : ControllerBase
    {
        // connection string to your Service Bus namespace
        static string AzureServiceBusConnectionString = "";

        // name of your Service Bus queue
        static string AzureServiceBusQueueName = "main";

        // the client that owns the connection and can be used to create senders and receivers
        static ServiceBusClient AzureServiceBusClient;

        // the sender used to publish messages to the queue
        static ServiceBusSender AzureServiceBusSender;

        static string AzureSqlServerConnectionString = "";

        private IConfiguration _config;

        public NftV2Controller(IConfiguration config)
        {
            _config = config;
            var clientOptions = new ServiceBusClientOptions() { TransportType = ServiceBusTransportType.AmqpWebSockets };
            AzureServiceBusClient = new ServiceBusClient(_config.GetValue<string>("AzureServiceBusConnectionString"), clientOptions);
            AzureServiceBusSender = AzureServiceBusClient.CreateSender(AzureServiceBusQueueName);
            AzureSqlServerConnectionString = _config.GetValue<string>("AzureSqlConnectionString");
        }

        /// <summary>
        /// Adds a claim to transfer queue that has been added to allowlist and does not have a matching address/nftdata record in Claimed table
        /// </summary>
        /// <param name="nftRecievers"></param>
        /// <returns>If the claim was added</returns>
        /// <remarks>
        /// Sample request:
        ///
        ///     POST /claim
        ///     {
        ///         "Address" : "0x36Cd6b3b9329c04df55d55D41C257a5fdD387ACd",
        ///         "NftData" : "0x14e15ad24d034f0883e38bcf95a723244a9a22e17d47eb34aa2b91220be0adC4"
        ///     }
        /// </remarks>
        /// <response code="200">If a claim is added</response>
        /// <response code="400">If something is wrong with the request</response>
        [HttpPost]
        [Route("claim")]
        public async Task<IActionResult> Send(List<NftReciever> nftRecievers)
        {
            try
            {
                // create a batch 
                using ServiceBusMessageBatch messageBatch = await AzureServiceBusSender.CreateMessageBatchAsync();
                using (SqlConnection db = new System.Data.SqlClient.SqlConnection(AzureSqlServerConnectionString))
                {
                    await db.OpenAsync();
                    foreach(NftReciever nftReciever in nftRecievers)
                    {
                        var claimableParameters = new { NftData = nftReciever.NftData };
                        var claimableSql = "select * from claimable where nftdata = @NftData";
                        var claimableResult = await db.QueryAsync<Claimable>(claimableSql, claimableParameters);
                        if (claimableResult.Count() == 1)
                        {
                            var allowListParameters = new { Address = nftReciever.Address, NftData = nftReciever.NftData };
                            var allowListSql = "select * from allowlist where nftdata = @NftData and address = @Address";
                            var allowListResult = await db.QueryAsync<AllowList>(allowListSql, allowListParameters);
                            if (allowListResult.Count() == 1)
                            {
                                var claimedListParameters = new { Address = nftReciever.Address, NftData = nftReciever.NftData };
                                var claimedListSql = "select * from claimed where nftdata = @NftData and address = @Address";
                                var claimedListResult = await db.QueryAsync<Claimed>(claimedListSql, claimedListParameters);
                                if (claimedListResult.Count() == 0)
                                {
                                    // try adding a message to the batch
                                    if (!messageBatch.TryAddMessage(new ServiceBusMessage($"{JsonSerializer.Serialize(nftReciever)}")))
                                    {
                                        // if it is too large for the batch
                                        throw new Exception($"The message is too large to fit in the batch.");
                                    }
                                }
                            }
                        }
                    }
 
                }

                if (messageBatch.Count > 0)
                {
                    try
                    {
                        await AzureServiceBusSender.SendMessagesAsync(messageBatch);
                        Console.WriteLine($"A batch of messages has been published to the queue.");
                    }
                    finally
                    {
                        // Calling DisposeAsync on client types is required to ensure that network
                        // resources and other unmanaged objects are properly cleaned up.
                        await AzureServiceBusSender.DisposeAsync();
                        await AzureServiceBusClient.DisposeAsync();

                    }
                    return Ok("Request added...");
                }
                else 
                {
                    return BadRequest("Request not added...");
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Checks if user is able to redeem a claim
        /// </summary>
        /// <param name="address"></param>
        /// <returns>If the user can redeem a claim</returns>
        /// <remarks>
        /// Sample request:
        ///
        ///     GET /redeemable?address=0x36Cd6b3b9329c04df55d55D41C257a5fdD387ACd&amp;nftData=0x14e15ad24d034f0883e38bcf95a723244a9a22e17d47eb34aa2b91220be0adC4
        /// </remarks>
        /// <response code="200">If a claim is redeemable</response>
        /// <response code="400">If something is wrong with the request</response>
        [HttpGet]
        [Route("redeemable")]
        public async Task<IActionResult> Redeemable(string address)
        {
            int? validStatus = null;
            try
            {
                using (SqlConnection db = new System.Data.SqlClient.SqlConnection(AzureSqlServerConnectionString))
                {
                    await db.OpenAsync();
                    var canClaim = new { Address = address};
                    var canClaimSql = "select case when b.claimeddate is null then 'True' else 'False' End as Redeemable, a.nftdata, a.Amount from allowlist a left join claimed b on a.address = b.address and a.nftdata = b.nftdata where a.address = @Address and a.nftdata in (select nftdata from claimable) and b.claimeddate is null";
                    var canClaimResult = await db.QueryAsync<CanClaimV2>(canClaimSql, canClaim);
                    if (canClaimResult.Count() > 0)
                    {
                        validStatus = 0;
                    }
                    else
                    {
                        validStatus = 1;
                    }

                    if (validStatus == 0)
                    {
                        return Ok(canClaimResult.ToList());
                    }
                    else
                    {
                        return BadRequest(canClaimResult.ToList());
                    }
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        /// <summary>
        /// Displays all claimable NFTs
        /// </summary>
        /// <returns>The claimable NFTs</returns>
        /// <remarks>
        /// Sample request:
        ///     GET /claimable
        /// </remarks>
        /// <response code="200">The list of claimable NFTs</response>
        /// <response code="400">If something is wrong with the request</response>
        [HttpGet]
        [Route("claimable")]
        public async Task<IActionResult> Claimable()
        {
            try
            {
                using (SqlConnection db = new System.Data.SqlClient.SqlConnection(AzureSqlServerConnectionString))
                {
                    await db.OpenAsync();
                    var canClaimSql = "select * from claimable";
                    var canClaimResult = await db.QueryAsync<Claimable>(canClaimSql);
                    return Ok(canClaimResult);
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
