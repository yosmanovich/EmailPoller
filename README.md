# SMTPPoller - Email Queue Processor

Azure Functions that monitor an EmailQueue table and send emails via SMTP relay. Provides both real-time SQL trigger processing and scheduled timer-based backup polling.

## How It Works

### SQL Trigger Function (EmailQueueTrigger)
1. New email records inserted into the `EmailQueue` table trigger the function via SQL Change Tracking
2. Function sends the email via configured SMTP relay
3. On success: calls `dbo.EmailQueue_Success` to mark the record as "Sent"
4. On failure: calls `dbo.EmailQueue_Failure` to increment retry count and update status

### Timer Trigger Function (EmailQueueTimerTrigger)
1. Runs on a configurable schedule (default: every 5 minutes)
2. Calls `dbo.EmailQueue_ClaimBatch` to atomically fetch and lock pending emails
3. Processes each email via SMTP relay
4. Acts as a backup mechanism for emails that weren't processed by the SQL trigger

## Configuration

### Connection Strings (Azure Portal → Configuration → Connection Strings)

| Name | Type | Description |
|------|------|-------------|
| `SqlConnectionString` | SQLAzure | Connection string to your Azure SQL database |

**Example (Managed Identity):**
```
Server=your-server.database.windows.net,1433;Database=your-database;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;
```

### Application Settings (Azure Portal → Configuration → Application Settings)

#### SMTP Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `SmtpHost` | SMTP relay server hostname | (required) |
| `SmtpPort` | SMTP port | 587 |
| `SmtpEnableSsl` | Enable SSL/TLS | true |
| `SmtpUsername` | SMTP username (for authenticated relay) | (empty) |
| `SmtpPassword` | SMTP password (for authenticated relay) | (empty) |
| `SmtpDefaultFromAddress` | Default from address | (required) |
| `SmtpTimeoutMs` | SMTP timeout in milliseconds | 30000 |

**Azure Communication Services SMTP Example:**
```
SmtpHost=smtp.azurecomm.net
SmtpPort=587
SmtpEnableSsl=true
SmtpUsername=<ResourceName>.<EntraAppId>.<EntraTenantId>
SmtpPassword=<EntraClientSecret>
SmtpDefaultFromAddress=DoNotReply@<AcsDomainId>.azurecomm.net
```

#### Timer Trigger Settings

| Setting | Description | Default |
|---------|-------------|---------|
| `EmailQueueTimerSchedule` | CRON expression for timer trigger | `0 */5 * * * *` (every 5 min) |
| `EmailQueueTimerMaxRecords` | Maximum records to process per run | 100 |

**CRON Format:** `{second} {minute} {hour} {day} {month} {day-of-week}`

Examples:
- `0 */5 * * * *` - Every 5 minutes
- `0 */1 * * * *` - Every minute
- `0 0 * * * *` - Every hour

## Prerequisites

### 1. SQL Server Database Setup

A comprehensive SQL deployment script is provided in `Database Scripts/SmtpPollerDeployment.sql`. This script creates:

- **EmailQueue table** with all required columns and constraints
- **Change Tracking** enabled on the database and table
- **Stored Procedures:**
  - `dbo.EmailQueue_Create` - Add new emails to the queue
  - `dbo.EmailQueue_Processing` - Mark a single email as processing
  - `dbo.EmailQueue_ClaimBatch` - Atomically fetch and lock pending emails (used by timer trigger)
  - `dbo.EmailQueue_Success` - Mark email as sent
  - `dbo.EmailQueue_Failure` - Handle failures with retry logic
  - `dbo.EmailQueue_Cancel` - Cancel a pending email
  - `dbo.EmailQueue_Retry` - Reset a failed email for retry
  - `dbo.EmailQueue_Purge` - Clean up old records
  - `dbo.EmailQueue_ResetStuck` - Reset emails stuck in Processing state

**To deploy:**
1. Open `Database Scripts/SmtpPollerDeployment.sql`
2. Replace `[YourDatabase]` with your actual database name
3. Execute the script against your Azure SQL database

### 2. Database Permissions

