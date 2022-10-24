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
    [Route("api/[controller]")]
    [ApiController]
    public class NftController : ControllerBase
    {
        // connection string to your Service Bus namespace
        static string AzureServiceBusConnectionString = "";

        // name of your Service Bus queue
        static string AzureServiceBusQueuName = "main";

        // the client that owns the connection and can be used to create senders and receivers
        static ServiceBusClient AzureServiceBusClient;

        // the sender used to publish messages to the queue
        static ServiceBusSender AzureServiceBusSender;

        static string AzureSqlServerConnectionString = "";

        private IConfiguration _config;

        public NftController(IConfiguration config)
        {
            _config = config;
            var clientOptions = new ServiceBusClientOptions() { TransportType = ServiceBusTransportType.AmqpWebSockets };
            AzureServiceBusClient = new ServiceBusClient(_config.GetValue<string>("AzureServiceBusConnectionString"), clientOptions);
            AzureServiceBusSender = AzureServiceBusClient.CreateSender(AzureServiceBusQueuName);
            AzureSqlServerConnectionString = _config.GetValue<string>("AzureSqlConnectionString");
        }

        [HttpPost]
        [Route("claim")]
        public async Task<IActionResult> Send(NftReciever nftReciever)
        {
            int? validStatus = null;
            try
            {
                using (IDbConnection db = new SqlConnection(AzureSqlServerConnectionString))
                {
                    db.Open();
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
                                validStatus = 0;
                            }
                            else
                            {
                                validStatus = 3;
                            }
                        }
                        else
                        {
                            validStatus = 2;
                        }
                    }
                    else
                    {
                        validStatus = 1;
                    }
                }

                if (validStatus == 0)
                {
                    try
                    {
                        await AzureServiceBusSender.SendMessageAsync(new ServiceBusMessage($"{JsonSerializer.Serialize(nftReciever)}"));
                        Console.WriteLine($"A batch of messages has been published to the queue.");
                    }
                    finally
                    {
                        // Calling DisposeAsync on client types is required to ensure that network
                        // resources and other unmanaged objects are properly cleaned up.
                        await AzureServiceBusSender.DisposeAsync();
                        await AzureServiceBusClient.DisposeAsync();

                    }
                    return Ok("Request added.");
                }
                else if(validStatus == 1)
                {
                    return BadRequest("Nft not claimable!");
                }
                else if (validStatus == 2)
                {
                    return BadRequest("This address is not in the allow list!");
                }
                else if (validStatus == 3)
                {
                    return BadRequest("This address has already claimed this Nft!");
                }
                else 
                {
                    return BadRequest("Something went wrong...");
                }
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
          

        }
    }
}
