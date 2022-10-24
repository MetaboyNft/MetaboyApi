# MetaboyApi
.NET 6 project for the MetaBoy API. 

## Nft Claim Endpoint
Nft Claims endpoint is available at https://localhost:7237/api/nft/claim (Local) or https://metaboyapi.azurewebsites.net/api/nft/claim (Production) with a POST of the following JSON

```json
{
    "Address" : "0x1C65331556cff08bb06c56fbb68fb0d1d2194f8a",
    "NftData" : "0x14e15ad24d034f0883e38bcf95a723244a9a22e17d47eb34aa2b91220be0adc4"
}
```

This endpoint will check if the nft data exists in the SQL database,if the address has been whitelisted to claim and hasn't claimed before. 

If it passes these checks it will add a message to the Azure Service Bus queue which will then be processed by the [Message Processor](https://github.com/MetaboyNft/MetaboyApiMessageProcessor) for claiming the NFT.

## Database Setup
The database should have the following tables:

![image](https://user-images.githubusercontent.com/5258063/197443450-b191d9a9-8573-4597-ab83-734c4775d8fc.png)

## Setup Local
To host locally create an appsettings.json file in the root directory with the following values, replacing with your own values: 

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "AzureServiceBusConnectionString": "",
  "AzureSqlConnectionString": ""
}
```

## Setup Azure
Create two appsetting variables on your Azure deployment, one for the AzureServiceBusConnectionString and one for the AzureSqlConnectionString.