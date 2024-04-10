/////////////////////////////////////////////////////////////////////////////
/// Sample code to insert data into a table:
/////////////////////////////////////////////////////////////////////////////

/*

CREATE SCHEMA [HR];
GO

CREATE TABLE [HR].[Employees]
(
    [EmployeeID] [int] IDENTITY(1,1) NOT NULL
    , [SSN] [char](11) NOT NULL
    , [FirstName] [nvarchar](50) NOT NULL
    , [LastName] [nvarchar](50) NOT NULL
    , [Salary] [money] NOT NULL
) ON [PRIMARY];

Then using either SSMS or PowerShell, 
you will enable Always Encrypted on the SSN and Salary columns:

CREATE TABLE [HR].[Employees](
    [EmployeeID] [int] IDENTITY(1,1) NOT NULL,
    [SSN] [char](11) COLLATE Latin1_General_BIN2 
        ENCRYPTED WITH COLUMN_ENCRYPTION_KEY = [CEK], 
        ENCRYPTION_TYPE = Randomized, 
        ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') 
        NOT NULL,
    [FirstName] [nvarchar](50) NOT NULL,
    [LastName] [nvarchar](50) NOT NULL,
    [Salary] [money] 
        ENCRYPTED WITH COLUMN_ENCRYPTION_KEY = [CEK], 
        ENCRYPTION_TYPE = Randomized, 
        ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') 
        NOT NULL
 ON [PRIMARY]
*/

/////////////////////////////////////////////////////////////////////////////
/// Written by Michael Howard 
/// Azure Data Security Team
/////////////////////////////////////////////////////////////////////////////

using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.AlwaysEncrypted.AzureKeyVaultProvider;
using System.Data;
using System.Diagnostics;
using System.Text;

// Random first and last names
var firstNames = File.ReadAllLines(@".\first-names.txt");
var lastNames = File.ReadAllLines(@".\last-names.txt");

Random rng = new();

// Edit for your Server and Database
string sqlConn = "server=tcp:xxxxxx.database.windows.net; " +
                 "database=ContosoMH; " +
                 "encrypt=true; Column Encryption Setting=Enabled; Attestation Protocol=None;";

// Login to Azure 
(TokenCredential? credential, string? oauth2TokenSql) = LoginToAure();
if (credential is null || oauth2TokenSql is null)
    throw new ArgumentNullException("Unable to login to Azure");

// Login to Azure SQL DB
using SqlConnection conn = new(sqlConn) {
    AccessToken = oauth2TokenSql
};
conn.Open();

// Setup this app so it can use AKV 
RegisterAkvForAe(credential);

// Here we gggoo...
const int _batchSize = 500;
const int _numberOfBatches = 1000;

// This builds a string that looks like this:
// INSERT INTO Employees (LastName, FirstName, Salary, SSN) VALUES 
//  (LastName0, FirstName0, Salary0, SSN0),
//  (LastName1, FirstName1, Salary1, SSN1),
//  (LastName2, FirstName2, Salary2, SSN2),
//  (LastName3, FirstName3, Salary3, SSN3),
//  (LastNameN, FirstNameN, SalaryN, SSNN)
// N is the batch size
StringBuilder sb = new (); 
sb.Append(@"INSERT INTO [HR].[Employees] (LastName, FirstName, Salary, SSN) VALUES ");
for (int i=0; i < _batchSize; i++)
    sb.Append($"(@LastName{i},@FirstName{i},@Salary{i},@SSN{i}), ");

// Remove trailing space and comma
var qryText = sb.ToString().Remove(sb.Length - 2);
long lastTime = 0;

for (int i = 0; i < _numberOfBatches; i++)
{
    Console.Write($"Sending Batch ({_batchSize} rows each): {i+1} of {_numberOfBatches}. Last exec: {lastTime}ms   \r");

    SqlCommand cmd = new(qryText, conn);

    // This code fills in all the numbered params
    // This way we can batch up a bunch of inserts
    for (int j = 0; j < _batchSize; j++)
    {
        SqlParameter minSalaryParam = new($"@Salary{j}", SqlDbType.Money) { Value = rng.Next(50_000, 250_000) };
        cmd.Parameters.Add(minSalaryParam);

        SqlParameter ssn = new($"@SSN{j}", SqlDbType.Char) { Value = $"{rng.Next(100, 1000):000}-{rng.Next(10, 100):00}-{rng.Next(1000, 10000):0000}" };
        cmd.Parameters.Add(ssn);

        SqlParameter fname = new($"@FirstName{j}", SqlDbType.Char) { Value = firstNames[rng.Next(firstNames.Length)] };
        cmd.Parameters.Add(fname);

        SqlParameter lname = new($"@LastName{j}", SqlDbType.Char) { Value = lastNames[rng.Next(lastNames.Length)] };
        cmd.Parameters.Add(lname);
    }

    Stopwatch sw = Stopwatch.StartNew();
    try
    {
        cmd.ExecuteNonQuery();
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.ToString());
    }
    sw.Stop();
    lastTime = sw.ElapsedMilliseconds;
}

#region Utils
////////////////////////////////////////////////////
/// UTILS
static (TokenCredential? tok, string? oauth2Sql) LoginToAure()
{
    try
    {
        var credential = new AzureCliCredential();
        var oauth2TokenSql = credential.GetToken(
                new TokenRequestContext(
                    ["https://database.windows.net/.default"])).Token;
        return (credential, oauth2TokenSql);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);
        return (null, null);
    }
}

static void RegisterAkvForAe(TokenCredential cred)
{
    var akvAeProvider = new SqlColumnEncryptionAzureKeyVaultProvider(cred);
    SqlConnection.RegisterColumnEncryptionKeyStoreProviders(
        customProviders: new Dictionary<string, SqlColumnEncryptionKeyStoreProvider>() {
                { SqlColumnEncryptionAzureKeyVaultProvider.ProviderName, akvAeProvider }
        });
}

#endregion