// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Data.Common;
using Microsoft.Data.Common.ConnectionString;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.ConnectionPool;

namespace Microsoft.Data.ProviderBase
{
    internal abstract class DbConnectionClosed : DbConnectionInternal
    {
        // Construct an "empty" connection
        protected DbConnectionClosed(ConnectionState state, bool hidePassword, bool allowSetConnectionString) : base(state, hidePassword, allowSetConnectionString)
        {
        }

        public override string ServerVersion => throw ADP.ClosedConnectionError();

        protected override void Activate(System.Transactions.Transaction transaction) => throw ADP.ClosedConnectionError();

        public override DbTransaction BeginTransaction(IsolationLevel il) => throw ADP.ClosedConnectionError();

        public override void ChangeDatabase(string database) => throw ADP.ClosedConnectionError();

        internal override void CloseConnection(DbConnection owningObject, SqlConnectionFactory connectionFactory)
        {
            // not much to do here...
        }

        protected override void Deactivate() => ADP.ClosedConnectionError();

        public override void EnlistTransaction(System.Transactions.Transaction transaction) => throw ADP.ClosedConnectionError();

        protected internal override DataTable GetSchema(
            SqlConnectionFactory factory,
            DbConnectionPoolGroup poolGroup,
            DbConnection outerConnection,
            string collectionName,
            string[] restrictions)
        {
            throw ADP.ClosedConnectionError();
        }

        protected override DbReferenceCollection CreateReferenceCollection() => throw ADP.ClosedConnectionError();

        internal override bool TryOpenConnection(
            DbConnection outerConnection,
            SqlConnectionFactory connectionFactory,
            TaskCompletionSource<DbConnectionInternal> retry,
            DbConnectionOptions userOptions) =>
            TryOpenConnectionInternal(outerConnection, connectionFactory, retry, userOptions);
    }

    internal abstract class DbConnectionBusy : DbConnectionClosed
    {
        protected DbConnectionBusy(ConnectionState state) : base(state, true, false)
        {
        }

        internal override bool TryOpenConnection(
            DbConnection outerConnection,
            SqlConnectionFactory connectionFactory,
            TaskCompletionSource<DbConnectionInternal> retry,
            DbConnectionOptions userOptions)
            => throw ADP.ConnectionAlreadyOpen(State);
    }

    internal sealed class DbConnectionClosedBusy : DbConnectionBusy
    {
        // Closed Connection, Currently Busy - changing connection string
        internal static readonly DbConnectionInternal SingletonInstance = new DbConnectionClosedBusy();   // singleton object

        private DbConnectionClosedBusy() : base(ConnectionState.Closed)
        {
        }
    }

    internal sealed class DbConnectionOpenBusy : DbConnectionBusy
    {
        // Open Connection, Currently Busy - closing connection
        internal static readonly DbConnectionInternal SingletonInstance = new DbConnectionOpenBusy();   // singleton object

        private DbConnectionOpenBusy() : base(ConnectionState.Open)
        {
        }
    }

    internal sealed class DbConnectionClosedConnecting : DbConnectionBusy
    {
        // Closed Connection, Currently Connecting
        internal static readonly DbConnectionInternal SingletonInstance = new DbConnectionClosedConnecting();   // singleton object

        private DbConnectionClosedConnecting() : base(ConnectionState.Connecting)
        {
        }

        internal override void CloseConnection(DbConnection owningObject, SqlConnectionFactory connectionFactory)
        {
            connectionFactory.SetInnerConnectionTo(owningObject, DbConnectionClosedPreviouslyOpened.SingletonInstance);
        }

        internal override bool TryReplaceConnection(
            DbConnection outerConnection,
            SqlConnectionFactory connectionFactory,
            TaskCompletionSource<DbConnectionInternal> retry,
            DbConnectionOptions userOptions) =>
            TryOpenConnection(outerConnection, connectionFactory, retry, userOptions);

        internal override bool TryOpenConnection(
            DbConnection outerConnection,
            SqlConnectionFactory connectionFactory,
            TaskCompletionSource<DbConnectionInternal> retry,
            DbConnectionOptions userOptions)
        {
            if (retry == null || !retry.Task.IsCompleted)
            {
                // retry is null if this is a synchronous call

                // if someone calls Open or OpenAsync while in this state, 
                // then the retry task will not be completed

                throw ADP.ConnectionAlreadyOpen(State);
            }

            // we are completing an asynchronous open
            Debug.Assert(retry.Task.Status == TaskStatus.RanToCompletion, "retry task must be completed successfully");
            DbConnectionInternal openConnection = retry.Task.Result;
            if (openConnection == null)
            {
                connectionFactory.SetInnerConnectionTo(outerConnection, this);
                throw ADP.InternalConnectionError(ADP.ConnectionError.GetConnectionReturnsNull);
            }
            connectionFactory.SetInnerConnectionEvent(outerConnection, openConnection);

            return true;
        }
    }

    internal sealed class DbConnectionClosedNeverOpened : DbConnectionClosed
    {
        // Closed Connection, Has Never Been Opened
        internal static readonly DbConnectionInternal SingletonInstance = new DbConnectionClosedNeverOpened();   // singleton object

        private DbConnectionClosedNeverOpened() : base(ConnectionState.Closed, false, true)
        {
        }
    }

    internal sealed class DbConnectionClosedPreviouslyOpened : DbConnectionClosed
    {
        // Closed Connection, Has Previously Been Opened
        internal static readonly DbConnectionInternal SingletonInstance = new DbConnectionClosedPreviouslyOpened();   // singleton object

        private DbConnectionClosedPreviouslyOpened() : base(ConnectionState.Closed, true, true)
        {
        }

        internal override bool TryReplaceConnection(
            DbConnection outerConnection,
            SqlConnectionFactory connectionFactory,
            TaskCompletionSource<DbConnectionInternal> retry,
            DbConnectionOptions userOptions) =>
            TryOpenConnection(outerConnection, connectionFactory, retry, userOptions);
    }
}
