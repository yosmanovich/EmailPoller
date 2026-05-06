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

## SMTP Throttling & Rate Limiting

The application includes built-in SMTP throttling protection to handle rate limits imposed by SMTP relays (e.g., Azure Communication Services limits to ~30 emails/minute).

### How Throttling Works

When an SMTP relay returns a rate-limiting error, the throttle service activates and implements an exponential backoff strategy:

1. **Throttle Detection** - Recognizes common rate-limiting responses:
   - SMTP error codes: `421`, `452`
   - Enhanced status codes: `4.5.127`
   - Message patterns: "rate limit", "throttl", "too many", "excessive"

2. **Exponential Backoff** - When throttling is detected:
   - Initial delay: **30 seconds**
   - Each subsequent throttle doubles the delay
   - Maximum delay cap: **15 minutes**

3. **Recovery Behavior**:
   - After **10 consecutive successful sends**, the backoff level decreases by one
   - Inter-message delays are added during recovery to prevent re-triggering limits

4. **Function Behavior During Throttling**:
   - **SQL Trigger**: Skips processing and returns immediately (message remains in queue)
   - **Timer Trigger**: Skips the entire batch run
   - Both functions log when throttling causes them to skip processing

### Throttle Levels & Timing

| Level | Delay Before Next Send | After Recovery (10 successes) |
|-------|------------------------|-------------------------------|
| 0 | No delay (normal) | - |
| 1 | 30 seconds | Returns to Level 0 |
| 2 | 60 seconds | Returns to Level 1 |
| 3 | 120 seconds (2 min) | Returns to Level 2 |
| 4 | 240 seconds (4 min) | Returns to Level 3 |
| 5 | 480 seconds (8 min) | Returns to Level 4 |
| 6+ | 900 seconds (15 min max) | Returns to Level 5 |

### Azure Communication Services (ACS) Considerations

ACS SMTP relay has specific rate limiting behavior:
- **Rate limit**: ~30 emails per minute
- **Cooldown period**: Rate limits typically reset after **1 hour** of reduced activity
- The throttle service works with this by reducing send frequency when limits are hit

### Architecture

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  EmailQueue     │────▶│  Trigger         │────▶│  SmtpThrottle   │
│  Trigger/Timer  │     │  Functions       │     │  Service        │
└─────────────────┘     └────────┬─────────┘     └────────┬────────┘
                                 │                        │
                                 ▼                        ▼
                        ┌──────────────────┐     ┌─────────────────┐
                        │  EmailService    │────▶│  SMTP Relay     │
                        │  (detects        │     │  (ACS, etc.)    │
                        │   throttling)    │     └─────────────────┘
                        └──────────────────┘
```

**Components:**
- **SmtpThrottleService** - Singleton service tracking throttle state across function invocations
- **SmtpThrottlingException** - Custom exception thrown when throttling is detected
- **EmailService** - Detects throttling responses and throws `SmtpThrottlingException`
- **Trigger Functions** - Check throttle state before processing, record outcomes

### Monitoring Throttling

Monitor throttling via Application Insights or log queries:

```kusto
// Find throttling events
traces
| where message contains "throttl" or message contains "rate limit"
| order by timestamp desc
| take 100