If using Managed Identity, grant the Function App's identity access to the database. See `Database Scripts/Permissions.sql` for required permissions.

## Azure Function App Hosting Plan Considerations

### SQL Trigger Support by Plan Type

**⚠️ IMPORTANT:** SQL triggers have specific hosting plan requirements. Not all Azure Functions hosting plans support SQL triggers properly.

| Plan Type | SQL Trigger Support | Notes |
|-----------|---------------------|-------|
| **Consumption** | ✅ Supported | Standard serverless plan |
| **Premium (EP1-EP3)** | ✅ Supported | Recommended for production |
| **Dedicated (Basic, Standard)** | ✅ Supported | Always-on capability |
| **Flex Consumption** | ❌ NOT Supported | Uses `NoOpListener` - SQL triggers will never fire |

### Why Flex Consumption Doesn't Work

Flex Consumption plans use **target-based scaling** which implements a `NoOpListener` for SQL triggers instead of the actual SQL trigger listener. This means:
- The function will deploy successfully
- The function will appear in the Azure Portal
- The Leases table (`az_func.Leases`) will **never be created**
- SQL triggers will **never fire**
- No error messages will indicate the issue - it simply doesn't work

### Recommended Hosting Plans

For production workloads using SQL triggers:

1. **Premium Plan (EP1)** - Best for variable load with cold-start protection
2. **Basic B1 App Service Plan** - Cost-effective for steady workloads, supports Always-On
3. **Standard Consumption** - Good for intermittent workloads with auto-scale

## Managed Identity Configuration

### Storage Account Access (Required)

Azure Functions require access to a storage account for internal operations (triggers, bindings, checkpoints). If your storage account disables shared key access (recommended security practice), you must configure managed identity authentication.

#### Step 1: Enable System-Assigned Managed Identity

```bash
az functionapp identity assign --name <your-function-app> --resource-group <your-rg>
```

#### Step 2: Grant Storage RBAC Roles

The function app's managed identity needs these roles on the storage account:

```bash
# Get the principal ID
PRINCIPAL_ID=$(az functionapp identity show --name <your-function-app> \
    --resource-group <your-rg> --query principalId -o tsv)

STORAGE_ID="/subscriptions/<subscription-id>/resourceGroups/<your-rg>/providers/Microsoft.Storage/storageAccounts/<storage-account>"

# Storage Blob Data Owner - for blob storage operations
az role assignment create --assignee $PRINCIPAL_ID \
    --role "Storage Blob Data Owner" --scope $STORAGE_ID

# Storage Queue Data Contributor - for queue operations
az role assignment create --assignee $PRINCIPAL_ID \
    --role "Storage Queue Data Contributor" --scope $STORAGE_ID

# Storage Table Data Contributor - for table operations (checkpoints, leases)
az role assignment create --assignee $PRINCIPAL_ID \
    --role "Storage Table Data Contributor" --scope $STORAGE_ID
```

#### Step 3: Configure App Settings for Identity-Based Storage

Replace the connection string with identity-based configuration:

```bash
# Remove the old connection string (if present)
az functionapp config appsettings delete --name <your-function-app> \
    --resource-group <your-rg> --setting-names AzureWebJobsStorage

# Add identity-based settings
az functionapp config appsettings set --name <your-function-app> \
    --resource-group <your-rg> \
    --settings "AzureWebJobsStorage__accountName=<storage-account>" \
               "AzureWebJobsStorage__credential=managedidentity"
```

#### Troubleshooting Storage Access

If you see errors like `KeyBasedAuthenticationNotPermitted`:
1. Verify the managed identity is enabled
2. Check that all three RBAC roles are assigned
3. Wait a few minutes for role assignments to propagate
4. Restart the function app

### SQL Database Access

For SQL database access via managed identity, see [Database Scripts/Permissions.sql](Database%20Scripts/Permissions.sql).

```sql
-- Create user for the function app's system-assigned managed identity
CREATE USER [<your-function-app>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [<your-function-app>];
```

The SQL connection string should use:
```
Server=<server>.database.windows.net,1433;Database=<database>;Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;
```

