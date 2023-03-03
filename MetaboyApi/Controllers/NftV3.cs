using Azure.Messaging.ServiceBus;
using Dapper;
using MetaboyApi.Models;
using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Text.Json;

namespace MetaboyApi.Controllers
{
    [ApiController]
    [Route("api/nft")]
    [ApiVersion("3.0")]
    public class NftV3Controller : ControllerBase
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

        public NftV3Controller(IConfiguration config)
        {
            _config = config;
            var clientOptions = new ServiceBusClientOptions() { TransportType = ServiceBusTransportType.AmqpWebSockets };
            AzureServiceBusClient = new ServiceBusClient(_config.GetValue<string>("AzureServiceBusConnectionString"), clientOptions);
            AzureServiceBusSender = AzureServiceBusClient.CreateSender(AzureServiceBusQueueName);
            AzureSqlServerConnectionString = _config.GetValue<string>("AzureSqlConnectionString");
        }

        /// <summary>
        /// Adds a claim to transfer queue that has been added to allowlist
        /// </summary>
        /// <param name="nftRecievers"></param>
        /// <returns>If the claim was added</returns>
        /// <remarks>
        /// Sample request:
        ///
        ///     POST /claim
        ///     {
        ///         "Address" : "0xd2af47d16b14d6579884eec8a86ce7bf9fdb05bb",
        ///         "NftData" : "0x2b8c1d8c554dfa85b3dfaaa580ffeb616adbc17364eff8155b33cf1f70bd9ca3",
        ///     }
        /// </remarks>
        /// <response code="200">If a claim is added</response>
        /// <response code="400">If something is wrong with the request</response>
        [HttpPost]
        [Route("claim")]
        public async Task<IActionResult> Send(List<NftReciever> nftRecievers) // List<nftReciever> { Address, NftData }
        {
            Console.WriteLine($"[INFO] API Has recieved a claim request for {nftRecievers.Count()} claims.");
            try
            {
                // create a batch 
                using ServiceBusMessageBatch messageBatch = await AzureServiceBusSender.CreateMessageBatchAsync();
                using (SqlConnection db = new System.Data.SqlClient.SqlConnection(AzureSqlServerConnectionString))
                {
                    await db.OpenAsync();
                    foreach(NftReciever nftReciever in nftRecievers)
                    {
                        // Ensure Nft is in the Claimable table
                        var claimableParameters = new { NftData = nftReciever.NftData };
                        var claimableSql = "SELECT * FROM Claimable WHERE NftData = @NftData";
                        var claimableResult = await db.QueryAsync<Claimable>(claimableSql, claimableParameters);
                        if (claimableResult.Count() == 1)
                        {
                            // Obtain valid Claim record
                            var allowListParameters = new { Address = nftReciever.Address, NftData = nftReciever.NftData };
                            var allowListSql = "SELECT * FROM AllowList WHERE nftdata = @NftData and address = @Address AND Amount > 0";
                            var allowListResult = await db.QueryAsync<AllowableClaim>(allowListSql, allowListParameters);
                            if (allowListResult.Count() == 1)
                            {
                                // Pass along valid AllowList (Address, NftName, Amount) to MessageProcessor
                                if (!messageBatch.TryAddMessage(new ServiceBusMessage($"{JsonSerializer.Serialize(nftReciever)}")))
                                {
                                    // if it is too large for the batch
                                    throw new Exception($"[ERROR] The message is too large to fit in the batch.");
                                }
                                else
                                {
                                    Console.WriteLine($"[CLAIM SUBMITTED] Claim passed to Service Bus - Address: {nftReciever.Address} NftData: {nftReciever.NftData}");
                                }
                                
                            }
                            else
                            {
                                return BadRequest($"[REJECTED CLAIM] Record not found in AllowList - Address: {nftReciever.Address} NftData: {nftReciever.NftData}");
                            }
                        }
                        else
                        {
                            return BadRequest($"[REJECTED CLAIM] NftData not found in Claimable: {nftReciever.NftData}");
                        }
                    }
                    await db.CloseAsync();
 
                }

                if (messageBatch.Count > 0)
                {
                    try
                    {
                        await AzureServiceBusSender.SendMessagesAsync(messageBatch);
                        Console.WriteLine($"A batch of messages has been published to the queue.");
                        // DEBUG
                        Console.WriteLine($"[DEBUG INFO] Passed {messageBatch.Count} to Service Bus");
                        foreach (NftReciever nftreciever in nftRecievers)
                        {
                            Console.WriteLine($"[DEBUG INFO] Passed to bus : {nftreciever.Address} - {nftreciever.NftData}");
                        }
                    }
                    finally
                    {
                        // Calling DisposeAsync on client types is required to ensure that network
                        // resources and other unmanaged objects are properly cleaned up.
                        await AzureServiceBusSender.DisposeAsync();
                        await AzureServiceBusClient.DisposeAsync();

                    }
                    return Ok($"Added {messageBatch.Count} entries to Service Bus for {nftRecievers.First()}");
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
        ///     GET /redeemable?address=0xd2af47d16b14d6579884eec8a86ce7bf9fdb05bb
        /// </remarks>
        /// <response code="200">If an address has at least 1 claim that is redeemable</response>
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
                    // Check AllowList Table with matching Address
                    var canClaimParameters = new { Address = address, MinimumAmount = 0 };

                    var canClaimSql = "SELECT * FROM AllowList WHERE Address = @Address AND Amount > @MinimumAmount";
                    var canClaimResult = await db.QueryAsync<AllowableClaim>(canClaimSql, canClaimParameters);
                    
                    if (canClaimResult.Count() > 0)
                    {
                        validStatus = 0; // More than 1 valid Claims with at least 1 Amount found - Proceed
                    }
                    else
                    {
                        validStatus = 1; // No valid Claims found - Bonk!
                    }
                    await db.CloseAsync();

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
                    var canClaimSql = "SELECT * FROM Claimable";
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
