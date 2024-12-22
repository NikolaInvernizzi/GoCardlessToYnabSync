First quick version of the readme:

# Gocardless to YNAB Sync using Azure Functions & CosmosDB
GoCardlessToYnabSync is project to sync transactions from your bank to YNAB using the GoCarddless API, YNAB API and Azure as host.

### Why:
I started this project because I was using a SaaS tool that did the same as this tool, but it stopped working for me after a week.. 
So I tried to create my personal "version" to sync transactions to YNAB that would costs less than the SaaS tool did while it is also customizable to my needs.

### Limitations
This only syncs **1** bank account to **1** YNAB account.
But it is possible to setup more Azure Functions to sync multiple bank accounts to the same YNAB account or a different once.

## Getting Started
Requirements:
- [.NET8](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Basic C# knownledge: to edit the code to fit your needs and get the correct information from the transactions.
- Basic Azure knownledge: to create resource so optimize cost and such.
- [A YNAB account](https://bankaccountdata.gocardless.com)
- [A GoCardless account](https://app.ynab.com/)

### Happy flow of the sync logic
- Timed Function
  * => trigger GoCardlessSync Function
- GoCardlessSync Function
  * => Retrieve transactions from GoCardless
  * => Add to the CosmosDB
  * => Trigger YnabSync Function
- YnabSync Function
  * => Retrieve transactions that haven't been sent yet from the CosmosDB
  * => Send to YNAB
  * => Send mail to user
(I added the sending of a email when new transactions are added to YNAB because the YNAB APP notifications don't seem to work on my phone)


### Configuration
The URIs are used by the timed Azure function so it can trigger the Azure functions that do the logic
```
  "FunctionUris": {
    "GoCardlessSync": "",
    "YnabSync": ""
  },
```

Smpt configuration to send an email when new transaction are added to YNAB
```
  "Smpt": {
    "Host": "",
    "Port": 0,
    "Email": "",
    "Password": "",
    "SendTo": ""
  },
```

Storage account information to save the Transactions to keep track if they have been sent to YNAB or not.
```
  "CosmosDb": {
    "ConnectionString": "",
    "ContainerRequisitions": "Requisitions",
    "ContainerRequisitionsPartikionKey": "/requisitionId",
    "ContainerTransactions": "Transactions",
    "ContainerTransactionsPartitionKey": "/bookingDate",
    "Database": ""
  },
```
  
Gocardless Account information:

**DaysInPastToRetrieve:** How many days of data to retrieve from GoCardless.  
**SecretId/Secret:** [Get this from GoCardless](https://bankaccountdata.gocardless.com/user-secrets/) by creating a secret.  
**BankId:**  See bottom of readme how to get your institution ID, not sure if this ID can be found another way.

```
  "GoCardless": {
    "DaysInPastToRetrieve": "7",
    "SecretId": "",
    "Secret": "",
    "BankId": ""
  },
```
  
YNAB account information
**Secret:** API Key/Token from YNAB, Create a new personal access token [here](https://app.ynab.com/settings/developer).  
**BudgetId:** the GUID for the budget, this GUID can be found in the URI when you are viewing a budget [https://app.ynab.com/#####-#####-#####-#####-#####/budget](#)  
**AccountName:** The name of the Account to add the transaction too.  
```
  "Ynab": {
    "Secret": "",  // API key
    "BudgetId": "", // Budget ID, can be found in the URI
    "AccountName": ""
  }
```
  
## Resource hosted on Azure
- App Service Plan
- Function App
- Storage Account
- Azure Cosmos DB account

Personally I currently have about a cost of 7 cents/month after using this for 3 months. 
The storage Account is the most costly resource, but I think the Storage/Storage Account could be more efficiently configured for this case.


## Getting your Institution Id
Send a request as below using postman or any other API Client, the secret_id and secret_key are created [here](https://bankaccountdata.gocardless.com/user-secrets/).
- **METHOD**: POST
- **URI**: https://bankaccountdata.gocardless.com/api/v2/token/new/
- **Body**: JSON
  ```
  {
    "secret_id": "####################",
    "secret_key": "############################################################################################################################################"
  }
  ```
This will give you the following JSON:
```
{
    "access": "ey############################################################################################################################################",
    "access_expires": 86400,
    "refresh": "ey############################################################################################################################################",
    "refresh_expires": 2592000
}
```

Using the `access` value, create the following reauest to get a list of all banks of a coutnry that are available in GoCardless and get the institution Id
- **METHOD**: GET
- **URI**: https://bankaccountdata.gocardless.com/api/v2/institutions/?country=**COUNTRY CODE**  _for example: be for Belgium or gb for Great Britain_
- **Headers**: `Authorization` : `Bearer ey############################################################################################################################################`

This will return the following JSON:
```
[
    {
        "id": "XXXX_XXX_XXX",
        "name": "XXXXXX BANK",
        "bic": "XXXXXXXXX",
        "transaction_total_days": "540",
        "countries": [
            "GB"
        ],
        "logo": "https://storage.googleapis.com/xxxxxxxxxxxxxxxxxxx.png",
        "max_access_valid_for_days": "90"
    },
    ...
]
```
What you need is the value for the Id property and use that as the value for the GoCardless BankId configuration property.