## Project Structure

```
SMTPPoller/
├── SMTPPollerFunction/
│   ├── Functions/
│   │   ├── EmailQueueTrigger.cs         # SQL trigger function
│   │   └── EmailQueueTimerTrigger.cs    # Timer trigger function
│   ├── Models/
│   │   └── EmailQueueRecord.cs          # EmailQueue table model
│   ├── Services/
│   │   ├── IEmailService.cs             # Email service interface
│   │   ├── EmailService.cs              # SMTP email implementation
│   │   ├── IEmailQueueRepository.cs     # Repository interface
│   │   └── EmailQueueRepository.cs      # Stored procedure calls
│   ├── Program.cs                       # Host and DI configuration
│   ├── SMTPPoller.csproj               # Project dependencies
│   ├── host.json                        # Azure Functions config
│   └── local.settings.json             # Local settings (not published)
├── SMTPPoller.Tests/
│   ├── Functions/
│   │   ├── EmailQueueTriggerTests.cs    # SQL trigger unit tests
│   │   └── EmailQueueTimerTriggerTests.cs # Timer trigger unit tests
│   ├── Services/
│   │   ├── EmailServiceTests.cs         # Email service unit tests
│   │   └── EmailQueueRepositoryTests.cs # Repository unit tests
│   └── Helpers/
│       └── EmailQueueRecordFactory.cs   # Test data factory
├── Database Scripts/
│   ├── SmtpPollerDeployment.sql        # Full database deployment script
│   └── Permissions.sql                  # Database permissions script
└── README.md
```

## Unit Tests

The solution includes comprehensive unit tests using xUnit and Moq:

```bash
# Run all tests
dotnet test SMTPPoller.sln

# Run with verbose output
dotnet test SMTPPoller.sln --verbosity normal

# Run specific test class
dotnet test --filter "FullyQualifiedName~EmailQueueTriggerTests"
```

**Test Coverage:**
- **EmailQueueTriggerTests** - SQL trigger function tests (insert/update/delete handling, error scenarios)
- **EmailQueueTimerTriggerTests** - Timer trigger function tests (batch processing, configuration, error handling)
- **EmailServiceTests** - SMTP service tests (email formatting, SSL, authentication)
- **EmailQueueRepositoryTests** - Repository tests (stored procedure calls, connection handling)

## Local Development

1. Copy `local.settings.json.example` to `local.settings.json` (if provided) or update `local.settings.json`:
   ```json
   {
     "IsEncrypted": false,
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
       "SmtpHost": "your-smtp-host",
       "SmtpPort": "587",
       "SmtpEnableSsl": "true",
       "SmtpUsername": "",
       "SmtpPassword": "",
       "SmtpDefaultFromAddress": "noreply@example.com",
       "SmtpTimeoutMs": "30000",
       "EmailQueueTimerSchedule": "0 */5 * * * *",
       "EmailQueueTimerMaxRecords": "100"
     },
     "ConnectionStrings": {
       "SqlConnectionString": "Server=localhost;Database=EmailQueue;Trusted_Connection=True;"
     }
   }
   ```

2. Ensure the database has Change Tracking enabled and stored procedures deployed

3. Run the function locally:
   ```bash
   cd SMTPPollerFunction
   func start
   ```

## Deployment

### Deploy using Azure Functions Core Tools

```bash
cd SMTPPollerFunction
func azure functionapp publish <YourFunctionAppName> --dotnet-isolated
```

### Deploy using VS Code

1. Install the Azure Functions extension
2. Right-click on `SMTPPollerFunction` folder
3. Select "Deploy to Function App..."

### Configure Azure Portal

1. **Connection Strings:** Go to Function App → Configuration → Connection Strings
   - Add `SqlConnectionString` (Type: SQLAzure)

2. **Application Settings:** Go to Function App → Configuration → Application Settings
   - Add all SMTP and Timer settings from the Configuration section above

3. **Managed Identity:** Go to Function App → Identity → System assigned
   - Enable system-assigned managed identity
   - Grant the identity access to your Azure SQL database
