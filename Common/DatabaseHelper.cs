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
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static GenerallyPositive.Enums;

namespace GenerallyPositive
{
    internal class DatabaseHelper
    {
        public static SqlConnection OpenConnection(DatabaseSettings databaseSettings)
        {
            // A connection that failed to open, or that the server dropped, is kept but closed, and was handed out as
            // is: every later call reported no open connection, even once the server was back. It is dropped and
            // opened again. Not while a transaction is open, as a new connection would silently lose it.
            if (null != databaseSettings.Connection && ConnectionState.Open != databaseSettings.Connection.State && null == databaseSettings.SqlTransaction)
            {
                databaseSettings.Connection.Dispose();
                databaseSettings.Connection = null;
            }

            if (null == databaseSettings.Connection)
            {
                try
                {
                    if (null != databaseSettings.ConnectionString)
                        databaseSettings.Connection = new SqlConnection(databaseSettings.ConnectionString);
                    else if (Enums.DatabaseType.SQLServer == databaseSettings.DatabaseType)
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.Append($"Data Source={databaseSettings.ServerName};Initial Catalog={databaseSettings.DatabaseName};");
                        if (null == databaseSettings.UserName)
                            sb.Append("Integrated Security=true;");
                        else
                        {
                            sb.Append($"User id={databaseSettings.UserName};password={databaseSettings.Password};");
                        }
                        databaseSettings.Connection = new SqlConnection(sb.ToString());

                    }

                    // No connection string and no database type: nothing to open.
                    if (null == databaseSettings.Connection)
                        GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                            "Nothing said what to connect to. Set .WithConnectionString, or .WithDatabaseType(DatabaseType.SQLServer) with a server and database name",
                            databaseSettings.Database, GPALObjectType.Database);
                    else
                        databaseSettings.Connection.Open();
                }
                catch (Exception ex)
                {
                    RecordFailure(databaseSettings, ex, $"Unable to open a connection to [{databaseSettings.DatabaseName}] on [{databaseSettings.ServerName}]", null, null, -1);
                }
            }
            return databaseSettings.Connection;
        }
        public static void Close(DatabaseSettings databaseSettings)
        {
            // Do not close a connection with an open transaction; that rolls it back.
            if (null == databaseSettings.SqlTransaction)
            {
                databaseSettings.Connection.Close();
                databaseSettings.Connection = null;
            }
        }

        /// <summary>
        /// Rolls back any open transaction, then closes and disposes the connection. Settings are kept, so the next
        /// call opens a new connection.
        /// </summary>
        /// <param name="databaseSettings">The database to release.</param>
        public static void CloseEverything(DatabaseSettings databaseSettings)
        {
            if (null != databaseSettings.SqlTransaction)
            {
                databaseSettings.SqlTransaction.Rollback();
                databaseSettings.SqlTransaction = null;
            }

            if (null != databaseSettings.Connection)
            {
                if (ConnectionState.Closed != databaseSettings.Connection.State)
                    databaseSettings.Connection.Close();

                databaseSettings.Connection.Dispose();
                databaseSettings.Connection = null;
            }
        }
        /// <summary>
        /// Sets the command timeout (only when asked; otherwise the provider default) and the transaction, which a
        /// connection with a pending transaction requires.
        /// </summary>
        /// <param name="databaseSettings">Settings holding the timeout and the transaction.</param>
        /// <param name="command">The command about to run.</param>
        private static void PrepareCommand(DatabaseSettings databaseSettings, SqlCommand command)
        {
            if (null != command)
            {
                if (0 <= databaseSettings.CommandTimeoutSeconds)
                    command.CommandTimeout = databaseSettings.CommandTimeoutSeconds;

                if (null != databaseSettings.SqlTransaction)
                    command.Transaction = databaseSettings.SqlTransaction;
            }
        }

        /// <summary>
        /// Adds one parameter with the length, precision and scale from its definition. Reads and writes both use this.
        /// </summary>
        /// <param name="command">The command to add the parameter to.</param>
        /// <param name="databaseColumn">The parameter definition.</param>
        /// <param name="parameterValue">The value as text; converted to the parameter's type here.</param>
        private static void AddParameter(SqlCommand command, DatabaseSettings.DatabaseColumn databaseColumn, string parameterValue)
        {
            SqlParameter sqlParameter = command.Parameters.Add($"@{databaseColumn.ColumnName}", databaseColumn.ColumnType, databaseColumn.ColumnLength);

            // Send the declared precision and scale. At zero the provider takes them from each value, so rows could be
            // sent differently.
            if (0 < databaseColumn.ColumnPrecision)
                sqlParameter.Precision = (byte)databaseColumn.ColumnPrecision;

            if (0 < databaseColumn.ColumnScale)
                sqlParameter.Scale = (byte)databaseColumn.ColumnScale;

            sqlParameter.Value = ReturnParameterOfType(parameterValue, databaseColumn.ColumnType);
        }

        /// <summary>
        /// Adds the read's one row of parameters: the grid's first row, or the values added one at a time. Uses typed
        /// definitions if any, otherwise names.
        /// </summary>
        /// <param name="databaseSettings">Settings holding the parameters.</param>
        /// <param name="command">The command about to run; null when there is nothing to read.</param>
        private static void AddReadParameters(DatabaseSettings databaseSettings, SqlCommand command)
        {
            List<string> values = 0 < (databaseSettings.ParametersGrid?.Rows ?? 0)
                ? new List<string>(databaseSettings.ParametersGrid[0])
                : databaseSettings.ParametersList;

            if (null != command && 0 < (values?.Count ?? 0))
            {
                if (0 < databaseSettings.ColumnList.Count)
                {
                    foreach (DatabaseSettings.DatabaseColumn databaseColumn in databaseSettings.ColumnList)
                    {
                        int columnIndex = databaseSettings.ColumnList.IndexOf(databaseColumn);

                        AddParameter(command, databaseColumn, values.Count > columnIndex ? values[columnIndex] : null);
                    }
                }
                else
                {
                    // Parameters by name only, as stored procedures are usually called; the server converts the text
                    // values.
                    int valueIndex = 0;

                    foreach (string parameterName in databaseSettings.ParameterNamesList)
                    {
                        command.Parameters.Add(new SqlParameter(parameterName,
                            values.Count > valueIndex && null != values[valueIndex] ? (object)values[valueIndex] : DBNull.Value));

                        valueIndex++;
                    }
                }
            }
        }

        /// <summary>
        /// Builds the read command from the first of StoredProcedure, SqlCommand, ReadSQL or TableName that is set, and
        /// adds the parameters. Used for both result sets and single values.
        /// </summary>
        /// <param name="databaseSettings">Settings saying what to read.</param>
        /// <param name="sqlConnection">The open connection to run it on.</param>
        /// <returns>The command, or null when nothing says what to read.</returns>
        private static SqlCommand BuildReadCommand(DatabaseSettings databaseSettings, SqlConnection sqlConnection)
        {
            SqlCommand cmd = null;

            // The verb's sql (.WithReadAs and the like) wins. It is used for this command only, so a stored procedure
            // the caller set is kept for later calls.
            if (false == string.IsNullOrWhiteSpace(databaseSettings.VerbSQL))
            {
                cmd = new SqlCommand(databaseSettings.VerbSQL, sqlConnection)
                {
                    CommandType = CommandType.Text
                };
            }
            // A stored procedure and a sql command share a field, so check SqlCommandType.
            else if (CommandType.StoredProcedure == databaseSettings.SqlCommandType && null != databaseSettings.StoredProcedure)
            {
                cmd = new SqlCommand(databaseSettings.StoredProcedure, sqlConnection)
                {
                    CommandType = CommandType.StoredProcedure
                };
            }
            else if (null != databaseSettings.SqlCommand)
            {
                cmd = new SqlCommand(databaseSettings.SqlCommand, sqlConnection)
                {
                    CommandType = databaseSettings.SqlCommandType
                };
            }
            else if (null != databaseSettings.ReadSQL)
            {
                cmd = new SqlCommand(databaseSettings.ReadSQL, sqlConnection)
                {
                    CommandType = databaseSettings.SqlCommandType
                };
            }
            else if (null != databaseSettings.TableName)
            {
                string sqlCommand = "Select " + ((-1 != databaseSettings.RowCount) ? $"TOP({databaseSettings.RowCount}) " : "") + $"* from {databaseSettings.TableName}";

                cmd = new SqlCommand(sqlCommand, sqlConnection)
                {
                    CommandType = CommandType.Text
                };
            }

            AddReadParameters(databaseSettings, cmd);

            PrepareCommand(databaseSettings, cmd);

            return cmd;
        }

        /// <summary>
        /// Returns the command a read will run, or null when the connection did not open (already published) or nothing
        /// says what to read.
        /// </summary>
        /// <param name="databaseSettings">Settings saying what to read, holding the connection.</param>
        /// <param name="sqlConnection">The connection the read would run on.</param>
        /// <returns>The command to run; null when there is nothing to run.</returns>
        private static SqlCommand ReadCommandOrNothing(DatabaseSettings databaseSettings, SqlConnection sqlConnection)
        {
            SqlCommand retVal = null;

            if (null == sqlConnection || ConnectionState.Open != sqlConnection.State)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"No open connection to [{databaseSettings.DatabaseName}] on [{databaseSettings.ServerName}], so nothing was read",
                    databaseSettings.Database, GPALObjectType.Database);
            else
            {
                retVal = BuildReadCommand(databaseSettings, sqlConnection);

                if (null == retVal)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        "Nothing said what to read. Set one of .WithSQLCommand, .WithReadAs, .WithTableName or .WithStoredProcedure",
                        databaseSettings.Database, GPALObjectType.Database);
            }

            return retVal;
        }

        /// <summary>
        /// Runs the read and returns the first column of the first row, for a count, maximum or status.
        /// </summary>
        /// <param name="databaseSettings">Settings saying what to read.</param>
        /// <param name="value">Receives the value; null when the query returned no rows or a null.</param>
        public static void GetScalar(DatabaseSettings databaseSettings, out string value)
        {
            string retVal = null;

            // Clear LastError before opening, so it only describes this call.
            databaseSettings.LastError = null;

            SqlConnection sqlConnection = OpenConnection(databaseSettings);
            SqlCommand cmd = ReadCommandOrNothing(databaseSettings, sqlConnection);

            if (null != cmd)
            {
                try
                {
                    object answer = cmd.ExecuteScalar();

                    retVal = DBNull.Value == answer ? null : answer?.ToString();

                    Close(databaseSettings);
                }
                catch (Exception ex)
                {
                    RecordFailure(databaseSettings, ex, $"Failed reading with [{cmd.CommandText}]", cmd.CommandText, databaseSettings.ParametersGrid, -1);
                }
            }

            value = retVal;
        }

        /// <summary>
        /// Runs whatever is defined in DatabaseSettings (SqlCommand/ReadSQL/TableName/StoredProcedure) and
        /// fills a DataSet. A query/sproc can return more than one result set, and we don't know in advance
        /// whether it will - so this is the one real execution path; GetGrid/GetDataTable are thin conversions
        /// of whatever comes back from here.
        /// </summary>
        public static int GetDataSet(DatabaseSettings databaseSettings, out DataSet dataSet)
        {
            DataSet returnDataSet = new DataSet();

            // Clear LastError before opening, so it only describes this call.
            databaseSettings.LastError = null;

            SqlConnection sqlConnection = OpenConnection(databaseSettings);
            SqlCommand cmd = ReadCommandOrNothing(databaseSettings, sqlConnection);

            if (null != cmd)
            {
                try
                {
                    using var adapter = new SqlDataAdapter(cmd);
                    adapter.Fill(returnDataSet);
                    Close(databaseSettings);
                }
                catch (Exception ex)
                {
                    RecordFailure(databaseSettings, ex, $"Failed reading with [{cmd.CommandText}]", cmd.CommandText, databaseSettings.ParametersGrid, -1);
                }
            }

            dataSet = returnDataSet;
            return dataSet.Tables.Count;
        }
        /// <summary>
        /// A grid is just rows of strings, so unlike DataTable it can hold every result set a query returns -
        /// each table's rows get appended in order, widening/narrowing row length per table as needed.
        /// </summary>
        public static int GetGrid(DatabaseSettings databaseSettings, out IGPALGrid<string> dataGrid)
        {
            IGPALGrid<string> returnDataGrid = GPAL.Grid.ToGPALObject();
            GetDataSet(databaseSettings, out DataSet dataSet);

            var rowCountsPerTable = new List<int>();
            int rowsAdded = 0;
            foreach (DataTable table in dataSet.Tables)
            {
                if (databaseSettings.ColumnList.Count == 0)
                {
                    foreach (DataColumn column in table.Columns)
                    {
                        SqlDbType sqlDbType = GetSqlDbTypeFromClrType(column.DataType);
                        databaseSettings.ColumnList.Add(new DatabaseSettings.DatabaseColumn(column.ColumnName, sqlDbType));
                    }
                }

                int rowsAddedForTable = 0;
                foreach (DataRow row in table.Rows)
                {
                    if (0 < databaseSettings.RowCount && rowsAdded >= databaseSettings.RowCount)
                        break;
                    returnDataGrid.AddRow(row.ItemArray.Select(v => v.ToString()).ToList());
                    rowsAdded++;
                    rowsAddedForTable++;
                }
                rowCountsPerTable.Add(rowsAddedForTable);
            }
            databaseSettings.LastGridRowCountsPerTable = rowCountsPerTable;

            dataGrid = returnDataGrid;
            return dataGrid.Count();
        }
        /// <summary>
        /// A DataTable can only hold one result set's schema. If the query returned more than one, everything
        /// past the first is discarded here - use SaveTo(out DataSet) instead if you need all of them.
        /// </summary>
        public static int GetDataTable(DatabaseSettings databaseSettings, out DataTable dataTable)
        {
            GetDataSet(databaseSettings, out DataSet dataSet);

            if (dataSet.Tables.Count > 1)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Query returned [{dataSet.Tables.Count}] result sets; SaveTo(out DataTable) only keeps the first. Use SaveTo(out DataSet) to get all of them.",
                    databaseSettings.Database, GPALObjectType.Database);
            }

            dataTable = dataSet.Tables.Count > 0 ? dataSet.Tables[0] : new DataTable();
            return dataTable.Rows.Count;
        }
        /// <summary>
        /// Builds an INSERT from the table name and parameter definitions, as a read with only a table name gets a
        /// SELECT. Each parameter becomes a column, in definition order.
        /// </summary>
        /// <param name="databaseSettings">Settings with the table name and parameters.</param>
        /// <returns>The INSERT, or null when the table name or the parameters are missing.</returns>
        internal static string BuildInsertStatement(DatabaseSettings databaseSettings)
        {
            string retVal = null;
            bool hasTable = false == string.IsNullOrWhiteSpace(databaseSettings.TableName);
            bool hasColumns = 0 < databaseSettings.ColumnList.Count;

            // If only one of table name or parameters is set, publish which is missing. If neither is set, the write
            // reports it.
            if (false == hasTable || false == hasColumns)
            {
                if (true == hasTable || true == hasColumns)
                    GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                        "An insert needs a table name and at least one parameter to write. Set .WithTableName and one of the parameter calls",
                        databaseSettings.Database, GPALObjectType.Database);
            }
            else
            {
                // joined with ", ", so no separator follows the last column
                string columns = string.Join(", ", databaseSettings.ColumnList.Select(databaseColumn => databaseColumn.ColumnName));
                string parameters = string.Join(", ", databaseSettings.ColumnList.Select(databaseColumn => $"@{databaseColumn.ColumnName}"));

                retVal = $"INSERT INTO {databaseSettings.TableName} ({columns}) VALUES ({parameters})";
            }

            return retVal;
        }

        /// <summary>
        /// Returns the statement a write runs, from the first of: the verb's sql, SqlCommand/StoredProcedure, or the
        /// table name. The table name is only used for .Create; an update or delete from it would have no key and no
        /// WHERE clause.
        /// </summary>
        /// <param name="databaseSettings">Settings saying what to write.</param>
        /// <returns>The statement to run; null when nothing says what to run.</returns>
        internal static string BuildWriteStatement(DatabaseSettings databaseSettings)
        {
            string retVal = false == string.IsNullOrWhiteSpace(databaseSettings.VerbSQL)
                ? databaseSettings.VerbSQL
                : databaseSettings.SqlCommand;

            if (string.IsNullOrWhiteSpace(retVal) && DatabaseSettings.CrudVerb.Create == databaseSettings.Verb)
                retVal = BuildInsertStatement(databaseSettings);

            return retVal;
        }
        /// <summary>
        /// Execute SQL passing in parameters
        /// NOTE: parameterTokens CAN be a Grid, in which case it will be assumed to be the ONE parameter which is a table value parameter
        /// </summary>
        /// <param name="databaseSettings"></param>
        /// <param name="sqlToRun"></param>
        /// <param name="parameterTokens"></param>
        /// <returns></returns>
        internal static int ExecuteSQL(DatabaseSettings databaseSettings, string sqlToRun, IGPALGrid<string> parameterTokens = null)
        {
            return ExecuteSQL(databaseSettings, sqlToRun, parameterTokens, out _);
        }
        /// <summary>Same execution as the other overload, but also reports the affected-row count of each
        /// individual statement in the batch (via SqlCommand.StatementCompleted), not just the aggregate total.</summary>
        internal static int ExecuteSQL(DatabaseSettings databaseSettings, string sqlToRun, IGPALGrid<string> parameterTokens, out List<int> rowCountsPerStatement)
        {
            // Clear LastError before opening, so it only describes this call.
            databaseSettings.LastError = null;

            // Use SqlCommandType only when running SqlCommand itself. A statement from the verb's sql or the table name
            // is Text, so a leftover stored procedure type does not apply.
            CommandType commandType = sqlToRun == databaseSettings.SqlCommand
                ? databaseSettings.SqlCommandType
                : CommandType.Text;

            // OpenConnection hands back the open connection, or opens one if it is missing or was left closed
            using var sqlCommand = new SqlCommand(sqlToRun, OpenConnection(databaseSettings))
            {
                CommandType = commandType
            };

            PrepareCommand(databaseSettings, sqlCommand);

            int rowCount = 0;
            int rowBeingWritten = -1;
            bool ranPerRow = false;
            var statementCounts = new List<int>();

            sqlCommand.StatementCompleted += (s, e) => statementCounts.Add(e.RecordCount);

            // No open connection or no sql: publish the reason instead of the provider's error.
            if (null == sqlCommand.Connection || ConnectionState.Open != sqlCommand.Connection.State)
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"No open connection to [{databaseSettings.DatabaseName}] on [{databaseSettings.ServerName}], so nothing was run",
                    databaseSettings.Database, GPALObjectType.Database);
            else if (string.IsNullOrWhiteSpace(sqlToRun))
                GPAL.PublishSimpleEvent(GPALEventType.ERROR,
                    "Nothing said what to run. Set one of .WithSQLCommand, .WithCreateAs, .WithUpdateAs, .WithDeleteAs or .WithStoredProcedure, or give .Create a .WithTableName and its parameters to write",
                    databaseSettings.Database, GPALObjectType.Database);
            else
            {
                try
                {
                    if (parameterTokens != null && parameterTokens.Rows > 0)
                    {
                        var structuredColumn = databaseSettings.ColumnList.FirstOrDefault(c => c.ColumnType == SqlDbType.Structured);
                        if (structuredColumn != null)
                        {
                            if (string.IsNullOrEmpty(structuredColumn.TvTableTypeName))
                                throw new InvalidOperationException($"UDTT Type Name required for Table Value column @[{structuredColumn.ColumnName}]. Use .WithUDDTName()");

                            var dataTable = new DataTable();
                            int columnCount = parameterTokens.Columns;

                            for (int i = 0; i < columnCount; i++)
                            {
                                dataTable.Columns.Add($"Column{i}", typeof(string));
                            }

                            foreach (var row in parameterTokens)
                            {
                                if (row.Count != columnCount)
                                {
                                    GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                                        $"Row [{row}] has [{row.Count}] values, but expected [{columnCount}]. Skipping row.",
                                        parameterTokens, GPALObjectType.Other);
                                    continue;
                                }
                                dataTable.Rows.Add(row.ToArray());
                            }

                            var param = sqlCommand.Parameters.AddWithValue($"@{structuredColumn.ColumnName}", dataTable);
                            param.SqlDbType = SqlDbType.Structured;
                            param.TypeName = structuredColumn.TvTableTypeName;
                        }
                        else
                        {
                            // Run the command once per grid row. Parameters added one at a time are one row.
                            foreach (var parameters in parameterTokens)
                            {
                                rowBeingWritten++;
                                sqlCommand.Parameters.Clear();

                                foreach (var databaseColumn in databaseSettings.ColumnList)
                                {
                                    int columnIndex = databaseSettings.ColumnList.IndexOf(databaseColumn);
                                    string paramValue = parameters.Count > columnIndex ? parameters[columnIndex] : null;

                                    AddParameter(sqlCommand, databaseColumn, paramValue);
                                }

                                rowCount += sqlCommand.ExecuteNonQuery();
                            }

                            ranPerRow = true;
                        }
                    }

                    // Run once for the table-valued path (whole grid in one command) and for a command with no
                    // parameters.
                    if (false == ranPerRow)
                        rowCount = sqlCommand.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
  
                    RecordFailure(databaseSettings, ex, $"Failed running [{sqlToRun}]", sqlToRun, parameterTokens, rowBeingWritten);
                }
            }

            rowCountsPerStatement = statementCounts;
            return rowCount;
        }
        /// <summary>
        /// Records the database error, publishes it and runs the failure callback. Every failure path calls this.
        /// </summary>
        /// <param name="databaseSettings">Settings whose database holds the error</param>
        /// <param name="ex">The exception thrown</param>
        /// <param name="message">What was being attempted, without the exception text</param>
        /// <param name="sql">The sql that was running; null when there was none yet</param>
        /// <param name="parameterTokens">The rows it was running against; null when there were none</param>
        /// <param name="row">The row being written; -1 when the failure was not on a row</param>
        private static void RecordFailure(DatabaseSettings databaseSettings, Exception ex, string message, string sql, IGPALGrid<string> parameterTokens, int row)
        {
            databaseSettings.LastError = new GPALDatabase.DatabaseError
            {
                Number = ex is SqlException sqlException ? sqlException.Number : 0,
                Message = ex.Message,
                Sql = sql,
                Parameters = parameterTokens,
                Row = row,
                RowValues = 0 <= row && row < (parameterTokens?.Rows ?? 0) ? new List<string>(parameterTokens[row]) : null,
                Occurred = DateTime.Now,
                Exception = ex
            };

            // A SqlException is published as ERROR, anything else as EXCEPTION. Passing the exception fires the
            // exception handler, and the publish takes the screenshot.
            GPAL.PublishSimpleEvent(ex is SqlException ? GPALEventType.ERROR : GPALEventType.EXCEPTION,
                message, databaseSettings.Database, GPALObjectType.Database, ex);

            CallOnErrorHandler(databaseSettings);
        }

        private static void CallOnErrorHandler(DatabaseSettings databaseSettings)
        {
            if (null != databaseSettings.CallOnErrorHandler)
                databaseSettings.CallOnErrorHandler(databaseSettings.Database);

            if (null != databaseSettings.SqlTransaction)
            {
                databaseSettings.SqlTransaction.Rollback();
                databaseSettings.SqlTransaction = null;
            }

            if (null != databaseSettings.Connection && ConnectionState.Open == databaseSettings.Connection.State)
            {
                databaseSettings.Connection.Close();
                databaseSettings.Connection = null;
            }
        }
        static SqlDbType GetSqlDbTypeFromName(string dataTypeName)
        {
            switch (dataTypeName.ToLower())
            {
                case "char":
                    return SqlDbType.Char;
                case "nchar":
                    return SqlDbType.NChar;
                case "ntext":
                    return SqlDbType.NText;
                case "nvarchar":
                    return SqlDbType.NVarChar;
                case "text":
                    return SqlDbType.Text;
                case "varchar":
                    return SqlDbType.VarChar;
                case "xml":
                    return SqlDbType.Xml;
                case "date":
                    return SqlDbType.Date;
                case "datetime":
                    return SqlDbType.DateTime;
                case "datetime2":
                    return SqlDbType.DateTime2;
                case "smalldatetime":
                    return SqlDbType.SmallDateTime;
                case "time":
                    return SqlDbType.Time;
                case "timestamp":
                    return SqlDbType.Timestamp;
                case "decimal":
                    return SqlDbType.Decimal;
                case "money":
                    return SqlDbType.Money;
                case "smallmoney":
                    return SqlDbType.SmallMoney;
                case "float":
                    return SqlDbType.Float;
                case "real":
                    return SqlDbType.Real;
                case "image":
                    return SqlDbType.Image;
                case "bit":
                    return SqlDbType.Bit;
                case "int":
                    return SqlDbType.Int;
                case "smallint":
                    return SqlDbType.SmallInt;
                // Add cases for other types as needed
                default:
                    throw new ArgumentException("Unknown SqlDbType: " + dataTypeName);
            }
        }
        /// <summary>
        /// DataTable columns (filled via SqlDataAdapter) only carry the CLR type, not the SQL Server native
        /// type name that GetSqlDbTypeFromName expects - so a separate mapping is needed here.
        /// </summary>
        static SqlDbType GetSqlDbTypeFromClrType(Type clrType)
        {
            switch (Type.GetTypeCode(clrType))
            {
                case TypeCode.String: return SqlDbType.NVarChar;
                case TypeCode.Boolean: return SqlDbType.Bit;
                case TypeCode.Byte: return SqlDbType.TinyInt;
                case TypeCode.Int16: return SqlDbType.SmallInt;
                case TypeCode.Int32: return SqlDbType.Int;
                case TypeCode.Int64: return SqlDbType.BigInt;
                case TypeCode.Single: return SqlDbType.Real;
                case TypeCode.Double: return SqlDbType.Float;
                case TypeCode.Decimal: return SqlDbType.Decimal;
                case TypeCode.DateTime: return SqlDbType.DateTime;
                default:
                    if (clrType == typeof(Guid)) return SqlDbType.UniqueIdentifier;
                    if (clrType == typeof(byte[])) return SqlDbType.VarBinary;
                    if (clrType == typeof(TimeSpan)) return SqlDbType.Time;
                    if (clrType == typeof(DateTimeOffset)) return SqlDbType.DateTimeOffset;
                    return SqlDbType.NVarChar;
            }
        }
        private static object ReturnParameterOfType(string parmValue, SqlDbType sqlDbType)
        {
            if (string.IsNullOrEmpty(parmValue))
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Parameter value is null or empty for SqlDbType [{sqlDbType}]. Returning DBNull.Value.",
                    null, GPALObjectType.Other);
                return DBNull.Value;
            }

            try
            {
                switch (sqlDbType)
                {
                    case SqlDbType.Char:
                    case SqlDbType.NChar:
                    case SqlDbType.NText:
                    case SqlDbType.NVarChar:
                    case SqlDbType.Text:
                    case SqlDbType.VarChar:
                    case SqlDbType.Xml:
                        return parmValue;

                    case SqlDbType.Date:
                    case SqlDbType.DateTime:
                    case SqlDbType.DateTime2:
                    case SqlDbType.SmallDateTime:
                        return DateTime.TryParse(parmValue, out DateTime dateResult) ? (object)dateResult : DBNull.Value;

                    // A SQL Server time column is a TimeSpan and rejects a DateTime. Keep only the time of day:
                    // "14:30:00" and "2026-09-03 14:30:00" both write 14:30.
                    case SqlDbType.Time:
                        if (TimeSpan.TryParse(parmValue, out TimeSpan timeResult))
                            return timeResult;

                        return DateTime.TryParse(parmValue, out DateTime timeOfDayResult) ? (object)timeOfDayResult.TimeOfDay : DBNull.Value;

                    case SqlDbType.DateTimeOffset:
                        return DateTimeOffset.TryParse(parmValue, out DateTimeOffset offsetResult) ? (object)offsetResult : DBNull.Value;

                    case SqlDbType.Decimal:
                    case SqlDbType.Money:
                    case SqlDbType.SmallMoney:
                        return Decimal.TryParse(parmValue, out decimal decimalResult) ? (object)decimalResult : DBNull.Value;

                    // SQL Server float is a Double (real is a Single); read it as Double.
                    case SqlDbType.Float:
                        return double.TryParse(parmValue, out double doubleResult) ? (object)doubleResult : DBNull.Value;

                    case SqlDbType.Real:
                        return float.TryParse(parmValue, out float floatResult) ? (object)floatResult : DBNull.Value;

                    case SqlDbType.Bit:
                        return bool.TryParse(parmValue, out bool boolResult) ? (object)boolResult : (int.TryParse(parmValue, out int intBitResult) ? (object)(intBitResult != 0) : DBNull.Value);

                    case SqlDbType.Int:
                    case SqlDbType.SmallInt:
                        return int.TryParse(parmValue, out int intResult) ? (object)intResult : DBNull.Value;

                    case SqlDbType.BigInt:
                        return long.TryParse(parmValue, out long longResult) ? (object)longResult : DBNull.Value;

                    case SqlDbType.UniqueIdentifier:
                        return Guid.TryParse(parmValue, out Guid guidResult) ? (object)guidResult : DBNull.Value;

                    case SqlDbType.Image:
                    case SqlDbType.VarBinary:
                    case SqlDbType.Binary:
                        try
                        {
                            return Convert.FromBase64String(parmValue);
                        }
                        catch (FormatException ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                                $"Failed to decode Base64 string [{parmValue}] for SqlDbType [{sqlDbType}]. Returning DBNull.Value.",
                                null, GPALObjectType.Other, ex);
                            return DBNull.Value;
                        }

                    case SqlDbType.Timestamp:
                        try
                        {
                            if (ulong.TryParse(parmValue, out ulong ulongResult))
                            {
                                return BitConverter.GetBytes(ulongResult).Reverse().ToArray();
                            }
                            throw new FormatException("Invalid numeric string for Timestamp");
                        }
                        catch (Exception ex)
                        {
                            GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                                $"Failed to parse [{parmValue}] as Timestamp. Returning DBNull.Value.",
                                null, GPALObjectType.Other, ex);
                            return DBNull.Value;
                        }

                    case SqlDbType.Variant:
                        if (int.TryParse(parmValue, out int variantIntResult))
                            return variantIntResult;
                        if (decimal.TryParse(parmValue, out decimal variantDecResult))
                            return variantDecResult;
                        if (DateTime.TryParse(parmValue, out DateTime variantDateResult))
                            return variantDateResult;
                        if (Guid.TryParse(parmValue, out Guid variantGuidResult))
                            return variantGuidResult;
                        return parmValue;

                    case SqlDbType.Structured:
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                            $"SqlDbType.Structured is not supported for individual parameter values. Use a multi-row GPALGrid<string> in ExecuteSQL. Returning DBNull.Value.",
                            null, GPALObjectType.Other);
                        return DBNull.Value;

                    default:
                        GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                            $"Unsupported SqlDbType [{sqlDbType}] for parameter value [{parmValue}]. Returning DBNull.Value.",
                            null, GPALObjectType.Other);
                        return DBNull.Value;
                }
            }
            catch (Exception ex)
            {
                GPAL.PublishSimpleEvent(GPALEventType.WARNING,
                    $"Failed to convert parameter value [{parmValue}] to SqlDbType [{sqlDbType}]. Returning DBNull.Value.",
                    null, GPALObjectType.Other, ex);
                return DBNull.Value;
            }
        }
        /// <summary>
        /// Run whatever is defined in the Database object, return the result set and tokenize it into rows/cols grid
        /// </summary>
        /// <param name="currentUOW">The unit of work the input database is attached to, when there is one</param>
        /// <param name="inputDatabase">The GPAL Database definition</param>
        internal static void TokenizeDatabase(UnitOfWork currentUOW, IGPALDatabase inputDatabase)
        {
            IGPALGrid<string> tokens;
            if (null != currentUOW)
                currentUOW.InputDatabase = inputDatabase;
            int rowCount = DatabaseHelper.GetGrid(((GPALDatabase)inputDatabase).DatabaseSettings, out tokens);
            ((GPALDatabase)inputDatabase).Tokens = tokens;
        }
        /// <summary>
        /// Converts a DataTable into string rows for the parameterized paths, and returns its column names and types.
        /// </summary>
        /// <param name="dataTable">The table to read.</param>
        /// <param name="columnsList">Receives one entry per column.</param>
        /// <returns>The rows; empty when the table is null or has no rows.</returns>
        public static IGPALGrid<string> DataTableToGrid(DataTable dataTable, out List<DatabaseSettings.DatabaseColumn> columnsList)
        {
            IGPALGrid<string> retVal = GPAL.Grid.ToGPALObject();

            columnsList = new List<DatabaseSettings.DatabaseColumn>();

            if (null != dataTable)
            {
                foreach (DataColumn column in dataTable.Columns)
                    columnsList.Add(new DatabaseSettings.DatabaseColumn(column.ColumnName, GetSqlDbTypeFromClrType(column.DataType)));

                foreach (DataRow row in dataTable.Rows)
                    retVal.AddRow(row.ItemArray.Select(value => DBNull.Value == value ? null : value?.ToString()).ToList());
            }

            return retVal;
        }
        public static void TokenizeDatabase(GPALDatabase inputDatabase)
        {
            IGPALGrid<string> tokens;
            int rowCount = DatabaseHelper.GetGrid(inputDatabase.DatabaseSettings, out tokens);
            inputDatabase.Tokens = tokens;
        }
    }

    /// <summary>
    /// DataTable extension method
    /// set the order of the columns in a datatable based upon the order of strings in an array
    /// </summary>
    public static class DataTableExtensions
    {
        public static void SetColumnsOrder(this DataTable table, params String[] columnNames)
        {
            int columnIndex = 0;
            foreach (var columnName in columnNames)
            {
                if (table.Columns.Contains(columnName))
                {
                    table.Columns[columnName].SetOrdinal(columnIndex);
                    columnIndex++;
                }
            }
        }
    }
}

