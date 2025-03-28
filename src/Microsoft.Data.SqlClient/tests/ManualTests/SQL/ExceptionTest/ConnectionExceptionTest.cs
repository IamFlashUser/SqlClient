// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests
{
    public class ConnectionExceptionTest
    {
        // test connection string
        private string connectionString = "server=tcp:server,1432;database=test;connect timeout=60;";

        // data value and server consts
        private const string badServer = "NotAServer";
        private const string sqlsvrBadConn = "A network-related or instance-specific error occurred while establishing a connection to SQL Server. The server was not found or was not accessible. Verify that the instance name is correct and that SQL Server is configured to allow remote connections.";
        private const string execReaderFailedMessage = "ExecuteReader requires an open and available Connection. The connection's current state is closed.";
        private const string orderIdQuery = "select orderid from orders where orderid < 10250";
        private static bool IsNotKerberos() => DataTestUtility.IsKerberosTest != true;

        [ActiveIssue("https://github.com/dotnet/SqlClient/issues/3031")]
        [ConditionalFact(nameof(IsNotKerberos))]
        public void TestConnectionStateWithErrorClass20()
        {
            using TestTdsServer server = TestTdsServer.StartTestServer();
            using SqlConnection conn = new(server.ConnectionString);

            conn.Open();
            SqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            int result = cmd.ExecuteNonQuery();

            Assert.Equal(-1, result);
            Assert.Equal(System.Data.ConnectionState.Open, conn.State);

            server.Dispose();
            try
            {
                int result2 = cmd.ExecuteNonQuery();
            }
            catch (SqlException ex)
            {
                Assert.Equal(11, ex.Class);
                Assert.NotNull(ex.InnerException);
                SqlException innerEx = Assert.IsType<SqlException>(ex.InnerException);
                Assert.Equal(20, innerEx.Class);
                Assert.StartsWith("A network-related or instance-specific error occurred while establishing a connection to SQL Server. The server was not found or was not accessible.", innerEx.Message);
                // Since the server is not accessible driver can close the close the connection
                // It is user responsibilty to maintain the connection.
                Assert.Equal(System.Data.ConnectionState.Closed, conn.State);
            }
        }

        [Fact]
        public void ExceptionTests()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);

            // tests improper server name thrown from constructor of tdsparser
            SqlConnectionStringBuilder badBuilder = new SqlConnectionStringBuilder(builder.ConnectionString) { DataSource = badServer, ConnectTimeout = 1 };

            VerifyConnectionFailure<SqlException>(() => GenerateConnectionException(badBuilder.ConnectionString), sqlsvrBadConn, VerifyException);
        }

        [Fact]
        public void VariousExceptionTests()
        {
            // Test exceptions - makes sure they are only thrown from upper layers
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);

            SqlConnectionStringBuilder badBuilder = new SqlConnectionStringBuilder(builder.ConnectionString) { DataSource = badServer, ConnectTimeout = 1 };
            using (var sqlConnection = new SqlConnection(badBuilder.ConnectionString))
            {
                using (SqlCommand command = sqlConnection.CreateCommand())
                {
                    command.CommandText = orderIdQuery;
                    VerifyConnectionFailure<InvalidOperationException>(() => command.ExecuteReader(), execReaderFailedMessage);
                }
            }
        }

        [Fact]
        public void IndependentConnectionExceptionTestOpenConnection()
        {
            // Test exceptions for existing connection to ensure proper exception and call stack
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);

            SqlConnectionStringBuilder badBuilder = new SqlConnectionStringBuilder(builder.ConnectionString) { DataSource = badServer, ConnectTimeout = 1 };
            using (var sqlConnection = new SqlConnection(badBuilder.ConnectionString))
            {
                VerifyConnectionFailure<SqlException>(() => sqlConnection.Open(), sqlsvrBadConn, VerifyException);
            }
        }

        [Fact]
        public void IndependentConnectionExceptionTestExecuteReader()
        {
            // Test exceptions for existing connection to ensure proper exception and call stack
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);

            SqlConnectionStringBuilder badBuilder = new SqlConnectionStringBuilder(builder.ConnectionString) { DataSource = badServer, ConnectTimeout = 1 };
            using (var sqlConnection = new SqlConnection(badBuilder.ConnectionString))
            {
                using (SqlCommand command = new SqlCommand(orderIdQuery, sqlConnection))
                {
                    VerifyConnectionFailure<InvalidOperationException>(() => command.ExecuteReader(), execReaderFailedMessage);
                }
            }
        }

        [Theory]
        [InlineData(@"np:\\.\pipe\sqlbad\query")]
        [InlineData(@"np:\\.\pipe\MSSQL$NonExistentInstance\sql\query")]
        [InlineData(@"\\.\pipe\sqlbad\query")]
        [InlineData(@"\\.\pipe\MSSQL$NonExistentInstance\sql\query")]
        [InlineData(@"np:\\localhost\pipe\sqlbad\query")]
        [InlineData(@"np:\\localhost\pipe\MSSQL$NonExistentInstance\sqlbad\query")]
        [InlineData(@"\\localhost\pipe\sqlbad\query")]
        [InlineData(@"\\localhost\pipe\MSSQL$NonExistentInstance\sqlbad\query")]
        [PlatformSpecific(TestPlatforms.Windows)] // Named pipes with the given input strings are not supported on Unix
        public void NamedPipeTest(string dataSource)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder.DataSource = dataSource;
            builder.ConnectTimeout = 1;

            using (SqlConnection connection = new SqlConnection(builder.ConnectionString))
            {
                string expectedErrorMsg = "(provider: Named Pipes Provider, error: 40 - Could not open a connection to SQL Server)";
                VerifyConnectionFailure<SqlException>(() => connection.Open(), expectedErrorMsg);
            }
        }

        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp)]
        [ConditionalFact(typeof(DataTestUtility), nameof(DataTestUtility.AreConnStringsSetup))]
        public void NamedPipeInvalidConnStringTest()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder.ConnectTimeout = 1;

            string invalidConnStringError = "(provider: Named Pipes Provider, error: 40 - Could not open a connection to SQL Server)";
            string fakeServerName = Guid.NewGuid().ToString("N");

            // Using forward slashes
            builder.DataSource = "np://" + fakeServerName + "/pipe/sql/query";
            OpenBadConnection(builder.ConnectionString, invalidConnStringError);

            invalidConnStringError = "(provider: Named Pipes Provider, error: 5 - Invalid parameter(s) found)";

            // Without pipe token
            builder.DataSource = @"np:\\" + fakeServerName + @"\sql\query";
            OpenBadConnection(builder.ConnectionString, invalidConnStringError);

            invalidConnStringError = "(provider: SQL Network Interfaces, error: 25 - Connection string is not valid)";

            // Without a pipe name
            builder.DataSource = @"np:\\" + fakeServerName + @"\pipe";
            OpenBadConnection(builder.ConnectionString, invalidConnStringError);

            // No server name
            builder.DataSource = @"np:\\\pipe\sql\query";
            OpenBadConnection(builder.ConnectionString, invalidConnStringError);

            // Nothing after server
            builder.DataSource = @"np:\\" + fakeServerName;
            OpenBadConnection(builder.ConnectionString, invalidConnStringError);

            // Nothing but slashes
            builder.DataSource = @"np:\\\\\";
            OpenBadConnection(builder.ConnectionString, invalidConnStringError);

            invalidConnStringError = "(provider: SQL Network Interfaces, error: 26 - Error Locating Server/Instance Specified)";

            // No leading slashes
            builder.DataSource = @"np:" + fakeServerName + @"\pipe\sql\query";
            OpenBadConnection(builder.ConnectionString, invalidConnStringError);
        }

        private void GenerateConnectionException(string connectionString)
        {
            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                using (SqlCommand command = sqlConnection.CreateCommand())
                {
                    command.CommandText = orderIdQuery;
                    command.ExecuteReader();
                }
            }
        }

        private TException VerifyConnectionFailure<TException>(Action connectAction, string expectedExceptionMessage, Func<TException, bool> exVerifier) where TException : Exception
        {
            TException ex = Assert.Throws<TException>(connectAction);

            // Some exception messages are different between Framework and Core
#if NETFRAMEWORK
            Assert.Contains(expectedExceptionMessage, ex.Message);
#endif
            Assert.True(exVerifier(ex), "FAILED Exception verifier failed on the exception.");

            return ex;
        }

        private void OpenBadConnection(string connectionString, string errorMsg)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                VerifyConnectionFailure<SqlException>(() => conn.Open(), errorMsg);
            }
        }

        private TException VerifyConnectionFailure<TException>(Action connectAction, string expectedExceptionMessage) where TException : Exception
        {
            return VerifyConnectionFailure<TException>(connectAction, expectedExceptionMessage, (ex) => true);
        }

        private bool VerifyException(SqlException exception)
        {
            VerifyException(exception, 1);
            return true;
        }

        private bool VerifyException(SqlException exception, int count, int? errorNumber = null, int? errorState = null, int? severity = null)
        {
            Assert.NotEmpty(exception.Errors);
            Assert.Equal(count, exception.Errors.Count);

            // Ensure that all errors have an error-level severity
            for (int i = 0; i < count; i++)
            {
                Assert.InRange(exception.Errors[i].Class, 10, byte.MaxValue);
            }

            // Check the properties of the exception populated by the server are correct
            if (errorNumber.HasValue)
            {
                Assert.Equal(errorNumber.Value, exception.Number);
            }

            if (errorState.HasValue)
            {
                Assert.Equal(errorState.Value, exception.State);
            }

            if (severity.HasValue)
            {
                Assert.Equal(severity.Value, exception.Class);
            }

            if ((errorNumber.HasValue) && (errorState.HasValue) && (severity.HasValue))
            {
                string expected = $"Error Number:{errorNumber.Value},State:{errorState.Value},Class:{severity.Value}";
                Assert.Contains(expected, exception.ToString());
            }

            return true;
        }
    }
}
