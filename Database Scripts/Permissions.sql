/*
-- This script creates a database user for the Azure Function identity and grants it the necessary permissions
-- to read and write data, execute stored procedures, and view change tracking information in the dbo schema.
*/
IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'az_func')
    EXEC('CREATE SCHEMA az_func');

CREATE USER [azurefunctionidentity] FROM EXTERNAL PROVIDER;

-- Grant execute on stored procedures
GRANT EXECUTE ON [dbo].[EmailQueue_Processing] TO [azurefunctionidentity];
GRANT EXECUTE ON [dbo].[EmailQueue_Success] TO [azurefunctionidentity];
GRANT EXECUTE ON [dbo].[EmailQueue_Failure] TO [azurefunctionidentity];
GRANT EXECUTE ON [dbo].[EmailQueue_ClaimBatch] TO [azurefunctionidentity];

-- Grant VIEW CHANGE TRACKING permission for the SQL trigger
GRANT VIEW CHANGE TRACKING ON SCHEMA::dbo TO [azurefunctionidentity];
GRANT SELECT ON [dbo].[EmailQueue] TO [azurefunctionidentity];

-- Grant permissions to create and manage the Leases table
GRANT CREATE TABLE TO [azurefunctionidentity];
GRANT ALTER, SELECT, INSERT, UPDATE, DELETE ON SCHEMA::az_func TO [azurefunctionidentity];