// Check for skipped processing due to throttling
traces
| where message contains "currently throttled"
| summarize count() by bin(timestamp, 1h)
```

## Azure Monitor Alerts

This application integrates with **Application Insights** for telemetry and logging. You can configure Azure Monitor alert rules to receive notifications when issues occur—no code changes required.

### Setting Up Alert Rules

1. Navigate to your **Application Insights** resource in the Azure Portal
2. Go to **Alerts** → **Create** → **Alert rule**
3. Configure the alert condition (see examples below)
4. Create or select an **Action Group** to define notification recipients

### Recommended Alert Conditions

#### 1. Email Send Failures

Alert when emails fail to send:

```kusto
traces
| where message contains "Failed to send email" or message contains "SmtpException"
| where timestamp > ago(5m)
```

- **Condition:** Custom log search
- **Threshold:** Greater than 0
- **Frequency:** Every 5 minutes
- **Severity:** Warning or Error

#### 2. SMTP Throttling Detection

Alert when SMTP rate limiting is triggered:

```kusto
traces
| where message contains "SMTP throttling activated" or message contains "throttle level"
| where timestamp > ago(15m)
```

- **Condition:** Custom log search
- **Threshold:** Greater than 0
- **Frequency:** Every 15 minutes
- **Severity:** Warning

#### 3. High Failure Rate

Alert when failure rate exceeds a percentage threshold:

```kusto
traces
| where timestamp > ago(30m)
| extend isFailure = message contains "Failed" or message contains "Error"
| summarize totalCount = count(), failureCount = countif(isFailure)
| extend failureRate = (failureCount * 100.0) / totalCount
| where failureRate > 10
```

- **Threshold:** Greater than 0 (query self-filters at 10% failure rate)
- **Frequency:** Every 30 minutes

#### 4. Function Execution Failures

Alert on function invocation exceptions:

```kusto
exceptions
| where timestamp > ago(5m)
| where cloud_RoleName contains "SMTPPoller"
```

Or use the built-in metric:
- **Signal:** `Failed requests`
- **Threshold:** Greater than 0
- **Frequency:** Every 5 minutes

#### 5. Email Queue Backlog

Alert when too many emails remain pending (requires custom telemetry or database query integration):

```kusto
traces
| where message contains "Found" and message contains "pending emails"
| parse message with * "Found " pendingCount:int " pending emails" *
| where pendingCount > 500
| where timestamp > ago(15m)
```

### Action Groups

Action groups define who gets notified and how. Create one at **Azure Monitor** → **Alerts** → **Action groups** → **Create**.

**Notification Types:**
| Type | Use Case |
|------|----------|
| **Email** | Primary notifications to operations team |
| **SMS** | Critical alerts requiring immediate attention |
| **Azure mobile app** | Push notifications to on-call personnel |
| **Voice call** | Urgent escalation for P1 incidents |
| **Webhook** | Integration with external systems (PagerDuty, Slack, etc.) |
| **Logic App** | Complex workflows (Teams messages, ticket creation, etc.) |
| **Azure Function** | Custom automated remediation |

### Example: Teams Notification via Logic App

1. Create a Logic App with an HTTP trigger
2. Add a "Post message to Teams" action
3. Configure the webhook URL as an Action Group action

### Alert Severity Guidelines

| Severity | When to Use | Example |
|----------|-------------|---------|
| **Sev 0 - Critical** | Complete service outage | All functions failing |
| **Sev 1 - Error** | Significant impact | SMTP relay unreachable |
| **Sev 2 - Warning** | Potential issues | Throttling activated, high retry rate |
| **Sev 3 - Informational** | Operational awareness | Unusual volume patterns |

### Cost Considerations

- **Log-based alerts:** Charged per evaluation (minimize frequency for non-critical alerts)
- **Metric alerts:** Generally lower cost than log alerts
- **Action group notifications:** SMS and voice calls incur per-notification costs

**Tip:** Start with longer evaluation frequencies (15-30 min) for warning-level alerts and reserve 5-minute frequencies for critical alerts.

### Quick Setup via Azure CLI

```bash
# Create an action group
az monitor action-group create \
  --resource-group <your-rg> \
  --name "EmailPollerAlerts" \
  --short-name "EmailAlert" \
  --email-receiver name="OpsTeam" email="ops@example.com"

# Create a log alert rule for failures
az monitor scheduled-query create \
  --resource-group <your-rg> \
  --name "EmailSendFailures" \
  --scopes "/subscriptions/<sub-id>/resourceGroups/<your-rg>/providers/microsoft.insights/components/<app-insights-name>" \
  --condition "count 'traces | where message contains \"Failed to send email\"' > 0" \
  --evaluation-frequency 5m \
  --window-size 5m \
  --severity 2 \
  --action-groups "/subscriptions/<sub-id>/resourceGroups/<your-rg>/providers/microsoft.insights/actionGroups/EmailPollerAlerts"
```

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
│   │   ├── EmailQueueRepository.cs      # Stored procedure calls
│   │   ├── ISmtpThrottleService.cs      # Throttle service interface
│   │   ├── SmtpThrottleService.cs       # Exponential backoff throttling
│   │   └── SmtpThrottlingException.cs   # Throttle detection exception
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
│   │   ├── EmailQueueRepositoryTests.cs # Repository unit tests
│   │   ├── SmtpThrottleServiceTests.cs  # Throttle service unit tests
│   │   └── SmtpThrottlingExceptionTests.cs # Throttle exception tests
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
- **EmailQueueTriggerTests** - SQL trigger function tests (insert/update/delete handling, error scenarios, throttling behavior)
- **EmailQueueTimerTriggerTests** - Timer trigger function tests (batch processing, configuration, error handling, throttling behavior)
- **EmailServiceTests** - SMTP service tests (email formatting, SSL, authentication)
- **EmailQueueRepositoryTests** - Repository tests (stored procedure calls, connection handling)
- **SmtpThrottleServiceTests** - Throttle service tests (backoff timing, recovery, state management)
- **SmtpThrottlingExceptionTests** - Throttle detection tests (error pattern recognition)

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
