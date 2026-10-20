// =============================================================================
// GPAL - Generally Positive Automation Library
// Copyright © 2026 Software Decisions, Inc. All rights reserved.
//
// This file is part of GPAL.
// Licensed under the Business Source License 1.1
//
// Primary development, architecture, and vision by Michael B. Vederman,
// CEO of Software Decisions, Inc., Texas.
//
// Internal development maintained privately.
// Public releases appear on GitHub: https://github.com/SoftwareDecisionsInc/GPAL.
//
// See LICENSE for full terms, including Additional Use Grant.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GenerallyPositive.Browser;
using System.Data;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    /// <summary>
    /// Class to define database usage. Currently only used for input from a table, sql or stored procedure.<br/>
    /// Instantiated with GPAL.Database
    /// </summary>
    public class GPALDatabase : IGPALDatabase, IAllowDatabaseGridSettings, IDisposable
    {
        private DatabaseSettings.DatabaseColumn lastDatabaseColumn { get; set; }
        internal DatabaseSettings DatabaseSettings { get; set; }

        public delegate int CallOnError(GPALDatabase database);

        /// <summary>
        /// Details of the last failed call, for the failure callback: the server's error number, the sql, its
        /// parameters, and the row being written.
        /// </summary>
        public class DatabaseError
        {
            /// <summary>The server's error number, 0 when the failure did not come from the server</summary>
            public int Number { get; internal set; }
            /// <summary>What the failure said</summary>
            public string Message { get; internal set; }
            /// <summary>The sql that was running</summary>
            public string Sql { get; internal set; }
            /// <summary>The rows of parameters the sql was running against</summary>
            public IGPALGrid<string> Parameters { get; internal set; }
            /// <summary>Which row of Parameters was being written, -1 when the failure was not on a row</summary>
            public int Row { get; internal set; } = -1;
            /// <summary>That row's values, so the callback does not have to index back into the grid</summary>
            public List<string> RowValues { get; internal set; }
            /// <summary>When it happened</summary>
            public DateTime Occurred { get; internal set; }
            /// <summary>The exception itself, for a caller that wants the stack</summary>
            public Exception Exception { get; internal set; }
        }

        /// <summary>
        /// The last failure, or null when the last call did not fail. Cleared at the start of every call, so it
        /// always describes the most recent one.
        /// </summary>
        public DatabaseError LastError
        {
            get
            {
                return DatabaseSettings.LastError;
            }
            internal set
            {
                DatabaseSettings.LastError = value;
            }
        }

        /// <summary>
        /// Returns the database as <see cref="IGPALDatabase"/>, moving the chain from settings to actions.
        /// <see cref="IAllowDatabaseSettings"/> has only settings and parameter definitions;
        /// <see cref="IGPALDatabase"/> has values, Execute, SaveTo and the verbs.
        /// </summary>
        /// <example>
        /// <code>
        /// GPAL.Database
        ///     .WithServerName(@"HORNET2\DEADMIKE")
        ///     .WithDatabaseName("GPAL")
        ///     .WithDatabaseType(DatabaseType.SQLServer)
        ///     .WithSQLCommand("insert into Orders (Name, Tally) values (@Name, @Tally)")
        ///     .WithVarCharParameter("Name").WithParameterLength(50)
        ///     .WithIntegerParameter("Tally")
        ///     .ToGPALObject()   // settings above, actions below
        ///     .WithParameter("first")
        ///     .WithParameter("1")
        ///     .Execute(out int inserted);
        /// </code>
        /// </example>
        /// <returns>The database, as the interface that can run commands</returns>
        public IGPALDatabase ToGPALObject()
        {
            return this;
        }

        internal GPALDatabase()
        {
            DatabaseSettings = new DatabaseSettings(this);
        }

        #region <Getters/Setters>
        // Each verb only records itself (see DatabaseSettings.Verb); the statement is chosen when the command is built.
        public IAllowParametersAndUsing Create
        {
            get
            {
                DatabaseSettings.Verb = DatabaseSettings.CrudVerb.Create;

                return this;
            }
        }
        public IAllowParametersAndInto Read
        {
            get
            {
                DatabaseSettings.Verb = DatabaseSettings.CrudVerb.Read;

                return this;
            }
        }
        public IAllowParametersAndUsing Update
        {
            get
            {
                DatabaseSettings.Verb = DatabaseSettings.CrudVerb.Update;

                return this;
            }
        }
        public IAllowParametersAndUsing Delete
        {
            get
            {
                DatabaseSettings.Verb = DatabaseSettings.CrudVerb.Delete;

                return this;
            }
        }
        /// <summary>
        /// If you have already defined parameter objectss on a GPAL.Database, you can clear them and add new parameter objects;
        /// </summary>
        public IAllowParameters ClearParameters
        { 
            get
            {
                DatabaseSettings.ParametersList?.Clear();
                DatabaseSettings.ParametersGrid?.Clear();

                return this;
            }
        }
        /// <summary>
        /// The rows/columns returned from the database.<br/>
        /// Tokens are loaded upon calling any [Append/FillIn/Insert]FromDatabase method.<br/>
        /// Intended for debugging only.
        /// </summary>
        /// <returns>A GPALGrid of tokens</returns>
        public IGPALGrid<string> Tokens
        {
            get
            {
                return DatabaseSettings.Tokens;
            }
            internal set
            {
                DatabaseSettings.Tokens = value;
            }
        }

        public IAllowParametersAndActions BeginTransaction
        {
            get
            {
                if (null == DatabaseSettings.SqlTransaction)
                {
                    try
                    {
                        DatabaseHelper.OpenConnection(DatabaseSettings);
                        DatabaseSettings.SqlTransaction = DatabaseSettings.Connection.BeginTransaction();
                    }
                    catch (Exception ex)
                    {
                        GPAL.PublishSimpleEvent(GPALEventType.EXCEPTION, "ERROR beginning transaction", this, Enums.GPALObjectType.Database, ex);
                    }
                }
                else
                {
                    GPAL.PublishSimpleEvent(GPALEventType.WARNING, "Already in a transaction. You cannot start a new transaction while in a transaction. Doing nothing.", this, Enums.GPALObjectType.Database);
                }
                return this;
            }
        }

        public IAllowDatabaseActions Rollback
        {
            get
            {
                DatabaseSettings.SqlTransaction?.Rollback();
                DatabaseSettings.SqlTransaction = null;
                DatabaseHelper.CloseEverything(DatabaseSettings);
                return this;
            }
        }

        public IAllowDatabaseActions Commit
        {
            get
            {
                DatabaseSettings.SqlTransaction?.Commit();
                DatabaseSettings.SqlTransaction = null;
                DatabaseHelper.CloseEverything(DatabaseSettings);
                return this;
            }
        }

        #endregion <Getters/Setters>
        #region <Fluent Methods>
        /// <summary>
        /// Set the connectrion string used to connect to the database.<br/>
        /// If no connection string is provided, GPAL will construct one from the server and database name, and if supplied, username and password.
        /// </summary>
        /// <example>
        /// <code>
        /// Database database = (Database)GPAL.Database
        ///     .WithServerName(@"HORNET\SqlServer")
        ///     .WithDatabaseName("GPAL")
        ///     .WithTableName("TestInput")
        ///     .WithRowCount(4);
        /// </code>
        /// </example>
        /// <param name="connectionString">The connection string</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithConnectionString(string connectionString)
        {
            DatabaseSettings.ConnectionString = connectionString;
            return this;
        }
        /// <summary>
        /// Set the username used to connect to the database.<br/>
        /// If no connection string is provided, GPAL will construct one from the server and database name, and if supplied, username and password.
        /// </summary>
        /// <param name="username">The username</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithUsername(string username)
        {
            DatabaseSettings.UserName = username;
            return this;
        }
        /// <summary>
        /// Set the password used to connect to the database.<br/>
        /// If no connection string is provided, GPAL will construct one from the server and database name, and if supplied, username and password.
        /// </summary>
        /// <param name="password">The password</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithPassword(string password)
        {
            DatabaseSettings.Password = password;
            return this;
        }
        /// <summary>
        /// The SQL command to execute for the input data grid (tokens)
        /// </summary>
        /// <param name="sqlCommand">The SQL command to run</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithSQLCommand(string sqlCommand)
        {
            DatabaseSettings.SqlCommand = sqlCommand;
            return this;
        }
        /// <summary>
        /// The stored procedure to execute for the input data grid (tokens)
        /// </summary>
        /// <param name="storedProcedure">The stored procedure to run</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithStoredProcedure(string storedProcedure)
        {
            DatabaseSettings.StoredProcedure = storedProcedure;
            return this;
        }
        /// <summary>
        /// Add a parameter name to the parameter names list.<br/>
        /// CAVEAT: we need to know the type of parameter, this gets as tedious as the columns
        /// </summary>
        /// <param name="parameterName">The stored procedure parameter</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithParameterName(string parameterName)
        {
            DatabaseSettings.ParameterNamesList.Add(parameterName);
            return this;
        }
        /// <summary>
        /// Add a parameter to the parameters list.<br/>
        /// </summary>
        /// <param name="parameter">The stored procedure parameter</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowParametersAndActions WithParameter(string parameter)
        {
            // TODO: if datbase action already called, clear the parms
            DatabaseSettings.ParametersList.Add(parameter);
            return this;
        }
        /// <summary>
        /// Add a parameter to the parameters list.<br/>
        /// </summary>
        /// <param name="parametersGrid">Grid of stored procedure parameters</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowParametersAndActions WithParameterGrid(IGPALGrid<string> parametersGrid)
        {
            DatabaseSettings.ParametersGrid = parametersGrid;
            return this;
        }
        // TODO: figure out fluent syntax for using the underlying sql
        public IAllowDatabaseSettings WithCreateAs(string sqlCommand)
        {
            DatabaseSettings.CreateSQL = sqlCommand;
            return this;
        }
        // TODO: figure out fluent syntax for using the underlying sql
        public IAllowDatabaseSettings WithReadAs(string sqlCommand)
        {
            DatabaseSettings.ReadSQL = sqlCommand;
            return this;
        }
        // TODO: figure out fluent syntax for using the underlying sql
        public IAllowDatabaseSettings WithUpdateAs(string sqlCommand)
        {
            DatabaseSettings.UpdateSQL = sqlCommand;
            return this;
        }
        // TODO: figure out fluent syntax for using the underlying sql
        public IAllowDatabaseSettings WithDeleteAs(string sqlCommand)
        {
            DatabaseSettings.DeleteSQL = sqlCommand;
            return this;
        }

        /// <summary>
        /// Set the type of database GPAL is interacting with.<br/>
        /// Currently only SQL Server is supported. (Do not need to specify)
        /// </summary>
        /// <example>
        /// <code>
        /// // Open database GPAL on server HORNET\SqlServer using integrated security, query 4 rows from the top of table TestInput
        /// Database database = (Database)GPAL.Database
        ///     .WithServerName(@"HORNET\SqlServer")
        ///     .WithDatabaseName("GPAL")
        ///     .WithDatabaseType(DatabaseType.SQLServer)
        ///     .WithTableName("TestInput")
        ///     .WithRowCount(4);
        /// </code>
        /// </example>
        /// <param name="databaseType">The database type. SQLServer is the only one GPAL speaks.</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithDatabaseType(Enums.DatabaseType databaseType)
        {
            DatabaseSettings.DatabaseType = databaseType;
            return this;
        }
        /// <summary>
        /// Set the database servername.<br/>
        /// Used with databasename and [optional] usename/password to construct a connection string.
        /// </summary>
        /// <example>
        /// <code>
        /// // Open database GPAL on server HORNET\SqlServer using integrated security, query 4 rows from the top of table TestInput
        /// Database database = (Database)GPAL.Database
        ///     .WithServerName(@"HORNET\SqlServer")
        ///     .WithDatabaseName("GPAL")
        ///     .WithTableName("TestInput")
        ///     .WithRowCount(4);
        /// </code>
        /// </example>
        /// <param name="serverName">The server name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithServerName(string serverName)
        {
            DatabaseSettings.ServerName = serverName;
            return this;
        }
        /// <summary>
        /// Set the database instance name.<br/>
        /// Used with database servername and [optional] usename/password to construct a connection string.
        /// </summary>
        /// <example>
        /// <code>
        /// // Open database GPAL on server HORNET\SqlServer using integrated security, query 4 rows from the top of table TestInput
        /// Database database = (Database)GPAL.Database
        ///     .WithServerName(@"HORNET\SqlServer")
        ///     .WithDatabaseName("GPAL")
        ///     .WithTableName("TestInput")
        ///     .WithRowCount(4);
        /// </code>
        /// </example>
        /// <param name="databaseName">The database name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithDatabaseName(string databaseName)
        {
            DatabaseSettings.DatabaseName = databaseName;
            return this;
        }
        /// <summary>
        /// Set the table name to query for input.<br/>
        /// If no SQL command or stored procedure is defined, then GPAL will query the table supplied to get input data.<br/>
        /// Use .WithRowCount to set the number of rows to select.
        /// </summary>
        /// <example>
        /// <code>
        /// // Open database GPAL on server HORNET\SqlServer using integrated security, query 4 rows from the top of table TestInput
        /// Database database = (Database)GPAL.Database
        ///     .WithServerName(@"HORNET\SqlServer")
        ///     .WithDatabaseName("GPAL")
        ///     .WithTableName("TestInput")
        ///     .WithRowCount(4);
        /// </code>
        /// </example>
        /// <param name="tableName">The table name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithTableName(string tableName)
        {
            DatabaseSettings.TableName = tableName;
            return this;
        }
        /// <summary>
        /// Sets how long a command can run. Default is the provider's (30 seconds for SQL Server).
        /// </summary>
        /// <example>
        /// <code>
        /// database.WithCommandTimeout(300);   // five minutes
        /// </code>
        /// </example>
        /// <param name="timeoutInSeconds">Seconds a command can run. Zero means no limit.</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithCommandTimeout(int timeoutInSeconds)
        {
            DatabaseSettings.CommandTimeoutSeconds = timeoutInSeconds;
            return this;
        }

        /// <summary>
        /// Closes the connection and rolls back a transaction still open on it. The database can be used again
        /// afterwards: the next action opens a new connection from the same settings.
        /// </summary>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings Close()
        {
            DatabaseHelper.CloseEverything(DatabaseSettings);
            return this;
        }

        /// <summary>
        /// Closes the connection, so a database can be held in a using block. Same work as <see cref="Close"/>.
        /// </summary>
        public void Dispose()
        {
            DatabaseHelper.CloseEverything(DatabaseSettings);
        }

        /// <summary>
        /// Set the numbers of rows to select from the table.
        /// </summary>
        /// <example>
        /// <code>
        /// // Open database GPAL on server HORNET\SqlServer using integrated security, query 4 rows from the top of table TestInput
        /// Database database = (Database)GPAL.Database
        ///     .WithServerName(@"HORNET\SqlServer")
        ///     .WithDatabaseName("GPAL")
        ///     .WithTableName("TestInput")
        ///     .WithRowCount(4);
        /// </code>
        /// </example>
        /// <param name="rowCount">The number of rows to select from the top of the table</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithRowCount(int rowCount)
        {
            DatabaseSettings.RowCount = rowCount;
            return this;
        }

        #region <Output Column Settings [not yet implemented]>
        /// <summary>
        /// Define a DateTime column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithDatetimeParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.DateTime);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define a Time column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithTimeParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.Time);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define a Timestamp column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithTimestampParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.Timestamp);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define a Decimal column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowPrecsionScaleAndSettings WithDecimalParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.Decimal);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define a Float column [Double/Real]
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithFloatParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.Float);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define an Image column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithImageParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.Image);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define an Integer column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithIntegerParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.Int);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define a Money column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings WithMoneyParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.Money);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define a Char column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowParameterLength WithCharParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.Char);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define an NChar column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowParameterLength WithNCharParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.NChar);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define a VarChar column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowParameterLength WithVarCharParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.VarChar);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define an NVarChar column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowParameterLength WithNVarCharParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.NVarChar);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define a Text column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowParameterLength WithTextParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.Text);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        /// <summary>
        /// Define a NText column
        /// </summary>
        /// <param name="parameterName">The parameter name, which is how the sql refers to it: @name</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowParameterLength WithNTextParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, System.Data.SqlDbType.NText);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        public IAllowDatabaseSettings WithParameterLength(int length)
        {
            if (true == NamedAColumn("length"))
                lastDatabaseColumn.ColumnLength = length;

            return this;
        }

        public IAllowPrecsionScaleAndSettings WithParameterPrecision(int columnPrecision)
        {
            if (true == NamedAColumn("precision"))
                lastDatabaseColumn.ColumnPrecision = columnPrecision;

            return this;
        }

        public IAllowPrecsionScaleAndSettings WithParameterScale(int columnScale)
        {
            if (true == NamedAColumn("scale"))
                lastDatabaseColumn.ColumnScale = columnScale;

            return this;
        }

        /// <summary>
        /// True when a parameter exists for the length, precision or scale. Otherwise an ERROR names the setting and
        /// the calls that define a parameter.
        /// </summary>
        /// <param name="setting">What was being set, for the message</param>
        /// <returns>True when there is a parameter to set it on</returns>
        private bool NamedAColumn(string setting)
        {
            bool retVal = null != lastDatabaseColumn;

            if (false == retVal)
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    $"No parameter to set a {setting} on. Define one first, with .WithVarCharParameter, .WithDecimalParameter or another of the parameter calls",
                    this, GPALObjectType.Database);

            return retVal;
        }

        public IAllowDatabaseSettings WithTableParameter(string parameterName)
        {
            DatabaseSettings.DatabaseColumn databaseColumn = new DatabaseSettings.DatabaseColumn(parameterName, SqlDbType.Structured);
            lastDatabaseColumn = databaseColumn;
            DatabaseSettings.ColumnList.Add(databaseColumn);
            return this;
        }
        public IAllowDatabaseSettings WithUDTTName(string typeName)
        {
            if (lastDatabaseColumn == null)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    "No column defined before setting UDTT name. Call .WithTableParameter() first.",
                    this, GPALObjectType.Database);
                return this;
            }
            if (lastDatabaseColumn.ColumnType != SqlDbType.Structured)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"UDTT name specified for non-Structured column [{lastDatabaseColumn.ColumnName}]. Ignoring.",
                    this, GPALObjectType.Database);
            }
            lastDatabaseColumn.TvTableTypeName = typeName;
            return this;
        }
        public IAllowDatabaseExecute Using(IGPALGrid<string> parametersGrid)
        {
            DatabaseSettings.ParametersGrid = parametersGrid;
            return this;
        }

        public IAllowDatabaseExecute Using(DataTable dataTable)
        {
            IGPALGrid<string> parametersGrid = DatabaseHelper.DataTableToGrid(dataTable, out List<DatabaseSettings.DatabaseColumn> columns);
            DatabaseSettings.ParametersGrid = parametersGrid;
            DatabaseSettings.ColumnList = columns;
            return this;
        }

        public IAllowDatabaseGridSettings SaveTo(out IGPALGrid<string> dataGrid)
        {
            DatabaseHelper.GetGrid(DatabaseSettings, out dataGrid);
            return this;
        }

        public IAllowDatabaseSettings GetRowCount(out List<int> rowCountsPerTable)
        {
            rowCountsPerTable = DatabaseSettings.LastGridRowCountsPerTable ?? new List<int>();
            return this;
        }

        public IAllowDatabaseSettings SaveTo(out DataTable dataTable)
        {
            DatabaseHelper.GetDataTable(DatabaseSettings, out dataTable);
            return this;
        }

        public IAllowDatabaseSettings SaveTo(out DataSet dataSet)
        {
            DatabaseHelper.GetDataSet(DatabaseSettings, out dataSet);
            return this;
        }

        /// <summary>
        /// Reads a single value: the first column of the first row, without building a grid around it. A count,
        /// a maximum, a status. Null when the query returned no rows, or returned null.
        /// </summary>
        /// <example>
        /// <code>
        /// database
        ///     .WithSQLCommand("select count(*) from Orders where Shipped is null")
        ///     .Read
        ///     .SaveTo(out string waiting);
        /// </code>
        /// </example>
        /// <param name="value">Receives the value read.</param>
        /// <returns>Fluent interface to define more database settings</returns>
        public IAllowDatabaseSettings SaveTo(out string value)
        {
            DatabaseHelper.GetScalar(DatabaseSettings, out value);
            return this;
        }

        public IAllowParametersAndActions Execute(out int rowCount)
        {
            rowCount = DatabaseHelper.ExecuteSQL(DatabaseSettings, DatabaseHelper.BuildWriteStatement(DatabaseSettings), ParameterTokens());
            return this;
        }

        public IAllowParametersAndActions Execute(out List<int> rowCountsPerStatement)
        {
            DatabaseHelper.ExecuteSQL(DatabaseSettings, DatabaseHelper.BuildWriteStatement(DatabaseSettings), ParameterTokens(), out rowCountsPerStatement);
            return this;
        }

        /// <summary>
        /// Returns the parameter rows: the grid from .Using or .WithParameterGrid, otherwise one row from the
        /// parameters added one at a time.
        /// </summary>
        /// <returns>The rows the command runs against</returns>
        private IGPALGrid<string> ParameterTokens()
        {
            IGPALGrid<string> retVal = DatabaseSettings.ParametersGrid;

            if (null == retVal || 0 == retVal.Rows)
            {
                Tokens.Clear();
                Tokens.AddRow(DatabaseSettings.ParametersList);

                retVal = Tokens;
            }

            return retVal;
        }

        public IAllowDatabaseSettings WithErrorCallback(GPALDatabase.CallOnError callOnError)
        {
            DatabaseSettings.CallOnErrorHandler = callOnError;
            return this;
        }
        #endregion <Output Column Settings [saving not yet implemented]>
        #endregion <Fluent Methods>
        #region <Private>
        #endregion <Private>
    }
}

